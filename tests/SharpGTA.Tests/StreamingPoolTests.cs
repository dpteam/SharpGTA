using System;
using System.Collections.Generic;
using SharpGTA.Assets;

namespace SharpGTA.Tests
{
    public sealed class StreamingPoolTests
    {
        [Test]
        public void RequestsCoalesceLoadInPriorityOrderAndTrimUnreferenced()
        {
            List<string> loaded = new List<string>();
            List<string> unloaded = new List<string>();
            StreamingPool<string, string> pool = new StreamingPool<string, string>(4,
                delegate(string key, out string value, out string failure)
                {
                    loaded.Add(key);
                    value = "resource:" + key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                },
                delegate(string key, string value) { unloaded.Add(key); },
                StringComparer.OrdinalIgnoreCase);

            pool.Request("slow", 1, false);
            pool.Request("SLOW", 1, false);
            pool.Request("urgent", 10, false);
            Assert.Equal(StreamingState.InQueue, pool.GetState("slow"));
            Assert.Equal(2, pool.GetReferenceCount("slow"));
            Assert.Equal(0, pool.LoadedCount);

            StreamingLoadReport report = pool.LoadAllRequested();
            Assert.Equal(2, report.LoadedCount);
            Assert.Equal(0, report.RemainingRequestedCount);
            Assert.Equal("urgent,slow", string.Join(",", loaded.ToArray()));
            Assert.Equal(StreamingState.Loaded, pool.GetState("SLOW"));
            string resource;
            Assert.True(pool.TryGetResource("slow", out resource));
            Assert.Equal("resource:slow", resource);

            Assert.True(pool.Release("slow"));
            Assert.True(pool.Release("slow"));
            Assert.False(pool.Release("slow"));
            pool.ReleaseAll();
            Assert.Equal(2, pool.LoadedCount);
            pool.Trim();
            Assert.Equal(0, pool.LoadedCount);
            Assert.Equal(2, unloaded.Count);
            pool.Dispose();
        }

