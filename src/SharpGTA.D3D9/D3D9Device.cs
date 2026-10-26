using System;
using System.Runtime.InteropServices;

namespace SharpGTA.D3D9
{
    internal sealed class D3D9Device : IDisposable
    {
        private IntPtr d3d;
        private IntPtr device;
        private IntPtr backBuffer;
        private int backBufferWidth;
        private int backBufferHeight;
        private bool disposed;

        public IntPtr Handle { get { return device; } }
        public string Description { get; private set; }
        public bool IsLost { get; private set; }

        public void Create(IntPtr window, int width, int height)
        {
            if (disposed)
                throw new ObjectDisposedException("D3D9Device");
            if (window == IntPtr.Zero)
                throw new ArgumentNullException("window");
            if (device != IntPtr.Zero || d3d != IntPtr.Zero)
                throw new InvalidOperationException("D3D device is already created.");
            ValidateSize(width, height);
            d3d = NativeMethods.Direct3DCreate9(D3dConstants.SdkVersion);
            if (d3d == IntPtr.Zero)
                throw new InvalidOperationException("Direct3DCreate9 returned null (D3D9 is unavailable).");

            int halResult = CreateDeviceType(D3dConstants.DeviceTypeHal, window, width, height);
            if (halResult == D3dConstants.D3DOk)
            {
                Description = "HAL, software vertex processing";
                backBufferWidth = width;
                backBufferHeight = height;
                return;
            }
            if (device != IntPtr.Zero)
                Release(ref device);

            // REF is a real D3D device too, and is useful on machines without a
            // working hardware driver. Report it explicitly; never pretend it is HAL.
            int result = CreateDeviceType(D3dConstants.DeviceTypeRef, window, width, height);
            if (result != D3dConstants.D3DOk)
                throw new InvalidOperationException("IDirect3D9::CreateDevice failed: HAL=0x" +
                    halResult.ToString("X8") + ", REF=0x" + result.ToString("X8"));
            Description = "REF, software vertex processing";
            backBufferWidth = width;
            backBufferHeight = height;
        }

