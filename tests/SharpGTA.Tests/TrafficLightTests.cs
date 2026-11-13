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
    /// Junction lights: phase timings, the light-to-phase angle test, and the follower stopping
    /// on the stop line instead of rolling through a red.
    /// </summary>
    public sealed class TrafficLightTests
    {
        [Test]
        public void PhaseTimingsMatchTheOriginal()
        {
            // LightForCars1: green 5 s, yellow 1 s, red until the 16 s cycle restarts.
            Assert.Equal(TrafficLightPhase.Green, TrafficLights.PhaseCars1(0));
            Assert.Equal(TrafficLightPhase.Green, TrafficLights.PhaseCars1(4999));
            Assert.Equal(TrafficLightPhase.Yellow, TrafficLights.PhaseCars1(5000));
            Assert.Equal(TrafficLightPhase.Yellow, TrafficLights.PhaseCars1(5999));
            Assert.Equal(TrafficLightPhase.Red, TrafficLights.PhaseCars1(6000));
            Assert.Equal(TrafficLightPhase.Red, TrafficLights.PhaseCars1(16383));
            Assert.Equal(TrafficLightPhase.Green, TrafficLights.PhaseCars1(16384));

            // LightForCars2: red 6 s, green 5 s, yellow 1 s, red.
            Assert.Equal(TrafficLightPhase.Red, TrafficLights.PhaseCars2(0));
            Assert.Equal(TrafficLightPhase.Red, TrafficLights.PhaseCars2(5999));
            Assert.Equal(TrafficLightPhase.Green, TrafficLights.PhaseCars2(6000));
            Assert.Equal(TrafficLightPhase.Green, TrafficLights.PhaseCars2(10999));
            Assert.Equal(TrafficLightPhase.Yellow, TrafficLights.PhaseCars2(11000));
            Assert.Equal(TrafficLightPhase.Yellow, TrafficLights.PhaseCars2(11999));
            Assert.Equal(TrafficLightPhase.Red, TrafficLights.PhaseCars2(12000));
        }

        [Test]
        public void SignalTypeUsesTheAngleBands()
        {
            // FindTrafficLightType: a light facing roughly east or west (60..150 or 240..330
            // degrees, measured with atan2(y, x)) governs phase 1.
            Assert.Equal(1, TrafficLights.SignalType(new Vector3(0.0f, 1.0f, 0.0f)));
            Assert.Equal(1, TrafficLights.SignalType(new Vector3(0.0f, -1.0f, 0.0f)));
            Assert.Equal(2, TrafficLights.SignalType(new Vector3(1.0f, 0.0f, 0.0f)));
            Assert.Equal(2, TrafficLights.SignalType(new Vector3(-1.0f, 0.0f, 0.0f)));
            Assert.Equal(2, TrafficLights.SignalType(new Vector3(1.0f, 1.0f, 0.0f)));
        }

        [Test]
        public void CarStopsOnTheStopLineAndResumesWhenTheLightTurnsGreen()
        {
            PathGraph graph = LongChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = SpawnVehicle(world, registry);
            // The car drives toward node 0, so the link between nodes 1 and 2 is the one it must
            // obey: the light sits at the link midpoint (x = 45), eight units before it.
            int link = graph.Nodes[2].FindLink(1);
            int owner = graph.Nodes[2].LinkOwnerNode(link);
            graph.Nodes[2].SetCarLinkTrafficLight(link, 2, owner != 1);
            Assert.Equal(1, graph.CarLinkTrafficLightCount);
            Assert.Equal(1, graph.CarLinkTrafficLightType2Count);

            PathFollowerModule module = new PathFollowerModule(graph, world);
            module.ObeyTrafficLights = true;
            Assert.True(module.Assign(vehicle, new int[] { 3, 2, 1, 0 }, 30.0f));

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(37);
            // Phase 2 is red for the first six seconds of every cycle, so the car sits exactly on
            // the stop line instead of entering the junction.
            Assert.Near(53.0f, vehicle.Position.X, 0.01f);
            Assert.True(module.TrafficHoldCount > 0);
            Assert.Equal(0, module.CompletedCount);

            // Long enough to see green (tick 180), clear the junction and finish the route.
            runtime.RunTicks(400);
            Assert.True(module.TrafficHoldCount > 100);
            Assert.Equal(1, module.CompletedCount);
            Assert.Near(0.0f, vehicle.Position.X, 0.01f);
        }

        [Test]
        public void CarIgnoresLightsWhenTheFollowerDoesNotObeyThem()
        {
            PathGraph graph = LongChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity vehicle = SpawnVehicle(world, registry);
            int link = graph.Nodes[2].FindLink(1);
            graph.Nodes[2].SetCarLinkTrafficLight(link, 2, graph.Nodes[2].LinkOwnerNode(link) != 1);

            PathFollowerModule module = new PathFollowerModule(graph, world);
            Assert.False(module.ObeyTrafficLights);
            Assert.True(module.Assign(vehicle, new int[] { 3, 2, 1, 0 }, 30.0f));
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(120);
            Assert.Equal(1, module.CompletedCount);
            Assert.Equal(0, module.TrafficHoldCount);
        }

        [Test]
        public void CarsQueueBehindEachOtherAtARedLight()
        {
            PathGraph graph = LongChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            int link = graph.Nodes[2].FindLink(1);
            graph.Nodes[2].SetCarLinkTrafficLight(link, 2, graph.Nodes[2].LinkOwnerNode(link) != 1);

            DynamicEntity ahead = SpawnVehicle(world, registry);
            DynamicEntity behind = SpawnVehicle(world, registry);
            PathFollowerModule module = new PathFollowerModule(graph, world);
            module.ObeyTrafficLights = true;
            module.VehicleGap = 6.0f;
            // The leader starts one node closer to the junction (x = 60) and reaches the stop line
            // (x = 53) almost at once; the follower comes from x = 90 and must queue behind it.
            Assert.True(module.Assign(ahead, new int[] { 2, 1, 0 }, 30.0f));
            Assert.True(module.Assign(behind, new int[] { 3, 2, 1, 0 }, 30.0f));

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(100);
            Assert.Near(53.0f, ahead.Position.X, 0.01f);
            Assert.Near(59.0f, behind.Position.X, 0.01f);
            Assert.Near(6.0f, behind.Position.X - ahead.Position.X, 0.01f);
            Assert.True(module.TrafficHoldCount > 0);

            // Green arrives at tick 180: both clear the junction and finish their routes.
            runtime.RunTicks(600);
            Assert.Equal(2, module.CompletedCount);
        }

        [Test]
        public void WithoutAGapCarsShareTheStopLine()
        {
            PathGraph graph = LongChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            int link = graph.Nodes[2].FindLink(1);
            graph.Nodes[2].SetCarLinkTrafficLight(link, 2, graph.Nodes[2].LinkOwnerNode(link) != 1);

            DynamicEntity ahead = SpawnVehicle(world, registry);
            DynamicEntity behind = SpawnVehicle(world, registry);
            PathFollowerModule module = new PathFollowerModule(graph, world);
            module.ObeyTrafficLights = true;
            Assert.Equal(0.0f, module.VehicleGap);
            Assert.True(module.Assign(ahead, new int[] { 2, 1, 0 }, 30.0f));
            Assert.True(module.Assign(behind, new int[] { 3, 2, 1, 0 }, 30.0f));

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(100);
            // Without car following the second car stands on the same stop line: this is the
            // overlap the gap exists to prevent, kept as an explicit baseline.
            Assert.Near(53.0f, ahead.Position.X, 0.01f);
            Assert.Near(53.0f, behind.Position.X, 0.01f);
        }

        [Test]
        public void PedWaitsAtALightControlledCrossing()
        {
            PathGraph graph = PedChainGraph();
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity ped = SpawnPed(world, registry);
            // Flag the first crossing the way a light scan does; the ped must wait on the node
            // until the walk phase instead of stepping onto the road.
            int link = graph.Nodes[0].FindLink(1);
            graph.Nodes[0].SetLinkPedTrafficLight(link, true);
            Assert.Equal(1, graph.PedTrafficLightLinkCount);

            PathFollowerModule module = new PathFollowerModule(graph, world);
            module.ObeyTrafficLights = true;
            Assert.True(module.Assign(ped, new int[] { 0, 1, 2 }, 1.5f));
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);

            // Ticks 0..360 are the 12 second "don't walk" window.
            runtime.RunTicks(100);
            Assert.Near(0.0f, ped.Position.X, 0.01f);
            Assert.True(module.TrafficHoldCount > 0);

            // Walk starts at 12 s (tick 361).
            runtime.RunTicks(300);
            Assert.True(ped.Position.X > 1.0f);

            // The next don't-walk window must not freeze a ped that already stepped out.
            float committed = ped.Position.X;
            runtime.RunTicks(300);
            Assert.True(ped.Position.X > committed);
        }

        [Test]
        public void PedLightPhaseTimingsMatchTheOriginal()
        {
            Assert.Equal(TrafficLights.PedPhase.DontWalk, TrafficLights.PhasePeds(0));
            Assert.Equal(TrafficLights.PedPhase.DontWalk, TrafficLights.PhasePeds(11999));
            Assert.Equal(TrafficLights.PedPhase.Walk, TrafficLights.PhasePeds(12000));
            Assert.Equal(TrafficLights.PedPhase.Walk, TrafficLights.PhasePeds(15383));
            Assert.Equal(TrafficLights.PedPhase.WalkBlink, TrafficLights.PhasePeds(15384));
            Assert.Equal(TrafficLights.PedPhase.DontWalk, TrafficLights.PhasePeds(16384));
        }

        [Test]
        public void LightsAreCachedAndRejectInvalidTypes()
        {
            PathGraph graph = LongChainGraph();
            int link = graph.Nodes[2].FindLink(1);
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.Nodes[2].SetCarLinkTrafficLight(link, 3, false);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.Nodes[2].SetCarLinkTrafficLight(-1, 1, false);
            });
            // Overwriting keeps a single counted link, like the original's shared CCarPathLink.
            graph.Nodes[2].SetCarLinkTrafficLight(link, 1, false);
            graph.Nodes[2].SetCarLinkTrafficLight(link, 2, true);
            Assert.Equal(1, graph.CarLinkTrafficLightCount);
            Assert.Equal(0, graph.CarLinkTrafficLightType1Count);
            Assert.Equal(1, graph.CarLinkTrafficLightType2Count);
            Assert.Equal(1, graph.CarLinkTrafficLightDirectionCount);
            Assert.Equal(2, graph.Nodes[2].LinkTrafficLightType(link));
            Assert.True(graph.Nodes[2].LinkTrafficLightDirection(link));
        }

        /// <summary>Four car nodes ninety units apart, so the stop line at eight units from a
        /// link midpoint still lies inside the segment.</summary>
        private static PathGraph LongChainGraph()
        {
            PathWorld pathWorld = new PathWorld();
            pathWorld.AddGroups(PathRecordParser.ParseGroups(
                new IdeReader(DataSchema.III).Read(
                    "path\ncar, -1, unused\n" +
                    "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                    "2, 2, 0, 480, 0, 0, 160, 1, 1\n" +
                    "2, 3, 0, 960, 0, 0, 160, 1, 1\n" +
                    "2, -1, 0, 1440, 0, 0, 160, 1, 1\n" +
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

        /// <summary>Three ped nodes ten units apart, for the crossing wait test.</summary>
        private static PathGraph PedChainGraph()
        {
            PathWorld pathWorld = new PathWorld();
            pathWorld.AddGroups(PathRecordParser.ParseGroups(
                new IdeReader(DataSchema.III).Read(
                    "path\nped, -1, unused\n" +
                    "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                    "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                    "2, -1, 0, 320, 0, 0, 160, 1, 1\n" +
                    "0, -1, 0, 0, 0, 0, 0, 0, 0\n" +
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

        private static DynamicEntity SpawnVehicle(StaticWorld world, ModelRegistry registry)
        {
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("landstal"),
                Vector3.Zero, Vector3.Zero, out entity));
            return entity;
        }

        private static DynamicEntity SpawnPed(StaticWorld world, ModelRegistry registry)
        {
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("civmale"),
                Vector3.Zero, Vector3.Zero, out entity));
            return entity;
        }
    }
}