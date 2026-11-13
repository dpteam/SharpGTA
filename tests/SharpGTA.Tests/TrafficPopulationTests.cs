using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class TrafficPopulationTests
    {
        [Test]
        public void FinishedVehicleIsDespawnedAndReplacedWithoutGrowingThePopulation()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity first = SpawnVehicle(world, registry);
            int firstHandle = first.Handle;

            PathFollowerModule followers = new PathFollowerModule(graph, world);
            Assert.True(followers.Assign(first, new int[] { 0, 1, 2, 3 }, 10.0f));

            TrafficPopulationModule population =
                new TrafficPopulationModule(world, graph, followers);
            population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                registry.Find("landstal"), 10.0f, 1);
            // The cohort adopts the actor the probe already spawned, so nothing is doubled.
            Assert.Equal(0, population.SpawnedCount);

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(followers);
            runtime.AddModule(population);
            runtime.RunTicks(1);
            Assert.Equal(0, population.SpawnedCount);
            Assert.Equal(1, followers.Count);

            // Thirty units at ten units per second: the route ends after roughly ninety ticks.
            runtime.RunTicks(120);
            Assert.Equal(1, population.RecycledCount);
            Assert.Equal(1, population.DespawnedCount);
            Assert.Equal(1, population.SpawnedCount);
            Assert.Equal(1, followers.Count);
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Vehicle));

            WorldEntity stale;
            Assert.False(world.Entities.TryGet(WorldEntityKind.Vehicle, firstHandle, out stale));
            Assert.True(world.DynamicEntities.Count == 1);
            Assert.False(ReferenceEquals(first, world.DynamicEntities[0]));

            TrafficCohortStatus cohort = population.Cohorts[0];
            Assert.Equal(1, cohort.LiveCount);
            Assert.Equal(1, cohort.TargetCount);
            Assert.Equal(WorldEntityKind.Vehicle, cohort.EntityKind);
            Assert.Equal(PathKind.Car, cohort.PathKind);
        }

        [Test]
        public void KindsWithoutACohortStayParkedInsteadOfBeingRecycled()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            // Only vehicles are recycled: a ped that finishes must keep standing where it stopped
            // and stay owned by the follower, because nothing would replace it.
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "peds\n7, civmale, civmale, CIVMALE, STAT_PLAYER, ped, 0\nend\n"));
            DynamicEntity ped;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("civmale"),
                Vector3.Zero, Vector3.Zero, out ped));

            PathFollowerModule followers = new PathFollowerModule(graph, world);
            Assert.True(followers.Assign(ped, new int[] { 0, 1 }, 10.0f));

            TrafficPopulationModule population =
                new TrafficPopulationModule(world, graph, followers);
            population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                registry.Find("landstal"), 10.0f, 0);
            Assert.True(followers.RecycleVehicles);
            Assert.False(followers.RecyclePeds);

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(followers);
            runtime.AddModule(population);
            runtime.RunTicks(60);

            Assert.Equal(1, followers.CompletedCount);
            Assert.Equal(1, followers.Count);
            Assert.Equal(0, followers.RetiredCount);
            Assert.Equal(0, population.RecycledCount);
            Assert.Equal(0, population.DespawnedCount);
            AssertVector(new Vector3(10.0f, 0.0f, 0.0f), ped.Position);
        }

        [Test]
        public void RecyclingKeepsTheTargetAndStaysDeterministic()
        {
            string first = RunRecycling(600);
            string second = RunRecycling(600);
            Assert.Equal(first, second);

            // A different tick count must produce different state, otherwise the hash ignores
            // the replacements entirely.
            Assert.False(first == RunRecycling(700));
        }

        private static string RunRecycling(int ticks)
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            PathFollowerModule followers = new PathFollowerModule(graph, world);
            DynamicEntity a = SpawnVehicle(world, registry);
            DynamicEntity b = SpawnVehicle(world, registry);
            followers.Assign(a, new int[] { 0, 1, 2, 3 }, 12.0f);
            followers.Assign(b, new int[] { 0, 1, 2, 3 }, 9.0f);

            TrafficPopulationModule population =
                new TrafficPopulationModule(world, graph, followers);
            population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                registry.Find("landstal"), 12.0f, 2);

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(followers);
            runtime.AddModule(population);
            runtime.RunTicks((ulong)ticks);
            if (followers.Count != 2 || world.Entities.Count(WorldEntityKind.Vehicle) != 2)
                throw new InvalidOperationException("The population did not stay at its target.");
            return "travelled=" + followers.TravelledDistance.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture) +
                " spawned=" + population.SpawnedCount +
                " despawned=" + population.DespawnedCount +
                " hash=" + runtime.ComputeStateHash();
        }

        [Test]
        public void OnlyOneCohortPerEntityKindIsAllowed()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            PathFollowerModule followers = new PathFollowerModule(graph, world);
            TrafficPopulationModule population =
                new TrafficPopulationModule(world, graph, followers);
            population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                registry.Find("landstal"), 10.0f, 2);
            Assert.Throws<ArgumentException>(delegate
            {
                population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                    registry.Find("landstal"), 10.0f, 2);
            });
            Assert.Throws<ArgumentException>(delegate
            {
                population.AddCohort(PathKind.Car, WorldEntityKind.Object,
                    registry.Find("landstal"), 10.0f, 2);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                new TrafficPopulationModule(null, graph, followers);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                population.AddCohort(PathKind.Car, WorldEntityKind.Ped, null, 10.0f, 2);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                population.AddCohort(PathKind.Car, WorldEntityKind.Ped,
                    registry.Find("landstal"), 0.0f, 2);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                population.AddCohort(PathKind.Car, WorldEntityKind.Ped,
                    registry.Find("landstal"), 10.0f, -1);
            });
        }

        private static PathGraph ChainGraph()
        {
            PathWorld pathWorld = new PathWorld();
            pathWorld.AddGroups(PathRecordParser.ParseGroups(
                new IdeReader(DataSchema.III).Read(
                    "path\ncar, -1, unused\n" +
                    "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                    "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                    "2, 3, 0, 320, 0, 0, 160, 1, 1\n" +
                    "2, -1, 0, 480, 0, 0, 160, 1, 1\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
                    "end\n", "iii-paths.ide").RawRecords,
                DataSchema.III, "iii-paths.ide"));
            return new PathGraph(pathWorld);
        }

        private static StaticWorld CreateWorld(out ModelRegistry registry)
        {
            registry = new ModelRegistry(new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n" +
                "cars\n20, landstal, landstal, car, LANDSTAL, LANDSTAL, normal, 10, 0, 1a, 0, 1.0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }

        private static DynamicEntity SpawnVehicle(StaticWorld world, ModelRegistry registry)
        {
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("landstal"),
                Vector3.Zero, Vector3.Zero, out entity));
            return entity;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X, 0.01f);
            Assert.Near(expected.Y, actual.Y, 0.01f);
            Assert.Near(expected.Z, actual.Z, 0.01f);
        }
    }
}