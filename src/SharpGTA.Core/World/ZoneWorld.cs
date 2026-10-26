using System;
using System.Collections.Generic;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// Headless zone, attribute-zone and occlusion-volume data. Raw level IDs stay
    /// scoped to this game's profile; they are never converted to a shared enum.
    /// Population, audio and visibility simulation are deliberately out of scope.
    /// </summary>
    public sealed class ZoneWorld
    {
        /// <summary>LEVEL_GENERIC is zero in both supported PC game profiles.</summary>
        public const int GenericRawLevel = 0;

        private readonly DataSchema schema;
        private readonly bool separateZoneArrays;
        private readonly List<ZoneRecord> navigation;
        private readonly List<ZoneRecord> information;
        private readonly List<ZoneRecord> mapZones;
        private readonly List<CullZoneRecord> cullZones;
        private readonly List<OcclusionVolumeRecord> occlusionVolumes;

        public ZoneWorld(GameDefinition definition)
            : this(GetDefinitionId(definition), GetDefinitionSchema(definition))
        {
        }

        public ZoneWorld(GameId gameId, DataSchema schema)
        {
            if (gameId != GameId.III && gameId != GameId.ViceCity)
                throw new ArgumentOutOfRangeException("gameId");
            if (schema == null)
                throw new ArgumentNullException("schema");

            this.schema = schema;
            separateZoneArrays = schema.HasSeparateZoneArrays;
            navigation = new List<ZoneRecord>();
            information = new List<ZoneRecord>();
            mapZones = new List<ZoneRecord>();
            cullZones = new List<CullZoneRecord>();
            occlusionVolumes = new List<OcclusionVolumeRecord>();

            if (gameId == GameId.III)
                CreateIIIRoots();
            else
                CreateVCRoots();

            Navigation = navigation.AsReadOnly();
            // III has one ZoneArray for Default, Navig and Info. Exposing the same
            // view as Information preserves that layout and its CITYZON root.
            Information = separateZoneArrays ? information.AsReadOnly() : Navigation;
            MapZones = mapZones.AsReadOnly();
            CullZones = cullZones.AsReadOnly();
            OcclusionVolumes = occlusionVolumes.AsReadOnly();
        }

        public IList<ZoneRecord> Navigation { get; private set; }
        public IList<ZoneRecord> Information { get; private set; }
        public IList<ZoneRecord> MapZones { get; private set; }
        public IList<CullZoneRecord> CullZones { get; private set; }
        public IList<OcclusionVolumeRecord> OcclusionVolumes { get; private set; }

        public void AddZone(ZoneRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            if (record.Kind == ZoneKind.MapZone)
            {
                AddMapZone(record);
                return;
            }
            if (record.Kind == ZoneKind.Info && separateZoneArrays)
            {
                information.Add(record);
                return;
            }
            if (record.Kind != ZoneKind.Default && record.Kind != ZoneKind.Navig &&
                record.Kind != ZoneKind.Info)
                throw new ArgumentException("Unknown zone kind.", "record");

            navigation.Add(record);
            InsertIntoHierarchy(navigation.Count - 1);
        }

        /// <summary>Adds a MAPZONE directive record only to the map-level array.</summary>
        public void AddMapZone(ZoneRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");
            mapZones.Add(record);
        }

        public void AddCull(CullZoneRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");
            cullZones.Add(record);
        }

        public void AddOcclusion(OcclusionVolumeRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");
            if (!schema.SupportsOcclusion)
                throw new NotSupportedException("Occlusion volumes are not supported by this game data profile.");
            occlusionVolumes.Add(record);
        }

        /// <summary>Returns a normal-zone array entry (VC's navigation array).</summary>
        public ZoneRecord GetZone(int index)
        {
            return navigation[index];
        }

        /// <summary>Finds the deepest normal zone, or the synthetic root as fallback.</summary>
        public ZoneRecord FindSmallestZone(Vector3 position)
        {
            if (separateZoneArrays)
                return FindSmallestNavigationZone(position, true, true);

            int best = 0;
            int index = navigation[0].ChildIndex;
            while (index >= 0)
            {
                ZoneRecord zone = navigation[index];
                if (zone.Bounds.Contains(position))
                {
                    if (IsNormalZone(zone.Kind))
                        best = index;
                    index = zone.ChildIndex;
                }
                else
                {
                    index = zone.NextIndex;
                }
            }
            return navigation[best];
        }

        /// <summary>Recreates VC FindSmallestNavigationZoneForPosition filtering.</summary>
        public ZoneRecord FindSmallestNavigationZone(Vector3 position, bool findDefault, bool findNavig)
        {
            int best = MatchesKind(navigation[0], findDefault, findNavig) ? 0 : -1;
            int index = navigation[0].ChildIndex;
            while (index >= 0)
            {
                ZoneRecord zone = navigation[index];
                if (zone.Bounds.Contains(position))
                {
                    if (MatchesKind(zone, findDefault, findNavig))
                        best = index;
                    index = zone.ChildIndex;
                }
                else
                {
                    index = zone.NextIndex;
                }
            }
            return best >= 0 ? navigation[best] : null;
        }

        public ZoneRecord FindInformationZone(Vector3 position)
        {
            if (!separateZoneArrays)
                return FindSmallestZone(position);

            // VC deliberately uses the first matching InfoZoneArray entry, not the
            // smallest or deepest one. Its root is returned even outside its bounds.
            for (int i = 1; i < information.Count; i++)
            {
                if (information[i].Bounds.Contains(position))
                    return information[i];
            }
            return information[0];
        }

        public int GetRawLevelFromPosition(Vector3 position)
        {
            for (int i = 1; i < mapZones.Count; i++)
            {
                if (mapZones[i].Bounds.Contains(position))
                    return mapZones[i].RawLevel;
            }
            return mapZones[0].RawLevel;
        }

        public int FindCullAttributes(Vector3 position, out int wantedLevelDrop)
        {
            int attributes = 0;
            wantedLevelDrop = 0;
            for (int i = 0; i < cullZones.Count; i++)
            {
                CullZoneRecord zone = cullZones[i];
                if (!zone.Bounds.Contains(position))
                    continue;
                attributes |= zone.Flags;
                if (zone.WantedLevelDrop > wantedLevelDrop)
                    wantedLevelDrop = zone.WantedLevelDrop;
            }
            return attributes;
        }

        private void CreateIIIRoots()
        {
            BoundingBox normalBounds = new BoundingBox(
                new Vector3(-4000.0f, -4000.0f, -500.0f),
                new Vector3(4000.0f, 4000.0f, 500.0f));
            navigation.Add(CreateRoot("CITYZON", ZoneKind.Default, normalBounds));
            mapZones.Add(CreateRoot("THEMAP", ZoneKind.MapZone, normalBounds));

            // A mismatched custom profile still gets a usable independent info root.
            if (separateZoneArrays)
                information.Add(CreateRoot("CITYZON", ZoneKind.Info, normalBounds));
        }

        private void CreateVCRoots()
        {
            BoundingBox navigationBounds = new BoundingBox(
                new Vector3(-2400.0f, -2000.0f, -500.0f),
                new Vector3(1600.0f, 2000.0f, 500.0f));
            navigation.Add(CreateRoot("VICE_C", ZoneKind.Default, navigationBounds));
            information.Add(CreateRoot("CITYINF", ZoneKind.Info, navigationBounds));
            mapZones.Add(CreateRoot("THEMAP", ZoneKind.MapZone, navigationBounds));
        }

        private static ZoneRecord CreateRoot(string name, ZoneKind kind, BoundingBox bounds)
        {
            return new ZoneRecord(0, name, kind, bounds, GenericRawLevel);
        }

        private void InsertIntoHierarchy(int innerIndex)
        {
            ZoneRecord inner = navigation[innerIndex];
            inner.ChildIndex = -1;
            inner.ParentIndex = -1;
            inner.NextIndex = -1;
            InsertIntoHierarchyRecursive(innerIndex, 0);
        }

        private bool InsertIntoHierarchyRecursive(int innerIndex, int outerIndex)
        {
            if (outerIndex < 0)
                return false;

            ZoneRecord inner = navigation[innerIndex];
            ZoneRecord outer = navigation[outerIndex];
            if (!IsEntirelyContained(inner, outer))
                return false;

            for (int childIndex = outer.ChildIndex; childIndex >= 0;
                childIndex = navigation[childIndex].NextIndex)
            {
                if (InsertIntoHierarchyRecursive(innerIndex, childIndex))
                    return true;
            }

            int containedChildren = 0;
            for (int childIndex = outer.ChildIndex; childIndex >= 0;
                childIndex = navigation[childIndex].NextIndex)
            {
                if (IsEntirelyContained(navigation[childIndex], inner))
                    containedChildren++;
            }

            inner.NextIndex = outer.ChildIndex;
            inner.ParentIndex = outerIndex;
            outer.ChildIndex = innerIndex;

            if (containedChildren != 0)
            {
                int insertIndex = innerIndex;
                int childIndex = inner.NextIndex;
                while (childIndex >= 0)
                {
                    ZoneRecord child = navigation[childIndex];
                    int nextIndex = child.NextIndex;
                    if (IsEntirelyContained(child, inner))
                    {
                        navigation[insertIndex].NextIndex = nextIndex;
                        child.ParentIndex = innerIndex;
                        child.NextIndex = inner.ChildIndex;
                        inner.ChildIndex = childIndex;
                    }
                    else
                    {
                        insertIndex = childIndex;
                    }
                    childIndex = nextIndex;
                }
            }
            return true;
        }

        private static bool IsEntirelyContained(ZoneRecord inner, ZoneRecord outer)
        {
            BoundingBox a = inner.Bounds;
            BoundingBox b = outer.Bounds;
            return a.Min.X >= b.Min.X && a.Max.X <= b.Max.X &&
                a.Min.Y >= b.Min.Y && a.Max.Y <= b.Max.Y &&
                a.Min.Z >= b.Min.Z && a.Max.Z <= b.Max.Z;
        }

        private static bool IsNormalZone(ZoneKind kind)
        {
            return kind == ZoneKind.Default || kind == ZoneKind.Navig || kind == ZoneKind.Info;
        }

        private static bool MatchesKind(ZoneRecord zone, bool findDefault, bool findNavig)
        {
            return findDefault && zone.Kind == ZoneKind.Default ||
                findNavig && zone.Kind == ZoneKind.Navig;
        }

        private static GameId GetDefinitionId(GameDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");
            return definition.Id;
        }

        private static DataSchema GetDefinitionSchema(GameDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");
            return definition.DataSchema;
        }
    }
}
