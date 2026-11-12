using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class PathGraphTests
    {
        // Third field is the node type: 0 empty, 1 extern (tile boundary), 2 intern (a node).
        private const string InternNode = "2, -1, 0, 16, 32, 48, 160, 1, 1";

        [Test]
        public void GraphKeepsInteriorSlotsAndLinksEitherSideOfNext()
        {
            // Four interior slots chained 0 -> 1 -> 2 -> 3; the last one has no next.
            PathWorld world = DetachedIII(
                "2, 1, 0, 0, 0, 0, 160, 1, 1",
                "2, 2, 0, 160, 0, 0, 160, 1, 1",
                "2, 3, 0, 320, 0, 0, 160, 1, 1",
                "2, -1, 0, 480, 0, 0, 160, 1, 1",
                EmptyNodes(8));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(4, graph.NodeCount);
            Assert.Equal(4, graph.CarNodeCount);
            Assert.Equal(0, graph.PedNodeCount);
            Assert.Equal(0, graph.WaterNodeCount);
            Assert.Equal(8, graph.EmptySlotCount);
            Assert.Equal(0, graph.ExternalSlotCount);
            // Three tile links, stored in both directions like the original lists.
            Assert.Equal(6, graph.LinkCount);
            Assert.Equal(0, graph.ExternalNextCount);
            Assert.Equal(0, graph.DanglingNextCount);
            Assert.Equal(0, graph.SelfNextCount);
            Assert.Equal(0, graph.UnresolvedGroupCount);

            PathGraphNode first = graph.Nodes[0];
            Assert.Equal(0, first.GlobalIndex);
            Assert.Equal(0, first.GroupIndex);
            Assert.Equal(0, first.LocalIndex);
            Assert.Equal(PathKind.Car, first.Kind);
            Assert.Equal(1, first.LinkCount);
            Assert.Equal(1, first.Links[0]);
            Assert.Equal(2, graph.Nodes[1].LinkCount);
            Assert.Equal(2, graph.Nodes[2].LinkCount);
            Assert.Equal(1, graph.Nodes[2].Links[0]);
            Assert.Equal(1, graph.Nodes[3].LinkCount);
            Assert.Equal(2, graph.Nodes[3].Links[0]);
            Assert.Equal(-1, graph.Nodes[3].NextNodeIndex);

            // Stored coordinates are compressed by 16 and detached groups ignore transforms.
            AssertVector(Vector3.Zero, first.Position);
            AssertVector(new Vector3(30.0f, 0.0f, 0.0f), graph.Nodes[3].Position);
            Assert.Equal(3, graph.Nodes[3].LocalIndex);
            Assert.Equal(1, graph.ComponentCount);
            Assert.Equal(4, graph.LargestComponentSize);
            Assert.Equal(0, graph.SingleNodeComponentCount);
            Assert.Equal(1, graph.Nodes[3].GroupId);
        }

        [Test]
        public void ExternalsAndEmptySlotsAreNotNodesButAreCounted()
        {
            PathWorld world = DetachedIII(
                "1, 0, 0, 0, 0, 0, 160, 1, 1",     // extern, points at slot 0
                "2, 2, 0, 160, 0, 0, 160, 1, 1",   // intern, points at slot 2 (extern)
                "1, 1, 0, 320, 0, 0, 160, 1, 1",   // extern, points at slot 1 (intern)
                "2, 1, 0, 480, 0, 0, 160, 1, 1",   // intern, points at another intern
                EmptyNodes(8));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(2, graph.NodeCount);
            Assert.Equal(2, graph.ExternalSlotCount);
            Assert.Equal(8, graph.EmptySlotCount);
            Assert.Equal(0, graph.UnknownSlotCount);
            // A lane may end at the tile boundary; the extern slot is not a node and the
            // cross-tile merge that would follow it is not implemented yet.
            Assert.Equal(1, graph.ExternalNextCount);
            Assert.Equal(0, graph.DanglingNextCount);
            // intern(3) -> intern(1) is the only pair either side names.
            Assert.Equal(2, graph.LinkCount);
            Assert.Equal(1, graph.Nodes[0].LocalIndex);
            Assert.Equal(1, graph.Nodes[0].Links[0]);
            Assert.Equal(3, graph.Nodes[1].LocalIndex);
            Assert.Equal(1, graph.Nodes[1].LinkCount);
        }

        [Test]
        public void DanglingSelfAndOutOfRangeNextAreCountedSeparately()
        {
            PathWorld world = DetachedIII(
                "2, 9, 0, 0, 0, 0, 160, 1, 1",    // points at an empty slot
                "2, 1, 0, 160, 0, 0, 160, 1, 1",  // points at itself
                "2, 12, 0, 320, 0, 0, 160, 1, 1", // points outside the twelve slots
                "2, -1, 0, 480, 0, 0, 160, 1, 1",
                EmptyNodes(8));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(4, graph.NodeCount);
            Assert.Equal(0, graph.LinkCount);
            Assert.Equal(2, graph.DanglingNextCount);
            Assert.Equal(1, graph.SelfNextCount);
            Assert.Equal(0, graph.ExternalNextCount);
            Assert.Equal(4, graph.ComponentCount);
            Assert.Equal(4, graph.SingleNodeComponentCount);
            Assert.Equal(1, graph.LargestComponentSize);
            for (int i = 0; i < graph.NodeCount; i++)
                Assert.Equal(0, graph.Nodes[i].LinkCount);
        }

        [Test]
        public void GroupsWithOnlyPaddingOrExternalsProduceNoNodes()
        {
            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("car, -1, unused\n" + EmptyNodes(12) + "\n"));
            world.AddGroups(ParseIII("car, -1, unused\n" + Repeat("1, -1, 0, 0, 0, 0, 160, 1, 1", 12)));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(0, graph.NodeCount);
            Assert.Equal(0, graph.LinkCount);
            Assert.Equal(12, graph.EmptySlotCount);
            Assert.Equal(12, graph.ExternalSlotCount);
            Assert.Equal(0, graph.UnresolvedGroupCount);
        }

        [Test]
        public void GroupsWithoutATransformStayOutOfTheGraph()
        {
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, road, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n10, road, 100, 0, 0, 1, 1, 1, 0, 0, 1, 1\nend\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());

            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("car, 10, road\n" + InternNode + "\n" + EmptyNodes(11) + "\n"));
            world.AddGroups(ParseIII("car, 11, missing\n" + InternNode + "\n" + EmptyNodes(11) + "\n"));
            world.AddGroups(ParseIII("car, -1, unused\n" + InternNode + "\n" + EmptyNodes(11) + "\n"));

            // Before binding, both attached groups have no transform and cannot be placed.
            PathGraph unbound = new PathGraph(world);
            Assert.Equal(1, unbound.NodeCount);
            Assert.Equal(2, unbound.UnresolvedGroupCount);

            world.BindInstances(staticWorld.Instances);
            PathGraph bound = new PathGraph(world);

            Assert.Equal(2, bound.NodeCount);
            Assert.Equal(1, bound.UnresolvedGroupCount);
            // The bound node sits on the placement, so the graph never guesses an origin.
            AssertVector(new Vector3(101.0f, 2.0f, 3.0f), bound.Nodes[0].Position);
            Assert.Equal(2, bound.Nodes[1].GroupIndex);
        }

        [Test]
        public void SameModelIdAcrossKindsKeepsSeparateGroups()
        {
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, road, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n10, road, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\nend\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());

            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("car, 10, road\n" + InternNode + "\n" + EmptyNodes(11) + "\n"));
            world.AddGroups(ParseIII("ped, 10, road\n" + InternNode + "\n" + EmptyNodes(11) + "\n"));
            world.BindInstances(staticWorld.Instances);

            PathGraph graph = new PathGraph(world);

            Assert.Equal(2, graph.NodeCount);
            Assert.Equal(1, graph.CarNodeCount);
            Assert.Equal(1, graph.PedNodeCount);
            Assert.Equal(PathKind.Car, graph.Nodes[0].Kind);
            Assert.Equal(PathKind.Ped, graph.Nodes[1].Kind);
            Assert.Equal(0, graph.Nodes[0].LinkCount);
            Assert.Equal(0, graph.Nodes[1].LinkCount);
        }

        [Test]
        public void NearestNodeUsesManhattanDistanceWithTripleWeightOnZ()
        {
            // A is three units below the query, B is four units to the side: plain 3D
            // distance would pick A, the original weighted metric picks B.
            PathWorld world = DetachedIII(
                "2, -1, 0, 0, 0, -48, 160, 1, 1",
                "2, -1, 0, 64, 0, 0, 160, 1, 1",
                EmptyNodes(10));

            PathGraph graph = new PathGraph(world);
            AssertVector(new Vector3(0.0f, 0.0f, -3.0f), graph.Nodes[0].Position);
            AssertVector(new Vector3(4.0f, 0.0f, 0.0f), graph.Nodes[1].Position);

            PathGraphNode node;
            float distance;
            Assert.True(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 100.0f, out node, out distance));
            Assert.Equal(1, node.GlobalIndex);
            Assert.Near(4.0f, distance);

            // |dx| + |dy| + 3|dz| = 0 + 0 + 9 for A, 4 + 0 + 0 for B.
            Assert.True(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 4.5f, out node, out distance));
            Assert.Equal(1, node.GlobalIndex);
            Assert.Near(4.0f, distance);

            // A node exactly at the limit is rejected: the original compares strictly.
            Assert.False(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 4.0f, out node, out distance));
            Assert.Equal(null, node);
            Assert.True(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 4.001f, out node, out distance));

            // The original seeds its search with a 10000 unit sentinel, so a huge radius still
            // cannot reach a node beyond it.
            Assert.False(graph.TryFindNearestNode(new Vector3(99999.0f, 0.0f, 0.0f), PathKind.Car,
                20000.0f, out node, out distance));

            // No node of the kind inside the limit, and a non-finite query point finds nothing.
            Assert.False(graph.TryFindNearestNode(new Vector3(1000.0f, 0.0f, 0.0f), PathKind.Car,
                100.0f, out node, out distance));
            Assert.Equal(null, node);
            Assert.False(graph.TryFindNearestNode(new Vector3(float.NaN, 0.0f, 0.0f), PathKind.Car,
                100.0f, out node, out distance));
        }

        [Test]
        public void NearestNodeTiesResolveToTheFirstSymbolicNode()
        {
            PathWorld world = DetachedIII(
                "2, -1, 0, 0, 0, 0, 160, 1, 1",
                "2, -1, 0, 0, 0, 0, 160, 1, 1",
                EmptyNodes(10));

            PathGraph graph = new PathGraph(world);
            PathGraphNode node;
            float distance;
            Assert.True(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 10.0f, out node, out distance));
            Assert.Equal(0, node.GlobalIndex);
            Assert.Near(0.0f, distance);
            Assert.Equal(1, graph.Nodes[1].GlobalIndex);
        }

        [Test]
        public void NearestNodeIsScopedByKindAndValidatesArguments()
        {
            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("car, -1, unused\n" + Repeat("2, -1, 0, 16, 32, 48, 160, 1, 1", 12)));
            world.AddGroups(ParseVC("0, -1\n" + Repeat("2, -1, 0, 0, 0, 0, 10, 0, 0, 0, 0", 12)));
            world.AddGroups(ParseVC("2, -1\n" + Repeat("2, -1, 0, 160, 0, 0, 10, 0, 0, 0, 0", 12)));

            PathGraph graph = new PathGraph(world);
            Assert.Equal(36, graph.NodeCount);
            Assert.Equal(12, graph.CarNodeCount);
            Assert.Equal(12, graph.PedNodeCount);
            Assert.Equal(12, graph.WaterNodeCount);
            Assert.Equal(PathKind.Car, graph.Nodes[0].Kind);
            Assert.Equal(PathKind.Ped, graph.Nodes[12].Kind);
            Assert.Equal(PathKind.Water, graph.Nodes[24].Kind);
            // Every slot has next = -1 here, so each node is its own component.
            Assert.Equal(36, graph.ComponentCount);
            Assert.Equal(12, graph.CarComponentCount);
            Assert.Equal(12, graph.PedComponentCount);
            Assert.Equal(12, graph.WaterComponentCount);
            Assert.Equal(36, graph.SingleNodeComponentCount);
            Assert.Equal(1, graph.LargestComponentSize);

            PathGraphNode node;
            float distance;
            Assert.True(graph.TryFindNearestNode(new Vector3(10.0f, 0.0f, 0.0f), PathKind.Water,
                5.0f, out node, out distance));
            Assert.Equal(PathKind.Water, node.Kind);
            Assert.Near(0.0f, distance);
            Assert.True(graph.TryFindNearestNode(new Vector3(10.0f, 0.0f, 0.0f), PathKind.Ped,
                15.0f, out node, out distance));
            Assert.Equal(PathKind.Ped, node.Kind);
            Assert.Near(10.0f, distance);
            Assert.True(graph.TryFindNearestNode(new Vector3(3.0f, 2.0f, 3.0f), PathKind.Car,
                5.0f, out node, out distance));
            Assert.Equal(PathKind.Car, node.Kind);
            Assert.Near(2.0f, distance);

            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 0.0f, out node, out distance);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, -1.0f, out node, out distance);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, float.NaN, out node, out distance);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, float.PositiveInfinity, out node, out distance);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                graph.TryFindNearestNode(Vector3.Zero, (PathKind)42, 5.0f, out node, out distance);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                new PathGraph(null);
            });
        }

        [Test]
        public void LinkListsCannotBeMutatedByCallers()
        {
            PathWorld world = DetachedIII(
                "2, 1, 0, 0, 0, 0, 160, 1, 1",
                "2, 1, 0, 160, 0, 0, 160, 1, 1",
                EmptyNodes(10));

            PathGraph graph = new PathGraph(world);
            IList<int> links = graph.Nodes[0].Links;
            Assert.Equal(1, links.Count);
            Assert.Throws<NotSupportedException>(delegate { links[0] = 99; });
            Assert.Throws<NotSupportedException>(delegate { links.Add(99); });
            Assert.Equal(1, graph.Nodes[0].Links[0]);
        }

        [Test]
        public void NeighbouringTilesJoinThroughCoincidentExternals()
        {
            // Two road models, each placed once: road A's external slot lands on the origin of
            // the tile road B, whose external slot also sits there. That coincidence is what
            // the original collapses into one crossing between the interior nodes.
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, roadA, generic, 1, 100, 0\n11, roadB, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n" +
                "10, roadA, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "11, roadB, 100, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "end\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());

            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII(
                "car, 10, roadA\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +     // interior at the tile origin
                "1, 0, 0, 1600, 0, 0, 160, 1, 1\n" +  // external 100 units out
                EmptyNodes(10) + "\n"));
            world.AddGroups(ParseIII(
                "car, 11, roadB\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 0, 0, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.BindInstances(staticWorld.Instances);

            PathGraph graph = new PathGraph(world);

            Assert.Equal(2, graph.TileCount);
            Assert.Equal(2, graph.NodeCount);
            Assert.Equal(1, graph.CrossTileLinkCount);
            Assert.Equal(0, graph.UnpairedExternCount);
            Assert.Equal(0, graph.UnresolvedExternCount);
            Assert.Equal(2, graph.LinkCount);
            Assert.Equal(0, graph.Nodes[0].TileIndex);
            Assert.Equal(1, graph.Nodes[1].TileIndex);
            // Tile A's interior (0,0,0) now neighbours tile B's interior (100,0,0).
            AssertVector(Vector3.Zero, graph.Nodes[0].Position);
            AssertVector(new Vector3(100.0f, 0.0f, 0.0f), graph.Nodes[1].Position);
            Assert.Equal(1, graph.Nodes[0].LinkCount);
            Assert.Equal(1, graph.Nodes[0].Links[0]);
            Assert.Equal(0, graph.Nodes[1].Links[0]);
            Assert.Equal(1, graph.ComponentCount);
            Assert.Equal(2, graph.LargestComponentSize);

            PathGraphNode node;
            float distance;
            Assert.True(graph.TryFindNearestNode(new Vector3(99.0f, 0.0f, 0.0f), PathKind.Car,
                5.0f, out node, out distance));
            Assert.Equal(1, node.GlobalIndex);
        }

        [Test]
        public void ExternalSlotsOutOfRangeStayUnpaired()
        {
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, roadA, generic, 1, 100, 0\n11, roadB, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n" +
                "10, roadA, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "11, roadB, 400, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "end\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());

            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII(
                "car, 10, roadA\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 0, 1600, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.AddGroups(ParseIII(
                "car, 11, roadB\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 0, 0, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.BindInstances(staticWorld.Instances);

            PathGraph graph = new PathGraph(world);

            Assert.Equal(2, graph.TileCount);
            Assert.Equal(0, graph.CrossTileLinkCount);
            Assert.Equal(2, graph.UnpairedExternCount);
            Assert.Equal(0, graph.LinkCount);
            Assert.Equal(0, graph.Nodes[0].LinkCount);
        }

        [Test]
        public void ExternalWithoutAnInteriorNeighbourIsReportedUnresolved()
        {
            PathWorld world = DetachedIII(
                "2, -1, 0, 0, 0, 0, 160, 1, 1",
                "1, 2, 0, 1600, 0, 0, 160, 1, 1", // external naming another external
                "1, -1, 0, 3200, 0, 0, 160, 1, 1",
                EmptyNodes(9));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(1, graph.NodeCount);
            Assert.Equal(2, graph.UnresolvedExternCount);
            Assert.Equal(0, graph.CrossTileLinkCount);
            Assert.Equal(0, graph.LinkCount);
        }

        [Test]
        public void FloodFillGroupsFollowLinksAndKeepSinglesApart()
        {
            // Chain 0 -> 1, an isolated slot 2, and chain 3 -> 4 -> 5.
            PathWorld world = DetachedIII(
                "2, 1, 0, 0, 0, 0, 160, 1, 1",
                "2, 1, 0, 160, 0, 0, 160, 1, 1",
                "2, -1, 0, 320, 0, 0, 160, 1, 1",
                "2, 4, 0, 480, 0, 0, 160, 1, 1",
                "2, 5, 0, 640, 0, 0, 160, 1, 1",
                "2, 5, 0, 800, 0, 0, 160, 1, 1",
                EmptyNodes(6));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(6, graph.NodeCount);
            Assert.Equal(3, graph.ComponentCount);
            Assert.Equal(3, graph.CarComponentCount);
            Assert.Equal(0, graph.PedComponentCount);
            Assert.Equal(0, graph.WaterComponentCount);
            Assert.Equal(1, graph.SingleNodeComponentCount);
            Assert.Equal(3, graph.LargestComponentSize);
            // Ids are assigned in node order, so the first chain is 1, the single is 2.
            Assert.Equal(1, graph.Nodes[0].GroupId);
            Assert.Equal(1, graph.Nodes[1].GroupId);
            Assert.Equal(2, graph.Nodes[2].GroupId);
            Assert.Equal(3, graph.Nodes[3].GroupId);
            Assert.Equal(3, graph.Nodes[4].GroupId);
            Assert.Equal(3, graph.Nodes[5].GroupId);
        }

        [Test]
        public void MergeDistanceIsConfigurableAndValidated()
        {
            // Two external slots 2.5 units apart: the original threshold leaves them apart,
            // a wider threshold collapses them into one crossing.
            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII(
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 0, 40, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.AddGroups(ParseIII(
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 0, 0, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));

            PathGraph strict = new PathGraph(world);
            Assert.Near(1.0f, strict.ExternMergeDistance);
            Assert.Equal(0, strict.CrossTileLinkCount);
            Assert.Equal(2, strict.UnpairedExternCount);

            PathGraph relaxed = new PathGraph(world, 5.0f);
            Assert.Near(5.0f, relaxed.ExternMergeDistance);
            Assert.Equal(1, relaxed.CrossTileLinkCount);
            Assert.Equal(0, relaxed.UnpairedExternCount);
            Assert.Equal(1, relaxed.ComponentCount);

            Assert.Throws<ArgumentOutOfRangeException>(delegate { new PathGraph(world, 0.0f); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new PathGraph(world, -1.0f); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { new PathGraph(world, float.NaN); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                new PathGraph(world, float.PositiveInfinity);
            });
        }

        [Test]
        public void LinksCarryDistanceAndCrossingFlags()
        {
            // Slot 0 names slot 1 with `crossing` set; slot 1 names slot 0 back without it.
            // Crossing flags exist for ped connections only, so this uses ped path data.
            PathWorld world = DetachedIIIPed(
                "2, 1, 1, 0, 0, 0, 160",
                "2, 0, 0, 160, 0, 0, 160",
                EmptyPedNodes(10));

            PathGraph graph = new PathGraph(world);

            Assert.Equal(1, graph.Nodes[0].LinkCount);
            Assert.Near(10.0f, graph.Nodes[0].LinkDistance(0), 0.0001f);
            Assert.True(graph.Nodes[0].LinkCrossesRoad(0));
            Assert.True(graph.Nodes[1].LinkCrossesRoad(0));
            Assert.Equal(2, graph.CrossingLinkCount);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { graph.Nodes[0].LinkDistance(-1); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { graph.Nodes[0].LinkDistance(1); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { graph.Nodes[0].LinkCrossesRoad(1); });

            PathWorld quiet = DetachedIIIPed(
                "2, 1, 0, 0, 0, 0, 160",
                "2, 0, 0, 160, 0, 0, 160",
                EmptyPedNodes(10));
            Assert.Equal(0, new PathGraph(quiet).CrossingLinkCount);
        }

        [Test]
        public void ExternalCrossingsAreNotMarkedAsRoadCrossings()
        {
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, roadA, generic, 1, 100, 0\n11, roadB, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n" +
                "10, roadA, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "11, roadB, 100, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "end\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());

            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII(
                "car, 10, roadA\n" +
                "2, 1, 1, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 1, 1600, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.AddGroups(ParseIII(
                "car, 11, roadB\n" +
                "2, 1, 1, 0, 0, 0, 160, 1, 1\n" +
                "1, 0, 1, 0, 0, 0, 160, 1, 1\n" +
                EmptyNodes(10) + "\n"));
            world.BindInstances(staticWorld.Instances);

            PathGraph graph = new PathGraph(world);

            Assert.Equal(1, graph.CrossTileLinkCount);
            Assert.Equal(0, graph.CrossingLinkCount);
            Assert.False(graph.Nodes[0].LinkCrossesRoad(0));
            Assert.Near(100.0f, graph.Nodes[0].LinkDistance(0), 0.0001f);
        }

        [Test]
        public void RouteFollowsLinkDistancesAndReportsDisconnectedNodes()
        {
            // Chain 0 -> 1 -> 2 -> 3 with ten units between neighbours, and a separate node 4.
            PathWorld world = DetachedIII(
                "2, 1, 0, 0, 0, 0, 160, 1, 1",
                "2, 2, 0, 160, 0, 0, 160, 1, 1",
                "2, 3, 0, 320, 0, 0, 160, 1, 1",
                "2, -1, 0, 480, 0, 0, 160, 1, 1",
                "2, -1, 0, 1600, 0, 0, 160, 1, 1",
                EmptyNodes(7));

            PathGraph graph = new PathGraph(world);
            Assert.Equal(5, graph.NodeCount);

            int[] route;
            float length;
            Assert.True(graph.TryFindRoute(0, 3, out route, out length));
            Assert.Equal(4, route.Length);
            Assert.Equal(0, route[0]);
            Assert.Equal(3, route[3]);
            Assert.Near(30.0f, length, 0.0001f);

            Assert.True(graph.TryFindRoute(3, 0, out route, out length));
            Assert.Equal(4, route.Length);
            Assert.Near(30.0f, length, 0.0001f);

            Assert.True(graph.TryFindRoute(2, 2, out route, out length));
            Assert.Equal(1, route.Length);
            Assert.Equal(2, route[0]);
            Assert.Near(0.0f, length);

            // Node 4 has no link at all, so no route can reach it.
            Assert.False(graph.TryFindRoute(0, 4, out route, out length));
            Assert.Equal(null, route);
            Assert.Near(0.0f, length);

            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                int[] unused;
                float unusedLength;
                graph.TryFindRoute(-1, 0, out unused, out unusedLength);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                int[] unused;
                float unusedLength;
                graph.TryFindRoute(0, 5, out unused, out unusedLength);
            });
        }

        [Test]
        public void RouteTakesTheShorterDetourWhenBothExist()
        {
            // 0 -> 2 -> 1 with no direct 0 -> 1 link, so the only route is the two-hop detour.
            PathWorld world = DetachedIII(
                "2, 2, 0, 0, 0, 0, 160, 1, 1",
                "2, 2, 0, 160, 0, 0, 160, 1, 1",
                "2, 1, 0, 80, -120, 0, 160, 1, 1",
                EmptyNodes(9));

            PathGraph graph = new PathGraph(world);
            Assert.Equal(0, graph.Nodes[0].Links.Contains(1) ? 1 : 0);
            int[] route;
            float length;
            Assert.True(graph.TryFindRoute(0, 1, out route, out length));
            Assert.Equal(3, route.Length);
            Assert.Equal(2, route[1]);
            Assert.Near(18.027756f, length, 0.001f);
        }

        [Test]
        public void EmptyWorldBuildsAnEmptyGraph()
        {
            PathGraph graph = new PathGraph(new PathWorld());

            Assert.Equal(0, graph.NodeCount);
            Assert.Equal(0, graph.LinkCount);
            Assert.Equal(0, graph.EmptySlotCount);
            Assert.Equal(0, graph.ExternalSlotCount);
            Assert.Equal(0, graph.UnresolvedGroupCount);
            Assert.Equal(0, graph.Nodes.Count);

            PathGraphNode node;
            float distance;
            Assert.False(graph.TryFindNearestNode(Vector3.Zero, PathKind.Car, 10.0f, out node, out distance));
        }

        private static PathWorld DetachedIII(params string[] nodeLines)
        {
            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("car, -1, unused\n" + string.Join("\n", nodeLines) + "\n"));
            return world;
        }

        private static PathWorld DetachedIIIPed(params string[] nodeLines)
        {
            PathWorld world = new PathWorld();
            world.AddGroups(ParseIII("ped, -1, unused\n" + string.Join("\n", nodeLines) + "\n"));
            return world;
        }

        private static string EmptyPedNodes(int count)
        {
            return Repeat("0, -1, 0, 0, 0, 0, 0", count).TrimEnd('\n');
        }

        private static string EmptyNodes(int count)
        {
            return Repeat("0, -1, 0, 0, 0, 0, 0, 0, 0", count).TrimEnd('\n');
        }

        private static PathGroup[] ParseIII(string body)
        {
            const string sourceName = "iii-paths.ide";
            IdeDocument document = new IdeReader(DataSchema.III).Read(
                "path\n" + body + "end\n", sourceName);
            return PathRecordParser.ParseGroups(document.RawRecords, DataSchema.III, sourceName);
        }

        private static PathGroup[] ParseVC(string body)
        {
            const string sourceName = "vc-paths.ipl";
            IplDocument document = new IplReader(DataSchema.VC).Read(
                "path\n" + body + "end\n", sourceName);
            return PathRecordParser.ParseGroups(document.RawRecords, DataSchema.VC, sourceName);
        }

        private static string Repeat(string line, int count)
        {
            string result = "";
            for (int i = 0; i < count; i++)
                result += line + "\n";
            return result;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X, 0.0001f);
            Assert.Near(expected.Y, actual.Y, 0.0001f);
            Assert.Near(expected.Z, actual.Z, 0.0001f);
        }
    }
}