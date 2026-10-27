using System;
using System.Runtime.InteropServices;
using SharpGTA.Assets;
using SharpGTA.D3D9;
using SharpGTA.Rendering;

namespace SharpGTA.Tests
{
    public sealed class D3D9ModelMeshTests
    {
        [Test]
        public void ConversionPreservesSharedVerticesAndOriginalColors()
        {
            DffGeometry geometry = DffReader.ReadClump(DffTestData.Create(0x34003u)).Geometries[0];
            GeometryRenderData converted = DffRenderConverter.ConvertSingleGeometry(geometry, "indexed");
            MeshData mesh = converted.Meshes[0];
            D3dModelVertex[] vertices = D3D9ModelRenderer.ConvertVertices(mesh);

            Assert.Equal(mesh.Vertices.Length, vertices.Length);
            Assert.Equal(3, vertices.Length);
            Assert.Equal(6, mesh.Indices.Length);
            Assert.Equal(D3D9ModelRenderer.ToD3dColor(mesh.Vertices[0].ColorRgba), vertices[0].Color);
            Assert.Near(mesh.Vertices[0].Position.X, vertices[0].X);
            Assert.Near(mesh.Vertices[0].Normal.Y, vertices[0].NormalY);
            Assert.Near(mesh.Vertices[0].Uv.Y, vertices[0].V);
        }

        [Test]
        public void MeshDisposeReleasesBothComBuffersExactlyOnce()
        {
            FakeCom vertex = new FakeCom();
            FakeCom index = new FakeCom();
            try
            {
                D3D9ModelMesh mesh = new D3D9ModelMesh(vertex.Instance, index.Instance, 3, 6);
                Assert.Equal(3, mesh.VertexCount);
                Assert.Equal(6, mesh.IndexCount);
                Assert.Equal(2, mesh.TriangleCount);
                mesh.Dispose();
                mesh.Dispose();
                Assert.Equal(1, vertex.ReleaseCount);
                Assert.Equal(1, index.ReleaseCount);
                Assert.Throws<ObjectDisposedException>(delegate
                {
                    IntPtr unused = mesh.VertexBuffer;
                });
            }
            finally
            {
                vertex.Dispose();
                index.Dispose();
            }
        }

        [Test]
        public void InvalidMeshHandlesAndUnavailableDeviceAreRejectedBeforeNativeCalls()
        {
            Assert.Throws<ArgumentNullException>(delegate
            {
                new D3D9ModelMesh(IntPtr.Zero, IntPtr.Zero, 3, 6);
            });
            D3D9Device device = new D3D9Device();
            Assert.Throws<InvalidOperationException>(delegate
            {
                device.CreateModelMesh(null, null);
            });
        }

        private sealed class FakeCom : IDisposable
        {
            private readonly IntPtr table;
            private ComReleaseDelegate release;

            internal FakeCom()
            {
                table = Marshal.AllocHGlobal(checked(3 * IntPtr.Size));
                Instance = Marshal.AllocHGlobal(IntPtr.Size);
                release = delegate(IntPtr self)
                {
                    if (self != Instance)
                        throw new ArgumentException("Wrong COM instance released.");
                    ReleaseCount++;
                    return 0u;
                };
                Marshal.WriteIntPtr(table, IntPtr.Zero);
                Marshal.WriteIntPtr(table, IntPtr.Size, IntPtr.Zero);
                Marshal.WriteIntPtr(table, 2 * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(release));
                Marshal.WriteIntPtr(Instance, table);
            }

            internal IntPtr Instance { get; private set; }
            internal int ReleaseCount { get; private set; }

            public void Dispose()
            {
                if (Instance != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Instance);
                    Instance = IntPtr.Zero;
                }
                if (table != IntPtr.Zero)
                    Marshal.FreeHGlobal(table);
                GC.KeepAlive(release);
                release = null;
            }
        }
    }
}
