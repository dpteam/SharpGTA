using System;
using System.Collections.Generic;
using System.Globalization;
using SharpGTA.Collision;
using SharpGTA.Data;
using SharpGTA.IO;

namespace SharpGTA.World
{
    /// <summary>
    /// Sparse raw model IDs in one game's capacity. IDs are never remapped or shared
    /// between game profiles; names are unique using OrdinalIgnoreCase comparison.
    /// </summary>
    public sealed class ModelRegistry
    {
        private const float BigBuildingLodDistance = 300.0f;
        private static readonly float[] NoLodDistances = new float[0];
        private readonly ModelDefinition[] models;
        private readonly Dictionary<string, ModelDefinition> byName =
            new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ColModel> collisions =
            new Dictionary<string, ColModel>(StringComparer.OrdinalIgnoreCase);

        public ModelRegistry(GameDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");
            Definition = definition;
            models = new ModelDefinition[definition.Limits.ModelInfo];
        }

        public GameDefinition Definition { get; private set; }
        public int Capacity { get { return models.Length; } }
        public int Count { get; private set; }
        internal int CollisionRevision { get; private set; }

        public ModelDefinition Find(int id)
        {
            return id >= 0 && id < models.Length ? models[id] : null;
        }

        public ModelDefinition Find(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            ModelDefinition model;
            return byName.TryGetValue(name, out model) ? model : null;
        }

        /// <summary>
        /// First model of a kind in raw id order, so callers that need "some vehicle" stay
        /// deterministic without depending on file order.
        /// </summary>
        public bool TryFindFirst(ModelKind kind, out ModelDefinition model)
        {
            for (int id = 0; id < models.Length; id++)
            {
                ModelDefinition candidate = models[id];
                if (candidate == null || candidate.Kind != kind)
                    continue;
                model = candidate;
                return true;
            }
            model = null;
            return false;
        }

        public bool TryGet(int id, out ModelDefinition model)
        {
            model = Find(id);
            return model != null;
        }

        public bool TryGet(string name, out ModelDefinition model)
        {
            model = Find(name);
            return model != null;
        }

