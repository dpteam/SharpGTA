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
                    NoLodDistances, 0, pending, reservedIds, reservedNames);
            }
            for (int i = 0; i < document.Peds.Count; i++)
            {
                PedRecord record = document.Peds[i];
                Stage(record, record.Id, ModelKind.Ped, record.ModelName, record.TxdName,
                    NoLodDistances, 0, pending, reservedIds, reservedNames);
            }
            for (int i = 0; i < document.Weapons.Count; i++)
            {
                WeaponRecord record = document.Weapons[i];
                Stage(record, record.Id, ModelKind.Weapon, record.ModelName, record.TxdName,
                    new float[] { record.LodDistance }, 0, pending, reservedIds, reservedNames);
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
                    NoLodDistances, 0, pending, reservedIds, reservedNames);
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
                record.LodDistances, record.Flags, pending, reservedIds, reservedNames);
        }

        private void Stage(RawDataRecord record, int id, ModelKind kind, string name,
            string txd, IList<float> lodDistances, uint flags, List<ModelDefinition> pending,
            bool[] reservedIds, HashSet<string> reservedNames)
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
            pending.Add(new ModelDefinition(id, kind, name, txd, lodDistances, flags));
        }

        private static AssetFormatException Error(RawDataRecord record, string message)
        {
            return new AssetFormatException("IDE " + record.Section + " line " +
                record.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }
    }
}
