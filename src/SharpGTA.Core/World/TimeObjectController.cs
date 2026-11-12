using System;
using System.Collections.Generic;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>An allocated timed world object; it exists only while its hour window is active.</summary>
    public sealed class TimeObject
    {
        internal TimeObject(StaticInstance instance)
        {
            Instance = instance;
        }

        public StaticInstance Instance { get; private set; }

        public ModelDefinition Model
        {
            get { return Instance == null ? null : Instance.Model; }
        }
    }

    /// <summary>
    /// Tracks the IDE tobj instances whose hour window is open. The original keeps the
    /// entity alive and gates visibility in the renderer and streaming code; this preview
    /// models the same state as a pool slot: an instance holds a slot exactly while its
    /// window is open, so allocation and release follow the game clock. Untimed placements
    /// are always visible.
    /// </summary>
    public sealed class TimeObjectController
    {
        /// <summary>III CObjectPool size (NUMOBJECTS); timed objects live in that pool.</summary>
        public const int DefaultCapacity = 450;

        private readonly EntityPool<TimeObject> pool;
        private readonly Dictionary<StaticInstance, int> handles =
            new Dictionary<StaticInstance, int>();
        private readonly HashSet<StaticInstance> wanted = new HashSet<StaticInstance>();
        private readonly List<StaticInstance> released = new List<StaticInstance>();

        public TimeObjectController()
            : this(DefaultCapacity)
        {
        }

        public TimeObjectController(int capacity)
        {
            pool = new EntityPool<TimeObject>(capacity);
        }

        public int Capacity { get { return pool.Capacity; } }
        public int AllocatedCount { get { return pool.UsedSpace; } }
        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int RejectedCount { get; private set; }

        /// <summary>
        /// Reconciles the allocated set with the clock. Allocation order follows the IPL
        /// instance order, so the same world always issues the same handles.
        /// </summary>
        public void Update(IList<StaticInstance> instances, GameClock clock)
        {
            if (instances == null)
                throw new ArgumentNullException("instances");
            if (clock == null)
                throw new ArgumentNullException("clock");

            wanted.Clear();
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (instance == null || instance.Model == null || !instance.Model.IsTimed)
                    continue;
                if (instance.Model.GetIsTimeInRange(clock))
                    wanted.Add(instance);
            }

            released.Clear();
            foreach (KeyValuePair<StaticInstance, int> pair in handles)
            {
                // A handle whose slot no longer resolves is dropped as well; the entity
                // is gone, so keeping the mapping would hide the placement forever.
                if (!wanted.Contains(pair.Key) || pool.GetAt(pair.Value) == null)
                    released.Add(pair.Key);
            }
            for (int i = 0; i < released.Count; i++)
            {
                StaticInstance instance = released[i];
                int handle = handles[instance];
                handles.Remove(instance);
                pool.Delete(handle);
                // Count the mapping release even when the slot was already gone, so
                // created - destroyed always equals the number of live mappings.
                DestroyedCount++;
            }

            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (instance == null || !wanted.Contains(instance) || handles.ContainsKey(instance))
                    continue;
                int handle = pool.New(new TimeObject(instance));
                if (handle < 0)
                {
                    RejectedCount++;
                    continue;
                }
                handles.Add(instance, handle);
                CreatedCount++;
            }
        }

        /// <summary>Timed placements are visible only while they hold a pool slot.</summary>
        public bool IsVisible(StaticInstance instance)
        {
            if (instance == null || instance.Model == null)
                return false;
            if (!instance.Model.IsTimed)
                return true;
            int handle;
            return handles.TryGetValue(instance, out handle) && pool.GetAt(handle) != null;
        }

        public void Reset()
        {
            handles.Clear();
            wanted.Clear();
            released.Clear();
            pool.Clear();
            CreatedCount = 0;
            DestroyedCount = 0;
            RejectedCount = 0;
        }
    }
}