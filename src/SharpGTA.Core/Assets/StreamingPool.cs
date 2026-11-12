using System;
using System.Collections.Generic;

namespace SharpGTA.Assets
{
    public enum StreamingState
    {
        NotLoaded,
        InQueue,
        Loaded,
        Failed
    }

    public enum StreamingLoadOutcome
    {
        Loaded,
        Failed,
        Deferred
    }

    public delegate StreamingLoadOutcome StreamingResourceLoader<TKey, TResource>(
        TKey key, out TResource resource, out string failure);

    public delegate void StreamingResourceUnloader<TKey, TResource>(
        TKey key, TResource resource);

    public struct StreamingLoadReport
    {
        internal int loaded;
        internal int failed;
        internal int evicted;
        internal int deferred;

        public int LoadedCount { get { return loaded; } }
        public int FailedCount { get { return failed; } }
        public int EvictedCount { get { return evicted; } }
        public int DeferredCount { get { return deferred; } }
        public int RemainingRequestedCount { get; internal set; }
    }

    /// <summary>
    /// Deterministic synchronous streaming slots with refcounts and LRU eviction.
    /// I/O and GPU work stay in the caller's loader/unloader delegates.
    /// </summary>
    public sealed class StreamingPool<TKey, TResource> : IDisposable
        where TResource : class
    {
        private sealed class Entry
        {
            internal TKey Key;
            internal StreamingState State;
            internal TResource Resource;
            internal int ReferenceCount;
            internal int Priority;
            internal long Sequence;
            internal long LastAccess;
            internal bool KeepInMemory;
            internal string Failure;
        }

        private readonly Dictionary<TKey, Entry> entries;
        private readonly StreamingResourceLoader<TKey, TResource> loader;
        private readonly StreamingResourceUnloader<TKey, TResource> unloader;
        private int maxLoaded;
        private int loadedCount;
        private long accessClock;
        private long requestSequence;
        private bool disposed;

        public StreamingPool(int maxLoaded,
            StreamingResourceLoader<TKey, TResource> loader,
            StreamingResourceUnloader<TKey, TResource> unloader,
            IEqualityComparer<TKey> comparer)
        {
            if (maxLoaded <= 0)
                throw new ArgumentOutOfRangeException("maxLoaded");
            if (loader == null)
                throw new ArgumentNullException("loader");
            this.maxLoaded = maxLoaded;
            this.loader = loader;
            this.unloader = unloader;
            entries = new Dictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int MaxLoaded
        {
            get { return maxLoaded; }
            set
            {
                ThrowIfDisposed();
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value");
                maxLoaded = value;
            }
        }

        public int LoadedCount { get { return loadedCount; } }
        public int RequestedCount { get { return CountState(StreamingState.InQueue); } }
        public int FailedCount { get { return CountState(StreamingState.Failed); } }
        public int EvictedCount { get; private set; }

        public void Request(TKey key)
        {
            Request(key, 0, false);
        }

        public void Request(TKey key, int priority, bool keepInMemory)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(key, null))
                throw new ArgumentNullException("key");
            Entry entry;
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new Entry { Key = key };
                entries.Add(key, entry);
            }
            if (entry.State == StreamingState.NotLoaded ||
                entry.State == StreamingState.Failed)
            {
                entry.State = StreamingState.InQueue;
                entry.Failure = null;
                entry.Resource = default(TResource);
                entry.Sequence = ++requestSequence;
            }
            entry.ReferenceCount++;
            if (priority > entry.Priority)
                entry.Priority = priority;
            if (keepInMemory)
                entry.KeepInMemory = true;
            if (entry.State == StreamingState.Loaded)
                entry.LastAccess = ++accessClock;
        }

        public bool Unpin(TKey key)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(key, null))
                throw new ArgumentNullException("key");
            Entry entry;
            if (!entries.TryGetValue(key, out entry))
                return false;
            entry.KeepInMemory = false;
            return true;
        }

        public bool Release(TKey key)
        {
            ThrowIfDisposed();
            if (ReferenceEquals(key, null))
                throw new ArgumentNullException("key");
            Entry entry;
            if (!entries.TryGetValue(key, out entry) || entry.ReferenceCount == 0)
                return false;
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 && entry.State == StreamingState.InQueue)
                entry.State = StreamingState.NotLoaded;
            return true;
        }

        public void ReleaseAll()
        {
            ThrowIfDisposed();
            foreach (Entry entry in entries.Values)
            {
                entry.ReferenceCount = 0;
                entry.Priority = 0;
                if (entry.State == StreamingState.InQueue)
                    entry.State = StreamingState.NotLoaded;
            }
        }

        public StreamingLoadReport LoadAllRequested()
        {
            ThrowIfDisposed();
            StreamingLoadReport report = new StreamingLoadReport();
            List<Entry> queue = new List<Entry>();
            foreach (Entry entry in entries.Values)
            {
                if (entry.State == StreamingState.InQueue && entry.ReferenceCount > 0)
                    queue.Add(entry);
            }
            queue.Sort(delegate(Entry left, Entry right)
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });

            for (int i = 0; i < queue.Count; i++)
            {
                Entry entry = queue[i];
                if (entry.State != StreamingState.InQueue)
                    continue;
                if (loadedCount >= maxLoaded)
                {
                    if (!EvictLeastRecentlyUsed())
                        break;
                    report.evicted++;
                }

                TResource resource;
                string failure;
                StreamingLoadOutcome outcome = loader(entry.Key, out resource, out failure);
                if (outcome == StreamingLoadOutcome.Loaded &&
                    !ReferenceEquals(resource, null))
                {
                    entry.Resource = resource;
                    entry.State = StreamingState.Loaded;
                    entry.Failure = null;
                    entry.LastAccess = ++accessClock;
                    loadedCount++;
                    report.loaded++;
                }
                else if (outcome == StreamingLoadOutcome.Deferred)
                {
                    entry.Resource = default(TResource);
                    entry.State = StreamingState.InQueue;
                    entry.Failure = failure;
                    report.deferred++;
                    break;
                }
                else
                {
                    entry.Resource = default(TResource);
                    entry.State = StreamingState.Failed;
                    entry.Failure = outcome == StreamingLoadOutcome.Loaded
                        ? "Resource loader returned null."
                        : failure ?? "Resource loader failed.";
                    report.failed++;
                }
            }
            report.RemainingRequestedCount = RequestedCount;
            return report;
        }

        public void Trim()
        {
            ThrowIfDisposed();
            while (EvictLeastRecentlyUsed())
            {
            }
        }

        public StreamingState GetState(TKey key)
        {
            ThrowIfDisposed();
            Entry entry;
            return entries.TryGetValue(key, out entry) ? entry.State : StreamingState.NotLoaded;
        }

        public int GetReferenceCount(TKey key)
        {
            ThrowIfDisposed();
            Entry entry;
            return entries.TryGetValue(key, out entry) ? entry.ReferenceCount : 0;
        }

        public string GetFailure(TKey key)
        {
            ThrowIfDisposed();
            Entry entry;
            return entries.TryGetValue(key, out entry) ? entry.Failure : null;
        }

        public bool TryGetResource(TKey key, out TResource resource)
        {
            ThrowIfDisposed();
            Entry entry;
            if (entries.TryGetValue(key, out entry) && entry.State == StreamingState.Loaded)
            {
                entry.LastAccess = ++accessClock;
                resource = entry.Resource;
                return true;
            }
            resource = default(TResource);
            return false;
        }

        public void Clear()
        {
            ThrowIfDisposed();
            ClearCore();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            ClearCore();
        }

        private void ClearCore()
        {
            Exception firstFailure = null;
            List<Entry> snapshot = new List<Entry>(entries.Values);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Entry entry = snapshot[i];
                try
                {
                    if (entry.State == StreamingState.Loaded)
                        Unload(entry);
                }
                catch (Exception exception)
                {
                    if (firstFailure == null)
                        firstFailure = exception;
                }
                entry.State = StreamingState.NotLoaded;
                entry.ReferenceCount = 0;
                entry.Resource = default(TResource);
                entry.Failure = null;
            }
            entries.Clear();
            loadedCount = 0;
            if (firstFailure != null)
                throw new InvalidOperationException("Resource unload failed during streaming clear.", firstFailure);
        }

        private bool EvictLeastRecentlyUsed()
        {
            Entry oldest = null;
            foreach (Entry entry in entries.Values)
            {
                if (entry.State != StreamingState.Loaded || entry.ReferenceCount != 0 ||
                    entry.KeepInMemory)
                    continue;
                if (oldest == null || entry.LastAccess < oldest.LastAccess)
                    oldest = entry;
            }
            if (oldest == null)
                return false;
            Unload(oldest);
            oldest.Failure = null;
            EvictedCount++;
            return true;
        }

        private void Unload(Entry entry)
        {
            if (loadedCount > 0)
                loadedCount--;
            TResource resource = entry.Resource;
            entry.State = StreamingState.NotLoaded;
            entry.Resource = default(TResource);
            if (unloader != null && !ReferenceEquals(resource, null))
                unloader(entry.Key, resource);
        }

        private int CountState(StreamingState state)
        {
            int count = 0;
            foreach (Entry entry in entries.Values)
            {
                if (entry.State == state)
                    count++;
            }
            return count;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("StreamingPool");
        }
    }
}