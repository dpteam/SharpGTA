using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class PathFollowerTests
    {
        [Test]
        public void VehicleWalksItsRouteAtConstantSpeed()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = SpawnVehicle(world, registry);

            PathFollowerModule module = new PathFollowerModule(graph);
            Assert.True(module.Assign(vehicle, new int[] { 0, 1, 2, 3 }, 10.0f));
            Assert.Equal(1, module.Count);
            AssertVector(Vector3.Zero, vehicle.Position);

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(30);
            // One second at ten units per second: the car is on the first ten-unit segment end.
            Assert.Near(10.0f, vehicle.Position.X, 0.01f);
            Assert.Near(10.0f, module.TravelledDistance, 0.01f);
            Assert.Equal(0, module.CompletedCount);

            runtime.RunTicks(150);
            // Thirty units of route are covered, so the car stops on the last node.
            AssertVector(new Vector3(30.0f, 0.0f, 0.0f), vehicle.Position);
            Assert.Equal(1, module.CompletedCount);
            AssertVector(Vector3.Zero, vehicle.Velocity);
            Assert.Near(30.0f, module.TravelledDistance, 0.01f);

            // Once finished it must not keep drifting.
            runtime.RunTicks(30);
            AssertVector(new Vector3(30.0f, 0.0f, 0.0f), vehicle.Position);
            Assert.Near(30.0f, module.TravelledDistance, 0.01f);
        }

        [Test]
        public void FollowerRejectsInvalidAssignments()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = SpawnVehicle(world, registry);
            PathFollowerModule module = new PathFollowerModule(graph);

            Assert.False(module.Assign(vehicle, new int[] { 0 }, 10.0f));

            Assert.Throws<ArgumentNullException>(delegate { module.Assign(null, new int[] { 0, 1 }, 10.0f); });
            Assert.Throws<ArgumentNullException>(delegate { module.Assign(vehicle, null, 10.0f); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                module.Assign(vehicle, new int[] { 0, 1 }, 0.0f);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                module.Assign(vehicle, new int[] { 0, 99 }, 10.0f);
            });
            Assert.Throws<ArgumentNullException>(delegate { new PathFollowerModule(null); });

            // Objects are not driven along the network.
            DynamicEntity box;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Object, registry.Find("plain"),
                Vector3.Zero, Vector3.Zero, out box));
            Assert.False(module.Assign(box, new int[] { 0, 1 }, 10.0f));
            Assert.Equal(0, module.Count);
        }

        [Test]
        public void VehicleWithoutDrawDistanceIsVisibleWithinTheViewRadius()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 200));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "cars\n130, landstal, landstal, car, LANDSTAL, LANDSTAL, normal, 10, 0, 1a, 0, 1.0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n130, landstal, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            DynamicEntity car;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("landstal"),
                new Vector3(10.0f, 0.0f, 0.0f), Vector3.Zero, out car));
            // Vehicle records carry no IDE draw distance, so the radius is the only limit.
            Assert.Near(0.0f, registry.Find("landstal").LargestLodDistance);
            Assert.True(SharpGTA.Rendering.StaticWorldScene.IsEntityVisible(car, Vector3.Zero, 100.0f));
            Assert.False(SharpGTA.Rendering.StaticWorldScene.IsEntityVisible(car, Vector3.Zero, 5.0f));
        }

        [Test]
        public void TwoFollowersOfTheSameRouteStayDeterministic()
        {
            PathGraph graph = ChainGraph();
            string first = RunRoute(graph, 45);
            string second = RunRoute(graph, 45);
            string different = RunRoute(graph, 46);
            Assert.Equal(first, second);
            Assert.False(first == different);
        }

        private static string RunRoute(PathGraph graph, int ticks)
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = SpawnVehicle(world, registry);
            PathFollowerModule module = new PathFollowerModule(graph);
            module.Assign(vehicle, new int[] { 0, 1, 2, 3 }, 7.5f);
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks((ulong)ticks);
            return runtime.ComputeStateHash();
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
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }

        private static DynamicEntity SpawnVehicle(StaticWorld world, ModelRegistry registry)
        {
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("plain"),
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