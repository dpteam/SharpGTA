using System;
using System.Collections.Generic;

namespace SharpGTA.World
{
    /// <summary>
    /// Builds simple deterministic routes over an existing path graph. This is a probe helper,
    /// not CPathFind: it walks the lowest-numbered neighbour that is not the previous node and
    /// stops after a hop budget or a dead end, so two runs of the same world produce the same
    /// chain without any random source.
    /// </summary>
    public static class PathRouteBuilder
    {
        public static int[] BuildGreedy(PathGraph graph, int start, int maximumHops)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (start < 0 || start >= graph.NodeCount)
                throw new ArgumentOutOfRangeException("start");
            if (maximumHops < 0)
                throw new ArgumentOutOfRangeException("maximumHops");

            List<int> route = new List<int>();
            route.Add(start);
            int current = start;
            int previous = -1;
            for (int hop = 0; hop < maximumHops; hop++)
            {
                IList<int> links = graph.Nodes[current].Links;
                int best = -1;
                for (int k = 0; k < links.Count; k++)
                {
                    int candidate = links[k];
                    if (candidate == previous)
                        continue;
                    if (best < 0 || candidate < best)
                        best = candidate;
                }
                if (best < 0)
                    break;
                route.Add(best);
                previous = current;
                current = best;
            }
            return route.ToArray();
        }
    }
}