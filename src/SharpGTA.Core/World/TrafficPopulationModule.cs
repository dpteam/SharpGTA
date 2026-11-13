using System;
using System.Collections.Generic;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Keeps a fixed number of actors per path kind alive: when <see cref="PathFollowerModule"/>
    /// hands back an actor that finished its route, this module despawns it and spawns a
    /// replacement somewhere else on the network. That is the first real traffic lifecycle —
    /// actors now come and go instead of driving forever or parking where they stopped.
    ///
    /// Node choice is a deterministic round robin with a stride over the candidate nodes, not
    /// the original `CPopulation` random-per-zone generator, and there is still no density per
    /// zone, no spawn distance check against the camera in headless and no traffic rules. The
    /// stride is documented here because it decides where replacements appear.
    /// </summary>
    public sealed class TrafficPopulationModule : ISimulationModule, IStateSerializable
    {
        /// <summary>Route length given to every spawned actor, both initial and replacement.</summary>
        public const int DefaultRouteHops = 40;

        private readonly StaticWorld world;
        private readonly PathGraph graph;
        private readonly PathFollowerModule followers;
        private readonly List<Cohort> cohorts = new List<Cohort>();
        private int routeHops = DefaultRouteHops;
        private int spawned;
        private int despawned;
        private int recycled;

        public TrafficPopulationModule(StaticWorld world, PathGraph graph, PathFollowerModule followers)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (followers == null)
                throw new ArgumentNullException("followers");
            this.world = world;
            this.graph = graph;
            this.followers = followers;
        }

        public string Name { get { return "TrafficPopulation"; } }

        /// <summary>
        /// Route length given to replacements; the initial spawn builds its own routes with the
        /// same policy. The preview lowers it, because a forty-hop route in III covers roughly
        /// 2800 units and the lifecycle would never be visible in a short run.
        /// </summary>
        public int RouteHops
        {
            get { return routeHops; }
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException("value");
                routeHops = value;
            }
        }

        /// <summary>Actor kinds currently recycled, as a read-only snapshot for diagnostics.</summary>
        public IList<TrafficCohortStatus> Cohorts
        {
            get
            {
                List<TrafficCohortStatus> status = new List<TrafficCohortStatus>(cohorts.Count);
                for (int i = 0; i < cohorts.Count; i++)
                {
                    Cohort cohort = cohorts[i];
                    status.Add(new TrafficCohortStatus(cohort.PathKind, cohort.EntityKind,
                        cohort.Live, cohort.Target));
                }
                return status.AsReadOnly();
            }
        }

        /// <summary>Actors spawned by this module, replacements included.</summary>
        public int SpawnedCount { get { return spawned; } }

        /// <summary>Actors removed from the world because their route ended.</summary>
        public int DespawnedCount { get { return despawned; } }

        /// <summary>
        /// Finished actors taken from the follower. It can exceed <see cref="DespawnedCount"/>
        /// when the world refuses the despawn, which is exactly what we want to see.
        /// </summary>
        public int RecycledCount { get { return recycled; } }

        /// <summary>
        /// Adds a group of actors kept at <paramref name="target"/> on every node of that kind.
        /// The follower starts handing that kind back on route end as soon as a cohort exists,
        /// so a kind can never be recycled without an owner. Actors spawned outside this module
        /// (the initial probe spawn) are counted as part of the target by the caller.
        /// </summary>
        public void AddCohort(PathKind pathKind, WorldEntityKind entityKind, ModelDefinition model,
            float speed, int target)
        {
            AddCohort(pathKind, entityKind, model, speed, target, Vector3.Zero, 0.0f);
        }

        /// <summary>
        /// Same, but only nodes within <paramref name="radius"/> of <paramref name="anchor"/> are
        /// candidates. The preview uses that so replacements stay near the camera instead of
        /// appearing on the other side of the city.
        /// </summary>
        public void AddCohort(PathKind pathKind, WorldEntityKind entityKind, ModelDefinition model,
            float speed, int target, Vector3 anchor, float radius)
        {
            if (model == null)
                throw new ArgumentNullException("model");
            if (entityKind != WorldEntityKind.Vehicle && entityKind != WorldEntityKind.Ped)
            {
                throw new ArgumentException(
                    "Only vehicles and peds are driven along paths.", "entityKind");
            }
            if (!(speed > 0.0f) || float.IsInfinity(speed))
                throw new ArgumentOutOfRangeException("speed");
            if (target < 0)
                throw new ArgumentOutOfRangeException("target");
            if (!(radius >= 0.0f) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            for (int i = 0; i < cohorts.Count; i++)
            {
                if (cohorts[i].EntityKind == entityKind)
                {
                    throw new ArgumentException(
                        "An entity kind can only have one recycling cohort.", "entityKind");
                }
            }

            if (entityKind == WorldEntityKind.Vehicle)
                followers.RecycleVehicles = true;
            else
                followers.RecyclePeds = true;

            Cohort cohort = new Cohort();
            cohort.PathKind = pathKind;
            cohort.EntityKind = entityKind;
            cohort.Model = model;
            cohort.Speed = speed;
            cohort.Target = target;
            cohort.Candidates = CollectCandidates(pathKind, anchor, radius);
            // A stride of candidates/target spreads replacements over the network; without it a
            // small population would recycle the same few nodes and look frozen.
            cohort.Stride = Math.Min(Math.Max(1, cohort.Candidates.Count / Math.Max(1, target)),
                Math.Max(1, cohort.Candidates.Count - 1));
            // Adopt actors the caller already spawned for this kind: the probe spawns them and
            // then hands the population a target equal to that count, and without this the very
            // first tick would spawn a second full set on top of them.
            cohort.Live = followers.CountOf(entityKind);
            if (cohort.Candidates.Count > 0)
                cohort.Cursor = (cohort.Live * cohort.Stride) % cohort.Candidates.Count;
            cohorts.Add(cohort);
        }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            DrainFinished();
            for (int i = 0; i < cohorts.Count; i++)
            {
                Cohort cohort = cohorts[i];
                while (cohort.Live < cohort.Target && TrySpawnNext(cohort))
                {
                }
            }
        }

        /// <summary>
        /// Removes every actor the follower retired: the follower no longer owns its position, so
        /// leaving it in the world would make it a ghost that nothing integrates or despawns.
        /// </summary>
        private void DrainFinished()
        {
            DynamicEntity entity;
            while (followers.TryTakeRetired(out entity))
            {
                Cohort owner = FindCohort(entity.Kind);
                if (owner != null && owner.Live > 0)
                    owner.Live--;
                recycled++;
                if (world.DespawnEntity(entity.Kind, entity.Handle))
                    despawned++;
            }
        }

        private Cohort FindCohort(WorldEntityKind kind)
        {
            for (int i = 0; i < cohorts.Count; i++)
            {
                if (cohorts[i].EntityKind == kind)
                    return cohorts[i];
            }
            return null;
        }

        private bool TrySpawnNext(Cohort cohort)
        {
            for (int attempt = 0; attempt < cohort.Candidates.Count; attempt++)
            {
                int nodeIndex = cohort.Candidates[cohort.Cursor];
                cohort.Cursor += cohort.Stride;
                if (cohort.Cursor >= cohort.Candidates.Count)
                    cohort.Cursor %= cohort.Candidates.Count;

                int[] route = BuildRoute(nodeIndex);
                if (route == null)
                    continue;
                DynamicEntity entity;
                if (!world.TrySpawnEntity(cohort.EntityKind, cohort.Model,
                    graph.Nodes[nodeIndex].Position, Vector3.Zero, out entity))
                {
                    // The pool is full: retrying every node this tick would just churn.
                    return false;
                }
                if (!followers.Assign(entity, route, cohort.Speed))
                {
                    // Never leave an unowned entity behind if the follower refuses the route.
                    world.DespawnEntity(cohort.EntityKind, entity.Handle);
                    continue;
                }
                cohort.Live++;
                spawned++;
                return true;
            }
            return false;
        }

        private int[] BuildRoute(int start)
        {
            int[] route = PathRouteBuilder.BuildGreedy(graph, start, routeHops, true);
            if (route.Length < 2)
                route = PathRouteBuilder.BuildGreedy(graph, start, routeHops);
            if (route.Length < 2)
                return null;
            return route;
        }

        private List<int> CollectCandidates(PathKind pathKind, Vector3 anchor, float radius)
        {
            List<int> candidates = new List<int>();
            float radiusSquared = radius > 0.0f ? radius * radius : 0.0f;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                PathGraphNode node = graph.Nodes[i];
                if (node.Kind != pathKind || node.LinkCount == 0)
                    continue;
                if (radiusSquared > 0.0f &&
                    (node.Position - anchor).LengthSquared() > radiusSquared)
                {
                    continue;
                }
                candidates.Add(i);
            }
            return candidates;
        }

        /// <summary>
        /// Serializes the population state, so replacing an actor changes `State@N` even when the
        /// replacement happens to stand where the old one stood.
        /// </summary>
        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteInt32(cohorts.Count);
            writer.WriteInt32(spawned);
            writer.WriteInt32(despawned);
            writer.WriteInt32(recycled);
            for (int i = 0; i < cohorts.Count; i++)
            {
                Cohort cohort = cohorts[i];
                writer.WriteInt32((int)cohort.PathKind);
                writer.WriteInt32((int)cohort.EntityKind);
                writer.WriteInt32(cohort.Target);
                writer.WriteInt32(cohort.Live);
                writer.WriteInt32(cohort.Cursor);
            }
        }

        private sealed class Cohort
        {
            internal PathKind PathKind;
            internal WorldEntityKind EntityKind;
            internal ModelDefinition Model;
            internal float Speed;
            internal int Target;
            internal int Live;
            internal int Cursor;
            internal int Stride;
            internal List<int> Candidates;
        }
    }

    /// <summary>Read-only view of one recycling cohort for diagnostics output.</summary>
    public sealed class TrafficCohortStatus
    {
        internal TrafficCohortStatus(PathKind pathKind, WorldEntityKind entityKind,
            int liveCount, int targetCount)
        {
            PathKind = pathKind;
            EntityKind = entityKind;
            LiveCount = liveCount;
            TargetCount = targetCount;
        }

        public PathKind PathKind { get; private set; }
        public WorldEntityKind EntityKind { get; private set; }
        public int LiveCount { get; private set; }
        public int TargetCount { get; private set; }
    }
}