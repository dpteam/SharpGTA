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
        private readonly List<StaticInstance>[] sectors = new List<StaticInstance>[SectorCount];
        private readonly List<StaticInstance> unindexed = new List<StaticInstance>();
        private readonly List<ModelRegistry> registries = new List<ModelRegistry>();
        private readonly List<int> collisionRevisions = new List<int>();

        public StaticWorld()
        {
            readOnlyInstances = instances.AsReadOnly();
        }

        public int Count { get { return instances.Count; } }
        public int UnindexedCount { get { return unindexed.Count; } }
        public IList<StaticInstance> Instances { get { return readOnlyInstances; } }

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
            if (TryGetSector(x, y, out sectorIndex))
            {
                List<StaticInstance> candidates = sectors[sectorIndex];
                if (candidates != null)
                {
                    for (int i = 0; i < candidates.Count; i++)
                        TestInstance(candidates[i], x, y, minZ, maxZ, ref found, ref closest, ref hit);
                }
            }
            for (int i = 0; i < unindexed.Count; i++)
                TestInstance(unindexed[i], x, y, minZ, maxZ, ref found, ref closest, ref hit);
            return found;
        }

        private void Add(StaticInstance instance)
        {
            instances.Add(instance);
            Index(instance);
        }

        private void Index(StaticInstance instance)
        {
            BoundingBox bounds = instance.WorldBounds;
            if (!IsFullyInsideGrid(bounds))
            {
                unindexed.Add(instance);
                return;
            }

            int minX = CoordinateToCell(bounds.Min.X);
            int maxX = CoordinateToCell(bounds.Max.X);
            int minY = CoordinateToCell(bounds.Min.Y);
            int maxY = CoordinateToCell(bounds.Max.Y);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * SectorSide + x;
                    if (sectors[index] == null)
                        sectors[index] = new List<StaticInstance>();
                    sectors[index].Add(instance);
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

            for (int i = 0; i < sectors.Length; i++)
                sectors[i] = null;
            unindexed.Clear();
            for (int i = 0; i < instances.Count; i++)
            {
                instances[i].RefreshWorldBounds();
                Index(instances[i]);
            }
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

        private static void TestInstance(StaticInstance instance, float x, float y,
            float minZ, float maxZ, ref bool found, ref float closest, ref StaticWorldHit closestHit)
        {
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
            if (coordinate == GridMaximum)
                return SectorSide - 1;
            int cell = (int)Math.Floor((coordinate - GridMinimum) / SectorSize);
            if (cell < 0)
                return 0;
            if (cell >= SectorSide)
                return SectorSide - 1;
            return cell;
        }
    }
}
