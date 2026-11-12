using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class WorldPreviewTests
    {
        [Test]
        public void VisibleStaticPlacementsKeepAtomicAndIplTransforms()
        {
            StaticWorld world = CreateWorld();
            GeometryRenderData[] atomics = DffRenderConverter.ConvertClump(
                DffReader.ReadClump(DffTestData.Create(0x34003u)), "world");
            Matrix[] atomicTransforms = { Matrix.CreateTranslation(2.0f, 0.0f, 0.0f) };
            int modelRequests = 0;
            int transformRequests = 0;
            Func<StaticInstance, GeometryRenderData[]> modelResolver = delegate(StaticInstance instance)
            {
                modelRequests++;
                return atomics;
            };
            Func<StaticInstance, Matrix[]> transformResolver = delegate(StaticInstance instance)
            {
                transformRequests++;
                return atomicTransforms;
            };

            RenderSceneSnapshot visible = StaticWorldScene.BuildVisible(world.Instances,
                new Vector3(10.0f, 20.0f, 30.0f), 5.0f, modelResolver, transformResolver);
            Assert.Equal(1, visible.DrawCalls.Count);
            Assert.Equal(1, modelRequests);
            Assert.Equal(1, transformRequests);
            Assert.Near(12.0f, visible.DrawCalls[0].Transform.Position.X);
            Assert.Near(20.0f, visible.DrawCalls[0].Transform.Position.Y);
            Assert.Near(30.0f, visible.DrawCalls[0].Transform.Position.Z);

            RenderSceneSnapshot culled = StaticWorldScene.BuildVisible(world.Instances,
                new Vector3(100.0f, 20.0f, 30.0f), 5.0f, modelResolver, transformResolver);
            Assert.Equal(0, culled.DrawCalls.Count);
            Assert.Equal(1, modelRequests);
            Assert.Equal(1, transformRequests);
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                StaticWorldScene.BuildVisible(world.Instances, Vector3.Zero, 0.0f,
                    modelResolver, transformResolver);
            });
        }

        [Test]
        public void ResolvesOriginalLodSuffixAndHandsOffDrawDistance()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, dinfoo, generic, 1, 120, 0\n2, LODfoo, generic, 1, 750, 0\n" +
                "3, zero, generic, 1, 0, 0\n4, damaged, generic, 2, 200, 100, 0\nend\n"));
            ModelDefinition detail = registry.Find("dinfoo");
            ModelDefinition lod = registry.Find("LODfoo");
            ModelDefinition zero = registry.Find("zero");
            ModelDefinition damaged = registry.Find("damaged");
            Assert.Equal(detail, lod.RelatedModel);
            Assert.Near(120.0f, lod.LodNearDistance);
            Assert.Near(750.0f, lod.LargestLodDistance);
            Assert.Equal(null, detail.RelatedModel);
            Assert.Near(0.0f, zero.LargestLodDistance);
            Assert.Equal(1, damaged.FirstDamaged);
            Assert.Near(200.0f, damaged.LargestLodDistance);

            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, dinfoo, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "2, LODfoo, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "3, zero, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "4, damaged, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            StaticInstance detailInstance = FindModel(world, "dinfoo");
            StaticInstance lodInstance = FindModel(world, "LODfoo");
            StaticInstance zeroInstance = FindModel(world, "zero");
            StaticInstance damagedInstance = FindModel(world, "damaged");
            Assert.False(StaticWorldScene.IsVisible(zeroInstance, Vector3.Zero, 1000.0f));
            Assert.True(StaticWorldScene.IsVisible(damagedInstance, Vector3.Zero, 1000.0f));

            Assert.True(StaticWorldScene.IsVisible(detailInstance, Vector3.Zero, 1000.0f));
            Assert.False(StaticWorldScene.IsVisible(lodInstance, Vector3.Zero, 1000.0f));
            Assert.True(StaticWorldScene.IsVisible(lodInstance, Vector3.Zero, 1000.0f,
                delegate(ModelDefinition model) { return false; }));
            Assert.True(StaticWorldScene.IsLodFallbackCandidate(lodInstance, Vector3.Zero, 1000.0f));
            Assert.False(StaticWorldScene.IsLodFallbackCandidate(detailInstance, Vector3.Zero, 1000.0f));
            Assert.False(StaticWorldScene.IsVisible(detailInstance,
                new Vector3(200.0f, 0.0f, 0.0f), 1000.0f));
            Assert.True(StaticWorldScene.IsVisible(lodInstance,
                new Vector3(200.0f, 0.0f, 0.0f), 1000.0f));
            Assert.False(StaticWorldScene.IsVisible(lodInstance,
                new Vector3(800.0f, 0.0f, 0.0f), 1000.0f));
        }

        [Test]
        public void FreeFlyCameraMovesInViewPlaneNormalizesInputAndClampsPitch()
        {
            FreeFlyCamera camera = new FreeFlyCamera();
            camera.Position = new Vector3(10.0f, 20.0f, 30.0f);
            camera.Speed = 18.0f;
            camera.Move(true, false, false, true, false, false, 1.0f);
            float diagonal = 18.0f / (float)Math.Sqrt(2.0);
            Assert.Near(10.0f + diagonal, camera.Position.X, 0.0005f);
            Assert.Near(20.0f + diagonal, camera.Position.Y, 0.0005f);
            Assert.Near(30.0f, camera.Position.Z);

            Vector3 before = camera.Position;
            camera.Move(true, true, true, true, true, true, 1.0f);
            Assert.Equal(before, camera.Position);
            camera.Move(false, false, false, false, false, true, 0.5f);
            Assert.Near(before.Z + 9.0f, camera.Position.Z);
            camera.Look(0.0f, 100.0f);
            Assert.Near(1.45f, camera.Pitch);
            camera.Look(0.0f, -100.0f);
            Assert.Near(-1.45f, camera.Pitch);
        }

        private static StaticInstance FindModel(StaticWorld world, string name)
        {
            for (int i = 0; i < world.Instances.Count; i++)
            {
                if (string.Equals(world.Instances[i].Model.Name, name, StringComparison.OrdinalIgnoreCase))
                    return world.Instances[i];
            }
            throw new InvalidOperationException("Synthetic instance was not found: " + name);
        }

        private static StaticWorld CreateWorld()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 100, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 10, 20, 30, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }
    }
}
