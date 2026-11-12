using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpGTA.IO
{
    public delegate byte[] AsyncByteReadDelegate(string key);

    /// <summary>
    /// Two background byte-delivery channels with a bounded LRU byte cache. Decoding,
    /// element creation and world mutation must still happen on the simulation thread.
    /// </summary>
    public sealed class AsyncByteCache : IDisposable
    {
        private sealed class CacheEntry
        {
            internal byte[] Bytes;
            internal long LastAccess;
            /// <summary>Bytes a waiting simulation-thread consumer is about to decode.</summary>
            internal bool Critical;
        }

        private sealed class Completion
        {
            internal string Key;
            internal byte[] Bytes;
            internal string Failure;
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> requested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> critical =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> pending = new Queue<string>();
        private readonly Queue<Completion> completed = new Queue<Completion>();
        private readonly Thread[] workers;
        private readonly AsyncByteReadDelegate reader;
        private readonly int maximumBytes;
        private long accessClock;
        private long cachedBytes;
        private int evictedCount;
        private int failureCount;
        private int oversizedCount;
        private bool stopping;
        private bool disposed;

        public AsyncByteCache(int maximumBytes, int workerCount, AsyncByteReadDelegate reader)
        {
            if (maximumBytes <= 0)
                throw new ArgumentOutOfRangeException("maximumBytes");
            if (workerCount <= 0)
                throw new ArgumentOutOfRangeException("workerCount");
            if (reader == null)
                throw new ArgumentNullException("reader");
            this.maximumBytes = maximumBytes;
            this.reader = reader;
            workers = new Thread[workerCount];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(WorkerLoop);
                workers[i].IsBackground = true;
                workers[i].Name = "SharpGTA I/O " + (i + 1).ToString();
                workers[i].Start();
            }
        }

        public int MaximumBytes { get { return maximumBytes; } }
        public int WorkerCount { get { return workers.Length; } }

        public long CachedBytes
        {
            get { lock (sync) { return cachedBytes; } }
        }

        public int CachedCount
        {
            get { lock (sync) { return cache.Count; } }
        }

        public int PendingCount
        {
            get { lock (sync) { return requested.Count; } }
        }

        public int EvictedCount
        {
            get { lock (sync) { return evictedCount; } }
        }

        public int FailedCount
        {
            get { lock (sync) { return failureCount; } }
        }

        /// <summary>Payloads that could not be cached because they exceed the budget.</summary>
        public int OversizedCount
        {
            get { lock (sync) { return oversizedCount; } }
        }

        public bool Request(string key)
        {
            ThrowIfDisposed();
            if (key == null)
                throw new ArgumentNullException("key");
            lock (sync)
            {
                if (cache.ContainsKey(key) || requested.Contains(key) || failures.ContainsKey(key))
                    return false;
                requested.Add(key);
                pending.Enqueue(key);
                Monitor.PulseAll(sync);
                return true;
            }
        }

        /// <summary>
        /// Clears a recorded read failure so the key can be requested again. Failed keys
        /// stay failed on their own; otherwise a broken asset would be re-read forever.
        /// </summary>
        public bool Forget(string key)
        {
            ThrowIfDisposed();
            if (key == null)
                throw new ArgumentNullException("key");
            lock (sync)
            {
                if (!failures.Remove(key))
                    return false;
                if (failureCount > 0)
                    failureCount--;
                return true;
            }
        }

        /// <summary>Drains background completions and enforces the byte LRU budget.</summary>
        public int Pump()
        {
            ThrowIfDisposed();
            int pumped = 0;
            lock (sync)
            {
                while (completed.Count > 0)
                {
                    Completion item = completed.Dequeue();
                    requested.Remove(item.Key);
                    pumped++;
                    if (item.Failure != null || item.Bytes == null)
                    {
                        failures[item.Key] = item.Failure ?? "I/O reader returned null.";
                        failureCount++;
                        continue;
                    }
                    if (item.Bytes.Length > maximumBytes && !critical.Contains(item.Key))
                    {
                        // The bytes cannot live in a cache this small. This is a cache miss,
                        // not a broken asset: a waiting consumer re-requests them directly.
                        oversizedCount++;
                        continue;
                    }
                    CacheEntry entry = new CacheEntry();
                    entry.Bytes = item.Bytes;
                    entry.LastAccess = ++accessClock;
                    entry.Critical = critical.Contains(item.Key);
                    if (cache.ContainsKey(item.Key))
                    {
                        cachedBytes -= cache[item.Key].Bytes.Length;
                        cache.Remove(item.Key);
                    }
                    cache.Add(item.Key, entry);
                    cachedBytes += item.Bytes.Length;
                    failures.Remove(item.Key);
                    while (cachedBytes > maximumBytes && EvictOldest())
                    {
                    }
                }
            }
            return pumped;
        }

        public bool TryTake(string key, out byte[] bytes)
        {
            ThrowIfDisposed();
            if (key == null)
                throw new ArgumentNullException("key");
            lock (sync)
            {
                CacheEntry entry;
                if (cache.TryGetValue(key, out entry))
                {
                    entry.LastAccess = ++accessClock;
                    bytes = entry.Bytes;
                    return true;
                }
                bytes = null;
                return false;
            }
        }

        public bool Remove(string key)
        {
            ThrowIfDisposed();
            if (key == null)
                throw new ArgumentNullException("key");
            lock (sync)
            {
                CacheEntry entry;
                if (cache.TryGetValue(key, out entry))
                {
                    cachedBytes -= entry.Bytes.Length;
                    cache.Remove(key);
                    critical.Remove(key);
                    return true;
                }
                critical.Remove(key);
                return false;
            }
        }

        public string GetFailure(string key)
        {
            ThrowIfDisposed();
            lock (sync)
            {
                string failure;
                return failures.TryGetValue(key ?? string.Empty, out failure) ? failure : null;
            }
        }

        /// <summary>
        /// Blocks until the key's bytes are cached, re-requesting them when they were
        /// evicted in the meantime. Waited bytes are protected from eviction until the
        /// consumer calls <see cref="Remove"/> after decoding them.
        /// </summary>
        public bool WaitFor(string key, int timeoutMilliseconds)
        {
            ThrowIfDisposed();
            if (timeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (true)
            {
                lock (sync)
                {
                    // Re-request when the bytes were evicted before this consumer took them.
                    if (!cache.ContainsKey(key) && !requested.Contains(key) &&
                        !failures.ContainsKey(key))
                    {
                        requested.Add(key);
                        pending.Enqueue(key);
                        Monitor.PulseAll(sync);
                    }
                    critical.Add(key);
                    CacheEntry waiting;
                    if (cache.TryGetValue(key, out waiting))
                        waiting.Critical = true;
                }
                Pump();
                byte[] bytes;
                if (TryTake(key, out bytes))
                    return true;
                if (GetFailure(key) != null)
                {
                    lock (sync) { critical.Remove(key); }
                    return false;
                }
                if (DateTime.UtcNow >= deadline)
                {
                    lock (sync) { critical.Remove(key); }
                    return false;
                }
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
                stopping = true;
                Monitor.PulseAll(sync);
            }
            for (int i = 0; i < workers.Length; i++)
            {
                if (workers[i].IsAlive)
                    workers[i].Join(5000);
            }
            lock (sync)
            {
                cache.Clear();
                failures.Clear();
                requested.Clear();
                critical.Clear();
                pending.Clear();
                completed.Clear();
                cachedBytes = 0;
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                string key;
                lock (sync)
                {
                    while (pending.Count == 0 && !stopping)
                        Monitor.Wait(sync);
                    if (stopping)
                        return;
                    key = pending.Dequeue();
                }

                Completion completion = new Completion();
                completion.Key = key;
                try
                {
                    completion.Bytes = reader(key);
                    if (completion.Bytes == null)
                        completion.Failure = "I/O reader returned null.";
                }
                catch (Exception exception)
                {
                    completion.Failure = exception.Message;
                }
                lock (sync)
                {
                    completed.Enqueue(completion);
                    Monitor.PulseAll(sync);
                }
            }
        }

        private bool EvictOldest()
        {
            string oldestKey = null;
            long oldestAccess = long.MaxValue;
            foreach (KeyValuePair<string, CacheEntry> pair in cache)
            {
                if (pair.Value.Critical)
                    continue;
                if (pair.Value.LastAccess < oldestAccess)
                {
                    oldestAccess = pair.Value.LastAccess;
                    oldestKey = pair.Key;
                }
            }
            if (oldestKey == null)
                return false;
            cachedBytes -= cache[oldestKey].Bytes.Length;
            cache.Remove(oldestKey);
            evictedCount++;
            return true;
        }

        private void ThrowIfDisposed()
        {
            // Reads under the lock: net40 has no volatile here and Dispose must not be
            // observable as "still alive" once the cache has been cleared.
            lock (sync)
            {
                if (disposed)
                    throw new ObjectDisposedException("AsyncByteCache");
            }
        }
    }
}