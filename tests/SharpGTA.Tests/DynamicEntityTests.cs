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
    public sealed class DynamicEntityTests
    {
        [Test]
        public void SpawnIndexesIntoTheSharedGridAndTicksAcrossSectors()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            ModelDefinition model = registry.Find("plain");

            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Object, model,
                new Vector3(10.0f, 0.0f, 0.0f), new Vector3(40.0f, 0.0f, 0.0f), out entity));
            Assert.True(entity.Handle > 0);
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Object));
            Assert.Equal(1, world.DynamicEntities.Count);
            Assert.False(entity.IsStatic);

            List<WorldEntity> entities = new List<WorldEntity>();
            // The grid is shared with placements, so the static instance at the origin is
            // returned too; membership of the dynamic entity is what matters here.
            Assert.Equal(2, world.QueryEntities(Vector3.Zero, 100.0f, entities));
            Assert.True(Contains(entities, entity));
            // Placement-only queries must not leak gameplay entities.
            List<StaticInstance> placements = new List<StaticInstance>();
            Assert.Equal(world.Instances.Count, world.QuerySectors(Vector3.Zero, 100.0f, placements));
            for (int i = 0; i < placements.Count; i++)
                Assert.False(ReferenceEquals(placements[i], entity));

            // One second at 40 u/s crosses a 40 unit sector boundary.
            Assert.Equal(1, world.AdvanceDynamic(1.0f));
            Assert.Near(50.0f, entity.Position.X, 0.001f);
            Assert.Equal(1, world.SectorMoves);
            world.QueryEntities(Vector3.Zero, 20.0f, entities);
            Assert.False(Contains(entities, entity));
            // Candidate sets are sector based, so the origin placement may still be listed;
            // what matters is that the moved entity is now found where it actually is.
            world.QueryEntities(new Vector3(50.0f, 0.0f, 0.0f), 20.0f, entities);
            Assert.True(Contains(entities, entity));

            // No movement means no membership churn.
            Assert.Equal(0, world.AdvanceDynamic(0.0f));
            Assert.Equal(1, world.SectorMoves);
        }

        [Test]
        public void DespawnReleasesTheHandleAndRemovesItFromTheGrid()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("plain"),
                new Vector3(5.0f, 5.0f, 0.0f), Vector3.Zero, out entity));
            int handle = entity.Handle;

            Assert.True(world.DespawnEntity(WorldEntityKind.Ped, handle));
            Assert.Equal(0, world.Entities.Count(WorldEntityKind.Ped));
            Assert.Equal(0, world.DynamicEntities.Count);
            WorldEntity stale;
            Assert.False(world.TryGetEntity(WorldEntityKind.Ped, handle, out stale));
            Assert.False(world.DespawnEntity(WorldEntityKind.Ped, handle));

            List<WorldEntity> entities = new List<WorldEntity>();
            world.QueryEntities(new Vector3(5.0f, 5.0f, 0.0f), 3.0f, entities);
            Assert.False(Contains(entities, entity));

            // Static placements cannot be despawned through the dynamic path.
            Assert.False(world.DespawnEntity(WorldEntityKind.Building, world.Instances[0].Handle));
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Building));
        }

        [Test]
        public void FullPoolRejectsSpawnsWithoutThrowing()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            ModelDefinition model = registry.Find("plain");
            // The synthetic definition allows 16 objects.
            int spawned = 0;
            for (int i = 0; i < 20; i++)
            {
                DynamicEntity entity;
                if (world.TrySpawnEntity(WorldEntityKind.Object, model,
                    new Vector3(i, 0.0f, 0.0f), Vector3.Zero, out entity))
                    spawned++;
            }
            Assert.Equal(16, spawned);
            Assert.Equal(16, world.Entities.Count(WorldEntityKind.Object));
            Assert.Equal(4, world.Entities.RejectedCount(WorldEntityKind.Object));
            Assert.Equal(16, world.DynamicEntities.Count);
        }

        [Test]
        public void OnlyGameplayKindsCanSpawn()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            ModelDefinition model = registry.Find("plain");
            DynamicEntity entity;
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                world.TrySpawnEntity(WorldEntityKind.Building, model, Vector3.Zero, Vector3.Zero,
                    out entity);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                world.TrySpawnEntity(WorldEntityKind.Object, null, Vector3.Zero, Vector3.Zero,
                    out entity);
            });
            Assert.Throws<InvalidOperationException>(delegate
            {
                new StaticWorld().TrySpawnEntity(WorldEntityKind.Object, model, Vector3.Zero,
                    Vector3.Zero, out entity);
            });
        }

        [Test]
        public void DeterministicAcrossIdenticalRunsAndRuntimeTicks()
        {
            ModelRegistry firstRegistry;
            StaticWorld first = CreateWorld(out firstRegistry);
            ModelRegistry secondRegistry;
            StaticWorld second = CreateWorld(out secondRegistry);
            SpawnSet(first, firstRegistry);
            SpawnSet(second, secondRegistry);

            SimulationRuntime runtime1 = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime1.AddModule(new DynamicEntityModule(first));
            SimulationRuntime runtime2 = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime2.AddModule(new DynamicEntityModule(second));
            runtime1.RunTicks(120);
            runtime2.RunTicks(120);

            Assert.Equal(120UL, runtime1.Clock.Tick);
            Assert.Equal(first.DynamicEntities.Count, second.DynamicEntities.Count);
            for (int i = 0; i < first.DynamicEntities.Count; i++)
            {
                DynamicEntity left = (DynamicEntity)first.DynamicEntities[i];
                DynamicEntity right = (DynamicEntity)second.DynamicEntities[i];
                Assert.Equal(left.Position.X, right.Position.X, "X drifted at entity " + i);
                Assert.Equal(left.Position.Y, right.Position.Y, "Y drifted at entity " + i);
                Assert.Equal(left.Position.Z, right.Position.Z, "Z drifted at entity " + i);
                Assert.Equal(left.Handle, right.Handle);
            }
            Assert.Equal(first.SectorMoves, second.SectorMoves);
            Assert.True(first.SectorMoves > 0, "Moving entities should cross sectors.");
        }

        [Test]
        public void SectorMembershipFollowsLargeJumpsNotOnlyTickCount()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity fast;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("plain"),
                Vector3.Zero, new Vector3(0.0f, 400.0f, 0.0f), out fast));
            // A single step jumps ten sectors; membership must move with it.
            Assert.Equal(1, world.AdvanceDynamic(1.0f));
            List<WorldEntity> entities = new List<WorldEntity>();
            world.QueryEntities(Vector3.Zero, 50.0f, entities);
            Assert.False(Contains(entities, fast));
            Assert.Equal(1, world.QueryEntities(new Vector3(0.0f, 400.0f, 0.0f), 50.0f, entities));
            Assert.True(Contains(entities, fast));
        }

        [Test]
        public void NonFinitePositionOrVelocityIsRejected()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            ModelDefinition model = registry.Find("plain");
            DynamicEntity entity;
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                world.TrySpawnEntity(WorldEntityKind.Object, model,
                    new Vector3(float.NaN, 0.0f, 0.0f), Vector3.Zero, out entity);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                world.TrySpawnEntity(WorldEntityKind.Object, model,
                    Vector3.Zero, new Vector3(0.0f, float.PositiveInfinity, 0.0f), out entity);
            });
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Object, model, Vector3.Zero,
                Vector3.Zero, out entity));
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                entity.Velocity = new Vector3(float.NaN, 0.0f, 0.0f);
            });
            Assert.Equal(0, world.DynamicRejectedCount);
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Object));
        }

        [Test]
        public void LeavingAndReenteringTheGridCountsAsSectorMoves()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity runner;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("plain"),
                new Vector3(1990.0f, 0.0f, 0.0f), new Vector3(100.0f, 0.0f, 0.0f), out runner));
            Assert.Equal(0, world.UnindexedCount);

            // 1 s at 100 u/s leaves the 2000 unit grid; membership changes even though the
            // clamped cell index would stay at 99.
            Assert.Equal(1, world.AdvanceDynamic(1.0f));
            Assert.Equal(1, world.UnindexedCount);
            List<WorldEntity> entities = new List<WorldEntity>();
            // Out-of-grid entities are always candidates (the grid cannot index them), so the
            // query still returns it; what matters is that it left the grid and the move counted.
            world.QueryEntities(Vector3.Zero, 50.0f, entities);
            Assert.True(Contains(entities, runner));

            runner.Velocity = new Vector3(-100.0f, 0.0f, 0.0f);
            Assert.Equal(1, world.AdvanceDynamic(1.0f));
            Assert.Equal(0, world.UnindexedCount);
            Assert.True(world.DespawnEntity(WorldEntityKind.Ped, runner.Handle));
            Assert.Equal(0, world.DynamicEntities.Count);
        }

        [Test]
        public void CollisionRevisionRebuildKeepsDynamicEntitiesIndexed()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            CollisionRepository collisions = new CollisionRepository(registry);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Vehicle, registry.Find("plain"),
                new Vector3(100.0f, 0.0f, 0.0f), Vector3.Zero, out entity));
            int handle = entity.Handle;

            // A late COL rebuilds the static grid; the dynamic entity must be re-indexed.
            collisions.Add(WorldTestAssets.CreateBoxCollision("plain", 20.0f, -1.0f, 1.0f), 0);
            List<WorldEntity> entities = new List<WorldEntity>();
            world.QueryEntities(new Vector3(100.0f, 0.0f, 0.0f), 20.0f, entities);
            Assert.True(Contains(entities, entity));
            Assert.Equal(handle, entity.Handle);
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Vehicle));
        }

        [Test]
        public void EntityVisibilityUsesRadiusAndModelDrawDistance()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry);
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Object, registry.Find("plain"),
                Vector3.Zero, Vector3.Zero, out entity));
            // "plain" advertises a 300 unit draw distance.
            Assert.True(StaticWorldScene.IsEntityVisible(entity, Vector3.Zero, 100.0f));
            Assert.False(StaticWorldScene.IsEntityVisible(entity, new Vector3(500.0f, 0.0f, 0.0f), 100.0f));
            Assert.True(StaticWorldScene.IsEntityVisible(entity, new Vector3(200.0f, 0.0f, 0.0f), 1000.0f));
            Assert.False(StaticWorldScene.IsEntityVisible(entity, new Vector3(400.0f, 0.0f, 0.0f), 1000.0f));
            Assert.False(StaticWorldScene.IsEntityVisible(null, Vector3.Zero, 100.0f));

            // A model with draw distance below 2 is never visible, like the original.
            ModelRegistry zeroRegistry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            zeroRegistry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, zero, generic, 1, 0, 0\nend\n"));
            StaticWorld zeroWorld = new StaticWorld();
            zeroWorld.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, zero, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                zeroRegistry, new LoadReport());
            DynamicEntity zero;
            Assert.True(zeroWorld.TrySpawnEntity(WorldEntityKind.Object, zeroRegistry.Find("zero"),
                Vector3.Zero, Vector3.Zero, out zero));
            Assert.False(StaticWorldScene.IsEntityVisible(zero, Vector3.Zero, 100.0f));

            int resolved = 0;
            RenderSceneSnapshot snapshot = StaticWorldScene.BuildVisibleEntities(
                new List<WorldEntity> { entity }, Vector3.Zero, 100.0f,
                delegate(WorldEntity e) { resolved++; return null; },
                delegate(WorldEntity e) { return new Matrix[0]; });
            Assert.Equal(1, resolved);
            Assert.Equal(0, snapshot.DrawCalls.Count);

            Assert.Throws<ArgumentNullException>(delegate
            {
                StaticWorldScene.BuildVisibleEntities(null, Vector3.Zero, 100.0f,
                    delegate(WorldEntity e) { return null; },
                    delegate(WorldEntity e) { return new Matrix[0]; });
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                StaticWorldScene.BuildVisibleEntities(new List<WorldEntity> { entity }, Vector3.Zero, 0.0f,
                    delegate(WorldEntity e) { return null; },
                    delegate(WorldEntity e) { return new Matrix[0]; });
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                StaticWorldScene.BuildVisibleEntities(new List<WorldEntity> { entity }, Vector3.Zero, 100.0f,
                    null, delegate(WorldEntity e) { return new Matrix[0]; });
            });
        }

        private static bool Contains(IList<WorldEntity> list, WorldEntity entity)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], entity))
                    return true;
            }
            return false;
        }

        private static void SpawnSet(StaticWorld world, ModelRegistry registry)
        {
            ModelDefinition model = registry.Find("plain");
            for (int i = 0; i < 6; i++)
            {
                DynamicEntity entity;
                Assert.True(world.TrySpawnEntity(WorldEntityKind.Object, model,
                    new Vector3(i * 10.0f, 0.0f, 0.0f), new Vector3(5.0f, i, 0.0f), out entity));
            }
        }

        private static StaticWorld CreateWorld(out ModelRegistry registry)
        {
            registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }
    }
}