using System;
using System.Text;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class AsyncByteCacheTests
    {
        private static byte[] Deliver(AsyncByteCache cache, string key)
        {
            byte[] bytes;
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!cache.TryTake(key, out bytes))
            {
                cache.Pump();
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("Timed out waiting for " + key + ".");
                System.Threading.Thread.Sleep(1);
            }
            return bytes;
        }

        [Test]
        public void CoalescesRequestsAndDeliversBytesOffTheSimulationThread()
        {
            int callerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int readerThread = callerThread;
            bool readerIsBackground = false;
            AsyncByteCache cache = new AsyncByteCache(128, 2,
                delegate(string key)
                {
                    readerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    readerIsBackground = System.Threading.Thread.CurrentThread.IsBackground;
                    return Encoding.ASCII.GetBytes(key);
                });
            try
            {
                Assert.Equal(2, cache.WorkerCount);
                Assert.True(cache.Request("brick"));
                Assert.False(cache.Request("BRICK"));
                Assert.True(cache.WaitFor("brick", 3000));
                byte[] bytes;
                Assert.True(cache.TryTake("brick", out bytes));
                Assert.Equal("brick", Encoding.ASCII.GetString(bytes));
                Assert.Equal(5L, cache.CachedBytes);
                Assert.True(readerThread != callerThread, "Byte reads must not run on the simulation thread.");
                Assert.True(readerIsBackground, "Byte read workers must be background threads.");
                cache.Pump();
                Assert.Equal(0, cache.PendingCount);
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void ByteBudgetEvictsLru()
        {
            AsyncByteCache cache = new AsyncByteCache(4, 1,
                delegate(string key) { return new byte[] { 1, 2, 3 }; });
            try
            {
                cache.Request("first");
                Deliver(cache, "first");
                cache.Request("second");
                Deliver(cache, "second");
                Assert.True(cache.CachedBytes <= 4L);
                Assert.Equal(1, cache.CachedCount);
                Assert.Equal(1, cache.EvictedCount);
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void WaitingBytesSurviveEvictionPressureUntilTheConsumerTakesThem()
        {
            AsyncByteCache cache = new AsyncByteCache(6, 2,
                delegate(string key) { return new byte[] { 1, 2, 3, 4 }; });
            try
            {
                Assert.True(cache.WaitFor("first", 3000));
                cache.Request("second");
                cache.Pump();
                // Both are 4 bytes with a 6 byte budget: "first" is still critical
                // until the consumer removes it, so only "second" can be evicted.
                byte[] first;
                Assert.True(cache.TryTake("first", out first));
                Assert.Equal(4, first.Length);
                Assert.True(cache.Remove("first"));
                Assert.False(cache.Remove("first"));
                Assert.True(cache.WaitFor("second", 3000));
                Assert.True(cache.CachedBytes <= 6L);
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void OversizedPayloadIsDeliveredToTheWaitingConsumerInsteadOfFailing()
        {
            AsyncByteCache cache = new AsyncByteCache(4, 1,
                delegate(string key) { return new byte[] { 1, 2, 3, 4, 5, 6 }; });
            try
            {
                // Unwaited oversize payloads are dropped as a cache miss, not as an error.
                Assert.True(cache.Request("big"));
                cache.Pump();
                while (cache.PendingCount > 0)
                {
                    cache.Pump();
                    System.Threading.Thread.Sleep(1);
                }
                Assert.Equal(0, cache.FailedCount);
                Assert.Equal(1, cache.OversizedCount);
                Assert.Equal(0L, cache.CachedBytes);

                Assert.True(cache.WaitFor("big", 3000));
                byte[] bytes;
                Assert.True(cache.TryTake("big", out bytes));
                Assert.Equal(6, bytes.Length);
                Assert.True(cache.Remove("big"));
                Assert.Equal(0, cache.FailedCount);
                Assert.Equal(0L, cache.CachedBytes);
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void FailedKeyIsNotRetriedUntilItIsForgotten()
        {
            int reads = 0;
            AsyncByteCache cache = new AsyncByteCache(64, 1,
                delegate(string key)
                {
                    reads++;
                    throw new InvalidOperationException("synthetic failure");
                });
            try
            {
                Assert.True(cache.Request("broken"));
                Assert.False(cache.WaitFor("broken", 3000));
                Assert.Equal(1, reads);
                Assert.Equal(1, cache.FailedCount);
                Assert.False(cache.Request("broken"));
                Assert.False(cache.WaitFor("broken", 3000));
                Assert.Equal(1, reads);
                Assert.True(cache.Forget("broken"));
                Assert.False(cache.Forget("broken"));
                Assert.Equal(0, cache.FailedCount);
                Assert.True(cache.Request("broken"));
                Assert.False(cache.WaitFor("broken", 3000));
                Assert.Equal(2, reads);
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void FailuresAreExplicitAndCanBeRetriedThenDisposeIsFinal()
        {
            AsyncByteCache cache = new AsyncByteCache(16, 1,
                delegate(string key)
                {
                    if (key == "broken")
                        throw new InvalidOperationException("synthetic I/O failure");
                    return new byte[] { 7 };
                });
            cache.Request("broken");
            Assert.False(cache.WaitFor("broken", 3000));
            Assert.True(cache.GetFailure("broken").Contains("synthetic I/O failure"));
            Assert.True(cache.Forget("broken"));
            Assert.True(cache.Request("broken"));
            Assert.False(cache.WaitFor("broken", 3000));
            cache.Dispose();
            Assert.Throws<ObjectDisposedException>(delegate { cache.Request("broken"); });
        }
    }
}