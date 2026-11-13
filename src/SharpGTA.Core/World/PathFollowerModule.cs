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
            followers.Add(follower);
            entity.Velocity = Vector3.Zero;
            Move(entity, graph.Nodes[follower.Route[0]].Position);
            return true;
        }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            float delta = context.Time.DeltaSeconds;
            if (!(delta > 0.0f))
                return;
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
                Vector3 start = graph.Nodes[follower.Route[follower.Segment]].Position;
                Vector3 end = graph.Nodes[follower.Route[follower.Segment + 1]].Position;
                float length = (end - start).Length();
                if (length <= 0.0001f)
                {
                    NextSegment(follower, end);
                    continue;
                }
                float left = length - follower.Progress;
                if (remaining < left)
                {
                    follower.Progress += remaining;
                    travelled += remaining;
                    PlaceOnSegment(follower, start, end, length);
                    return;
                }
                remaining -= left;
                travelled += left;
                NextSegment(follower, end);
            }
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
                return;
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
        }
    }
}