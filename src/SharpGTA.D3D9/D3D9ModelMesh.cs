using System;

namespace SharpGTA.D3D9
{
    /// <summary>Managed-pool vertex/index buffers shared by every placement of one mesh.</summary>
    internal sealed class D3D9ModelMesh : IDisposable
    {
        private IntPtr vertexBuffer;
        private IntPtr indexBuffer;

        internal D3D9ModelMesh(IntPtr vertexBuffer, IntPtr indexBuffer,
            int vertexCount, int indexCount)
        {
            if (vertexBuffer == IntPtr.Zero)
                throw new ArgumentNullException("vertexBuffer");
            if (indexBuffer == IntPtr.Zero)
                throw new ArgumentNullException("indexBuffer");
            if (vertexCount <= 0)
                throw new ArgumentOutOfRangeException("vertexCount");
            if (indexCount <= 0 || indexCount % 3 != 0)
                throw new ArgumentOutOfRangeException("indexCount");
            this.vertexBuffer = vertexBuffer;
            this.indexBuffer = indexBuffer;
            VertexCount = vertexCount;
            IndexCount = indexCount;
        }

        internal IntPtr VertexBuffer
        {
            get
            {
                if (vertexBuffer == IntPtr.Zero)
                    throw new ObjectDisposedException("D3D9ModelMesh");
                return vertexBuffer;
            }
        }

        internal IntPtr IndexBuffer
        {
            get
            {
                if (indexBuffer == IntPtr.Zero)
                    throw new ObjectDisposedException("D3D9ModelMesh");
                return indexBuffer;
            }
        }

        public int VertexCount { get; private set; }
        public int IndexCount { get; private set; }
        public int TriangleCount { get { return IndexCount / 3; } }

        public void Dispose()
        {
            Release(ref indexBuffer);
            Release(ref vertexBuffer);
        }

        private static void Release(ref IntPtr instance)
        {
            if (instance == IntPtr.Zero)
                return;
            ComReleaseDelegate release = ComVtable.GetMethod<ComReleaseDelegate>(
                instance, D3dVtableSlots.ComRelease);
            release(instance);
            instance = IntPtr.Zero;
        }
    }
}
