using System;
using System.Collections.Generic;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Rendering
{
    /// <summary>
    /// Presentation-only line batch: positions come in pairs (start, end) with a colour per
    /// vertex, so a renderer can submit one line-list draw call.
    /// </summary>
    public sealed class RenderLineBatch
    {
        public RenderLineBatch(Vector3[] positions, uint[] colors)
        {
            if (positions == null)
                throw new ArgumentNullException("positions");
            if (colors == null)
                throw new ArgumentNullException("colors");
            if (positions.Length % 2 != 0)
                throw new ArgumentException("Line batches need an even vertex count.", "positions");
            if (colors.Length != positions.Length)
                throw new ArgumentException("Every line vertex needs a colour.", "colors");
            Positions = positions;
            Colors = colors;
        }

        public Vector3[] Positions { get; private set; }
        public uint[] Colors { get; private set; }
        public int SegmentCount { get { return Positions.Length / 2; } }
    }

    /// <summary>
    /// Builds a debug line batch for the path graph. This is a diagnostic overlay, not a
    /// gameplay renderer: it draws one segment per connection, colours by path kind and stops
    /// at a fixed segment cap so the batch stays bounded.
    /// </summary>
    public static class PathOverlayBuilder
    {
        /// <summary>
        /// Colours follow the Core convention used by RenderVertex/MaterialData: bits are
        /// R | G&lt;&lt;8 | B&lt;&lt;16 | A&lt;&lt;24, which the D3D9 backend converts to D3DCOLOR.
        /// </summary>
        public const uint CarColor = 0xFF3FA0FFu;
        public const uint PedColor = 0xFFFFD03Fu;
        public const uint WaterColor = 0xFF60603Fu;

        public static RenderLineBatch Build(PathGraph graph, Vector3 center, float radius,
            int maxSegments)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (!(radius > 0.0f) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            if (maxSegments <= 0)
                throw new ArgumentOutOfRangeException("maxSegments");

            float radiusSquared = radius * radius;
            List<Vector3> positions = new List<Vector3>();
            List<uint> colors = new List<uint>();
            IList<PathGraphNode> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count && colors.Count / 2 < maxSegments; i++)
            {
                PathGraphNode node = nodes[i];
                bool nodeInside = (node.Position - center).LengthSquared() <= radiusSquared;
                IList<int> links = node.Links;
                for (int k = 0; k < links.Count; k++)
                {
                    int target = links[k];
                    // One segment per physical connection: only walk from the lower index.
                    if (target <= i)
                        continue;
                    PathGraphNode other = nodes[target];
                    bool otherInside = (other.Position - center).LengthSquared() <= radiusSquared;
                    if (!nodeInside && !otherInside)
                        continue;
                    uint color = ColorOf(node.Kind);
                    positions.Add(node.Position);
                    colors.Add(color);
                    positions.Add(other.Position);
                    colors.Add(color);
                    if (colors.Count / 2 >= maxSegments)
                        break;
                }
            }
            return new RenderLineBatch(positions.ToArray(), colors.ToArray());
        }

        private static uint ColorOf(PathKind kind)
        {
            switch (kind)
            {
                case PathKind.Car:
                    return CarColor;
                case PathKind.Ped:
                    return PedColor;
                case PathKind.Water:
                    return WaterColor;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        /// <summary>Route segments use one bright colour so they stand out over the graph.</summary>
        public const uint RouteColor = 0xFFFFFFFFu;

        /// <summary>Builds the polyline of an already resolved node chain.</summary>
        public static RenderLineBatch BuildRoute(PathGraph graph, int[] route)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (route == null)
                throw new ArgumentNullException("route");
            if (route.Length < 2)
                return new RenderLineBatch(new Vector3[0], new uint[0]);

            Vector3[] positions = new Vector3[(route.Length - 1) * 2];
            uint[] colors = new uint[positions.Length];
            for (int i = 0; i < route.Length - 1; i++)
            {
                if (route[i] < 0 || route[i] >= graph.NodeCount ||
                    route[i + 1] < 0 || route[i + 1] >= graph.NodeCount)
                {
                    throw new ArgumentOutOfRangeException("route",
                        "Route contains a node index outside the graph.");
                }
                positions[i * 2] = graph.Nodes[route[i]].Position;
                positions[i * 2 + 1] = graph.Nodes[route[i + 1]].Position;
                colors[i * 2] = RouteColor;
                colors[i * 2 + 1] = RouteColor;
            }
            return new RenderLineBatch(positions, colors);
        }

        /// <summary>Joins two batches so a caller can submit one line-list draw call.</summary>
        public static RenderLineBatch Concat(RenderLineBatch first, RenderLineBatch second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                return first;
            if (second.SegmentCount == 0)
                return first;
            if (first.SegmentCount == 0)
                return second;
            Vector3[] positions = new Vector3[first.Positions.Length + second.Positions.Length];
            uint[] colors = new uint[positions.Length];
            Array.Copy(first.Positions, 0, positions, 0, first.Positions.Length);
            Array.Copy(second.Positions, 0, positions, first.Positions.Length,
                second.Positions.Length);
            Array.Copy(first.Colors, 0, colors, 0, first.Colors.Length);
            Array.Copy(second.Colors, 0, colors, first.Colors.Length, second.Colors.Length);
            return new RenderLineBatch(positions, colors);
        }
    }
}