using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    /// <summary>
    /// `CCarPathLink` lane geometry: cars drive beside the path line, not on it, and the side
    /// depends on the travel direction so opposing traffic never shares one line.
    /// </summary>
    public sealed class CarLaneTests
    {
        /// <summary>1/1 lane fallback on the synthetic chain, so half a lane is 2.5 units.</summary>
        private const float HalfLane = 2.5f;

        [Test]
        public void LaneFormulasMatchTheOriginal()
        {
            // CCarPathLink::OneWayLaneOffset: centred lanes on a one-way link, travel side on two.
            Assert.Near(0.0f, CarLanes.OneWayLaneOffset(0, 1));
            Assert.Near(-0.5f, CarLanes.OneWayLaneOffset(0, 2));
            Assert.Near(-0.5f, CarLanes.OneWayLaneOffset(2, 0));
            Assert.Near(0.5f, CarLanes.OneWayLaneOffset(1, 1));
            Assert.Near(0.5f, CarLanes.OneWayLaneOffset(3, 2));
            // LanesInDirection: +1 travels toward the lower node index and uses the left lanes.
            Assert.Equal(2, CarLanes.LanesInDirection(2, 3, 1));
            Assert.Equal(3, CarLanes.LanesInDirection(2, 3, -1));
            // Offset is the right-hand normal times lane units times LANE_WIDTH (5).
            AssertVector(new Vector3(0.0f, -HalfLane, 0.0f),
                CarLanes.Offset(new Vector3(1.0f, 0.0f, 0.0f), 0.5f));
            AssertVector(new Vector3(5.0f, 0.0f, 0.0f),
                CarLanes.Offset(new Vector3(0.0f, 1.0f, 0.0f), 1.0f));
            Assert.Near(5.0f, CarLanes.LaneWidth);
        }

        [Test]
        public void OpposingCarsDriveOnOppositeSidesOfTheLine()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity forward = Spawn(world, registry, WorldEntityKind.Vehicle, "landstal");
            DynamicEntity backward = Spawn(world, registry, WorldEntityKind.Vehicle, "landstal");

            PathFollowerModule module = new PathFollowerModule(graph, world);
            Assert.True(module.Assign(forward, new int[] { 0, 1, 2, 3 }, 10.0f));
            Assert.True(module.Assign(backward, new int[] { 3, 2, 1, 0 }, 10.0f));

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(30);

            // Both cars are between the same pair of nodes, on opposite sides of the line.
            Assert.Near(10.0f, forward.Position.X, 0.01f);
            Assert.Near(20.0f, backward.Position.X, 0.01f);
            Assert.Near(-HalfLane, forward.Position.Y, 0.01f);
            Assert.Near(HalfLane, backward.Position.Y, 0.01f);
            Assert.True(Math.Abs(forward.Position.Y + backward.Position.Y) < 0.01f);
        }

        [Test]
        public void PedsWalkOnTheNodeLineBecauseTheyHaveNoCarLinks()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity ped = Spawn(world, registry, WorldEntityKind.Ped, "civmale");

            PathFollowerModule module = new PathFollowerModule(graph, world);
            Assert.True(module.Assign(ped, new int[] { 0, 1, 2 }, 10.0f));
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(45);

            // 45 ticks at ten units per second: fifteen units along the chain, exactly on the line.
            Assert.Near(15.0f, ped.Position.X, 0.01f);
            Assert.Near(0.0f, ped.Position.Y, 0.01f);
        }

        [Test]
        public void LateralOffsetIsStableForTheSameSpawn()
        {
            // The lane comes from the entity handle, not from wall time, so two identical runs
            // put the car in the same place.
            float first = RunToLaneY();
            float second = RunToLaneY();
            Assert.Near(first, second);
            Assert.Near(-HalfLane, first);
        }

        private static float RunToLaneY()
        {
            PathGraph graph = ChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = Spawn(world, registry, WorldEntityKind.Vehicle, "landstal");
            PathFollowerModule module = new PathFollowerModule(graph, world);
            module.Assign(vehicle, new int[] { 0, 1, 2, 3 }, 10.0f);
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(30);
            return vehicle.Position.Y;
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
                "cars\n20, landstal, landstal, car, LANDSTAL, LANDSTAL, normal, 10, 0, 1a, 0, 1.0\nend\n" +
                "peds\n7, civmale, civmale, CIVMALE, STAT_PLAYER, ped, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }

        private static DynamicEntity Spawn(StaticWorld world, ModelRegistry registry,
            WorldEntityKind kind, string model)
        {
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(kind, registry.Find(model), Vector3.Zero,
                Vector3.Zero, out entity));
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