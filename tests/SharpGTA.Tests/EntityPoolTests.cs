using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class EntityPoolTests
    {
        private sealed class Item
        {
            internal Item(string name) { Name = name; }
            internal string Name;
        }

        [Test]
        public void HandlesEncodeSlotAndGenerationAndSurviveReuse()
        {
            EntityPool<Item> pool = new EntityPool<Item>(3);
            Assert.Equal(3, pool.Capacity);
            Assert.Equal(0, pool.UsedSpace);
            Assert.Equal(3, pool.FreeSpace);

            // CPool::New starts after the current cursor, so slot 0 is used last.
            int first = pool.New(new Item("a"));
            int second = pool.New(new Item("b"));
            Assert.Equal(1, first >> 8);
            Assert.Equal(1, first & 0xFF);
            Assert.Equal(2, second >> 8);
            Assert.Equal(2, pool.UsedSpace);
            Assert.True(ReferenceEquals(pool.GetAt(first), pool.GetSlot(1)));
            Assert.Equal("a", pool.GetAt(first).Name);

            Assert.True(pool.Delete(first));
            Assert.Equal(null, pool.GetAt(first));
            Assert.Equal(-1, pool.GetIndex(new Item("foreign")));

            // Reuse follows the original cursor: after Delete(1) the cursor is 1, and
            // the next New lands on slot 0 because slot 1 is skipped by the pre-increment.
            int reused = pool.New(new Item("c"));
            Assert.Equal(0, reused >> 8);
            Assert.Equal(1, reused & 0xFF);
            Assert.Equal(null, pool.GetAt(first));
            Assert.Equal("c", pool.GetAt(reused).Name);
            pool.Clear();
            Assert.Equal(0, pool.UsedSpace);
        }

        [Test]
        public void FullPoolReturnsMinusOneAndDeleteRewindsTheCursor()
        {
            EntityPool<Item> pool = new EntityPool<Item>(2);
            Item a = new Item("a");
            Item b = new Item("b");
            int first = pool.New(a);
            int second = pool.New(b);
            Assert.True(first >= 0 && second >= 0);
            Assert.Equal(-1, pool.New(new Item("c")));
            Assert.Equal(2, pool.UsedSpace);

            Assert.True(pool.Delete(second));
            Assert.Equal(1, pool.UsedSpace);
            int third = pool.New(new Item("d"));
            Assert.Equal(second >> 8, third >> 8);
            Assert.Equal(null, pool.GetAt(second));
            Assert.Equal("d", pool.GetAt(third).Name);

            Assert.False(pool.Delete(third + 1));
            Assert.False(pool.Delete(-2));
            Assert.Equal(null, pool.GetAt(-1));
            Assert.Equal(null, pool.GetAt(0x7FFF));
        }

        [Test]
        public void GenerationWrapsAfterOneHundredTwentySevenReuses()
        {
            EntityPool<Item> pool = new EntityPool<Item>(1);
            int handle = pool.New(new Item("x"));
            int index = handle >> 8;
            int generation = handle & 0xFF;
            for (int i = 0; i < 127; i++)
            {
                Assert.True(pool.Delete(handle));
                handle = pool.New(new Item("x"));
                Assert.Equal(index, handle >> 8);
                generation = (generation + 1) & 0x7F;
                Assert.Equal(generation, handle & 0xFF);
            }
            // The original 7-bit generation wraps, so an ancient handle becomes valid again.
            Assert.Equal(0, generation);
            Assert.Equal("x", pool.GetAt(handle).Name);
        }

        [Test]
        public void SlotAndIndexQueriesRespectUseAndRange()
        {
            EntityPool<Item> pool = new EntityPool<Item>(2);
            Item live = new Item("live");
            int handle = pool.New(live);
            int index = handle >> 8;
            Assert.Equal(handle, pool.GetIndex(live));
            Assert.True(ReferenceEquals(live, pool.GetSlot(index)));
            Assert.Equal(null, pool.GetSlot(-1));
            Assert.Equal(null, pool.GetSlot(2));
            Assert.Equal(null, pool.GetSlot(0));
            Assert.Throws<ArgumentNullException>(delegate { pool.GetIndex(null); });

            Assert.True(pool.Delete(handle));
            Assert.Equal(null, pool.GetSlot(index));
            Assert.Equal(-1, pool.GetIndex(live));
            Assert.False(pool.Delete(handle));
            pool.VerifyUsedSpace();
            Assert.Equal(0, pool.UsedSpace);
        }

        [Test]
        public void ClearResetsSlotsAndGenerations()
        {
            EntityPool<Item> pool = new EntityPool<Item>(2);
            Item first = new Item("a");
            int handle = pool.New(first);
            pool.New(new Item("b"));
            pool.Clear();
            Assert.Equal(0, pool.UsedSpace);
            Assert.Equal(2, pool.FreeSpace);
            Assert.Equal(null, pool.GetAt(handle));

            // Clear restarts the slot flags, so generation numbering restarts too.
            int reused = pool.New(first);
            Assert.Equal(1, reused >> 8);
            Assert.Equal(1, reused & 0xFF);
            pool.VerifyUsedSpace();
        }
    }

    public sealed class GameClockRuntimeTests
    {
        [Test]
        public void GameClockStartsAtNoonAndTicksOneMinutePerThirtyFrames()
        {
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 8));
            GameClock clock = new GameClock();
            runtime.AddModule(clock);
            Assert.Equal(12, clock.Hours);
            Assert.Equal(0, clock.Minutes);

            runtime.RunTicks(29);
            Assert.Equal(12, clock.Hours);
            Assert.Equal(0, clock.Minutes);
            Assert.Equal(0, clock.TotalGameMinutes);
            runtime.RunTicks(1);
            Assert.Equal(12, clock.Hours);
            Assert.Equal(1, clock.Minutes);
            Assert.Equal(1, clock.TotalGameMinutes);

            runtime.RunTicks(30 * 60);
            Assert.Equal(13, clock.Hours);
            Assert.Equal(1, clock.Minutes);

            clock.SetTime(23, 59);
            runtime.RunTicks(30);
            Assert.Equal(0, clock.Hours);
            Assert.Equal(0, clock.Minutes);
            Assert.Equal(1, clock.DaysPassed);
        }

        [Test]
        public void TimeRangesUseExclusiveEndAndWrapPastMidnight()
        {
            GameClock clock = new GameClock();
            clock.SetTime(12, 0);
            Assert.True(clock.IsTimeInRange(6, 19));
            Assert.False(clock.IsTimeInRange(19, 6));
            clock.SetTime(21, 0);
            Assert.True(clock.IsTimeInRange(19, 6));
            Assert.False(clock.IsTimeInRange(6, 19));
            clock.SetTime(6, 0);
            Assert.True(clock.IsTimeInRange(6, 19));
            clock.SetTime(19, 0);
            Assert.False(clock.IsTimeInRange(6, 19));
            Assert.True(clock.IsTimeInRange(19, 6));
            // Equal bounds are an empty interval, exactly like CClock::GetIsTimeInRange.
            Assert.False(clock.IsTimeInRange(12, 12));
        }

        [Test]
        public void ClockSecondsFollowThePartialMinuteAndSetTimeRewindsThem()
        {
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 8));
            GameClock clock = new GameClock();
            runtime.AddModule(clock);
            runtime.RunTicks(15);
            Assert.Equal(0, clock.Minutes);
            Assert.True(clock.Seconds >= 30 && clock.Seconds < 60,
                "Half a game minute must report about 30 seconds, got " + clock.Seconds);
            runtime.RunTicks(15);
            Assert.Equal(1, clock.Minutes);
            Assert.True(clock.Seconds <= 3, "A fresh minute must restart seconds, got " + clock.Seconds);
            clock.SetTime(7, 30);
            Assert.Equal(7, clock.Hours);
            Assert.Equal(30, clock.Minutes);
            Assert.Equal(0, clock.Seconds);
        }
    }

    public sealed class TimeObjectControllerTests
    {
        private static ModelRegistry CreateRegistry()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n" +
                "1, house01, generic, 1, 300, 0\n" +
                "end\n" +
                "tobj\n" +
                "2, window_nt, generic, 1, 100, 0, 19, 6\n" +
                "3, window_dy, generic, 1, 100, 0, 6, 19\n" +
                "4, always_nt, generic, 1, 100, 0, 8, 19\n" +
                "end\n"));
            return registry;
        }

        private static StaticWorld CreateWorld(ModelRegistry registry)
        {
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "1, house01, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "2, window_nt, 10, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "3, window_dy, 20, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "4, always_nt, 30, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "end\n"), registry, new LoadReport());
            return world;
        }

        [Test]
        public void TimedModelsPairByDayNightSuffix()
        {
            ModelRegistry registry = CreateRegistry();
            ModelDefinition night = registry.Find("window_nt");
            ModelDefinition day = registry.Find("window_dy");
            Assert.True(night.IsTimed);
            Assert.Equal(19, night.TimeOn);
            Assert.Equal(6, night.TimeOff);
            Assert.Equal(day, night.OtherTimeModel);
            Assert.Equal(night, day.OtherTimeModel);
            Assert.Equal(null, registry.Find("always_nt").OtherTimeModel);
            Assert.Equal(null, registry.Find("house01").OtherTimeModel);
            Assert.False(registry.Find("house01").IsTimed);
        }

        [Test]
        public void PoolFollowsTheClockAndKeepsUntimedPlacementsVisible()
        {
            ModelRegistry registry = CreateRegistry();
            StaticWorld world = CreateWorld(registry);
            StaticInstance house = Find(world, "house01");
            StaticInstance night = Find(world, "window_nt");
            StaticInstance day = Find(world, "window_dy");
            StaticInstance lone = Find(world, "always_nt");

            GameClock clock = new GameClock();
            TimeObjectController controller = new TimeObjectController(4);
            clock.SetTime(12, 0);
            controller.Update(world.Instances, clock);
            Assert.True(controller.IsVisible(house));
            Assert.False(controller.IsVisible(night));
            Assert.True(controller.IsVisible(day));
            Assert.True(controller.IsVisible(lone));
            Assert.Equal(2, controller.AllocatedCount);
            Assert.Equal(2, controller.CreatedCount);
            Assert.Equal(0, controller.DestroyedCount);

            clock.SetTime(21, 0);
            controller.Update(world.Instances, clock);
            Assert.False(controller.IsVisible(day));
            Assert.True(controller.IsVisible(night));
            Assert.False(controller.IsVisible(lone));
            Assert.Equal(1, controller.AllocatedCount);
            Assert.Equal(3, controller.CreatedCount);
            Assert.Equal(2, controller.DestroyedCount);

            // Repeating the same hour must not churn the pool.
            controller.Update(world.Instances, clock);
            Assert.Equal(1, controller.AllocatedCount);
            Assert.Equal(3, controller.CreatedCount);
            Assert.Equal(2, controller.DestroyedCount);
            // Diagnostics rely on this invariant: live mappings == created - destroyed.
            Assert.Equal(controller.AllocatedCount,
                controller.CreatedCount - controller.DestroyedCount);
        }

        [Test]
        public void FullPoolRejectsNewTimeObjectsWithoutHidingOtherPlacements()
        {
            ModelRegistry registry = CreateRegistry();
            StaticWorld world = CreateWorld(registry);
            GameClock clock = new GameClock();
            clock.SetTime(12, 0);
            TimeObjectController controller = new TimeObjectController(1);
            controller.Update(world.Instances, clock);
            Assert.Equal(1, controller.AllocatedCount);
            Assert.Equal(1, controller.RejectedCount);
            Assert.True(controller.IsVisible(Find(world, "house01")));
            // window_dy and always_nt both wanted a slot; only the first got one.
            Assert.True(controller.IsVisible(Find(world, "window_dy")));
            Assert.False(controller.IsVisible(Find(world, "always_nt")));
        }

        [Test]
        public void SceneVisibilityDropsTimedPlacementsOutOfRange()
        {
            ModelRegistry registry = CreateRegistry();
            StaticWorld world = CreateWorld(registry);
            StaticInstance house = Find(world, "house01");
            StaticInstance night = Find(world, "window_nt");
            StaticInstance day = Find(world, "window_dy");

            GameClock clock = new GameClock();
            TimeObjectController controller = new TimeObjectController(4);
            clock.SetTime(12, 0);
            controller.Update(world.Instances, clock);
            Vector3 center = new Vector3(15.0f, 0.0f, 0.0f);
            Assert.True(StaticWorldScene.IsVisible(house, center, 1000.0f, null, controller));
            Assert.False(StaticWorldScene.IsVisible(night, center, 1000.0f, null, controller));
            Assert.True(StaticWorldScene.IsVisible(day, center, 1000.0f, null, controller));

            clock.SetTime(21, 0);
            controller.Update(world.Instances, clock);
            Assert.True(StaticWorldScene.IsVisible(night, center, 1000.0f, null, controller));
            Assert.False(StaticWorldScene.IsVisible(day, center, 1000.0f, null, controller));
            // Without a controller the old behaviour is unchanged.
            Assert.True(StaticWorldScene.IsVisible(night, center, 1000.0f));
        }

        [Test]
        public void TimedModelsTakePartInSuffixLodPairing()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "tobj\n" +
                "7, redfoo_nt, generic, 1, 130, 0, 19, 6\n" +
                "8, LODfoo_nt, generic, 1, 750, 0, 19, 6\n" +
                "end\n"));
            ModelDefinition detail = registry.Find("redfoo_nt");
            ModelDefinition lod = registry.Find("LODfoo_nt");
            Assert.True(detail.IsTimed && lod.IsTimed);
            Assert.Equal(detail, lod.RelatedModel);
            Assert.Near(130.0f, lod.LodNearDistance);
            Assert.Equal(null, detail.RelatedModel);

            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "7, redfoo_nt, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "8, LODfoo_nt, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            StaticInstance detailInstance = Find(world, "redfoo_nt");
            StaticInstance lodInstance = Find(world, "LODfoo_nt");

            GameClock clock = new GameClock();
            TimeObjectController controller = new TimeObjectController(4);
            clock.SetTime(21, 0);
            controller.Update(world.Instances, clock);
            // Both are in their hour window, so the LOD handoff decides visibility.
            Assert.True(StaticWorldScene.IsVisible(detailInstance, Vector3.Zero, 1000.0f, null, controller));
            Assert.False(StaticWorldScene.IsVisible(lodInstance, Vector3.Zero, 1000.0f, null, controller));
            Assert.True(StaticWorldScene.IsVisible(lodInstance,
                new Vector3(200.0f, 0.0f, 0.0f), 1000.0f, null, controller));
            Assert.False(StaticWorldScene.IsVisible(detailInstance,
                new Vector3(200.0f, 0.0f, 0.0f), 1000.0f, null, controller));
        }

        [Test]
        public void TimeSuffixSwapDropsTailAndKeepsPrefixNamesDistinct()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 64));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "tobj\n" +
                "11, a_nt_extra, generic, 1, 100, 0, 19, 6\n" +
                "12, a_dy, generic, 1, 100, 0, 6, 19\n" +
                "13, b_nt, generic, 1, 100, 0, 19, 6\n" +
                "14, b_dy, generic, 1, 100, 0, 6, 19\n" +
                "15, c_dy, generic, 1, 100, 0, 6, 19\n" +
                "16, c_nt2, generic, 1, 100, 0, 19, 6\n" +
                "17, d_NT, generic, 1, 100, 0, 19, 6\n" +
                "18, d_DY, generic, 1, 100, 0, 6, 19\n" +
                "end\n"));
            // The original truncates the name at the swapped token, so the tail is dropped.
            Assert.Equal(registry.Find("a_dy"), registry.Find("a_nt_extra").OtherTimeModel);
            Assert.Equal(registry.Find("b_dy"), registry.Find("b_nt").OtherTimeModel);
            // Uppercase tokens still switch; the counterpart is found case-insensitively.
            Assert.Equal(registry.Find("d_DY"), registry.Find("d_NT").OtherTimeModel);
            // "c_dy" switches to "c_nt", which does not exist; "c_nt2" truncates to "c_dy"
            // and does pair. A prefix comparison would also have paired "c_dy" with "c_nt2".
            Assert.Equal(null, registry.Find("c_dy").OtherTimeModel);
            Assert.Equal(registry.Find("c_dy"), registry.Find("c_nt2").OtherTimeModel);
        }

        [Test]
        public void ResetClearsMappingsCountersAndVisibility()
        {
            ModelRegistry registry = CreateRegistry();
            StaticWorld world = CreateWorld(registry);
            StaticInstance day = Find(world, "window_dy");
            GameClock clock = new GameClock();
            clock.SetTime(12, 0);
            TimeObjectController controller = new TimeObjectController(4);
            controller.Update(world.Instances, clock);
            Assert.Equal(2, controller.AllocatedCount);
            Assert.True(controller.IsVisible(day));

            controller.Reset();
            Assert.Equal(0, controller.AllocatedCount);
            Assert.Equal(0, controller.CreatedCount);
            Assert.Equal(0, controller.DestroyedCount);
            Assert.Equal(0, controller.RejectedCount);
            Assert.False(controller.IsVisible(day));

            controller.Update(world.Instances, clock);
            Assert.Equal(2, controller.AllocatedCount);
            Assert.Equal(2, controller.CreatedCount);
        }

        private static StaticInstance Find(StaticWorld world, string name)
        {
            for (int i = 0; i < world.Instances.Count; i++)
            {
                if (string.Equals(world.Instances[i].Model.Name, name, StringComparison.OrdinalIgnoreCase))
                    return world.Instances[i];
            }
            throw new InvalidOperationException("Synthetic instance was not found: " + name);
        }
    }
}