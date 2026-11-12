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
    }
}