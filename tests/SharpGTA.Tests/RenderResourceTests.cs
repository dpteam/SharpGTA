using System;
using SharpGTA.Assets;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;

namespace SharpGTA.Tests
{
    public sealed class RenderResourceTests
    {
        [Test]
        public void ConvertsGeometryMaterialsAndSharedVerticesWithoutLosingBindings()
        {
            DffGeometry geometry = DffReader.ReadClump(DffTestData.Create(0x34003u)).Geometries[0];
            GeometryRenderData converted = DffRenderConverter.ConvertSingleGeometry(geometry, "test");
            Assert.Equal(1, converted.Meshes.Length);
            Assert.Equal(2, converted.Materials.Length);
            Assert.True(ReferenceEquals(converted.Vertices, converted.Meshes[0].Vertices));
            Assert.Equal(0, converted.Meshes[0].MaterialIndex);
            Assert.Equal("brick", converted.Materials[0].TextureName);
            Assert.Equal(2, converted.Meshes[0].Indices.Length / 3);
            Assert.Equal(0, converted.Meshes[0].Indices[0]);
            Assert.Equal(3, converted.Vertices.Length);
            Assert.Equal(0x04030201u, converted.Vertices[0].ColorRgba);
            Assert.Near(0.1f, converted.Vertices[0].Uv.X);
        }

        [Test]
        public void TextureCacheDecodesOnceAndKeepsMissingLookupSeparate()
        {
            RenderResourceManager resources = new RenderResourceManager();
            resources.AddDictionary(TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("brick", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 1, 255 } }))));
            TextureData first = resources.GetTexture("BRICK");
            TextureData second = resources.GetTexture("brick");
            Assert.NotNull(first);
            Assert.True(ReferenceEquals(first, second));
            Assert.Equal(1, resources.TextureCount);
            Assert.Equal(4L, resources.TextureBytes);
            Assert.Equal((byte)1, first.Rgba[0]);
            Assert.Equal(null, resources.GetTexture("missing"));
            Assert.Equal(1, resources.MissingTextureCount);
        }

        [Test]
        public void GenericFallbackDoesNotOverrideModelDictionaryTextures()
        {
            RenderResourceManager resources = new RenderResourceManager();
            resources.AddFallbackDictionary(TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("shared", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 9, 255 } }),
                TxdTestData.CreateTexture("generic_only", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 7, 255 } }))));
            Assert.Equal((byte)9, resources.GetTexture("shared").Rgba[0]);
            resources.AddDictionary(TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("shared", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 1, 255 } }))));

            // Late model TXD registration must invalidate an already cached fallback.
            Assert.Equal((byte)1, resources.GetTexture("shared").Rgba[0]);
            Assert.Equal((byte)7, resources.GetTexture("generic_only").Rgba[0]);
            Assert.Equal(0, resources.MissingTextureCount);
        }

        [Test]
        public void ModelDictionaryContextSeparatesEqualNamesBeforeGenericFallback()
        {
            RenderResourceManager resources = new RenderResourceManager();
            resources.AddFallbackDictionary(TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("shared", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 9, 255 } }),
                TxdTestData.CreateTexture("fallback", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 7, 255 } }))));
            resources.AddDictionary("first", TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("shared", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 1, 255 } }))));
            resources.AddDictionary("second", TxdReader.Read(TxdTestData.CreateArchive(
                TxdTestData.CreateTexture("shared", 1, 1, TxdCompression.Uncompressed,
                    0x0500u, new byte[][] { new byte[] { 3, 2, 2, 255 } }))));

            TextureData first = resources.GetTexture("first", "shared");
            TextureData second = resources.GetTexture("second", "shared");
            Assert.Equal((byte)1, first.Rgba[0]);
            Assert.Equal((byte)2, second.Rgba[0]);
            Assert.False(ReferenceEquals(first, second));
            Assert.Equal((byte)9, resources.GetTexture("generic", "shared").Rgba[0]);
            Assert.Equal((byte)7, resources.GetTexture("second", "fallback").Rgba[0]);
            Assert.True(ReferenceEquals(first, resources.GetTexture("FIRST", "SHARED")));
            Assert.Equal(4, resources.TextureCount);
        }

        [Test]
        public void NullRendererValidatesSceneAndDoesNotUseNativeApis()
        {
            DffGeometry geometry = DffReader.ReadClump(DffTestData.Create(0x34003u)).Geometries[0];
            GeometryRenderData converted = DffRenderConverter.ConvertSingleGeometry(geometry, "scene");
            RenderDrawCall call = new RenderDrawCall(converted, converted.Meshes[0],
                converted.Materials[0], Matrix.CreateTranslation(new Vector3(1.0f, 2.0f, 3.0f)));
            RenderSceneSnapshot snapshot = new RenderSceneSnapshot(new RenderDrawCall[] { call });
            using (NullRenderDevice device = new NullRenderDevice())
            {
                device.Render(snapshot);
                Assert.Equal(1, device.FrameCount);
                Assert.Equal(1, device.LastStatistics.DrawCallCount);
                Assert.Equal(2, device.LastStatistics.TriangleCount);
                Assert.Equal(6L, device.LastStatistics.VertexCount);
                Assert.Equal(0, device.LastStatistics.InvalidReferenceCount);
                device.Dispose();
                Assert.Throws<ObjectDisposedException>(delegate { device.Render(snapshot); });
            }
        }
    }
}
