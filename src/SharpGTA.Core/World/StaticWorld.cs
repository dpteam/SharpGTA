using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using SharpGTA.Assets;
using SharpGTA.Collision;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// Headless static IPL placements and collision queries. This is deliberately not
    /// a dynamic physics world or a complete CWorld implementation.
    /// </summary>
    public sealed class StaticWorld
    {
        public const int SectorSide = 100;
        public const float SectorSize = 40.0f;
        public const float GridMinimum = -2000.0f;
        public const float GridMaximum = 2000.0f;

        private const int SectorCount = SectorSide * SectorSide;
        private readonly List<StaticInstance> instances = new List<StaticInstance>();
        private readonly ReadOnlyCollection<StaticInstance> readOnlyInstances;
        private readonly ReadOnlyCollection<WorldEntity> readOnlyDynamicEntities;
        private readonly List<WorldEntity>[] sectorMain = new List<WorldEntity>[SectorCount];
        private readonly List<WorldEntity>[] sectorOverlap = new List<WorldEntity>[SectorCount];
        private readonly List<WorldEntity> unindexed = new List<WorldEntity>();
        private readonly List<WorldEntity> dynamicEntities = new List<WorldEntity>();
        private int scanCode;
        private readonly List<ModelRegistry> registries = new List<ModelRegistry>();
        private readonly List<int> collisionRevisions = new List<int>();
        private readonly List<WorldEntity> queryBuffer = new List<WorldEntity>();
        private WorldEntities entities;
        private GameDefinition entityDefinition;
        private ObjectData objectData;
        private PathWorld paths;

        public StaticWorld()
        {
            readOnlyInstances = instances.AsReadOnly();
            readOnlyDynamicEntities = dynamicEntities.AsReadOnly();
        }

        public int Count { get { return instances.Count; } }
        public int UnindexedCount { get { return unindexed.Count; } }
        public IList<StaticInstance> Instances { get { return readOnlyInstances; } }

        /// <summary>Gameplay entities in deterministic spawn order.</summary>
        public IList<WorldEntity> DynamicEntities { get { return readOnlyDynamicEntities; } }

        /// <summary>Sector membership changes caused by moving dynamic entities.</summary>
        public int SectorMoves { get; private set; }

        /// <summary>Per-kind entity pools for this world, or null before the first Bind.</summary>
        public WorldEntities Entities { get { return entities; } }

        /// <summary>
        /// Map placements that could not get a handle because their pool was full. Gameplay
        /// spawn rejections are counted separately so the placement diagnostics stay honest.
        /// </summary>
        public int InstancesWithoutHandle
        {
            get
            {
                if (entities == null)
                    return 0;
                return entities.RejectedCount(WorldEntityKind.Building) +
                    entities.RejectedCount(WorldEntityKind.Dummy) +
                    entities.RejectedCount(WorldEntityKind.Treadable);
            }
        }

        /// <summary>Gameplay spawns rejected because their kind's pool was full.</summary>
        public int DynamicRejectedCount
        {
            get
            {
                if (entities == null)
                    return 0;
                return entities.RejectedCount(WorldEntityKind.Object) +
                    entities.RejectedCount(WorldEntityKind.Ped) +
                    entities.RejectedCount(WorldEntityKind.Vehicle);
            }
        }

        /// <summary>Resolves a building handle back to its placement, or false when stale.</summary>
        public bool TryGetBuilding(int handle, out StaticInstance instance)
        {
            instance = null;
            if (entities == null)
                return false;
            WorldEntity entity;
            if (!entities.TryGet(WorldEntityKind.Building, handle, out entity))
                return false;
            instance = entity as StaticInstance;
            return instance != null;
        }

        /// <summary>Resolves a handle of any kind back to its entity, or false when stale.</summary>
        public bool TryGetEntity(WorldEntityKind kind, int handle, out WorldEntity entity)
        {
            if (entities == null)
            {
                entity = null;
                return false;
            }
            return entities.TryGet(kind, handle, out entity);
        }

        /// <summary>Adds resolvable instances and reports every missing raw model ID.</summary>
        public int Bind(IplDocument document, ModelRegistry registry, LoadReport report)
        {
            return Bind(document, registry, report, "IPL");
        }

        public int Bind(IplDocument document, ModelRegistry registry, LoadReport report, string sourcePath)
        {
            if (document == null)
                throw new ArgumentNullException("document");
            if (registry == null)
                throw new ArgumentNullException("registry");
            if (report == null)
                throw new ArgumentNullException("report");

            EnsureEntities(registry);
            EnsureCollisionBounds();
            int added = 0;
            for (int i = 0; i < document.Instances.Count; i++)
            {
                ObjectInstanceRecord record = document.Instances[i];
                ModelDefinition model = registry.Find(record.Id);
                if (model == null)
                {
                    report.Add(LoadIssueSeverity.Warning, "MissingModel", sourcePath,
                        record.LineNumber, "Skipped IPL instance '" + record.ModelName +
                        "': model ID " + record.Id.ToString(CultureInfo.InvariantCulture) + " is not registered.");
                    continue;
                }
                Add(new StaticInstance(record, model));
                added++;
            }
            TrackCollisionRevision(registry);
            return added;
        }

        /// <summary>Casts a world-space downward ray through static collision only.</summary>
        public bool QueryVertical(float x, float y, float minZ, float maxZ, out StaticWorldHit hit)
        {
            if (minZ > maxZ)
            {
                float temporary = minZ;
                minZ = maxZ;
                maxZ = temporary;
            }
            hit = new StaticWorldHit();
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(minZ) || float.IsNaN(maxZ) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(minZ) || float.IsInfinity(maxZ))
                return false;

            EnsureCollisionBounds();
            bool found = false;
            float closest = float.PositiveInfinity;
            int sectorIndex;
            int code = NextScanCode();
            if (TryGetSector(x, y, out sectorIndex))
            {
                TestSectorList(sectorMain[sectorIndex], code, x, y, minZ, maxZ, ref found, ref closest, ref hit);
                TestSectorList(sectorOverlap[sectorIndex], code, x, y, minZ, maxZ, ref found, ref closest, ref hit);
            }
            for (int i = 0; i < unindexed.Count; i++)
                TestInstance(unindexed[i], x, y, minZ, maxZ, ref found, ref closest, ref hit);
            return found;
        }

        /// <summary>
        /// Collects instances whose bound rect touches a sector overlapped by the square
        /// around the view point. This is a sector-level candidate set: callers still apply
        /// their own distance and visibility tests, exactly like CWorld sector scanning.
        /// </summary>
        public int QuerySectors(Vector3 center, float radius, IList<StaticInstance> result)
        {
            if (result == null)
                throw new ArgumentNullException("result");
            QueryEntities(center, radius, queryBuffer);
            result.Clear();
            for (int i = 0; i < queryBuffer.Count; i++)
            {
                StaticInstance instance = queryBuffer[i] as StaticInstance;
                if (instance != null)
                    result.Add(instance);
            }
            return result.Count;
        }

        /// <summary>
        /// Same sector scan as <see cref="QuerySectors"/>, without dropping dynamic entities.
        /// The candidate set is mixed because the original keeps every kind in one CSector
        /// array; callers filter by type when they need only placements.
        /// </summary>
        public int QueryEntities(Vector3 center, float radius, IList<WorldEntity> result)
        {
            if (result == null)
                throw new ArgumentNullException("result");
            if (!(radius > 0.0f) || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            result.Clear();
            if (float.IsNaN(center.X) || float.IsNaN(center.Y) ||
                float.IsInfinity(center.X) || float.IsInfinity(center.Y))
                return 0;

            // Collision can arrive after the IPLs, which changes WorldBounds and therefore
            // sector membership; refresh before reading the grid, as QueryVertical does.
            EnsureCollisionBounds();
            int code = NextScanCode();
            int xstart = CoordinateToCell(center.X - radius);
            int xend = CoordinateToCell(center.X + radius);
            int ystart = CoordinateToCell(center.Y - radius);
            int yend = CoordinateToCell(center.Y + radius);
            for (int y = ystart; y <= yend; y++)
            {
                for (int x = xstart; x <= xend; x++)
                {
                    int index = y * SectorSide + x;
                    AppendSectorList(sectorMain[index], code, result);
                    AppendSectorList(sectorOverlap[index], code, result);
                }
            }
            for (int i = 0; i < unindexed.Count; i++)
                AppendInstance(unindexed[i], code, result);
            return result.Count;
        }

        /// <summary>Sector column of a world coordinate, clamped to the original grid.</summary>
        public static int SectorIndexX(float x)
        {
            return CoordinateToCell(x);
        }

        /// <summary>Sector row of a world coordinate, clamped to the original grid.</summary>
        public static int SectorIndexY(float y)
        {
            return CoordinateToCell(y);
        }

        /// <summary>World X of a sector column's minimum edge.</summary>
        public static float SectorOriginX(int column)
        {
            if (column < 0 || column >= SectorSide)
                throw new ArgumentOutOfRangeException("column");
            return GridMinimum + column * SectorSize;
        }

        /// <summary>World Y of a sector row's minimum edge.</summary>
        public static float SectorOriginY(int row)
        {
            if (row < 0 || row >= SectorSide)
                throw new ArgumentOutOfRangeException("row");
            return GridMinimum + row * SectorSize;
        }

        /// <summary>
        /// The sector's main or overlap list, or null when nothing touches that cell.
        /// Mirrors CWorld::GetSector(x, y)->m_lists[ENTITYLIST_*]. The returned list is
        /// read-only; callers must not mutate sector contents.
        /// </summary>
        public IList<WorldEntity> GetSectorList(int column, int row, bool overlap)
        {
            if (column < 0 || column >= SectorSide)
                throw new ArgumentOutOfRangeException("column");
            if (row < 0 || row >= SectorSide)
                throw new ArgumentOutOfRangeException("row");
            List<WorldEntity> list = (overlap ? sectorOverlap : sectorMain)[row * SectorSide + column];
            return list == null ? null : list.AsReadOnly();
        }

        /// <summary>
        /// Linear-scan equivalent of <see cref="QuerySectors"/> with the same candidate
        /// predicate: instances whose bound rect touches the covered sector square, plus
        /// every instance outside the grid. Used by tests and `--verify-sectors`, not per
        /// frame. Cell overlap is compared with the same helper the index uses, so a rect
        /// exactly on a sector seam cannot disagree between the two paths.
        /// </summary>
        public int QuerySectorsLinear(Vector3 center, float radius, IList<StaticInstance> result)
        {
            if (result == null)
                throw new ArgumentNullException("result");
            if (!(radius > 0.0f) || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            result.Clear();
            if (float.IsNaN(center.X) || float.IsNaN(center.Y) ||
                float.IsInfinity(center.X) || float.IsInfinity(center.Y))
                return 0;

            EnsureCollisionBounds();
            int xstart = CoordinateToCell(center.X - radius);
            int xend = CoordinateToCell(center.X + radius);
            int ystart = CoordinateToCell(center.Y - radius);
            int yend = CoordinateToCell(center.Y + radius);
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                BoundingBox bounds = instance.WorldBounds;
                if (!IsFullyInsideGrid(bounds))
                {
                    result.Add(instance);
                    continue;
                }
                // The index stores an instance in every cell from cell(Min) to cell(Max)
                // inclusive, so cell overlap is the exact predicate.
                if (CoordinateToCell(bounds.Max.X) < xstart ||
                    CoordinateToCell(bounds.Min.X) > xend)
                    continue;
                if (CoordinateToCell(bounds.Max.Y) < ystart ||
                    CoordinateToCell(bounds.Min.Y) > yend)
                    continue;
                result.Add(instance);
            }
            return result.Count;
        }

        private int NextScanCode()
        {
            // A wrapped stamp could otherwise match an old one and silently skip instances,
            // so clear every stamp before reusing the counter space.
            if (scanCode == int.MaxValue)
            {
                for (int i = 0; i < instances.Count; i++)
                    instances[i].ScanCode = 0;
                for (int i = 0; i < unindexed.Count; i++)
                    unindexed[i].ScanCode = 0;
                for (int i = 0; i < dynamicEntities.Count; i++)
                    dynamicEntities[i].ScanCode = 0;
                scanCode = 0;
            }
            scanCode++;
            return scanCode;
        }

        private static void AppendSectorList(List<WorldEntity> list, int code,
            IList<WorldEntity> result)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
                AppendInstance(list[i], code, result);
        }

        private static void AppendInstance(WorldEntity instance, int code,
            IList<WorldEntity> result)
        {
            // An instance spanning several sectors appears in several overlap lists; the
            // scan code keeps one entry per query, like CEntity::m_scanCode.
            if (instance.ScanCode == code)
                return;
            instance.ScanCode = code;
            result.Add(instance);
        }

        private static void TestSectorList(List<WorldEntity> list, int code, float x, float y,
            float minZ, float maxZ, ref bool found, ref float closest, ref StaticWorldHit closestHit)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                WorldEntity instance = list[i];
                if (instance.ScanCode == code)
                    continue;
                instance.ScanCode = code;
                TestInstance(instance, x, y, minZ, maxZ, ref found, ref closest, ref closestHit);
            }
        }

        /// <summary>
        /// Spawns a gameplay entity (object, ped or vehicle), allocating its kind's pool slot
        /// and indexing it in the shared CWorld sector grid. Returns false when the pool is
        /// full; the entity is not created and the rejection is counted.
        /// </summary>
        public bool TrySpawnEntity(WorldEntityKind kind, ModelDefinition model, Vector3 position,
            Vector3 velocity, out DynamicEntity entity)
        {
            entity = null;
            if (entities == null)
                throw new InvalidOperationException("Bind a world before spawning entities.");
            if (model == null)
                throw new ArgumentNullException("model");
            RequireFinite(position, "position");
            RequireFinite(velocity, "velocity");
            DynamicEntity candidate = new DynamicEntity(model, kind, position, velocity);
            int handle = entities.Create(kind, candidate);
            if (handle < 0)
                return false;
            candidate.Handle = handle;
            dynamicEntities.Add(candidate);
            Index(candidate);
            entity = candidate;
            return true;
        }

        /// <summary>
        /// Moves a gameplay entity and keeps the sector index in sync. Any code that repositions
        /// an entity must go through this (or AdvanceDynamic), otherwise queries and Despawn
        /// would keep looking at the old sectors.
        /// </summary>
        public void MoveEntity(DynamicEntity entity, Vector3 position)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            RequireFinite(position, "position");
            if (!dynamicEntities.Contains(entity))
                throw new InvalidOperationException("The entity is not spawned in this world.");
            Unindex(entity);
            entity.SetPosition(position);
            Index(entity);
        }

        /// <summary>Despawns a gameplay entity; static placements cannot be despawned here.</summary>
        public bool DespawnEntity(WorldEntityKind kind, int handle)
        {
            if (entities == null)
                return false;
            WorldEntity entity;
            if (!entities.TryGet(kind, handle, out entity) || entity.IsStatic)
                return false;
            Unindex(entity);
            dynamicEntities.Remove(entity);
            return entities.Release(kind, handle);
        }

        /// <summary>
        /// Integrates gameplay entities and moves their sector membership. Returns how many
        /// entities changed sector this step; static placements are never touched.
        /// </summary>
        public int AdvanceDynamic(float deltaSeconds)
        {
            if (!(deltaSeconds > 0.0f) || float.IsInfinity(deltaSeconds))
                return 0;
            int moves = 0;
            for (int i = 0; i < dynamicEntities.Count; i++)
            {
                DynamicEntity entity = dynamicEntities[i] as DynamicEntity;
                if (entity == null)
                    continue;
                int before = CellIndex(entity.WorldBounds);
                Unindex(entity);
                entity.Advance(deltaSeconds);
                Index(entity);
                if (CellIndex(entity.WorldBounds) != before)
                    moves++;
            }
            SectorMoves += moves;
            return moves;
        }

        /// <summary>
        /// Sector cell of the middle of an entity's bound rect, or -1 when the entity left the
        /// grid: the original moves it out of every sector list at that point, and a clamped
        /// index would hide that transition.
        /// </summary>
        private static int CellIndex(BoundingBox bounds)
        {
            if (!IsFullyInsideGrid(bounds))
                return -1;
            return CoordinateToCell((bounds.Min.Y + bounds.Max.Y) * 0.5f) * SectorSide +
                CoordinateToCell((bounds.Min.X + bounds.Max.X) * 0.5f);
        }

        private static void RequireFinite(Vector3 value, string name)
        {
            if (float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z) ||
                float.IsInfinity(value.X) || float.IsInfinity(value.Y) || float.IsInfinity(value.Z))
                throw new ArgumentOutOfRangeException(name);
        }

        /// <summary>Removes an entity from every sector its current bounds touch.</summary>
        private void Unindex(WorldEntity entity)
        {
            BoundingBox bounds = entity.WorldBounds;
            if (!IsFullyInsideGrid(bounds))
            {
                unindexed.Remove(entity);
                return;
            }
            int minX = SectorIndexX(bounds.Min.X);
            int maxX = SectorIndexX(bounds.Max.X);
            int middleX = SectorIndexX((bounds.Min.X + bounds.Max.X) * 0.5f);
            int minY = SectorIndexY(bounds.Min.Y);
            int maxY = SectorIndexY(bounds.Max.Y);
            int middleY = SectorIndexY((bounds.Min.Y + bounds.Max.Y) * 0.5f);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    List<WorldEntity>[] lists =
                        (x == middleX && y == middleY) ? sectorMain : sectorOverlap;
                    List<WorldEntity> list = lists[y * SectorSide + x];
                    if (list != null)
                        list.Remove(entity);
                }
            }
        }

        private void Add(StaticInstance instance)
        {
            instances.Add(instance);
            // Every IPL placement becomes a CBuilding in the original, so it receives a
            // building-pool handle; a full pool is reported instead of throwing.
            instance.Handle = entities.Create(Classify(instance.Model), instance);
            Index(instance);
        }

        /// <summary>
        /// The original decides an IPL placement's entity kind by model: OBJECT.DAT models
        /// become CDummyObjects, models used by a path header become CTreadables, and the rest
        /// are CBuildings.
        /// </summary>
        private WorldEntityKind Classify(ModelDefinition model)
        {
            if (objectData != null && objectData.Contains(model.Name))
                return WorldEntityKind.Dummy;
            if (paths != null && paths.IsPathObject(model.Name))
                return WorldEntityKind.Treadable;
            return WorldEntityKind.Building;
        }

        /// <summary>
        /// Installs the classification sources. Placements are classified when they are added,
        /// so this must happen before the first Bind; changing it later would leave already
        /// created entities in the wrong pool, which is rejected instead of hidden.
        /// </summary>
        public void SetEntityClassification(ObjectData objects, PathWorld pathWorld)
        {
            if (instances.Count != 0)
                throw new InvalidOperationException(
                    "Entity classification must be set before instances are bound.");
            objectData = objects;
            paths = pathWorld;
        }

        /// <summary>
        /// Creates the pools from the first bound game definition. Mixing definitions in one
        /// world would mix capacities and raw IDs, so it is rejected explicitly.
        /// </summary>
        private void EnsureEntities(ModelRegistry registry)
        {
            GameDefinition definition = registry.Definition;
            if (entities == null)
            {
                entityDefinition = definition;
                entities = new WorldEntities(definition.Limits);
                return;
            }
            if (entityDefinition.Id != definition.Id)
                throw new InvalidOperationException(
                    "A static world cannot mix game profiles; its entity pools are per game.");
        }

        private void Index(WorldEntity instance)
        {
            BoundingBox bounds = instance.WorldBounds;
            if (!IsFullyInsideGrid(bounds))
            {
                unindexed.Add(instance);
                return;
            }

            // CEntity::Add puts the entity into every sector its bound rect touches, in the
            // main list only for the middle sector and in the overlap list everywhere else.
            int minX = SectorIndexX(bounds.Min.X);
            int maxX = SectorIndexX(bounds.Max.X);
            int middleX = SectorIndexX((bounds.Min.X + bounds.Max.X) * 0.5f);
            int minY = SectorIndexY(bounds.Min.Y);
            int maxY = SectorIndexY(bounds.Max.Y);
            int middleY = SectorIndexY((bounds.Min.Y + bounds.Max.Y) * 0.5f);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * SectorSide + x;
                    List<WorldEntity>[] lists =
                        (x == middleX && y == middleY) ? sectorMain : sectorOverlap;
                    if (lists[index] == null)
                        lists[index] = new List<WorldEntity>();
                    lists[index].Add(instance);
                }
            }
        }

        private void EnsureCollisionBounds()
        {
            bool rebuild = false;
            for (int i = 0; i < registries.Count; i++)
            {
                if (collisionRevisions[i] != registries[i].CollisionRevision)
                {
                    rebuild = true;
                    break;
                }
            }
            if (!rebuild)
                return;

            for (int i = 0; i < SectorCount; i++)
            {
                sectorMain[i] = null;
                sectorOverlap[i] = null;
            }
            unindexed.Clear();
            for (int i = 0; i < instances.Count; i++)
            {
                instances[i].RefreshWorldBounds();
                Index(instances[i]);
            }
            // Dynamic entities live in the same grid, so the rebuild must restore them too.
            for (int i = 0; i < dynamicEntities.Count; i++)
                Index(dynamicEntities[i]);
            for (int i = 0; i < registries.Count; i++)
                collisionRevisions[i] = registries[i].CollisionRevision;
        }

        private void TrackCollisionRevision(ModelRegistry registry)
        {
            for (int i = 0; i < registries.Count; i++)
            {
                if (registries[i] == registry)
                {
                    collisionRevisions[i] = registry.CollisionRevision;
                    return;
                }
            }
            registries.Add(registry);
            collisionRevisions.Add(registry.CollisionRevision);
        }

        private static void TestInstance(WorldEntity entity, float x, float y,
            float minZ, float maxZ, ref bool found, ref float closest, ref StaticWorldHit closestHit)
        {
            // Dynamic entities are indexed in the same grid but carry no collision yet, and
            // StaticWorldHit addresses a placement, so only statics can hit.
            StaticInstance instance = entity as StaticInstance;
            if (instance == null)
                return;
            ColModel collision = instance.Model.Collision;
            if (collision == null)
                return;
            BoundingBox bounds = instance.WorldBounds;
            if (x < bounds.Min.X || x > bounds.Max.X || y < bounds.Min.Y || y > bounds.Max.Y ||
                maxZ < bounds.Min.Z || minZ > bounds.Max.Z)
                return;

            Matrix transform = instance.Transform;
            Vector3 worldOrigin = new Vector3(x, y, maxZ);
            Vector3 worldDirection = new Vector3(0.0f, 0.0f, -1.0f);
            Vector3 localOrigin = transform.InverseTransformPoint(worldOrigin);
            Vector3 localDirection = InverseTransformDirection(transform, worldDirection);
            Ray localRay = new Ray(localOrigin, localDirection, maxZ - minZ);
            ColHit localHit;
            if (!collision.RayCast(localRay, out localHit) || localHit.Distance >= closest)
                return;

            Vector3 worldPosition = transform.TransformPoint(localHit.Position);
            Vector3 worldNormal = transform.TransformDirection(localHit.Normal);
            if (worldNormal.LengthSquared() > 0.0f)
                worldNormal = worldNormal.Normalize();
            closest = localHit.Distance;
            closestHit = new StaticWorldHit(instance, worldPosition, worldNormal,
                localHit.Distance, localHit);
            found = true;
        }

        private static Vector3 InverseTransformDirection(Matrix transform, Vector3 direction)
        {
            return new Vector3(
                Vector3.Dot(transform.Right, direction),
                Vector3.Dot(transform.Forward, direction),
                Vector3.Dot(transform.Up, direction));
        }

        private static bool IsFullyInsideGrid(BoundingBox bounds)
        {
            return bounds.Min.X >= GridMinimum && bounds.Max.X <= GridMaximum &&
                bounds.Min.Y >= GridMinimum && bounds.Max.Y <= GridMaximum;
        }

        private static bool TryGetSector(float x, float y, out int index)
        {
            index = -1;
            if (x < GridMinimum || x > GridMaximum || y < GridMinimum || y > GridMaximum)
                return false;
            index = CoordinateToCell(y) * SectorSide + CoordinateToCell(x);
            return true;
        }

        private static int CoordinateToCell(float coordinate)
        {
            if (float.IsNaN(coordinate))
                return 0;
            // Work in double and clamp before the cast: a huge or infinite radius must not
            // reach an out-of-range integer conversion.
            double cell = ((double)coordinate - GridMinimum) / SectorSize;
            if (cell <= 0.0)
                return 0;
            if (cell >= SectorSide)
                return SectorSide - 1;
            return (int)Math.Floor(cell);
        }
    }
}
