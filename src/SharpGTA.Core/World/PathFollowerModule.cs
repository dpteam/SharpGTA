using System;
using System.Collections.Generic;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Drives vehicles along a resolved path route at a constant speed. This is the first
    /// gameplay motion that follows the network instead of drifting in a straight line: each
    /// assigned entity walks its node chain, takes its heading from the current segment and
    /// stops at the end. There is no physics, collision response, lane offset or traffic rule
    /// yet, and the follower owns its vehicles' positions (their plain integration is
    /// overwritten every tick).
    /// </summary>
    public sealed class PathFollowerModule : ISimulationModule, IStateSerializable
    {
        private readonly PathGraph graph;
        private readonly List<Follower> followers = new List<Follower>();
        private float travelled;
        private int completed;
        private int replanned;

        /// <summary>
        /// Optional route provider: when a car reaches the end of its chain the module asks for
        /// a fresh route from that node so it keeps driving. Returning null or a short route
        /// leaves the car parked, which is the documented behaviour without a provider.
        /// </summary>
        public Func<int, int[]> RouteSource { get; set; }

        public PathFollowerModule(PathGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            this.graph = graph;
        }

        public string Name { get { return "PathFollowers"; } }

        public int Count { get { return followers.Count; } }
        public int CompletedCount { get { return completed; } }

        /// <summary>Routes that were replaced by a new one from <see cref="RouteSource"/>.</summary>
        public int ReplanCount { get { return replanned; } }

        /// <summary>Total distance the followers have driven, for probes and diagnostics.</summary>
        public float TravelledDistance { get { return travelled; } }

        /// <summary>
        /// Assigns a resolved route to a spawned vehicle. The vehicle is snapped to the first
        /// node, so callers should spawn it there; a shorter route is rejected.
        /// </summary>
        public bool Assign(DynamicEntity entity, int[] route, float speed)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            if (route == null)
                throw new ArgumentNullException("route");
            if (entity.Kind != WorldEntityKind.Vehicle || route.Length < 2)
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

            Follower follower = new Follower();
            follower.Entity = entity;
            follower.Route = (int[])route.Clone();
            follower.Speed = speed;
            followers.Add(follower);
            entity.Velocity = Vector3.Zero;
            entity.SetPosition(graph.Nodes[follower.Route[0]].Position);
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
        }

        private void Advance(Follower follower, float deltaSeconds)
        {
            float remaining = follower.Speed * deltaSeconds;
            while (remaining > 0.0f && !follower.Finished)
            {
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
            follower.Entity.SetPosition(position);
            follower.Entity.Velocity = (end - start) * (follower.Speed / length);
        }

        private void NextSegment(Follower follower, Vector3 segmentEnd)
        {
            follower.Segment++;
            follower.Progress = 0.0f;
            if (follower.Segment < follower.Route.Length - 1)
                return;
            follower.Entity.SetPosition(segmentEnd);
            completed++;
            int[] next = RouteSource == null ? null : RouteSource(follower.Route[follower.Route.Length - 1]);
            if (next != null && next.Length >= 2)
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

        /// <summary>Serializes progress so State@N reacts to path-following motion.</summary>
        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteInt32(followers.Count);
            for (int i = 0; i < followers.Count; i++)
            {
                Follower follower = followers[i];
                writer.WriteInt32(follower.Entity.Handle);
                writer.WriteInt32(follower.Segment);
                writer.WriteSingle(follower.Progress);
                writer.WriteSingle(follower.Speed);
                writer.WriteByte(follower.Finished ? (byte)1 : (byte)0);
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