        /// <summary>Binds all typed records and raw three-field hier records atomically.</summary>
        public void Bind(IdeDocument document)
        {
            if (document == null)
                throw new ArgumentNullException("document");

            bool[] reservedIds = new bool[models.Length];
            HashSet<string> reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ModelDefinition> pending = new List<ModelDefinition>();

            for (int i = 0; i < document.Objects.Count; i++)
                StageSimple(document.Objects[i], ModelKind.Simple, pending, reservedIds, reservedNames);
            for (int i = 0; i < document.TimedObjects.Count; i++)
                StageSimple(document.TimedObjects[i], ModelKind.Timed, pending, reservedIds, reservedNames);
            for (int i = 0; i < document.Vehicles.Count; i++)
            {
                VehicleRecord record = document.Vehicles[i];
                Stage(record, record.Id, ModelKind.Vehicle, record.ModelName, record.TxdName,
                    NoLodDistances, 0, 0, 0, pending, reservedIds, reservedNames);
            }
            for (int i = 0; i < document.Peds.Count; i++)
            {
                PedRecord record = document.Peds[i];
                Stage(record, record.Id, ModelKind.Ped, record.ModelName, record.TxdName,
                    NoLodDistances, 0, 0, 0, pending, reservedIds, reservedNames);
            }
            for (int i = 0; i < document.Weapons.Count; i++)
            {
                WeaponRecord record = document.Weapons[i];
                Stage(record, record.Id, ModelKind.Weapon, record.ModelName, record.TxdName,
                    new float[] { record.LodDistance }, 0, 0, 0, pending, reservedIds, reservedNames);
            }
            for (int i = 0; i < document.RawRecords.Count; i++)
            {
                RawDataRecord record = document.RawRecords[i];
                if (!string.Equals(record.Section, "hier", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (record.Tokens.Count != 3)
                    throw Error(record, "A hier record must contain exactly id, model name and TXD name.");

                int id;
                if (!int.TryParse(record.Tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    throw Error(record, "Invalid hier model ID '" + record.Tokens[0] + "'.");
                Stage(record, id, ModelKind.Clump, record.Tokens[1], record.Tokens[2],
                    NoLodDistances, 0, 0, 0, pending, reservedIds, reservedNames);
            }

            for (int i = 0; i < pending.Count; i++)
            {
                ModelDefinition model = pending[i];
                models[model.Id] = model;
                byName.Add(model.Name, model);
                Count++;
                ColModel collision;
                if (collisions.TryGetValue(model.Name, out collision))
                    model.Collision = collision;
            }
            ResolveLodRelationships();
            ResolveTimeModelRelationships();
        }

        /// <summary>
        /// Resolves the timed-model pair the way CTimeModelInfo::FindOtherTimeModel does:
        /// the first "_nt" becomes "_dy" and vice versa, and everything after the token is
        /// dropped because the original writes a terminated three-character replacement.
        /// The original then compares model names as a case-sensitive prefix; here the
        /// switched name is looked up exactly (case-insensitively), which is equivalent for
        /// the real III/VC names and cannot invent a pair for prefix-overlapping ones.
        /// </summary>
        private void ResolveTimeModelRelationships()
        {
            for (int i = 0; i < models.Length; i++)
            {
                ModelDefinition model = models[i];
                if (model == null || model.Kind != ModelKind.Timed)
                    continue;
                string counterpart = SwapTimeSuffix(model.Name);
                if (counterpart == null)
                    continue;
                ModelDefinition found = Find(counterpart);
                if (found != null && found.Kind == ModelKind.Timed)
                    model.OtherTimeModel = found;
            }
        }

        private static string SwapTimeSuffix(string name)
        {
            int night = name.IndexOf("_nt", StringComparison.OrdinalIgnoreCase);
            if (night >= 0)
                return name.Substring(0, night) + "_dy";
            int day = name.IndexOf("_dy", StringComparison.OrdinalIgnoreCase);
            if (day >= 0)
                return name.Substring(0, day) + "_nt";
            return null;
        }

        /// <summary>
        /// CBaseModelInfo::IsSimple is true for both MITYPE_SIMPLE and MITYPE_TIME, so timed
        /// models take part in LOD pairing and draw-distance handoff exactly like plain ones.
        /// </summary>
        private static bool IsSimpleOrTimed(ModelDefinition model)
        {
            return model.Kind == ModelKind.Simple || model.Kind == ModelKind.Timed;
        }

        private void ResolveLodRelationships()
        {
            for (int i = 0; i < models.Length; i++)
            {
                ModelDefinition model = models[i];
                if (model == null || !IsSimpleOrTimed(model) ||
                    model.LodDistances.Count == 0 ||
                    model.LodDistances[0] <= BigBuildingLodDistance ||
                    model.RelatedModel != null)
                    continue;
                for (int j = 0; j < models.Length; j++)
                {
                    ModelDefinition candidate = models[j];
                    if (candidate == null || candidate == model ||
                        !IsSimpleOrTimed(candidate) ||
                        !SameLodSuffix(model.Name, candidate.Name))
                        continue;
                    model.RelatedModel = candidate;
                    model.LodNearDistance = candidate.LargestLodDistance;
                    break;
                }
            }
        }

        private static bool SameLodSuffix(string left, string right)
        {
            if (left.Length < 3 || right.Length < 3 || left.Length != right.Length)
                return false;
            return string.CompareOrdinal(left, 3, right, 3, left.Length - 3) == 0;
        }

        /// <summary>
        /// Attaches collision now or remembers it for a model bound later. The latest
        /// attachment for a case-insensitive model name wins.
        /// </summary>
        public bool AttachCollision(ColModel collision)
        {
            if (collision == null)
                throw new ArgumentNullException("collision");
            collisions[collision.ModelName] = collision;
            CollisionRevision++;
            ModelDefinition model = Find(collision.ModelName);
            if (model == null)
                return false;
            model.Collision = collision;
            return true;
        }

        private void StageSimple(SimpleObjectRecord record, ModelKind kind,
            List<ModelDefinition> pending, bool[] reservedIds, HashSet<string> reservedNames)
        {
            Stage(record, record.Id, kind, record.ModelName, record.TxdName,
                record.LodDistances, record.Flags, record.TimeOn, record.TimeOff,
                pending, reservedIds, reservedNames);
        }

        private void Stage(RawDataRecord record, int id, ModelKind kind, string name,
            string txd, IList<float> lodDistances, uint flags, int timeOn, int timeOff,
            List<ModelDefinition> pending, bool[] reservedIds, HashSet<string> reservedNames)
        {
            if (id < 0 || id >= models.Length)
            {
                throw Error(record, "Model ID " + id.ToString(CultureInfo.InvariantCulture) +
                    " is outside the game capacity [0, " + models.Length.ToString(CultureInfo.InvariantCulture) + ").");
            }
            if (models[id] != null || reservedIds[id])
                throw Error(record, "Duplicate model ID " + id.ToString(CultureInfo.InvariantCulture) + ".");
            if (byName.ContainsKey(name) || !reservedNames.Add(name))
                throw Error(record, "Duplicate model name '" + name + "' (case-insensitive).");

            reservedIds[id] = true;
            pending.Add(new ModelDefinition(id, kind, name, txd, lodDistances, flags,
                timeOn, timeOff));
        }

        private static AssetFormatException Error(RawDataRecord record, string message)
        {
            return new AssetFormatException("IDE " + record.Section + " line " +
                record.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }
    }
}
