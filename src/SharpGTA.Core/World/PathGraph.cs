using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>One interior path node of a bound path group with its world position.</summary>
    public sealed class PathGraphNode
    {
        private static readonly ReadOnlyCollection<int> NoLinks =
            new ReadOnlyCollection<int>(new int[0]);
        private ReadOnlyCollection<int> links = NoLinks;

        internal PathGraphNode(int globalIndex, int groupIndex, PathGroup group,
            PathNode source, int localIndex, Vector3 position)
        {
            GlobalIndex = globalIndex;
            GroupIndex = groupIndex;
            Group = group;
            Source = source;
            LocalIndex = localIndex;
            Position = position;
        }

        /// <summary>Index in <see cref="PathGraph.Nodes"/>, in group and slot order.</summary>
        public int GlobalIndex { get; private set; }

        /// <summary>Tile slot this node occupies; `next` values are this same index space.</summary>
        public int LocalIndex { get; private set; }

        public int GroupIndex { get; private set; }
        public PathGroup Group { get; private set; }
        public PathNode Source { get; private set; }
        public Vector3 Position { get; private set; }

        /// <summary>
        /// Global indices this node is connected to inside its tile. Adjacency is symmetric:
        /// the original links a pair when either side's `next` names the other, so every
        /// connection appears in both nodes' lists.
        /// </summary>
        public IList<int> Links { get { return links; } }

        public PathKind Kind { get { return Group.Kind; } }

        /// <summary>Group-local `next` value: a slot index inside the group, or -1 for none.</summary>
        public int NextNodeIndex { get { return Source.NextNodeIndex; } }

        public int LinkCount { get { return links.Count; } }

        internal void SetLinks(ReadOnlyCollection<int> value)
        {
            links = value;
        }
    }

    /// <summary>
    /// Intra-tile path graph over the parsed IDE/IPL path sections, mirroring the node and
    /// link part of CPathFind::PreparePathDataForType. Only interior slots (type 2) become
    /// nodes, as in the original, where `m_pathNodes` receives nothing else; external slots
    /// (type 1) and empty slots (type 0) are not nodes. A pair of interior slots in one tile
    /// is linked when either side's `next` names the other, so the adjacency is symmetric.
    /// External slots exist to join neighbouring tiles: the original collapses two externals
    /// closer than one unit and links the interior nodes behind them. That merge, the tile
    /// union-find groups, `m_connections` flags and any route search are not reproduced yet,
    /// so a connection never leaves its tile and no path is computed.
    /// </summary>
    public sealed class PathGraph
    {
        private readonly List<PathGraphNode> nodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> carNodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> pedNodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> waterNodes = new List<PathGraphNode>();
        private readonly ReadOnlyCollection<PathGraphNode> readOnlyNodes;
        private int emptySlots;
        private int externalSlots;
        private int unknownSlots;
        private int externalNexts;
        private int danglingNexts;
        private int selfNexts;

        public PathGraph(PathWorld world)
        {
            if (world == null)
                throw new ArgumentNullException("world");

            int[][] internNodes = new int[world.GroupCount][];
            for (int g = 0; g < world.GroupCount; g++)
            {
                PathGroup group = world.Groups[g];
                int[] map = new int[group.Nodes.Length];
                for (int i = 0; i < map.Length; i++)
                    map[i] = -1;
                internNodes[g] = map;
                if (!group.IsDetached && !group.HasTransform)
                {
                    // No bound instance and no explicit transform: the tile origin is
                    // unknown, so its slots stay out of the graph instead of being guessed.
                    UnresolvedGroupCount++;
                    emptySlots += group.Nodes.Length;
                    continue;
                }
                for (int n = 0; n < group.Nodes.Length; n++)
                {
                    PathNode source = group.Nodes[n];
                    if (source.Type == 0)
                    {
                        emptySlots++;
                        continue;
                    }
                    if (source.Type == 1)
                    {
                        externalSlots++;
                        continue;
                    }
                    if (source.Type != 2)
                    {
                        unknownSlots++;
                        continue;
                    }
                    PathGraphNode node = new PathGraphNode(nodes.Count, g, group, source, n,
                        world.GetWorldPosition(group, source));
                    map[n] = nodes.Count;
                    nodes.Add(node);
                    switch (group.Kind)
                    {
                        case PathKind.Car:
                            carNodes.Add(node);
                            break;
                        case PathKind.Ped:
                            pedNodes.Add(node);
                            break;
                        case PathKind.Water:
                            waterNodes.Add(node);
                            break;
                    }
                }
            }
            readOnlyNodes = nodes.AsReadOnly();

            List<int>[] adjacency = new List<int>[nodes.Count];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>(2);

            int links = 0;
            for (int g = 0; g < world.GroupCount; g++)
            {
                int[] map = internNodes[g];
                PathGroup group = world.Groups[g];
                for (int a = 0; a < map.Length; a++)
                {
                    int left = map[a];
                    if (left < 0)
                        continue;
                    CountNextTarget(group, a);
                    for (int b = 0; b < map.Length; b++)
                    {
                        int right = map[b];
                        if (right < 0 || right == left)
                            continue;
                        // Either side naming the other links the pair; both directions are
                        // stored, exactly like the original's per-node connection lists.
                        if (group.Nodes[a].NextNodeIndex != b &&
                            group.Nodes[b].NextNodeIndex != a)
                            continue;
                        if (!adjacency[left].Contains(right))
                        {
                            adjacency[left].Add(right);
                            links++;
                        }
                        if (!adjacency[right].Contains(left))
                        {
                            adjacency[right].Add(left);
                            links++;
                        }
                    }
                }
            }
            LinkCount = links;

            for (int i = 0; i < nodes.Count; i++)
                nodes[i].SetLinks(adjacency[i].AsReadOnly());
        }

        public int NodeCount { get { return nodes.Count; } }
        public int CarNodeCount { get { return carNodes.Count; } }
        public int PedNodeCount { get { return pedNodes.Count; } }
        public int WaterNodeCount { get { return waterNodes.Count; } }

        /// <summary>Adjacency entries; symmetric, so this is twice the number of tile links.</summary>
        public int LinkCount { get; private set; }

        /// <summary>Interior nodes whose `next` names an external slot: their lane leaves the tile.</summary>
        public int ExternalNextCount { get { return externalNexts; } }

        /// <summary>Interior nodes whose `next` names an empty, unknown or missing slot.</summary>
        public int DanglingNextCount { get { return danglingNexts; } }

        /// <summary>Interior nodes whose `next` names their own slot; the original links nothing.</summary>
        public int SelfNextCount { get { return selfNexts; } }

        /// <summary>Type-1 slots: tile boundary markers, never nodes.</summary>
        public int ExternalSlotCount { get { return externalSlots; } }

        /// <summary>Type-0 padding slots plus every slot of a group without a known transform.</summary>
        public int EmptySlotCount { get { return emptySlots; } }

        /// <summary>Slots with a type outside 0..2, ignored like the original ignores them.</summary>
        public int UnknownSlotCount { get { return unknownSlots; } }

        /// <summary>Attached groups with no matching instance, so no usable world transform.</summary>
        public int UnresolvedGroupCount { get; private set; }

        public IList<PathGraphNode> Nodes { get { return readOnlyNodes; } }

        /// <summary>
        /// Nearest node of a kind, mirroring CPathFind::FindNodeClosestToCoors: it walks every
        /// node of the kind and keeps the smallest |dx| + |dy| + 3*|dz| below the limit, so a
        /// node exactly at distLimit is rejected and a level change costs three times as much
        /// as horizontal distance. Like the original it seeds the search with a 10000 unit
        /// sentinel, so a larger radius still cannot select a node beyond it. The original
        /// returns -1 for a non-positive limit and node 0 when nothing was found; an explicit
        /// failure result is used here instead of either fallback. Disabled and
        /// between-level nodes do not exist in the text path data yet.
        /// </summary>
        public bool TryFindNearestNode(Vector3 position, PathKind kind, float distLimit,
            out PathGraphNode node, out float distance)
        {
            if (!(distLimit > 0.0f) || float.IsInfinity(distLimit))
                throw new ArgumentOutOfRangeException("distLimit");
            IList<PathGraphNode> candidates = Candidates(kind);
            node = null;
            distance = 0.0f;
            float best = 10000.0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                PathGraphNode candidate = candidates[i];
                Vector3 offset = candidate.Position - position;
                float metric = Math.Abs(offset.X) + Math.Abs(offset.Y) + 3.0f * Math.Abs(offset.Z);
                if (!(metric < best))
                    continue;
                best = metric;
                node = candidate;
            }
            if (node == null || !(best < distLimit))
            {
                node = null;
                return false;
            }
            distance = best;
            return true;
        }

        private void CountNextTarget(PathGroup group, int slot)
        {
            int next = group.Nodes[slot].NextNodeIndex;
            if (next < 0)
                return;
            if (next == slot)
            {
                selfNexts++;
                return;
            }
            if (next >= group.Nodes.Length)
            {
                danglingNexts++;
                return;
            }
            int type = group.Nodes[next].Type;
            if (type == 2)
                return;
            if (type == 1)
                externalNexts++;
            else
                danglingNexts++;
        }

        private IList<PathGraphNode> Candidates(PathKind kind)
        {
            switch (kind)
            {
                case PathKind.Car:
                    return carNodes;
                case PathKind.Ped:
                    return pedNodes;
                case PathKind.Water:
                    return waterNodes;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }
    }
}