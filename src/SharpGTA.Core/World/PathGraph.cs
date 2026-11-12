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

        internal PathGraphNode(int globalIndex, int groupIndex, int tileIndex, PathGroup group,
            PathNode source, int localIndex, Vector3 position)
        {
            GlobalIndex = globalIndex;
            GroupIndex = groupIndex;
            TileIndex = tileIndex;
            Group = group;
            Source = source;
            LocalIndex = localIndex;
            Position = position;
        }

        /// <summary>Index in <see cref="PathGraph.Nodes"/>, in tile and slot order.</summary>
        public int GlobalIndex { get; private set; }

        /// <summary>
        /// One placed tile per instance, like the original's `m_mapObjects`; a model placed
        /// twice produces two tiles with the same <see cref="GroupIndex"/>.
        /// </summary>
        public int TileIndex { get; private set; }

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
    /// Path graph over the parsed IDE/IPL path sections, mirroring the node and link part of
    /// CPathFind::PreparePathDataForType. Only interior slots (type 2) become nodes, as in the
    /// original, where `m_pathNodes` receives nothing else; external slots (type 1) are tile
    /// boundary markers and type-0 slots are padding. Two interior slots of one tile are
    /// linked when either side's `next` names the other, so the adjacency is symmetric.
    ///
    /// Neighbouring tiles are joined the way the original does it: two external slots of the
    /// same kind closer than one unit collapse into one connection between the interior nodes
    /// behind them. Tile union-find groups, `m_connections` flags (`bCrossesRoad`,
    /// `bTrafficLight`), the collapsed lane direction/lane counts and any route search are not
    /// reproduced yet, so the graph is connected but no path is computed.
    /// </summary>
    public sealed class PathGraph
    {
        private sealed class ExternEntry
        {
            internal Vector3 Position;
            internal int NodeIndex;
            internal bool Paired;
        }

        /// <summary>One placed path tile: a group's twelve slots under one world transform.</summary>
        private sealed class Tile
        {
            internal int GroupIndex;
            internal PathGroup Group;
            internal Matrix? Transform;
            internal int[] SlotNodes;
        }

        /// <summary>Original `maxdist` passed to PreparePathDataForType for both path types.</summary>
        private const float ExternMergeDistance = 1.0f;

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

            List<Tile> tiles = new List<Tile>();
            List<int[]> externSlots = new List<int[]>();
            for (int g = 0; g < world.GroupCount; g++)
            {
                PathGroup group = world.Groups[g];
                Matrix[] transforms = TileTransforms(group);
                if (transforms == null)
                {
                    // No bound instance and no explicit transform: the tile origin is
                    // unknown, so its slots stay out of the graph instead of being guessed.
                    UnresolvedGroupCount++;
                    emptySlots += group.Nodes.Length;
                    continue;
                }
                for (int t = 0; t < transforms.Length; t++)
                {
                    Tile tile = new Tile();
                    tile.GroupIndex = g;
                    tile.Group = group;
                    tile.Transform = group.IsDetached ? (Matrix?)null : new Matrix?(transforms[t]);
                    tile.SlotNodes = new int[group.Nodes.Length];
                    for (int i = 0; i < tile.SlotNodes.Length; i++)
                        tile.SlotNodes[i] = -1;
                    tiles.Add(tile);
                    TileCount++;
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
                            externSlots.Add(new int[] { tiles.Count - 1, n });
                            continue;
                        }
                        if (source.Type != 2)
                        {
                            unknownSlots++;
                            continue;
                        }
                        PathGraphNode node = new PathGraphNode(nodes.Count, g, tiles.Count - 1, group, source, n,
                            world.GetWorldPosition(group, source, tile.Transform));
                        tile.SlotNodes[n] = nodes.Count;
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
            }
            readOnlyNodes = nodes.AsReadOnly();

            List<int>[] adjacency = new List<int>[nodes.Count];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>(2);

            int links = 0;
            for (int t = 0; t < tiles.Count; t++)
            {
                Tile tile = tiles[t];
                int[] map = tile.SlotNodes;
                PathGroup group = tile.Group;
                for (int a = 0; a < map.Length; a++)
                {
                    int left = map[a];
                    if (left < 0)
                        continue;
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
                        Connect(adjacency, left, right, ref links);
                    }
                }
            }
            LinkCount = links;

            for (int t = 0; t < tiles.Count; t++)
            {
                PathGroup group = tiles[t].Group;
                for (int a = 0; a < tiles[t].SlotNodes.Length; a++)
                {
                    if (tiles[t].SlotNodes[a] >= 0)
                        CountNextTarget(group, a);
                }
            }

            // Join neighbouring tiles: two same-kind external slots closer than one unit are
            // one crossing, so the interior nodes behind them become neighbours.
            List<ExternEntry> carPending = new List<ExternEntry>();
            List<ExternEntry> pedPending = new List<ExternEntry>();
            List<ExternEntry> waterPending = new List<ExternEntry>();
            for (int e = 0; e < externSlots.Count; e++)
            {
                Tile tile = tiles[externSlots[e][0]];
                PathGroup group = tile.Group;
                int slot = externSlots[e][1];
                Vector3 position = world.GetWorldPosition(group, group.Nodes[slot], tile.Transform);
                int interior = ResolveExternInterior(group, tile.SlotNodes, slot);
                if (interior < 0)
                {
                    // The original would index m_nodeIndices[-1] here; the data is expected to
                    // be consistent, so a broken tile is reported instead of guessed.
                    UnresolvedExternCount++;
                    continue;
                }
                List<ExternEntry> pending = Pending(carPending, pedPending, waterPending, group.Kind);
                int nearestId = FindNearestExtern(pending, position);
                if (nearestId < 0)
                {
                    ExternEntry entry = new ExternEntry();
                    entry.Position = position;
                    entry.NodeIndex = interior;
                    pending.Add(entry);
                    continue;
                }
                ExternEntry found = pending[nearestId];
                found.Paired = true;
                // The original averages the two collapsed positions and keeps the pair.
                found.Position = (found.Position + position) * 0.5f;
                CrossTileLinkCount++;
                Connect(adjacency, found.NodeIndex, interior, ref links);
            }
            LinkCount = links;

            for (int i = 0; i < carPending.Count; i++)
            {
                if (!carPending[i].Paired)
                    UnpairedExternCount++;
            }
            for (int i = 0; i < pedPending.Count; i++)
            {
                if (!pedPending[i].Paired)
                    UnpairedExternCount++;
            }
            for (int i = 0; i < waterPending.Count; i++)
            {
                if (!waterPending[i].Paired)
                    UnpairedExternCount++;
            }

            for (int i = 0; i < nodes.Count; i++)
                nodes[i].SetLinks(adjacency[i].AsReadOnly());
        }

        public int NodeCount { get { return nodes.Count; } }
        public int CarNodeCount { get { return carNodes.Count; } }
        public int PedNodeCount { get { return pedNodes.Count; } }
        public int WaterNodeCount { get { return waterNodes.Count; } }

        /// <summary>Placed tiles: one per bound instance, or one per detached group.</summary>
        public int TileCount { get; private set; }

        /// <summary>Adjacency entries, including both directions of every tile and external link.</summary>
        public int LinkCount { get; private set; }

        /// <summary>External pairs collapsed into one crossing between two neighbouring tiles.</summary>
        public int CrossTileLinkCount { get; private set; }

        /// <summary>External slots that never found a partner within one unit.</summary>
        public int UnpairedExternCount { get; private set; }

        /// <summary>External slots whose interior node could not be resolved from `next`.</summary>
        public int UnresolvedExternCount { get; private set; }

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

        /// <summary>
        /// Interior node behind an external slot: the slot it names with `next`, or, when the
        /// external does not name one (the normal VC case), the slot whose `next` names this
        /// external. Returns -1 when neither side resolves to an interior slot.
        /// </summary>
        private static int ResolveExternInterior(PathGroup group, int[] internNodes, int slot)
        {
            int next = group.Nodes[slot].NextNodeIndex;
            if (next < 0)
            {
                next = -1;
                for (int k = 0; k < group.Nodes.Length; k++)
                {
                    if (k != slot && group.Nodes[k].NextNodeIndex == slot)
                    {
                        next = k;
                        break;
                    }
                }
            }
            if (next < 0 || next >= group.Nodes.Length)
                return -1;
            return group.Nodes[next].Type == 2 ? internNodes[next] : -1;
        }

        /// <summary>
        /// Nearest waiting external of the same kind, reproducing the original's search: a
        /// candidate must beat the current best on both |dx| and |dy|, and the best shrinks to
        /// Max(|dx|, |dy|). Strictly nearer only, so ties keep the earlier slot.
        /// </summary>
        private static int FindNearestExtern(List<ExternEntry> pending, Vector3 position)
        {
            int nearestId = -1;
            float nearestDist = ExternMergeDistance;
            for (int k = 0; k < pending.Count; k++)
            {
                if (pending[k].Paired)
                    continue;
                float dx = Math.Abs(pending[k].Position.X - position.X);
                if (!(dx < nearestDist))
                    continue;
                float dy = Math.Abs(pending[k].Position.Y - position.Y);
                if (!(dy < nearestDist))
                    continue;
                nearestDist = Math.Max(dx, dy);
                nearestId = k;
            }
            return nearestId;
        }

        /// <summary>
        /// Transforms to build tiles from: one per bound instance, a single transform when only
        /// the explicit one is known, or null when an attached group has no placement at all.
        /// </summary>
        private static Matrix[] TileTransforms(PathGroup group)
        {
            if (group.IsDetached)
                return new Matrix[1];
            if (group.InstanceTransforms != null && group.InstanceTransforms.Length != 0)
                return group.InstanceTransforms;
            if (group.HasTransform)
                return new Matrix[] { group.ObjectTransform.Value };
            return null;
        }

        private static List<ExternEntry> Pending(List<ExternEntry> car, List<ExternEntry> ped,
            List<ExternEntry> water, PathKind kind)
        {
            switch (kind)
            {
                case PathKind.Car:
                    return car;
                case PathKind.Ped:
                    return ped;
                case PathKind.Water:
                    return water;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        private static void Connect(List<int>[] adjacency, int left, int right, ref int links)
        {
            if (left < 0 || right < 0 || left == right)
                return;
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