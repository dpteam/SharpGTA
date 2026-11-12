using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.Collision;
using SharpGTA.Data;
using System.Globalization;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class SectorTests
    {
        [Test]
        public void SectorGridMatchesTheOriginalWorldConstants()
        {
            Assert.Equal(100, StaticWorld.SectorSide);
            Assert.Near(40.0f, StaticWorld.SectorSize);
            Assert.Near(-2000.0f, StaticWorld.GridMinimum);
            Assert.Near(2000.0f, StaticWorld.GridMaximum);

            Assert.Equal(0, StaticWorld.SectorIndexX(-2000.0f));
            Assert.Equal(0, StaticWorld.SectorIndexX(-1999.0f));
            Assert.Equal(1, StaticWorld.SectorIndexX(-1960.0f));
            Assert.Equal(99, StaticWorld.SectorIndexX(1999.9f));
            Assert.Equal(99, StaticWorld.SectorIndexX(2000.0f));
            // Out-of-world coordinates clamp instead of indexing outside the grid.
            Assert.Equal(0, StaticWorld.SectorIndexX(-5000.0f));
            Assert.Equal(99, StaticWorld.SectorIndexX(5000.0f));
            Assert.Equal(0, StaticWorld.SectorIndexY(-5000.0f));
            Assert.Equal(99, StaticWorld.SectorIndexY(5000.0f));

            Assert.Near(-2000.0f, StaticWorld.SectorOriginX(0));
            Assert.Near(-1960.0f, StaticWorld.SectorOriginX(1));
            Assert.Near(1960.0f, StaticWorld.SectorOriginX(99));
            Assert.Near(-1960.0f, StaticWorld.SectorOriginY(1));
            Assert.Throws<ArgumentOutOfRangeException>(delegate { StaticWorld.SectorOriginX(100); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { StaticWorld.SectorOriginY(-1); });
        }

        [Test]
        public void SpanningInstanceSitsInMainAndEveryOverlapSectorOnce()
        {
            StaticInstance big;
            StaticWorld world = CreateWorld(60.0f, out big);
            // A 120x120 box centred on the origin covers sectors (49,49)..(51,51).
            int middleX = StaticWorld.SectorIndexX(big.WorldBounds.Min.X +
                (big.WorldBounds.Max.X - big.WorldBounds.Min.X) * 0.5f);
            int middleY = StaticWorld.SectorIndexY(big.WorldBounds.Min.Y +
                (big.WorldBounds.Max.Y - big.WorldBounds.Min.Y) * 0.5f);
            Assert.Equal(50, middleX);
            Assert.Equal(50, middleY);
            Assert.Equal(1, Count(world.GetSectorList(50, 50, false)));
            Assert.Equal(null, world.GetSectorList(50, 50, true));
            Assert.Equal(1, Count(world.GetSectorList(48, 48, true)));
            Assert.Equal(1, Count(world.GetSectorList(51, 51, true)));
            Assert.Equal(null, world.GetSectorList(48, 48, false));
            Assert.Equal(null, world.GetSectorList(47, 47, true));

            // The query must return the spanning instance once despite nine sector entries.
            List<StaticInstance> results = new List<StaticInstance>();
            Assert.Equal(1, world.QuerySectors(Vector3.Zero, 10.0f, results));
            Assert.True(ReferenceEquals(big, results[0]));
        }

        [Test]
        public void SectorQueryMatchesTheLinearScanForSeveralCenters()
        {
            StaticInstance big;
            StaticWorld world = CreateWorld(60.0f, out big);
            AddPointWorlds(world);
            List<StaticInstance> sector = new List<StaticInstance>();
            List<StaticInstance> linear = new List<StaticInstance>();
            Vector3[] centers = new Vector3[]
            {
                Vector3.Zero,
                new Vector3(100.0f, 100.0f, 0.0f),
                new Vector3(-1990.0f, -1990.0f, 0.0f),
                new Vector3(1990.0f, 1990.0f, 0.0f),
                new Vector3(5000.0f, 0.0f, 0.0f),
                new Vector3(-5000.0f, -5000.0f, 0.0f)
            };
            for (int i = 0; i < centers.Length; i++)
            {
                float radius = i == 0 ? 10.0f : 390.0f;
                int expected = world.QuerySectorsLinear(centers[i], radius, linear);
                int actual = world.QuerySectors(centers[i], radius, sector);
                Assert.Equal(expected, actual);
                Assert.Equal(expected, sector.Count);
                HashSet<StaticInstance> set = new HashSet<StaticInstance>(sector);
                for (int j = 0; j < linear.Count; j++)
                {
                    if (!set.Contains(linear[j]))
                        Assert.Fail("Sector query dropped an instance from the linear scan.");
                }
                // Repeated queries must not leak scan codes into the result.
                Assert.Equal(actual, world.QuerySectors(centers[i], radius, sector));
                Assert.Equal(expected, linear.Count);
            }
        }

        [Test]
        public void SectorQueryClearsTheResultAndRejectsBadArguments()
        {
            StaticInstance small;
            StaticWorld world = CreateWorld(1.0f, out small);
            List<StaticInstance> results = new List<StaticInstance>();
            results.Add(small);
            Assert.Equal(1, world.QuerySectors(Vector3.Zero, 5.0f, results));
            Assert.Equal(1, results.Count);
            Assert.Equal(0, world.QuerySectors(new Vector3(1000.0f, 1000.0f, 0.0f), 5.0f, results));
            Assert.Equal(0, results.Count);

            Assert.Throws<ArgumentNullException>(delegate { world.QuerySectors(Vector3.Zero, 5.0f, null); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { world.QuerySectors(Vector3.Zero, 0.0f, results); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                world.QuerySectors(Vector3.Zero, float.NaN, results);
            });
            Assert.Throws<ArgumentNullException>(delegate { world.QuerySectorsLinear(Vector3.Zero, 5.0f, null); });
        }

        [Test]
        public void InstancesOutsideTheGridAreAlwaysCandidates()
        {
            ModelRegistry registry = CreateRegistry(1.0f);
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "1, ground, 3000, 3000, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "1, ground, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "end\n"), registry, new LoadReport());
            Assert.Equal(2, world.Count);
            Assert.Equal(1, world.UnindexedCount);

            List<StaticInstance> results = new List<StaticInstance>();
            Assert.Equal(2, world.QuerySectors(Vector3.Zero, 10.0f, results));
            Assert.Equal(2, world.QuerySectorsLinear(Vector3.Zero, 10.0f, results));
        }

        [Test]
        public void SectorQueryReturnsTheExpectedMembershipForAKnownScene()
        {
            StaticInstance big;
            StaticWorld world = CreateWorld(60.0f, out big);
            AddPointWorlds(world);
            Assert.Equal(4, world.Count);
            StaticInstance firstMarker = world.Instances[1];
            StaticInstance secondMarker = world.Instances[2];
            StaticInstance cornerMarker = world.Instances[3];

            List<StaticInstance> results = new List<StaticInstance>();
            // A 10 unit query only covers the box that spans the origin.
            Assert.Equal(1, world.QuerySectors(Vector3.Zero, 10.0f, results));
            Assert.True(ReferenceEquals(big, results[0]));

            // 100 units reach both nearby markers but not the corner one.
            Assert.Equal(3, world.QuerySectors(Vector3.Zero, 100.0f, results));
            Assert.True(Contains(results, big));
            Assert.True(Contains(results, firstMarker));
            Assert.True(Contains(results, secondMarker));
            Assert.False(Contains(results, cornerMarker));

            // The corner marker is only reachable from its own sector.
            Assert.Equal(1, world.QuerySectors(new Vector3(1999.0f, 1999.0f, 0.0f), 10.0f, results));
            Assert.True(ReferenceEquals(cornerMarker, results[0]));
        }

        [Test]
        public void SeamInstanceOnTheLastScannedEdgeIsNotACandidate()
        {
            // A point instance exactly on a sector seam belongs to the next cell. A query
            // whose covered square ends on that seam must exclude it in both code paths;
            // a strict coordinate comparison would return it as an "extra".
            StaticInstance seam;
            StaticWorld world = CreatePointWorld(new Vector3(-1000.0f, 0.0f, 0.0f), out seam);
            List<StaticInstance> sector = new List<StaticInstance>();
            List<StaticInstance> linear = new List<StaticInstance>();
            // center + radius = -1019, so the covered square ends exactly at -1000.
            Vector3 center = new Vector3(-1059.0f, 0.0f, 0.0f);
            Assert.Equal(0, world.QuerySectors(center, 40.0f, sector));
            Assert.Equal(0, world.QuerySectorsLinear(center, 40.0f, linear));
            Assert.Equal(25, StaticWorld.SectorIndexX(-1000.0f));

            // Just inside the scanned cell the instance is a candidate in both paths.
            StaticInstance inside;
            StaticWorld inner = CreatePointWorld(new Vector3(-1000.001f, 0.0f, 0.0f), out inside);
            Assert.Equal(1, inner.QuerySectors(center, 40.0f, sector));
            Assert.Equal(1, inner.QuerySectorsLinear(center, 40.0f, linear));

            // An instance exactly on the first scanned edge is inside the candidate square.
            StaticInstance first;
            StaticWorld left = CreatePointWorld(new Vector3(-1120.0f, 0.0f, 0.0f), out first);
            Assert.Equal(22, StaticWorld.SectorIndexX(-1120.0f));
            Assert.Equal(1, left.QuerySectors(center, 40.0f, sector));
            Assert.Equal(1, left.QuerySectorsLinear(center, 40.0f, linear));
        }

        [Test]
        public void VerticalQueryFindsInstancesThroughTheOverlapList()
        {
            StaticInstance big;
            StaticWorld world = CreateWorld(60.0f, out big);
            // The box spans cells 48..51 and its middle cell is (50,50). A vertical query in
            // the (51,50) corner cell only resolves through the overlap list.
            Assert.Equal(1, Count(world.GetSectorList(51, 50, true)));
            Assert.Equal(null, world.GetSectorList(51, 50, false));
            StaticWorldHit hit;
            Assert.True(world.QueryVertical(60.0f, 0.0f, 10.0f, -10.0f, out hit));
            Assert.True(ReferenceEquals(big, hit.Instance));
            Assert.Near(1.0f, hit.Position.Z, 0.001f);
        }

        [Test]
        public void CollisionArrivingAfterBindRefreshesTheSectorGrid()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            CollisionRepository collisions = new CollisionRepository(registry);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 500, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 100, 100, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            List<StaticInstance> results = new List<StaticInstance>();
            Vector3 center = new Vector3(100.0f, 300.0f, 0.0f);
            // Before COL arrives the instance is a point at (100, 100).
            Assert.Equal(0, world.QuerySectors(center, 10.0f, results));
            Assert.Equal(0, world.QuerySectorsLinear(center, 10.0f, results));

            collisions.Add(WorldTestAssets.CreateBoxCollision("ground", 200.0f, -1.0f, 1.0f), 0);
            // The sector query refreshes bounds before reading the grid.
            Assert.Equal(1, world.QuerySectors(center, 10.0f, results));
            Assert.Near(300.0f, world.Instances[0].WorldBounds.Max.Y, 0.001f);
            Assert.Equal(1, world.QuerySectorsLinear(center, 10.0f, results));
        }

        [Test]
        public void UnindexedInstancesStayOutOfSectorListsButInsideQueries()
        {
            ModelRegistry registry = CreateRegistry(1.0f);
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "1, ground, 3000, 3000, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "1, ground, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "end\n"), registry, new LoadReport());
            Assert.Equal(1, world.UnindexedCount);
            Assert.Equal(null, world.GetSectorList(99, 99, false));
            List<StaticInstance> results = new List<StaticInstance>();
            Assert.Equal(2, world.QuerySectors(Vector3.Zero, 10.0f, results));
            Assert.True(Contains(results, world.Instances[0]));

            // Sector lists are read-only views.
            IList<StaticInstance> main = world.GetSectorList(50, 50, false);
            Assert.NotNull(main);
            Assert.Throws<NotSupportedException>(delegate { main.Add(world.Instances[0]); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { world.GetSectorList(100, 0, false); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { world.GetSectorList(0, -1, true); });
        }

        [Test]
        public void SectorQueriesHandleInvalidCentersAndRadii()
        {
            StaticInstance unused;
            StaticWorld world = CreateWorld(1.0f, out unused);
            List<StaticInstance> results = new List<StaticInstance>();
            Assert.Equal(0, world.QuerySectors(new Vector3(float.NaN, 0.0f, 0.0f), 5.0f, results));
            Assert.Equal(0, world.QuerySectors(new Vector3(0.0f, float.PositiveInfinity, 0.0f), 5.0f, results));
            Assert.Equal(0, world.QuerySectorsLinear(new Vector3(float.NaN, 0.0f, 0.0f), 5.0f, results));
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                world.QuerySectors(Vector3.Zero, float.PositiveInfinity, results);
            });
            // A huge coordinate must clamp, not overflow the cell conversion.
            Assert.Equal(0, StaticWorld.SectorIndexX(float.NegativeInfinity));
            Assert.Equal(99, StaticWorld.SectorIndexX(float.PositiveInfinity));
        }

        private static StaticWorld CreatePointWorld(Vector3 position, out StaticInstance instance)
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n3, marker, generic, 1, 500, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n3, marker, " + position.X.ToString(CultureInfo.InvariantCulture) + ", " +
                position.Y.ToString(CultureInfo.InvariantCulture) + ", 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            instance = world.Instances[0];
            return world;
        }

        private static bool Contains(IList<StaticInstance> list, StaticInstance instance)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], instance))
                    return true;
            }
            return false;
        }

        private static int Count(IList<StaticInstance> list)
        {
            return list == null ? 0 : list.Count;
        }

        private static ModelRegistry CreateRegistry(float halfExtent)
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            CollisionRepository collisions = new CollisionRepository(registry);
            collisions.Add(WorldTestAssets.CreateBoxCollision("ground", halfExtent, -1.0f, 1.0f), 0);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 500, 0\nend\n"));
            return registry;
        }

        private static StaticWorld CreateWorld(float halfExtent, out StaticInstance instance)
        {
            ModelRegistry registry = CreateRegistry(halfExtent);
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            instance = world.Instances[0];
            return world;
        }

        private static void AddPointWorlds(StaticWorld world)
        {
            // Point bounds (no collision) exercise the main/overlap bookkeeping for
            // instances that land in exactly one sector.
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n2, marker, generic, 1, 500, 0\nend\n"));
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "2, marker, 100, 100, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "2, marker, -100, -100, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "2, marker, 1999, 1999, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "end\n"), registry, new LoadReport());
        }
    }
}