        public void DrawTestTriangle()
        {
            ThrowIfUnavailable();
            BeginSceneChecked();
            bool sceneOpen = true;
            try
            {
                ClearChecked(D3dConstants.ClearTarget, 0xFF202040, 1.0f, 0);
                D3dMatrix identity = D3dMatrix.Identity();
                SetTransformChecked(D3dConstants.StateWorld, ref identity);
                SetTransformChecked(D3dConstants.StateView, ref identity);
                SetTransformChecked(D3dConstants.StateProjection, ref identity);
                SetRenderStateChecked(D3dConstants.StateLighting, 0);
                SetRenderStateChecked(D3dConstants.StateCullMode, D3dConstants.CullNone);
                SetFvfChecked(D3dConstants.FvfVertex);

                D3dVertex[] vertices = {
                    // XYZRHW vertices are already in screen-space pixels, not clip space.
                    new D3dVertex { X = 320.0f, Y = 120.0f, Z = 0.0f, Rhw = 1.0f, Color = 0xFFFF0000 },
                    new D3dVertex { X = 480.0f, Y = 360.0f, Z = 0.0f, Rhw = 1.0f, Color = 0xFF00FF00 },
                    new D3dVertex { X = 160.0f, Y = 360.0f, Z = 0.0f, Rhw = 1.0f, Color = 0xFF0000FF }
                };
                int vertexSize = Marshal.SizeOf(typeof(D3dVertex));
                IntPtr data = Marshal.AllocHGlobal(checked(vertexSize * vertices.Length));
                try
                {
                    for (int i = 0; i < vertices.Length; i++)
                        Marshal.StructureToPtr(vertices[i], new IntPtr(data.ToInt64() + (long)i * vertexSize), false);
                    DrawPrimitiveUpChecked(1, data, (uint)vertexSize);
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
                EndSceneChecked();
                sceneOpen = false;
            }
            finally
            {
                if (sceneOpen)
                {
                    try { EndSceneChecked(); }
                    catch { /* Preserve the original rendering failure. */ }
                }
            }
        }

        public void Present()
        {
            ThrowIfUnavailable();
            DevicePresentDelegate present = ComVtable.GetMethod<DevicePresentDelegate>(device, D3dVtableSlots.Present);
            Check(present(device, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero), "Present");
        }

        public int TestCooperativeLevel()
        {
            ThrowIfUnavailable();
            DeviceTestCooperativeLevelDelegate test = ComVtable.GetMethod<DeviceTestCooperativeLevelDelegate>(
                device, D3dVtableSlots.TestCooperativeLevel);
            int result = test(device);
            if (result == D3dConstants.D3DErrorDeviceLost ||
                result == D3dConstants.D3DErrorDeviceNotReset)
                IsLost = true;
            return result;
        }

        public void Reset(IntPtr window, int width, int height)
        {
            ThrowIfUnavailable();
            if (window == IntPtr.Zero)
                throw new ArgumentNullException("window");
            ValidateSize(width, height);
            if (backBuffer != IntPtr.Zero)
                Release(ref backBuffer);
            backBufferWidth = 0;
            backBufferHeight = 0;
            D3dPresentParameters parameters = CreatePresentParameters(window, width, height);
            DeviceResetDelegate reset = ComVtable.GetMethod<DeviceResetDelegate>(device, D3dVtableSlots.Reset);
            Check(reset(device, ref parameters), "Reset");
            backBufferWidth = width;
            backBufferHeight = height;
            IsLost = false;
        }

        public byte[] ReadBackBufferBgra(int width, int height)
        {
            ThrowIfUnavailable();
            ValidateSize(width, height);
            if (width != backBufferWidth || height != backBufferHeight)
                throw new ArgumentException("Readback size must match the current back buffer.");
            if (backBuffer == IntPtr.Zero)
            {
                DeviceGetBackBufferDelegate get = ComVtable.GetMethod<DeviceGetBackBufferDelegate>(
                    device, D3dVtableSlots.GetBackBuffer);
                Check(get(device, 0, 0, D3dConstants.BackBufferTypeMono, out backBuffer), "GetBackBuffer");
            }

            D3dLockedRect locked = new D3dLockedRect();
            SurfaceLockRectDelegate lockRect = ComVtable.GetMethod<SurfaceLockRectDelegate>(
                backBuffer, D3dVtableSlots.SurfaceLockRect);
            Check(lockRect(backBuffer, ref locked, IntPtr.Zero, 0), "LockRect");
            try
            {
                if (locked.Bits == IntPtr.Zero || locked.Pitch < width * 4)
                    throw new InvalidOperationException("Invalid back-buffer lock result: pitch=" +
                        locked.Pitch + ", bits=0x" + locked.Bits.ToString("X"));
                byte[] pixels = new byte[checked(width * height * 4)];
                for (int y = 0; y < height; y++)
                {
                    IntPtr row = new IntPtr(locked.Bits.ToInt64() + (long)y * locked.Pitch);
                    Marshal.Copy(row, pixels, y * width * 4, width * 4);
                }
                return pixels;
            }
            finally
            {
                SurfaceUnlockRectDelegate unlock = ComVtable.GetMethod<SurfaceUnlockRectDelegate>(
                    backBuffer, D3dVtableSlots.SurfaceUnlockRect);
                Check(unlock(backBuffer), "UnlockRect");
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (backBuffer != IntPtr.Zero)
                Release(ref backBuffer);
            if (device != IntPtr.Zero)
                Release(ref device);
            if (d3d != IntPtr.Zero)
                Release(ref d3d);
        }

        private int CreateDeviceType(uint type, IntPtr window, int width, int height)
        {
            D3dPresentParameters parameters = CreatePresentParameters(window, width, height);
            CreateDeviceNativeDelegate create = ComVtable.GetMethod<CreateDeviceNativeDelegate>(
                d3d, D3dVtableSlots.CreateDevice);
            return create(d3d, 0, type, window, D3dConstants.BehaviorSoftwareVertexProcessing,
                ref parameters, out device);
        }

        private static D3dPresentParameters CreatePresentParameters(IntPtr window, int width, int height)
        {
            return new D3dPresentParameters
            {
                BackBufferWidth = (uint)width,
                BackBufferHeight = (uint)height,
                BackBufferFormat = D3dConstants.FormatA8R8G8B8,
                BackBufferCount = 1,
                MultiSampleType = 0,
                MultiSampleQuality = 0,
                SwapEffect = D3dConstants.SwapEffectDiscard,
                DeviceWindow = window,
                Windowed = true,
                EnableAutoDepthStencil = false,
                AutoDepthStencilFormat = 0,
                Flags = D3dConstants.PresentFlagLockableBackBuffer,
                FullScreenRefreshRateInHz = 0,
                PresentationInterval = 0
            };
        }

        private void BeginSceneChecked()
        {
            DeviceBeginSceneDelegate begin = ComVtable.GetMethod<DeviceBeginSceneDelegate>(
                device, D3dVtableSlots.BeginScene);
            Check(begin(device), "BeginScene");
        }

        private void EndSceneChecked()
        {
            DeviceEndSceneDelegate end = ComVtable.GetMethod<DeviceEndSceneDelegate>(
                device, D3dVtableSlots.EndScene);
            Check(end(device), "EndScene");
        }

        private void ClearChecked(uint flags, uint color, float z, uint stencil)
        {
            DeviceClearDelegate clear = ComVtable.GetMethod<DeviceClearDelegate>(device, D3dVtableSlots.Clear);
            Check(clear(device, 0, IntPtr.Zero, flags, color, z, stencil), "Clear");
        }

        private void SetTransformChecked(uint state, ref D3dMatrix matrix)
        {
            DeviceSetTransformDelegate set = ComVtable.GetMethod<DeviceSetTransformDelegate>(
                device, D3dVtableSlots.SetTransform);
            Check(set(device, state, ref matrix), "SetTransform");
        }

        private void SetRenderStateChecked(uint state, uint value)
        {
            DeviceSetRenderStateDelegate set = ComVtable.GetMethod<DeviceSetRenderStateDelegate>(
                device, D3dVtableSlots.SetRenderState);
            Check(set(device, state, value), "SetRenderState");
        }

        private void SetFvfChecked(uint fvf)
        {
            DeviceSetFvfDelegate set = ComVtable.GetMethod<DeviceSetFvfDelegate>(device, D3dVtableSlots.SetFvf);
            Check(set(device, fvf), "SetFVF");
        }

        private void DrawPrimitiveUpChecked(uint primitiveCount, IntPtr data, uint stride)
        {
            DeviceDrawPrimitiveUpDelegate draw = ComVtable.GetMethod<DeviceDrawPrimitiveUpDelegate>(
                device, D3dVtableSlots.DrawPrimitiveUp);
            Check(draw(device, D3dConstants.PrimitiveTriangleList, primitiveCount, data, stride), "DrawPrimitiveUP");
        }

        private static void Release(ref IntPtr instance)
        {
            if (instance == IntPtr.Zero)
                return;
            ComReleaseDelegate release = ComVtable.GetMethod<ComReleaseDelegate>(instance, D3dVtableSlots.ComRelease);
            release(instance);
            instance = IntPtr.Zero;
        }

        private void Check(int result, string operation)
        {
            if (result == D3dConstants.D3DErrorDeviceLost ||
                result == D3dConstants.D3DErrorDeviceNotReset)
                IsLost = true;
            if (result != D3dConstants.D3DOk)
                throw new InvalidOperationException(operation + " failed: 0x" + result.ToString("X8"));
        }

        private void ThrowIfUnavailable()
        {
            if (disposed)
                throw new ObjectDisposedException("D3D9Device");
            if (device == IntPtr.Zero)
                throw new InvalidOperationException("D3D device has not been created.");
        }

        private static void ValidateSize(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if ((long)width * height * 4 > 256 * 1024 * 1024)
                throw new ArgumentOutOfRangeException("height", "Back buffer exceeds the readback size limit.");
        }
    }
}
