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
