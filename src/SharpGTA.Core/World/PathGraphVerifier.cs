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
        public int NodeCount { get; internal set; }
        public int LinkCount { get; internal set; }
        public int SelfLinkCount { get; internal set; }
        public int AsymmetricLinkCount { get; internal set; }
        public int OverLengthLinkCount { get; internal set; }

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
                return SelfLinkCount == 0 && AsymmetricLinkCount == 0 &&
                    MisgroupedNodeCount == 0 && UnlabelledNodeCount == 0 &&
                    NearestMismatchCount == 0;
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
                    if (distance > report.LongestLink)
                    {
                        report.LongestLink = distance;
                        report.LongestLinkFrom = i;
                        report.LongestLinkTo = target;
                    }
                    if (distance > maximumLinkLength)
                        report.OverLengthLinkCount++;
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