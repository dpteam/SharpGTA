using System;

namespace SharpGTA.World
{
    /// <summary>Entity kinds that own a pool in CPools.</summary>
    public enum WorldEntityKind
    {
        /// <summary>CBuilding: a plain IPL placement.</summary>
        Building,

        /// <summary>CObject: a gameplay object; nothing is created from map data yet.</summary>
        Object,

        /// <summary>CDummyObject: an IPL placement of a model listed in OBJECT.DAT.</summary>
        Dummy,

        /// <summary>CTreadable: an IPL placement of a model used by a path header.</summary>
        Treadable
    }

    /// <summary>
    /// Entity pools mirroring CPools: one fixed pool per kind, sized from the selected game's
    /// limits. Buildings, dummies and treadables are materialized because map loading creates
    /// those entities; the object pool is reserved for gameplay objects. Ped and vehicle
    /// capacities come from the same limits table until gameplay entities exist.
    /// </summary>
    public sealed class WorldEntities
    {
        private readonly EntityPool<StaticInstance> buildings;
        private readonly EntityPool<StaticInstance> objects;
        private readonly EntityPool<StaticInstance> dummies;
        private readonly EntityPool<StaticInstance> treadables;
        private readonly int pedCapacity;
        private readonly int vehicleCapacity;
        private readonly int[] rejected = new int[4];

        public WorldEntities(GameLimits limits)
        {
            if (limits == null)
                throw new ArgumentNullException("limits");
            buildings = new EntityPool<StaticInstance>(limits.Buildings);
            objects = new EntityPool<StaticInstance>(limits.Objects);
            dummies = new EntityPool<StaticInstance>(limits.Dummies);
            treadables = new EntityPool<StaticInstance>(limits.Treadables);
            pedCapacity = limits.Peds;
            vehicleCapacity = limits.Vehicles;
        }

        public int PedCapacity { get { return pedCapacity; } }
        public int VehicleCapacity { get { return vehicleCapacity; } }

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
        public int Create(WorldEntityKind kind, StaticInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            int handle = Pool(kind).New(instance);
            if (handle < 0)
                rejected[(int)kind]++;
            return handle;
        }

        public bool Release(WorldEntityKind kind, int handle)
        {
            return Pool(kind).Delete(handle);
        }

        public bool TryGet(WorldEntityKind kind, int handle, out StaticInstance instance)
        {
            instance = Pool(kind).GetAt(handle);
            return instance != null;
        }

        /// <summary>
        /// Drops every handle and keeps the rejection counters at zero. Existing
        /// StaticInstance.Handle values become unresolvable, so only clear a world that is
        /// being discarded.
        /// </summary>
        public void Clear()
        {
            buildings.Clear();
            objects.Clear();
            dummies.Clear();
            treadables.Clear();
            for (int i = 0; i < rejected.Length; i++)
                rejected[i] = 0;
        }

        private EntityPool<StaticInstance> Pool(WorldEntityKind kind)
        {
            switch (Validate(kind))
            {
                case WorldEntityKind.Building:
                    return buildings;
                case WorldEntityKind.Object:
                    return objects;
                case WorldEntityKind.Dummy:
                    return dummies;
                default:
                    return treadables;
            }
        }

        private static WorldEntityKind Validate(WorldEntityKind kind)
        {
            if (kind != WorldEntityKind.Building && kind != WorldEntityKind.Object &&
                kind != WorldEntityKind.Dummy && kind != WorldEntityKind.Treadable)
                throw new ArgumentOutOfRangeException("kind");
            return kind;
        }
    }
}