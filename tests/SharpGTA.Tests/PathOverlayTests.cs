using System;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class PathOverlayTests
    {
        [Test]
        public void BuildsOneSegmentPerConnectionAndColoursByKind()
        {
            PathGraph graph = Graph(ParseIII(
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                "2, -1, 0, 320, 0, 0, 160, 1, 1\n" +
                EmptyNodes(9) + "\n"));

            RenderLineBatch batch = PathOverlayBuilder.Build(graph, Vector3.Zero, 1000.0f, 100);

            Assert.Equal(2, batch.SegmentCount);
            Assert.Equal(4, batch.Positions.Length);
            Assert.Equal(4, batch.Colors.Length);
            Assert.Equal(PathOverlayBuilder.CarColor, batch.Colors[0]);
            AssertVector(new Vector3(0.0f, 0.0f, 0.0f), batch.Positions[0]);
            AssertVector(new Vector3(10.0f, 0.0f, 0.0f), batch.Positions[1]);
            AssertVector(new Vector3(10.0f, 0.0f, 0.0f), batch.Positions[2]);
            AssertVector(new Vector3(20.0f, 0.0f, 0.0f), batch.Positions[3]);
        }

        [Test]
        public void FiltersByRadiusAndHonoursTheSegmentCap()
        {
            PathGraph graph = Graph(ParseIII(
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                "2, -1, 0, 320, 0, 0, 160, 1, 1\n" +
                EmptyNodes(9) + "\n"));

            // Every connection with at least one endpoint near the query point is kept.
            Assert.Equal(1, PathOverlayBuilder.Build(graph, Vector3.Zero, 5.0f, 100).SegmentCount);
            Assert.Equal(2, PathOverlayBuilder.Build(graph, Vector3.Zero, 15.0f, 100).SegmentCount);
            // A cap of one keeps the batch bounded and deterministic.
            RenderLineBatch capped = PathOverlayBuilder.Build(graph, Vector3.Zero, 1000.0f, 1);
            Assert.Equal(1, capped.SegmentCount);
            AssertVector(Vector3.Zero, capped.Positions[0]);

            Assert.Throws<ArgumentNullException>(delegate
            {
                PathOverlayBuilder.Build(null, Vector3.Zero, 10.0f, 10);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                PathOverlayBuilder.Build(graph, Vector3.Zero, 0.0f, 10);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                PathOverlayBuilder.Build(graph, Vector3.Zero, 10.0f, 0);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                PathOverlayBuilder.Build(graph, new Vector3(float.NaN, 0.0f, 0.0f), 10.0f, 10);
            });
        }

        [Test]
        public void PedAndWaterSegmentsUseTheirOwnColours()
        {
            PathGraph graph = Graph(ParseVC(
                "0, -1\n" +
                "2, 1, 0, 0, 0, 0, 10, 0, 0, 0, 0\n" +
                "2, 0, 0, 160, 0, 0, 10, 0, 0, 0, 0\n" +
                Repeat("0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0", 10) + "\n" +
                "2, -1\n" +
                "2, 1, 0, 0, 0, 0, 10, 0, 0, 0, 0\n" +
                "2, 0, 0, 160, 0, 0, 10, 0, 0, 0, 0\n" +
                Repeat("0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0", 10) + "\n"));

            RenderLineBatch batch = PathOverlayBuilder.Build(graph, Vector3.Zero, 1000.0f, 100);

            Assert.Equal(2, batch.SegmentCount);
            Assert.Equal(PathOverlayBuilder.PedColor, batch.Colors[0]);
            Assert.Equal(PathOverlayBuilder.WaterColor, batch.Colors[2]);
        }

        [Test]
        public void RouteAndConcatProducePolylineSegments()
        {
            PathGraph graph = Graph(ParseIII(
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                "2, -1, 0, 320, 0, 0, 160, 1, 1\n" +
                EmptyNodes(9) + "\n"));

            RenderLineBatch route = PathOverlayBuilder.BuildRoute(graph, new int[] { 0, 1, 2 });
            Assert.Equal(2, route.SegmentCount);
            Assert.Equal(PathOverlayBuilder.RouteColor, route.Colors[0]);
            AssertVector(Vector3.Zero, route.Positions[0]);
            AssertVector(new Vector3(10.0f, 0.0f, 0.0f), route.Positions[1]);

            // A single node is a valid (empty) route, not an error.
            Assert.Equal(0, PathOverlayBuilder.BuildRoute(graph, new int[] { 0 }).SegmentCount);

            RenderLineBatch graphBatch = PathOverlayBuilder.Build(graph, Vector3.Zero, 1000.0f, 100);
            RenderLineBatch joined = PathOverlayBuilder.Concat(graphBatch, route);
            Assert.Equal(graphBatch.SegmentCount + route.SegmentCount, joined.SegmentCount);

            // Joining with an empty batch returns the other batch unchanged.
            Assert.Equal(route.SegmentCount,
                PathOverlayBuilder.Concat(new RenderLineBatch(new Vector3[0], new uint[0]), route)
                    .SegmentCount);
            Assert.Equal(route.SegmentCount,
                PathOverlayBuilder.Concat(route, new RenderLineBatch(new Vector3[0], new uint[0]))
                    .SegmentCount);

            Assert.Throws<ArgumentNullException>(delegate
            {
                PathOverlayBuilder.BuildRoute(null, new int[] { 0, 1 });
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                PathOverlayBuilder.BuildRoute(graph, null);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                PathOverlayBuilder.BuildRoute(graph, new int[] { 0, 5 });
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                PathOverlayBuilder.Concat(null, route);
            });
        }

        [Test]
        public void LineBatchRejectsMismatchedCounts()
        {
            Assert.Throws<ArgumentNullException>(delegate
            {
                new RenderLineBatch(null, new uint[2]);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                new RenderLineBatch(new Vector3[2], null);
            });
            Assert.Throws<ArgumentException>(delegate
            {
                new RenderLineBatch(new Vector3[3], new uint[3]);
            });
            Assert.Throws<ArgumentException>(delegate
            {
                new RenderLineBatch(new Vector3[2], new uint[1]);
            });
            Assert.Equal(1, new RenderLineBatch(new Vector3[2], new uint[2]).SegmentCount);
        }

        private static PathGraph Graph(PathGroup[] groups)
        {
            PathWorld world = new PathWorld();
            world.AddGroups(groups);
            return new PathGraph(world);
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

        private static string EmptyNodes(int count)
        {
            return Repeat("0, -1, 0, 0, 0, 0, 0, 0, 0", count).TrimEnd('\n');
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