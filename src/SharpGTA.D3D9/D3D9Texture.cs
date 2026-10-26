using System;

namespace SharpGTA.D3D9
{
    /// <summary>A managed-pool A8R8G8B8 texture. It survives an explicit device Reset.</summary>
    internal sealed class D3D9Texture : IDisposable
    {
        private IntPtr handle;

        internal D3D9Texture(IntPtr handle, int width, int height)
        {
            if (handle == IntPtr.Zero)
                throw new ArgumentNullException("handle");
            this.handle = handle;
            Width = width;
            Height = height;
        }

        internal IntPtr Handle
        {
            get
            {
                if (handle == IntPtr.Zero)
                    throw new ObjectDisposedException("D3D9Texture");
                return handle;
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
                return;
            ComReleaseDelegate release = ComVtable.GetMethod<ComReleaseDelegate>(handle, D3dVtableSlots.ComRelease);
            release(handle);
            handle = IntPtr.Zero;
        }
    }
}
