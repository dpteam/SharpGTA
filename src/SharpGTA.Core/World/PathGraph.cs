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
        private float[] linkDistances = new float[0];
        private bool[] linkCrosses = new bool[0];
        private int[] linkCarLinks = new int[0];
        private PathGraph graph;

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

        /// <summary>
        /// Straight-line length of a link, the way `m_distances` stores it for a connection.
        /// </summary>
        public float LinkDistance(int index)
        {
            if (index < 0 || index >= linkDistances.Length)
                throw new ArgumentOutOfRangeException("index");
            return linkDistances[index];
        }

        /// <summary>
        /// `m_connectionFlags.bCrossesRoad` for a link: true when the named side of the tile
        /// pair has `crossing` set. The original only fills this for ped connections.
        /// </summary>
        public bool LinkCrossesRoad(int index)
        {
            if (index < 0 || index >= linkCrosses.Length)
                throw new ArgumentOutOfRangeException("index");
            return linkCrosses[index];
        }

        public PathKind Kind { get { return Group.Kind; } }

        /// <summary>Group-local `next` value: a slot index inside the group, or -1 for none.</summary>
        public int NextNodeIndex { get { return Source.NextNodeIndex; } }

        public int LinkCount { get { return links.Count; } }

        /// <summary>1-based connected component id, like `m_pathNodes[i].group`.</summary>
        public int GroupId { get; private set; }

        internal void SetLinks(ReadOnlyCollection<int> value)
        {
            links = value;
        }

        internal void SetLinkData(float[] distances, bool[] crosses, int[] carLinks,
            PathGraph owner)
        {
            linkDistances = distances;
            linkCrosses = crosses;
            linkCarLinks = carLinks;
            graph = owner;
        }

        /// <summary>True when this connection carries a `CCarPathLink` (car paths only).</summary>
        public bool HasCarLink(int index)
        {
            if (index < 0 || index >= linkCarLinks.Length)
                throw new ArgumentOutOfRangeException("index");
            return linkCarLinks[index] >= 0;
        }

        /// <summary>Lane direction of a car link, normalised in XY and pointing at the lower node.</summary>
        public Vector3 LinkDirection(int index)
        {
            return graph.GetCarLink(GetCarLinkIndex(index)).Direction;
        }

        /// <summary>XY midpoint between the two nodes of a car link.</summary>
        public Vector3 LinkMidpoint(int index)
        {
            return graph.GetCarLink(GetCarLinkIndex(index)).Midpoint;
        }

        /// <summary>Left lanes of a car link, inherited from crossings or defaulted to 1.</summary>
        public int LinkLeftLanes(int index)
        {
            return graph.GetCarLink(GetCarLinkIndex(index)).LeftLanes;
        }

        /// <summary>Right lanes of a car link, inherited from crossings or defaulted to 1.</summary>
        public int LinkRightLanes(int index)
        {
            return graph.GetCarLink(GetCarLinkIndex(index)).RightLanes;
        }

        private int GetCarLinkIndex(int index)
        {
            if (index < 0 || index >= linkCarLinks.Length)
                throw new ArgumentOutOfRangeException("index");
            int carLink = linkCarLinks[index];
            if (carLink < 0)
            {
                throw new InvalidOperationException(
                    "Only car path connections carry a CCarPathLink.");
            }
            return carLink;
        }

        internal void SetGroupId(int value)
        {
            GroupId = value;
        }
    }

    /// <summary>
    /// Path graph over the parsed IDE/IPL path sections, mirroring the node and link part of
    /// CPathFind::PreparePathDataForType. A tile is one (path block, placement) pair: a model
    /// with both a car and a ped block owns two tiles per placement, so TileCount is not
    /// m_numMapObjects and a tile index cannot be compared with it. Only interior slots
    /// (type 2) become nodes, as in the original, where `m_pathNodes` receives nothing else;
    /// external slots (type 1) are tile boundary markers and type-0 slots are padding. Two
    /// interior slots of one tile are linked when either side's `next` names the other, so the
    /// adjacency is symmetric.
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
            internal int LeftLanes;
            internal int RightLanes;
        }

        /// <summary>One `CCarPathLink`: lane direction, midpoint and lane counts per connection.</summary>
        internal sealed class CarLink
        {
            internal Vector3 Direction;
            internal Vector3 Midpoint;
            internal int LeftLanes;
            internal int RightLanes;
            internal int NodeIndex;
        }

        /// <summary>One placed path tile: a group's twelve slots under one world transform.</summary>
        private sealed class Tile
        {
            internal PathGroup Group;
            internal Matrix? Transform;
            internal int[] SlotNodes;
        }

        /// <summary>Original `maxdist` passed to PreparePathDataForType for both path types.</summary>
        private const float DefaultExternMergeDistance = 1.0f;

        private readonly List<PathGraphNode> nodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> carNodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> pedNodes = new List<PathGraphNode>();
        private readonly List<PathGraphNode> waterNodes = new List<PathGraphNode>();
        private readonly List<CarLink> carLinks = new List<CarLink>();
        private readonly ReadOnlyCollection<PathGraphNode> readOnlyNodes;
        private int emptySlots;
        private int externalSlots;
        private int unknownSlots;
        private int externalNexts;
        private int danglingNexts;
        private int selfNexts;

        public PathGraph(PathWorld world)
            : this(world, DefaultExternMergeDistance)
        {
        }

        /// <summary>
        /// Builds the graph, merging external pairs that are closer than
        /// <paramref name="externMergeDistance"/>. The original always uses one unit; a larger
        /// value is a deliberate, documented divergence used to measure how far apart the
        /// authored externals of a tree really are.
        /// </summary>
        public PathGraph(PathWorld world, float externMergeDistance)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            if (!(externMergeDistance > 0.0f) || float.IsInfinity(externMergeDistance))
                throw new ArgumentOutOfRangeException("externMergeDistance");
            ExternMergeDistance = externMergeDistance;

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
            List<bool>[] crossing = new List<bool>[nodes.Count];
            List<int>[] carIndex = new List<int>[nodes.Count];
            for (int i = 0; i < adjacency.Length; i++)
            {
                adjacency[i] = new List<int>(2);
                crossing[i] = new List<bool>(2);
                carIndex[i] = new List<int>(2);
            }

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
                        // The original marks a ped connection as crossing the road when the side that
                        // names the other has its `crossing` flag set; car links carry lane
                        // data instead and leave the flag clear.
                        bool crossesRoad = group.Kind == PathKind.Ped &&
                            ((group.Nodes[a].NextNodeIndex == b && group.Nodes[a].Cross) ||
                             (group.Nodes[b].NextNodeIndex == a && group.Nodes[b].Cross));
                        int carLink = -1;
                        if (group.Kind == PathKind.Car)
                            carLink = RegisterIntraTileCarLink(left, right);
                        Connect(adjacency, crossing, carIndex, left, right, crossesRoad, carLink,
                            ref links);
                    }
                }
            }
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
                int leftLanes = group.Nodes[slot].NumLeftLanes;
                int rightLanes = group.Nodes[slot].NumRightLanes;
                if (nearestId < 0)
                {
                    ExternEntry entry = new ExternEntry();
                    entry.Position = position;
                    entry.NodeIndex = interior;
                    entry.LeftLanes = leftLanes;
                    entry.RightLanes = rightLanes;
                    pending.Add(entry);
                    continue;
                }
                ExternEntry found = pending[nearestId];
                found.Paired = true;
                // The original averages the two collapsed positions and keeps the pair.
                found.Position = (found.Position + position) * 0.5f;
                CrossTileLinkCount++;
                int crossingCarLink = -1;
                if (group.Kind == PathKind.Car)
                {
                    // The original builds the crossing direction from link1 - link2 and takes
                    // the lane counts of the first external slot, swapping them in when the
                    // partner is the one that carries lane data.
                    Vector3 first = nodes[found.NodeIndex].Position;
                    Vector3 second = nodes[interior].Position;
                    float dx = first.X - second.X;
                    float dy = first.Y - second.Y;
                    float magnitude = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (magnitude > 0.0f)
                    {
                        dx /= magnitude;
                        dy /= magnitude;
                    }
                    if (found.LeftLanes != 0 && found.RightLanes != 0 &&
                        (leftLanes == 0 || rightLanes == 0))
                    {
                        found.LeftLanes = rightLanes;
                        found.RightLanes = leftLanes;
                    }
                    crossingCarLink = RegisterCarLink(dx, dy, found.Position.X, found.Position.Y,
                        found.LeftLanes, found.RightLanes, found.NodeIndex);
                }
                Connect(adjacency, crossing, carIndex, found.NodeIndex, interior, false,
                    crossingCarLink, ref links);
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
            {
                PathGraphNode node = nodes[i];
                List<int> targets = adjacency[i];
                node.SetLinks(targets.AsReadOnly());
                float[] distances = new float[targets.Count];
                bool[] crosses = new bool[targets.Count];
                int[] cars = new int[targets.Count];
                for (int k = 0; k < targets.Count; k++)
                {
                    distances[k] = (nodes[targets[k]].Position - node.Position).Length();
                    crosses[k] = crossing[i][k];
                    cars[k] = carIndex[i][k];
                    if (crosses[k])
                        CrossingLinkCount++;
                }
                node.SetLinkData(distances, crosses, cars, this);
            }

            CarLinkCount = carLinks.Count;
            InheritCarLanes(carIndex);
            AssignFloodFillGroups();
        }

        /// <summary>
        /// Labels connected components the way CPathFind::CountFloodFillGroups does: walk the
        /// symmetric links from the first unvisited node, one component id per kind, and leave
        /// link-less nodes as single-node components. The traversal order only affects ids, not
        /// membership, and ids are assigned in node order so they stay deterministic.
        /// </summary>
        private void AssignFloodFillGroups()
        {
            int[] sizes = new int[3];
            Stack<int> pending = new Stack<int>();
            int next = 0;
            for (int start = 0; start < nodes.Count; start++)
            {
                if (nodes[start].GroupId != 0)
                    continue;
                next++;
                pending.Push(start);
                nodes[start].SetGroupId(next);
                int size = 0;
                while (pending.Count != 0)
                {
                    PathGraphNode node = nodes[pending.Pop()];
                    size++;
                    IList<int> links = node.Links;
                    for (int i = 0; i < links.Count; i++)
                    {
                        if (nodes[links[i]].GroupId != 0)
                            continue;
                        nodes[links[i]].SetGroupId(next);
                        pending.Push(links[i]);
                    }
                }
                ComponentCount++;
                sizes[(int)nodes[start].Kind]++;
                if (size == 1)
                    SingleNodeComponentCount++;
                if (size > LargestComponentSize)
                    LargestComponentSize = size;
            }
            CarComponentCount = sizes[(int)PathKind.Car];
            PedComponentCount = sizes[(int)PathKind.Ped];
            WaterComponentCount = sizes[(int)PathKind.Water];
        }

        public int NodeCount { get { return nodes.Count; } }
        public int CarNodeCount { get { return carNodes.Count; } }
        public int PedNodeCount { get { return pedNodes.Count; } }
        public int WaterNodeCount { get { return waterNodes.Count; } }

        /// <summary>Placed tiles: one per bound instance, or one per detached group.</summary>
        public int TileCount { get; private set; }

        /// <summary>Connected components over the symmetric links, like CountFloodFillGroups.</summary>
        public int ComponentCount { get; private set; }

        public int CarComponentCount { get; private set; }
        public int PedComponentCount { get; private set; }
        public int WaterComponentCount { get; private set; }

        /// <summary>Components holding exactly one node, i.e. a lane with no link at all.</summary>
        public int SingleNodeComponentCount { get; private set; }

        public int LargestComponentSize { get; private set; }

        /// <summary>Distance under which two external slots are treated as one crossing.</summary>
        public float ExternMergeDistance { get; private set; }

        /// <summary>
        /// Shortest route between two already resolved nodes over the symmetric links, weighted
        /// by <see cref="PathGraphNode.LinkDistance"/>. Dijkstra with an explicit heap; the
        /// original runs a bucket queue over the same distances, so the cost is the same but
        /// equal-length ties may resolve differently. Returns false when the nodes are not
        /// connected, which is common on III because its lanes are fragmented.
        /// </summary>
        public bool TryFindRoute(int startIndex, int goalIndex, out int[] route, out float length)
        {
            if (startIndex < 0 || startIndex >= nodes.Count)
                throw new ArgumentOutOfRangeException("startIndex");
            if (goalIndex < 0 || goalIndex >= nodes.Count)
                throw new ArgumentOutOfRangeException("goalIndex");

            route = null;
            length = 0.0f;
            if (startIndex == goalIndex)
            {
                route = new int[] { startIndex };
                return true;
            }

            float[] best = new float[nodes.Count];
            int[] previous = new int[nodes.Count];
            bool[] settled = new bool[nodes.Count];
            for (int i = 0; i < best.Length; i++)
            {
                best[i] = float.PositiveInfinity;
                previous[i] = -1;
            }
            NodeHeap heap = new NodeHeap(nodes.Count);
            best[startIndex] = 0.0f;
            heap.Push(0.0f, startIndex);
            bool found = false;
            while (heap.Count != 0)
            {
                float distance;
                int index;
                heap.Pop(out distance, out index);
                if (settled[index])
                    continue;
                settled[index] = true;
                if (index == goalIndex)
                {
                    found = true;
                    break;
                }
                PathGraphNode node = nodes[index];
                IList<int> links = node.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    int target = links[i];
                    if (settled[target])
                        continue;
                    float candidate = distance + node.LinkDistance(i);
                    if (!(candidate < best[target]))
                        continue;
                    best[target] = candidate;
                    previous[target] = index;
                    heap.Push(candidate, target);
                }
            }
            if (!found)
                return false;

            int count = 1;
            for (int at = goalIndex; at != startIndex; at = previous[at])
                count++;
            int[] result = new int[count];
            int position = count - 1;
            for (int at = goalIndex; at != -1; at = previous[at])
            {
                result[position--] = at;
                if (at == startIndex)
                    break;
            }
            route = result;
            length = best[goalIndex];
            return true;
        }

        /// <summary>Min-heap keyed by (distance, node index) so equal lengths stay ordered.</summary>
        private sealed class NodeHeap
        {
            private readonly float[] keys;
            private readonly int[] items;
            private int count;

            internal NodeHeap(int capacity)
            {
                keys = new float[capacity * 2 + 1];
                items = new int[capacity * 2 + 1];
            }

            internal int Count { get { return count; } }

            internal void Push(float key, int item)
            {
                if (count == keys.Length)
                    throw new InvalidOperationException("Path heap overflow.");
                keys[count] = key;
                items[count] = item;
                int index = count++;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(index, parent))
                        break;
                    Swap(index, parent);
                    index = parent;
                }
            }

            internal void Pop(out float key, out int item)
            {
                key = keys[0];
                item = items[0];
                count--;
                if (count == 0)
                    return;
                keys[0] = keys[count];
                items[0] = items[count];
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= count)
                        break;
                    int smallest = left;
                    int right = left + 1;
                    if (right < count && Less(right, left))
                        smallest = right;
                    if (!Less(smallest, index))
                        break;
                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private bool Less(int left, int right)
            {
                if (keys[left] != keys[right])
                    return keys[left] < keys[right];
                return items[left] < items[right];
            }

            private void Swap(int left, int right)
            {
                float key = keys[left];
                keys[left] = keys[right];
                keys[right] = key;
                int item = items[left];
                items[left] = items[right];
                items[right] = item;
            }
        }

        /// <summary>Adjacency entries, including both directions of every tile and external link.</summary>
        public int LinkCount { get; private set; }

        /// <summary>Adjacency entries whose `bCrossesRoad` flag is set.</summary>
        public int CrossingLinkCount { get; private set; }

        /// <summary>`CCarPathLink` entries: one per physical car connection.</summary>
        public int CarLinkCount { get; private set; }

        /// <summary>Lane sides that fell back to the default of one lane.</summary>
        public int CarLinkLaneFallbackCount { get; private set; }

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
        private int FindNearestExtern(List<ExternEntry> pending, Vector3 position)
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

        private static void Connect(List<int>[] adjacency, List<bool>[] crossing,
            List<int>[] carIndex, int left, int right, bool crossesRoad, int carLink,
            ref int links)
        {
            if (left < 0 || right < 0 || left == right)
                return;
            if (!adjacency[left].Contains(right))
            {
                adjacency[left].Add(right);
                crossing[left].Add(crossesRoad);
                carIndex[left].Add(carLink);
                links++;
            }
            if (!adjacency[right].Contains(left))
            {
                adjacency[right].Add(left);
                crossing[right].Add(crossesRoad);
                carIndex[right].Add(carLink);
                links++;
            }
        }

        /// <summary>
        /// `CCarPathLink` entry for a tile pair: the original stores the direction toward the
        /// lower node index, the midpoint of the two nodes, and -1 lane counts that later get
        /// inherited from the external crossings. Entries are shared by both directions of one
        /// physical link, exactly like `m_carPathLinks`.
        /// </summary>
        private int RegisterIntraTileCarLink(int left, int right)
        {
            PathGraphNode first = nodes[left];
            PathGraphNode second = nodes[right];
            float posX = (first.Position.X + second.Position.X) * 0.5f;
            float posY = (first.Position.Y + second.Position.Y) * 0.5f;
            float dx = second.Position.X - first.Position.X;
            float dy = second.Position.Y - first.Position.Y;
            float magnitude = (float)Math.Sqrt(dx * dx + dy * dy);
            if (magnitude > 0.0f)
            {
                dx /= magnitude;
                dy /= magnitude;
            }
            if (left < right)
            {
                // The original flips the direction so it always points at the lower index.
                dx = -dx;
                dy = -dy;
            }
            return RegisterCarLink(dx, dy, posX, posY, -1, -1, left);
        }

        private int RegisterCarLink(float dirX, float dirY, float posX, float posY,
            int leftLanes, int rightLanes, int nodeIndex)
        {
            for (int k = 0; k < carLinks.Count; k++)
            {
                CarLink existing = carLinks[k];
                if (existing.Direction.X == dirX && existing.Direction.Y == dirY &&
                    existing.Midpoint.X == posX && existing.Midpoint.Y == posY)
                    return k;
            }
            CarLink link = new CarLink();
            link.Direction = new Vector3(dirX, dirY, 0.0f);
            link.Midpoint = new Vector3(posX, posY, 0.0f);
            link.LeftLanes = leftLanes;
            link.RightLanes = rightLanes;
            link.NodeIndex = nodeIndex;
            carLinks.Add(link);
            return carLinks.Count - 1;
        }

        /// <summary>
        /// Lane counts come only from external crossings, so the original propagates them along
        /// nodes that own exactly two links (swapping left and right when the source link points
        /// back at that node) and finally falls back to one lane per side.
        /// </summary>
        private void InheritCarLanes(List<int>[] carIndex)
        {
            bool done = false;
            for (int pass = 0; !done && pass < 10; pass++)
            {
                done = true;
                for (int i = 0; i < nodes.Count; i++)
                {
                    List<int> indexes = carIndex[i];
                    if (indexes.Count != 2)
                        continue;
                    int firstIndex = indexes[0];
                    int secondIndex = indexes[1];
                    if (firstIndex < 0 || secondIndex < 0)
                        continue;
                    CarLink first = carLinks[firstIndex];
                    CarLink second = carLinks[secondIndex];
                    if (first.LeftLanes == -1 && second.LeftLanes != -1)
                    {
                        done = false;
                        if (second.NodeIndex == i)
                        {
                            first.LeftLanes = second.RightLanes;
                            first.RightLanes = second.LeftLanes;
                        }
                        else
                        {
                            first.LeftLanes = second.LeftLanes;
                            first.RightLanes = second.RightLanes;
                        }
                        first.NodeIndex = i;
                    }
                    else if (first.LeftLanes != -1 && second.LeftLanes == -1)
                    {
                        done = false;
                        if (first.NodeIndex == i)
                        {
                            second.LeftLanes = first.RightLanes;
                            second.RightLanes = first.LeftLanes;
                        }
                        else
                        {
                            second.LeftLanes = first.LeftLanes;
                            second.RightLanes = first.RightLanes;
                        }
                        second.NodeIndex = i;
                    }
                    else if (first.LeftLanes == -1 && second.LeftLanes == -1)
                    {
                        done = false;
                    }
                }
            }
            for (int k = 0; k < carLinks.Count; k++)
            {
                if (carLinks[k].LeftLanes < 0)
                {
                    carLinks[k].LeftLanes = 1;
                    CarLinkLaneFallbackCount++;
                }
                if (carLinks[k].RightLanes < 0)
                {
                    carLinks[k].RightLanes = 1;
                    CarLinkLaneFallbackCount++;
                }
            }
        }

        internal CarLink GetCarLink(int index)
        {
            return carLinks[index];
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