using System;
using System.Collections.Generic;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// Result of the independent consistency audit over a built <see cref="PathGraph"/>.
    /// Every counter is measured from the graph itself, not from the inputs that produced it.
    /// </summary>
    public sealed class PathGraphReport
    {
        /// <summary>Details for the longest over-length connections, in discovery order.</summary>
        public sealed class LongLink
        {
            public int From { get; internal set; }
            public int To { get; internal set; }
            public float Distance { get; internal set; }
            public PathKind Kind { get; internal set; }
            public Vector3 FromPosition { get; internal set; }
            public Vector3 ToPosition { get; internal set; }
            public int FromTile { get; internal set; }
            public int ToTile { get; internal set; }
        }

        private readonly List<LongLink> overLength = new List<LongLink>();

        /// <summary>Bounded sample of over-length links so a report stays printable.</summary>
        public IList<LongLink> OverLengthSamples { get { return overLength; } }

        internal void AddOverLength(LongLink link)
        {
            if (overLength.Count < 8)
                overLength.Add(link);
        }

        public int NodeCount { get; internal set; }
        public int LinkCount { get; internal set; }
        public int SelfLinkCount { get; internal set; }
        public int AsymmetricLinkCount { get; internal set; }
        public int OverLengthLinkCount { get; internal set; }
        public int DistanceMismatchCount { get; internal set; }
        public int OverLengthCarCount { get; internal set; }
        public int OverLengthPedCount { get; internal set; }
        public int OverLengthWaterCount { get; internal set; }

        /// <summary>Longest connection in world units, and the nodes it joins.</summary>
        public float LongestLink { get; internal set; }
        public int LongestLinkFrom { get; internal set; }
        public int LongestLinkTo { get; internal set; }

        /// <summary>Nodes reached by walking links, compared with the flood-fill labels.</summary>
        public int UnlabelledNodeCount { get; internal set; }
        public int MisgroupedNodeCount { get; internal set; }

        /// <summary>Sampled nodes whose weighted nearest-node search did not return themselves.</summary>
        public int NearestMismatchCount { get; internal set; }
        public int NearestSampleCount { get; internal set; }

        public bool IsConsistent
        {
            get
            {
                // Water lanes are legitimately long (hundreds of units), so only car and ped
                // over-length links mean a bogus merge and make the graph inconsistent.
                return SelfLinkCount == 0 && AsymmetricLinkCount == 0 &&
                    MisgroupedNodeCount == 0 && UnlabelledNodeCount == 0 &&
                    NearestMismatchCount == 0 && DistanceMismatchCount == 0 &&
                    OverLengthCarCount == 0 && OverLengthPedCount == 0;
            }
        }
    }

    /// <summary>
    /// Self-audit for the path graph. It re-derives properties that the builder never looks at,
    /// so a merge bug (a one-way link, a link across the map, a node left out of its component)
    /// shows up as a counter instead of a silent wrong answer from a query.
    /// </summary>
    public static class PathGraphVerifier
    {
        public static PathGraphReport Verify(PathGraph graph, float maximumLinkLength)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (!(maximumLinkLength > 0.0f) || float.IsInfinity(maximumLinkLength))
                throw new ArgumentOutOfRangeException("maximumLinkLength");

            PathGraphReport report = new PathGraphReport();
            report.NodeCount = graph.NodeCount;
            IList<PathGraphNode> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                PathGraphNode node = nodes[i];
                IList<int> links = node.Links;
                for (int k = 0; k < links.Count; k++)
                {
                    int target = links[k];
                    report.LinkCount++;
                    if (target == i)
                    {
                        report.SelfLinkCount++;
                        continue;
                    }
                    float distance = node.LinkDistance(k);
                    // The stored distance must be the real Euclidean length; a copied or stale
                    // value would silently skew every route through this link.
                    Vector3 offset = nodes[target].Position - node.Position;
                    float expected = offset.Length();
                    if (Math.Abs(distance - expected) > 0.01f)
                        report.DistanceMismatchCount++;
                    if (distance > report.LongestLink)
                    {
                        report.LongestLink = distance;
                        report.LongestLinkFrom = i;
                        report.LongestLinkTo = target;
                    }
                    if (distance > maximumLinkLength)
                    {
                        report.OverLengthLinkCount++;
                        switch (node.Kind)
                        {
                            case PathKind.Car:
                                report.OverLengthCarCount++;
                                break;
                            case PathKind.Ped:
                                report.OverLengthPedCount++;
                                break;
                            default:
                                report.OverLengthWaterCount++;
                                break;
                        }
                        PathGraphReport.LongLink detail = new PathGraphReport.LongLink();
                        detail.From = i;
                        detail.To = target;
                        detail.Distance = distance;
                        detail.Kind = node.Kind;
                        detail.FromPosition = node.Position;
                        detail.ToPosition = nodes[target].Position;
                        detail.FromTile = node.TileIndex;
                        detail.ToTile = nodes[target].TileIndex;
                        report.AddOverLength(detail);
                    }
                    if (!nodes[target].Links.Contains(i))
                        report.AsymmetricLinkCount++;
                }
            }

            VerifyGroups(nodes, report);
            VerifyNearestNodes(graph, report);
            return report;
        }

        /// <summary>
        /// Every node must carry a flood-fill label, and walking the links from a node must
        /// never leave that label. This catches components that were merged or split wrongly.
        /// </summary>
        private static void VerifyGroups(IList<PathGraphNode> nodes, PathGraphReport report)
        {
            bool[] visited = new bool[nodes.Count];
            Queue<int> pending = new Queue<int>();
            for (int start = 0; start < nodes.Count; start++)
            {
                if (visited[start])
                    continue;
                if (nodes[start].GroupId == 0)
                {
                    report.UnlabelledNodeCount++;
                    continue;
                }
                int label = nodes[start].GroupId;
                visited[start] = true;
                pending.Enqueue(start);
                while (pending.Count != 0)
                {
                    PathGraphNode node = nodes[pending.Dequeue()];
                    IList<int> links = node.Links;
                    for (int k = 0; k < links.Count; k++)
                    {
                        int target = links[k];
                        if (nodes[target].GroupId != label)
                        {
                            report.MisgroupedNodeCount++;
                            continue;
                        }
                        if (visited[target])
                            continue;
                        visited[target] = true;
                        pending.Enqueue(target);
                    }
                }
            }
        }

        /// <summary>
        /// The weighted nearest-node search must find each node from its own position. Samples
        /// are spread evenly so the audit stays bounded on large trees.
        /// </summary>
        private static void VerifyNearestNodes(PathGraph graph, PathGraphReport report)
        {
            const int MaximumSamples = 200;
            IList<PathGraphNode> nodes = graph.Nodes;
            if (nodes.Count == 0)
                return;
            int step = Math.Max(1, nodes.Count / MaximumSamples);
            for (int i = 0; i < nodes.Count; i += step)
            {
                PathGraphNode node = nodes[i];
                PathGraphNode found;
                float distance;
                report.NearestSampleCount++;
                if (!graph.TryFindNearestNode(node.Position, node.Kind, 10000.0f, out found,
                    out distance))
                {
                    report.NearestMismatchCount++;
                    continue;
                }
                if (distance > 0.001f)
                    report.NearestMismatchCount++;
            }
        }
    }
}