        [Test]
        public void CapacityEvictsOldestUnreferencedButNeverReferencedEntry()
        {
            List<string> loaded = new List<string>();
            List<string> unloaded = new List<string>();
            StreamingPool<string, string> pool = new StreamingPool<string, string>(2,
                delegate(string key, out string value, out string failure)
                {
                    loaded.Add(key);
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                },
                delegate(string key, string value) { unloaded.Add(key); },
                StringComparer.OrdinalIgnoreCase);
            try
            {
                pool.Request("a");
                pool.Request("b");
                pool.LoadAllRequested();
                pool.ReleaseAll();
                string untouched;
                Assert.True(pool.TryGetResource("a", out untouched));

                pool.Request("c");
                StreamingLoadReport evicted = pool.LoadAllRequested();
                Assert.Equal(1, evicted.LoadedCount);
                Assert.Equal(1, evicted.EvictedCount);
                Assert.Equal(StreamingState.Loaded, pool.GetState("a"));
                Assert.Equal(StreamingState.NotLoaded, pool.GetState("b"));
                Assert.Equal(StreamingState.Loaded, pool.GetState("c"));
                Assert.Equal("b", unloaded[0]);

                pool.ReleaseAll();
                pool.Request("a", 0, false);
                pool.Request("c", 0, false);
                pool.Request("d", 20, false);
                StreamingLoadReport blocked = pool.LoadAllRequested();
                Assert.Equal(0, blocked.LoadedCount);
                Assert.Equal(1, blocked.RemainingRequestedCount);
                Assert.Equal(StreamingState.InQueue, pool.GetState("d"));
                Assert.Equal(2, pool.LoadedCount);
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void ReleaseAllClearsFramePriorityBeforeTheNextRequest()
        {
            StreamingPool<string, string> pool = new StreamingPool<string, string>(2,
                delegate(string key, out string value, out string failure)
                {
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                }, null, StringComparer.OrdinalIgnoreCase);
            try
            {
                pool.Request("pinned", 0, true);
                pool.Request("filler", 0, false);
                Assert.Equal(2, pool.LoadAllRequested().LoadedCount);
                pool.ReleaseAll();

                pool.Request("old-vip", 10, false);
                Assert.Equal(StreamingState.InQueue, pool.GetState("old-vip"));
                pool.ReleaseAll();

                pool.Request("old-vip", 0, false);
                pool.Request("current", 1, false);
                StreamingLoadReport report = pool.LoadAllRequested();
                Assert.Equal(1, report.LoadedCount);
                Assert.Equal(1, report.EvictedCount);
                Assert.Equal(StreamingState.InQueue, pool.GetState("old-vip"));
                Assert.Equal(StreamingState.Loaded, pool.GetState("current"));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void DeferredLoadStaysQueuedAndUnpinAllowsTrim()
        {
            int unloads = 0;
            StreamingPool<string, string> pool = new StreamingPool<string, string>(1,
                delegate(string key, out string value, out string failure)
                {
                    if (key == "deferred")
                    {
                        value = null;
                        failure = "dependency capacity";
                        return StreamingLoadOutcome.Deferred;
                    }
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                },
                delegate(string key, string value) { unloads++; },
                StringComparer.OrdinalIgnoreCase);
            try
            {
                pool.Request("pinned", 0, true);
                pool.LoadAllRequested();
                pool.Release("pinned");
                pool.Trim();
                Assert.Equal(1, pool.LoadedCount);
                Assert.True(pool.Unpin("pinned"));
                Assert.False(pool.Unpin("missing"));
                pool.Trim();
                Assert.Equal(0, pool.LoadedCount);
                Assert.Equal(1, unloads);

                pool.Request("deferred");
                StreamingLoadReport report = pool.LoadAllRequested();
                Assert.Equal(1, report.DeferredCount);
                Assert.Equal(0, report.FailedCount);
                Assert.Equal(StreamingState.InQueue, pool.GetState("deferred"));
            }
            finally
            {
                pool.Dispose();
            }
        }

        [Test]
        public void PinnedEntrySurvivesTrimAndDisposeUnloadsItOnce()
        {
            int loadCount = 0;
            int unloadCount = 0;
            StreamingPool<string, string> pool = new StreamingPool<string, string>(2,
                delegate(string key, out string value, out string failure)
                {
                    loadCount++;
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                },
                delegate(string key, string value) { unloadCount++; },
                StringComparer.OrdinalIgnoreCase);
            pool.Request("generic", 0, true);
            pool.LoadAllRequested();
            pool.Release("generic");
            Assert.Equal(0, pool.GetReferenceCount("generic"));
            Assert.Equal(StreamingState.Loaded, pool.GetState("generic"));
            pool.Trim();
            Assert.Equal(1, pool.LoadedCount);
            Assert.Equal(1, loadCount);

            pool.Dispose();
            pool.Dispose();
            Assert.Equal(1, unloadCount);
            Assert.Throws<ObjectDisposedException>(delegate { pool.Request("generic"); });
        }

        [Test]
        public void DisposeStaysDisposedWhenAnUnloaderThrows()
        {
            StreamingPool<string, string> pool = new StreamingPool<string, string>(1,
                delegate(string key, out string value, out string failure)
                {
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                },
                delegate(string key, string value)
                {
                    throw new InvalidOperationException("synthetic unload failure");
                }, StringComparer.OrdinalIgnoreCase);
            pool.Request("one");
            pool.LoadAllRequested();
            Assert.Throws<InvalidOperationException>(delegate { pool.Dispose(); });
            Assert.Throws<ObjectDisposedException>(delegate { pool.Request("one"); });
            Assert.Equal(0, pool.LoadedCount);
        }

        [Test]
        public void FailedAndCancelledRequestsRemainExplicitAndCanRetry()
        {
            bool fail = true;
            StreamingPool<string, string> pool = new StreamingPool<string, string>(2,
                delegate(string key, out string value, out string failure)
                {
                    if (fail)
                    {
                        value = null;
                        failure = "synthetic failure";
                        return StreamingLoadOutcome.Failed;
                    }
                    value = key;
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                }, null, StringComparer.OrdinalIgnoreCase);
            try
            {
                pool.Request("broken");
                StreamingLoadReport failed = pool.LoadAllRequested();
                Assert.Equal(1, failed.FailedCount);
                Assert.Equal(1, pool.FailedCount);
                Assert.Equal(StreamingState.Failed, pool.GetState("broken"));
                Assert.Equal("synthetic failure", pool.GetFailure("broken"));
                string resource;
                Assert.False(pool.TryGetResource("broken", out resource));

                fail = false;
                pool.ReleaseAll();
                pool.Request("broken");
                Assert.Equal(StreamingState.InQueue, pool.GetState("broken"));
                Assert.Equal(0, pool.FailedCount);
                Assert.Equal(1, pool.LoadAllRequested().LoadedCount);
                Assert.Equal(StreamingState.Loaded, pool.GetState("broken"));

                pool.ReleaseAll();
                pool.Request("cancelled");
                pool.Release("cancelled");
                Assert.Equal(StreamingState.NotLoaded, pool.GetState("cancelled"));
                Assert.Equal(0, pool.LoadAllRequested().LoadedCount);
            }
            finally
            {
                pool.Dispose();
            }
        }
    }
}