using System;
using System.Collections.Generic;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Drives vehicles and peds along a resolved path route at a constant speed. This is the
    /// first gameplay motion that follows the network instead of drifting in a straight line:
    /// each assigned entity walks its node chain, takes its heading from the current segment
    /// and stops at the end. Cars walk car nodes and peds walk ped nodes, but one module owns
    /// both so a mixed spawn still has exactly one position owner per entity. There is no
    /// physics, collision response, lane offset or traffic rule yet, and the follower owns its
    /// actors' positions (their plain integration is overwritten every tick).
    /// </summary>
    public sealed class PathFollowerModule : ISimulationModule, IStateSerializable
    {
        /// <summary>Upper bound on segment steps per tick, so a bad route cannot stall a frame.</summary>
        private const int MaximumStepsPerTick = 256;

        private readonly PathGraph graph;
        private readonly StaticWorld world;
        private readonly List<Follower> followers = new List<Follower>();
        private readonly List<DynamicEntity> retired = new List<DynamicEntity>();
        private float travelled;
        private int completed;
        private int replanned;
        private int stalledSteps;
        private int trafficHolds;

        private uint lightMilliseconds;

        /// <summary>
        /// Optional route provider: when an actor reaches the end of its chain the module asks for
        /// a fresh route from that node so it keeps moving. Returning null or a short route
        /// leaves the actor parked, which is the documented behaviour without a provider.
        /// </summary>
        public Func<int, int[]> RouteSource { get; set; }

        /// <summary>
        /// When true a finished vehicle is handed back through <see cref="TryTakeRetired"/> instead
        /// of staying parked: traffic recycling despawns it and spawns a replacement elsewhere.
        /// The module stops owning its position at that point, so the caller must despawn it.
        /// </summary>
        public bool RecycleVehicles { get; set; }

        /// <summary>Same as <see cref="RecycleVehicles"/>, for peds.</summary>
        public bool RecyclePeds { get; set; }

        /// <summary>
        /// When true vehicles stop at junction lights: a red or yellow link within eight units of
        /// its midpoint on the approach side holds the car until the phase turns green. Off by
        /// default so a probe that measures constant speed does not silently stop; the headless
        /// and preview wiring turn it on.
        /// </summary>
        public bool ObeyTrafficLights { get; set; }

        /// <summary>
        /// Gap kept behind another vehicle in the same lane, in world units; zero disables car
        /// following. The original relies on real collision volumes (`CPhysical`), which do not
        /// exist here, so this is the stand-in that makes a queue form behind a red light instead
        /// of every car standing in the same spot.
        /// </summary>
        public float VehicleGap { get; set; }

        public PathFollowerModule(PathGraph graph)
            : this(graph, null)
        {
        }

        /// <summary>
        /// The optional world keeps the sector index in sync when a follower repositions or
        /// snaps an actor; without it the caller owns that bookkeeping.
        /// </summary>
        public PathFollowerModule(PathGraph graph, StaticWorld world)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            this.graph = graph;
            this.world = world;
        }

        public string Name { get { return "PathFollowers"; } }

        public int Count { get { return followers.Count; } }
        public int CompletedCount { get { return completed; } }

        /// <summary>Followers of one entity kind, so a population can adopt an existing spawn.</summary>
        public int CountOf(WorldEntityKind kind)
        {
            int count = 0;
            for (int i = 0; i < followers.Count; i++)
            {
                if (followers[i].Entity.Kind == kind)
                    count++;
            }
            return count;
        }

        /// <summary>Routes that were replaced by a new one from <see cref="RouteSource"/>.</summary>
        public int ReplanCount { get { return replanned; } }

        /// <summary>Ticks that hit the per-tick step budget because a route made no progress.</summary>
        public int StalledStepCount { get { return stalledSteps; } }

        /// <summary>
        /// Actor-ticks spent waiting: at a red or yellow light, or behind the vehicle ahead in the
        /// same lane.
        /// </summary>
        public int TrafficHoldCount { get { return trafficHolds; } }

        /// <summary>Total distance the followers have driven, for probes and diagnostics.</summary>
        public float TravelledDistance { get { return travelled; } }

        /// <summary>Finished actors waiting for their owner to despawn them.</summary>
        public int RetiredCount { get { return retired.Count; } }

        /// <summary>
        /// Takes the oldest finished actor off the retired queue. The entity is still in the
        /// world; the caller decides whether to despawn it or reuse it.
        /// </summary>
        public bool TryTakeRetired(out DynamicEntity entity)
        {
            if (retired.Count == 0)
            {
                entity = null;
                return false;
            }
            entity = retired[0];
            retired.RemoveAt(0);
            return true;
        }

        /// <summary>
        /// Stops following an entity, for example before despawning it. The entity keeps its
        /// current position and velocity, so the caller decides what happens next.
        /// </summary>
        public bool Release(DynamicEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            for (int i = 0; i < followers.Count; i++)
            {
                if (!ReferenceEquals(followers[i].Entity, entity))
                    continue;
                followers.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>True when this module currently drives the entity.</summary>
        public bool Contains(DynamicEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            for (int i = 0; i < followers.Count; i++)
            {
                if (ReferenceEquals(followers[i].Entity, entity))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Assigns a resolved route to a spawned vehicle or ped. The actor is snapped to the
        /// first node, so callers should spawn it there; a shorter route is rejected.
        /// </summary>
        public bool Assign(DynamicEntity entity, int[] route, float speed)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            if (route == null)
                throw new ArgumentNullException("route");
            // Only gameplay actors walk paths: objects have no heading and no route semantics.
            if (entity.Kind != WorldEntityKind.Vehicle && entity.Kind != WorldEntityKind.Ped)
                return false;
            if (route.Length < 2)
                return false;
            if (!(speed > 0.0f) || float.IsInfinity(speed))
                throw new ArgumentOutOfRangeException("speed");
            for (int i = 0; i < route.Length; i++)
            {
                if (route[i] < 0 || route[i] >= graph.NodeCount)
                {
                    throw new ArgumentOutOfRangeException("route",
                        "Route contains a node index outside the graph.");
                }
            }

            for (int i = 0; i < followers.Count; i++)
            {
                if (ReferenceEquals(followers[i].Entity, entity))
                    throw new InvalidOperationException("The entity is already following a route.");
            }

            Follower follower = new Follower();
            follower.Entity = entity;
            follower.Route = (int[])route.Clone();
            follower.Speed = speed;
            follower.Lateral = LateralFor(follower, 0);
            followers.Add(follower);
            entity.Velocity = Vector3.Zero;
            Move(entity, graph.Nodes[follower.Route[0]].Position + follower.Lateral);
            return true;
        }

        /// <summary>
        /// Lane offset for one route segment, from the `CCarPathLink` of the connection the actor
        /// is about to drive. Peds have no car links and stay on the node line; a car on a link
        /// whose direction has no lanes (a one-way road driven against its direction) also keeps
        /// the node line instead of inventing a lane.
        /// </summary>
        private Vector3 LateralFor(Follower follower, int segment)
        {
            DynamicEntity entity = follower.Entity;
            if (entity.Kind != WorldEntityKind.Vehicle)
                return Vector3.Zero;
            int fromNode = follower.Route[segment];
            int toNode = follower.Route[segment + 1];
            PathGraphNode node = graph.Nodes[fromNode];
            int link = node.FindLink(toNode);
            if (link < 0 || !node.HasCarLink(link))
                return Vector3.Zero;
            int left = node.LinkLeftLanes(link);
            int right = node.LinkRightLanes(link);
            // The stored direction points at the lower node index, so travelling to a lower index
            // is the original's +1 direction; the lane counts and the offset side both follow it.
            int travel = toNode < fromNode ? 1 : -1;
            int lanes = CarLanes.LanesInDirection(left, right, travel);
            if (lanes <= 0)
                return Vector3.Zero;
            // The original picks a random lane; this is a stable per-actor choice so two runs of
            // the same spawn hash the same, and there is no lane switching yet.
            int lane = (entity.Handle & 0x7FFFFFFF) % lanes;
            Vector3 forward = node.LinkDirection(link);
            if (travel < 0)
                forward = -forward;
            return CarLanes.Offset(forward, lane + CarLanes.OneWayLaneOffset(left, right));
        }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            float delta = context.Time.DeltaSeconds;
            if (!(delta > 0.0f))
                return;
            lightMilliseconds = context.Time.Milliseconds;
            for (int i = 0; i < followers.Count; i++)
            {
                if (!followers[i].Finished)
                    Advance(followers[i], delta);
            }
            ReapFinished();
        }

        /// <summary>
        /// Moves finished followers into the retired queue so their owner can replace them. Kept
        /// out of the advance loop: removing entries while walking the list would skip actors and
        /// shift the order the state hash depends on.
        /// </summary>
        private void ReapFinished()
        {
            int write = 0;
            for (int read = 0; read < followers.Count; read++)
            {
                Follower follower = followers[read];
                if (follower.Finished && IsRecyclable(follower.Entity.Kind))
                {
                    retired.Add(follower.Entity);
                    continue;
                }
                followers[write] = follower;
                write++;
            }
            if (write != followers.Count)
                followers.RemoveRange(write, followers.Count - write);
        }

        private bool IsRecyclable(WorldEntityKind kind)
        {
            if (kind == WorldEntityKind.Vehicle)
                return RecycleVehicles;
            if (kind == WorldEntityKind.Ped)
                return RecyclePeds;
            return false;
        }

        private void Advance(Follower follower, float deltaSeconds)
        {
            float remaining = follower.Speed * deltaSeconds;
            int steps = 0;
            while (remaining > 0.0f && !follower.Finished)
            {
                if (++steps > MaximumStepsPerTick)
                {
                    // A route built from zero-length segments, or a provider that keeps
                    // returning them, must not spin forever inside one tick.
                    stalledSteps++;
                    return;
                }
                Vector3 start = graph.Nodes[follower.Route[follower.Segment]].Position + follower.Lateral;
                Vector3 end = graph.Nodes[follower.Route[follower.Segment + 1]].Position + follower.Lateral;
                float length = (end - start).Length();
                if (length <= 0.0001f)
                {
                    NextSegment(follower, end);
                    continue;
                }
                float left = length - follower.Progress;
                if (left <= 0.0f)
                {
                    // Float rounding can put progress exactly on the node; advance instead of
                    // letting the zero-length remainder look like a held car.
                    NextSegment(follower, end);
                    continue;
                }
                float allowed = left;
                bool blocked = false;
                if (ObeyTrafficLights)
                {
                    float limit = StopLimit(follower, start, end, length);
                    if (limit < allowed)
                    {
                        allowed = limit;
                        blocked = true;
                    }
                }
                float gap = FollowingLimit(follower);
                if (gap < allowed)
                {
                    allowed = gap;
                    blocked = true;
                }
                if (allowed <= 0.0f)
                {

                    // Parked on the stop line: hold the position and report the wait.
                    follower.Entity.Velocity = Vector3.Zero;
                    trafficHolds++;
                    return;
                }
                if (remaining < allowed)
                {
                    follower.Progress += remaining;
                    travelled += remaining;
                    PlaceOnSegment(follower, start, end, length);
                    return;
                }
                follower.Progress += allowed;
                travelled += allowed;
                remaining -= allowed;
                if (blocked)
                {
                    PlaceOnSegment(follower, start, end, length);
                    follower.Entity.Velocity = Vector3.Zero;
                    trafficHolds++;
                    return;
                }
                NextSegment(follower, end);
            }
        }

        /// <summary>
        /// Distance this actor may still travel before it reaches the vehicle ahead in the same
        /// lane, or <see cref="float.MaxValue"/>. Only a leader on the same directed segment and in
        /// the same lane counts; cars in other lanes, in the opposite direction or already past the
        /// node do not block. A leader exactly beside us does not count either, so two actors that
        /// start on one node are not deadlocked against each other.
        /// </summary>
        private float FollowingLimit(Follower follower)
        {
            if (!(VehicleGap > 0.0f))
                return float.MaxValue;
            if (follower.Entity.Kind != WorldEntityKind.Vehicle)
                return float.MaxValue;
            int fromNode = follower.Route[follower.Segment];
            int toNode = follower.Route[follower.Segment + 1];
            float limit = float.MaxValue;
            for (int i = 0; i < followers.Count; i++)
            {
                Follower other = followers[i];
                if (ReferenceEquals(other, follower))
                    continue;
                if (other.Entity.Kind != WorldEntityKind.Vehicle)
                    continue;
                if (other.Segment > other.Route.Length - 2)
                    continue;
                if (other.Route[other.Segment] != fromNode ||
                    other.Route[other.Segment + 1] != toNode)
                {
                    continue;
                }
                if (!SameLane(other.Lateral, follower.Lateral))
                    continue;
                if (!(other.Progress > follower.Progress))
                    continue;
                float gap = other.Progress - follower.Progress - VehicleGap;
                if (gap < limit)
                    limit = gap;
            }
            return limit;
        }

        private static bool SameLane(Vector3 left, Vector3 right)
        {
            return Math.Abs(left.X - right.X) < 0.01f && Math.Abs(left.Y - right.Y) < 0.01f;
        }

        /// <summary>
        /// Distance this actor may still travel on the segment before it must stand at a red or
        /// yellow light, or <see cref="float.MaxValue"/> when the segment is free. The stop line is
        /// eight units before the link midpoint on the approach side, exactly where
        /// `CTrafficLights::ShouldCarStopForLight` blocks a car; a car already inside that window
        /// holds immediately instead of rolling through.
        /// </summary>
        private float StopLimit(Follower follower, Vector3 start, Vector3 end, float length)
        {
            if (follower.Entity.Kind != WorldEntityKind.Vehicle)
                return float.MaxValue;
            int fromNode = follower.Route[follower.Segment];
            int toNode = follower.Route[follower.Segment + 1];
            PathGraphNode node = graph.Nodes[fromNode];
            int link = node.FindLink(toNode);
            if (link < 0 || !node.HasCarLink(link))
                return float.MaxValue;
            int type = node.LinkTrafficLightType(link);

            if (type == 0)
                return float.MaxValue;
            int owner = node.LinkOwnerNode(link);
            bool directionFlag = node.LinkTrafficLightDirection(link);
            // The light only applies when the link points at the node we are heading for, unless
            // the scan flagged the direction, which flips that requirement.
            if (directionFlag != (owner != toNode))
                return float.MaxValue;
            TrafficLightPhase phase = type == 1
                ? TrafficLights.PhaseCars1(lightMilliseconds)
                : TrafficLights.PhaseCars2(lightMilliseconds);
            if (phase == TrafficLightPhase.Green)
                return float.MaxValue;

            Vector3 midpoint = node.LinkMidpoint(link);
            Vector3 direction = node.LinkDirection(link);
            // +1 travels toward the lower node index, which is the direction the link stores.
            int travel = toNode < fromNode ? 1 : -1;
            float target = travel > 0 ? -8.0f : 8.0f;
            float startDistance = Vector3.Dot(start - midpoint, direction);
            float endDistance = Vector3.Dot(end - midpoint, direction);
            if (endDistance == startDistance)
                return float.MaxValue;
            float progress = follower.Progress / length;
            float current = startDistance + (endDistance - startDistance) * progress;
            bool inWindow = travel > 0
                ? (current < 0.0f && current > -8.0f)
                : (current > 0.0f && current < 8.0f);
            if (inWindow)
                return 0.0f;
            float atTarget = (target - startDistance) / (endDistance - startDistance);
            float remainingToTarget = (atTarget - progress) * length;
            if (remainingToTarget > 0.0f)
                return remainingToTarget;
            // The stop line is at or just behind us. Hold only while we are still on the approach
            // side of the midpoint; a car that already committed through the junction keeps going.
            bool beforeMidpoint = travel > 0 ? current < 0.0f : current > 0.0f;
            return beforeMidpoint ? 0.0f : float.MaxValue;
        }

        private void PlaceOnSegment(Follower follower, Vector3 start, Vector3 end, float length)
        {
            Vector3 position = start + (end - start) * (follower.Progress / length);
            Move(follower.Entity, position);
            follower.Entity.Velocity = (end - start) * (follower.Speed / length);
        }

        /// <summary>Repositions through the world so the sector index follows the actor.</summary>
        private void Move(DynamicEntity entity, Vector3 position)
        {
            if (world == null)
                entity.SetPosition(position);
            else
                world.MoveEntity(entity, position);
        }

        private void NextSegment(Follower follower, Vector3 segmentEnd)
        {
            follower.Segment++;
            follower.Progress = 0.0f;
            if (follower.Segment < follower.Route.Length - 1)
            {
                // The next link can have a different lane layout and travel side, so the lane
                // offset is recomputed per segment instead of being fixed for the whole route.
                follower.Lateral = LateralFor(follower, follower.Segment);
                return;
            }
            Move(follower.Entity, segmentEnd);
            completed++;
            int lastNode = follower.Route[follower.Route.Length - 1];
            int[] next = RouteSource == null ? null : RouteSource(lastNode);
            // A provider must continue from the node the actor stands on, or it would jump.
            if (next != null && next.Length >= 2 && next[0] == lastNode &&
                IsValidRoute(next))
            {
                follower.Route = next;
                follower.Segment = 0;
                follower.Progress = 0.0f;
                follower.Lateral = LateralFor(follower, 0);
                replanned++;
                return;
            }
            follower.Finished = true;
            follower.Entity.Velocity = Vector3.Zero;
        }

        private bool IsValidRoute(int[] route)
        {
            for (int i = 0; i < route.Length; i++)
            {
                if (route[i] < 0 || route[i] >= graph.NodeCount)
                    return false;
            }
            return true;
        }

        /// <summary>Serializes progress so State@N reacts to path-following motion.</summary>
        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteInt32(followers.Count);
            writer.WriteSingle(travelled);
            writer.WriteInt32(completed);
            writer.WriteInt32(replanned);
            writer.WriteInt32(trafficHolds);
            // Normally empty: the population module drains it in the same tick. It stays in the
            // hash so a state captured between follower and population still differs from a
            // state where nothing finished.
            writer.WriteInt32(retired.Count);
            for (int i = 0; i < retired.Count; i++)
                writer.WriteInt32(retired[i].Handle);
            for (int i = 0; i < followers.Count; i++)
            {
                Follower follower = followers[i];
                writer.WriteInt32(follower.Entity.Handle);
                writer.WriteInt32(follower.Segment);
                writer.WriteSingle(follower.Progress);
                writer.WriteSingle(follower.Speed);
                writer.WriteByte(follower.Finished ? (byte)1 : (byte)0);
                // The route itself is part of the state: two runs that follow different chains
                // must not hash the same just because they are at the same progress.
                writer.WriteInt32(follower.Route.Length);
                for (int k = 0; k < follower.Route.Length; k++)
                    writer.WriteInt32(follower.Route[k]);
            }
        }

        private sealed class Follower
        {
            internal DynamicEntity Entity;
            internal int[] Route;
            internal float Speed;
            internal int Segment;
            internal float Progress;
            internal bool Finished;
            /// <summary>
            /// Sideways offset of the lane this actor drives in on the current segment. It is
            /// derived from the link and the handle, never from wall time, so it stays stable and
            /// two runs of the same route put actors in the same lanes.
            /// </summary>
            internal Vector3 Lateral;
        }
    }
}