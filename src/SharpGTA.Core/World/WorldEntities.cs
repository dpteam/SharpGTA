using System;

namespace SharpGTA.World
{
    /// <summary>Entity kinds that own a pool in CPools.</summary>
    public enum WorldEntityKind
    {
        /// <summary>CBuilding: a plain IPL placement.</summary>
        Building,

        /// <summary>CObject: a gameplay object.</summary>
        Object,

        /// <summary>CDummyObject: an IPL placement of a model listed in OBJECT.DAT.</summary>
        Dummy,

        /// <summary>CTreadable: an IPL placement of a model used by a path header.</summary>
        Treadable,

        /// <summary>CPed: a gameplay character.</summary>
        Ped,

        /// <summary>CVehicle: a gameplay vehicle.</summary>
        Vehicle
    }

    /// <summary>
    /// Entity pools mirroring CPools: one fixed pool per kind, sized from the selected game's
    /// limits. Map loading fills the building, dummy and treadable pools; the simulation
    /// fills object, ped and vehicle pools through StaticWorld.TrySpawnEntity. Pools hold
    /// WorldEntity so a placement and a gameplay entity can share one per-kind pool, as in the
    /// original.
    /// </summary>
    public sealed class WorldEntities
    {
        private readonly EntityPool<WorldEntity>[] pools;
        private readonly int[] rejected = new int[6];

        public WorldEntities(GameLimits limits)
        {
            if (limits == null)
                throw new ArgumentNullException("limits");
            pools = new EntityPool<WorldEntity>[6];
            pools[(int)WorldEntityKind.Building] = new EntityPool<WorldEntity>(limits.Buildings);
            pools[(int)WorldEntityKind.Object] = new EntityPool<WorldEntity>(limits.Objects);
            pools[(int)WorldEntityKind.Dummy] = new EntityPool<WorldEntity>(limits.Dummies);
            pools[(int)WorldEntityKind.Treadable] = new EntityPool<WorldEntity>(limits.Treadables);
            pools[(int)WorldEntityKind.Ped] = new EntityPool<WorldEntity>(limits.Peds);
            pools[(int)WorldEntityKind.Vehicle] = new EntityPool<WorldEntity>(limits.Vehicles);
        }

        public int Capacity(WorldEntityKind kind)
        {
            return Pool(kind).Capacity;
        }

        public int Count(WorldEntityKind kind)
        {
            return Pool(kind).UsedSpace;
        }

        /// <summary>Handles that could not be allocated because that pool was full.</summary>
        public int RejectedCount(WorldEntityKind kind)
        {
            return rejected[(int)Validate(kind)];
        }

        public int TotalRejectedCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < rejected.Length; i++)
                    total += rejected[i];
                return total;
            }
        }

        /// <summary>Allocates a handle of this kind, or -1 when the pool is full.</summary>
        public int Create(WorldEntityKind kind, WorldEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            int handle = Pool(kind).New(entity);
            if (handle < 0)
                rejected[(int)kind]++;
            return handle;
        }

        public bool Release(WorldEntityKind kind, int handle)
        {
            return Pool(kind).Delete(handle);
        }

        public bool TryGet(WorldEntityKind kind, int handle, out WorldEntity entity)
        {
            entity = Pool(kind).GetAt(handle);
            return entity != null;
        }

        /// <summary>
        /// Drops every handle and keeps the rejection counters at zero. Existing
        /// WorldEntity.Handle values become unresolvable, so only clear a world that is
        /// being discarded.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < pools.Length; i++)
            {
                pools[i].Clear();
                rejected[i] = 0;
            }
        }

        private EntityPool<WorldEntity> Pool(WorldEntityKind kind)
        {
            return pools[(int)Validate(kind)];
        }

        private static WorldEntityKind Validate(WorldEntityKind kind)
        {
            if (kind != WorldEntityKind.Building && kind != WorldEntityKind.Object &&
                kind != WorldEntityKind.Dummy && kind != WorldEntityKind.Treadable &&
                kind != WorldEntityKind.Ped && kind != WorldEntityKind.Vehicle)
                throw new ArgumentOutOfRangeException("kind");
            return kind;
        }
    }
}