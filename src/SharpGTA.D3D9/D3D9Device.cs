using System;
using System.Runtime.InteropServices;
using SharpGTA.Mathematics;

namespace SharpGTA.D3D9
{
    internal sealed class D3D9Device : IDisposable
    {
        private IntPtr d3d;
        private IntPtr device;
        private IntPtr backBuffer;
        private int backBufferWidth;
        private int backBufferHeight;
        private bool sceneOpen;
        private bool verticalSync;
        private bool disposed;

        public IntPtr Handle { get { return device; } }
        public string Description { get; private set; }
        public bool IsLost { get; private set; }
        public int Width { get { return backBufferWidth; } }
        public int Height { get { return backBufferHeight; } }
        internal bool SceneOpen { get { return sceneOpen; } }
        public bool VerticalSync { get { return verticalSync; } set { verticalSync = value; } }

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
            BeginFrame(0xFF202040);
            try
            {
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
                GCHandle pin = GCHandle.Alloc(vertices, GCHandleType.Pinned);
                try
                {
                    DrawPrimitiveUpChecked(1, pin.AddrOfPinnedObject(), (uint)Marshal.SizeOf(typeof(D3dVertex)));
                }
                finally
                {
                    pin.Free();
                }
                EndFrame();
            }
            finally
            {
                if (sceneOpen)
                {
                    try { EndFrame(); }
                    catch { /* Preserve the original rendering failure. */ }
                }
            }
        }

        public void BeginFrame(uint color)
        {
            ThrowIfUnavailable();
            if (sceneOpen)
                throw new InvalidOperationException("A D3D scene is already open.");
            BeginSceneChecked();
            sceneOpen = true;
            try
            {
                ClearChecked(D3dConstants.ClearTarget | D3dConstants.ClearZBuffer, color, 1.0f, 0);
            }
            catch
            {
                try { EndFrame(); }
                catch { /* Preserve the original Clear failure. */ }
                throw;
            }
        }

        public void EndFrame()
        {
            ThrowIfUnavailable();
            if (!sceneOpen)
                throw new InvalidOperationException("No D3D scene is open.");
            try
            {
                EndSceneChecked();
            }
            finally
            {
                sceneOpen = false;
            }
        }

        public void DrawSprite(D3D9Texture texture, float x, float y, float right, float bottom,
            float u0, float v0, float u1, float v1, uint color)
        {
            if (texture == null)
                throw new ArgumentNullException("texture");
            DrawQuads(texture, CreateQuad(x, y, right, bottom, u0, v0, u1, v1, color));
        }

        public void DrawRectangle(float x, float y, float right, float bottom, uint color)
        {
            DrawQuads(null, CreateQuad(x, y, right, bottom, 0, 0, 1, 1, color));
        }

        public void DrawQuads(D3D9Texture texture, D3dTexturedVertex[] vertices)
        {
            ThrowIfUnavailable();
            if (!sceneOpen)
                throw new InvalidOperationException("DrawQuads requires an open frame.");
            if (vertices == null)
                throw new ArgumentNullException("vertices");
            if (vertices.Length == 0 || vertices.Length % 6 != 0)
                throw new ArgumentException("Quad batches must contain six vertices per quad.", "vertices");

            SetFvfChecked(D3dConstants.FvfTexturedVertex);
            SetTextureChecked(0, texture == null ? IntPtr.Zero : texture.Handle);
            SetRenderStateChecked(D3dConstants.StateLighting, 0);
            SetRenderStateChecked(D3dConstants.StateCullMode, D3dConstants.CullNone);
            SetRenderStateChecked(D3dConstants.StateZEnable, 0);
            SetRenderStateChecked(D3dConstants.StateZWriteEnable, 0);
            SetRenderStateChecked(D3dConstants.StateAlphaTestEnable, 0);
            SetRenderStateChecked(D3dConstants.StateAlphaBlendEnable, 1);
            SetRenderStateChecked(D3dConstants.StateSourceBlend, D3dConstants.BlendSourceAlpha);
            SetRenderStateChecked(D3dConstants.StateDestinationBlend, D3dConstants.BlendInverseSourceAlpha);
            SetRenderStateChecked(D3dConstants.StateAlphaFunc, D3dConstants.CompareAlways);
            SetRenderStateChecked(D3dConstants.StateAlphaRef, 0);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorOp,
                texture == null ? D3dConstants.TextureOpSelectArg1 : D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorArg1,
                texture == null ? D3dConstants.TextureArgumentDiffuse : D3dConstants.TextureArgumentTexture);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorArg2, D3dConstants.TextureArgumentDiffuse);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaOp,
                texture == null ? D3dConstants.TextureOpSelectArg1 : D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaArg1,
                texture == null ? D3dConstants.TextureArgumentDiffuse : D3dConstants.TextureArgumentTexture);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaArg2, D3dConstants.TextureArgumentDiffuse);
            SetTextureStageStateChecked(1, D3dConstants.TextureColorOp, D3dConstants.TextureOpDisable);
            SetSamplerStateChecked(0, D3dConstants.SamplerAddressU, D3dConstants.TextureAddressClamp);
            SetSamplerStateChecked(0, D3dConstants.SamplerAddressV, D3dConstants.TextureAddressClamp);
            SetSamplerStateChecked(0, D3dConstants.SamplerMagFilter, D3dConstants.TextureFilterLinear);
            SetSamplerStateChecked(0, D3dConstants.SamplerMinFilter, D3dConstants.TextureFilterLinear);
            SetSamplerStateChecked(0, D3dConstants.SamplerMipFilter, D3dConstants.TextureFilterNone);

            GCHandle pin = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            try
            {
                DrawPrimitiveUpChecked((uint)(vertices.Length / 3), pin.AddrOfPinnedObject(),
                    (uint)Marshal.SizeOf(typeof(D3dTexturedVertex)));
            }
            finally
            {
                pin.Free();
            }
        }

        internal void SetTransform(uint state, ref D3dMatrix matrix)
        {
            SetTransformChecked(state, ref matrix);
        }

        internal void SetDirectionalLight(D3dVector direction, uint ambient)
        {
            ThrowIfUnavailable();
            D3dLight light = new D3dLight();
            light.Type = D3dConstants.LightDirectional;
            light.Diffuse = new D3dColorValue(1.0f, 1.0f, 1.0f, 1.0f);
            light.Specular = new D3dColorValue(0.25f, 0.25f, 0.25f, 1.0f);
            light.Direction = direction;
            DeviceSetLightDelegate set = ComVtable.GetMethod<DeviceSetLightDelegate>(device, D3dVtableSlots.SetLight);
            Check(set(device, 0, ref light), "SetLight");
            DeviceLightEnableDelegate enable = ComVtable.GetMethod<DeviceLightEnableDelegate>(device, D3dVtableSlots.LightEnable);
            Check(enable(device, 0, true), "LightEnable");
            SetRenderStateChecked(D3dConstants.StateAmbient, ambient);
            SetRenderStateChecked(D3dConstants.StateColorVertex, 1);
            SetRenderStateChecked(D3dConstants.StateDiffuseMaterialSource, D3dConstants.MaterialSourceVertexColor);
            SetRenderStateChecked(D3dConstants.StateAmbientMaterialSource, D3dConstants.MaterialSourceVertexColor);
        }

        internal void SetDirectionalLight(D3dVector direction)
        {
            SetDirectionalLight(direction, 0xFFA0A0A0);
        }

        internal void DrawModelMesh(D3D9Texture texture, uint materialColor, D3dModelVertex[] vertices)
        {
            ThrowIfUnavailable();
            if (!sceneOpen)
                throw new InvalidOperationException("DrawModelMesh requires an open frame.");
            if (vertices == null)
                throw new ArgumentNullException("vertices");
            if (vertices.Length == 0 || vertices.Length % 3 != 0)
                throw new ArgumentException("Model batches must contain complete triangles.", "vertices");

            SetFvfChecked(D3dConstants.FvfModelVertex);
            SetTextureChecked(0, texture == null ? IntPtr.Zero : texture.Handle);
            SetRenderStateChecked(D3dConstants.StateLighting, 1);
            SetRenderStateChecked(D3dConstants.StateCullMode, D3dConstants.CullNone);
            SetRenderStateChecked(D3dConstants.StateZEnable, 1);
            SetRenderStateChecked(D3dConstants.StateZWriteEnable, 1);
            SetRenderStateChecked(D3dConstants.StateAlphaTestEnable, 0);
            SetRenderStateChecked(D3dConstants.StateAlphaBlendEnable, 1);
            SetRenderStateChecked(D3dConstants.StateSourceBlend, D3dConstants.BlendSourceAlpha);
            SetRenderStateChecked(D3dConstants.StateDestinationBlend, D3dConstants.BlendInverseSourceAlpha);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorOp,
                texture == null ? D3dConstants.TextureOpSelectArg1 : D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorArg1,
                texture == null ? D3dConstants.TextureArgumentDiffuse : D3dConstants.TextureArgumentTexture);
            SetTextureStageStateChecked(0, D3dConstants.TextureColorArg2, D3dConstants.TextureArgumentDiffuse);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaOp,
                texture == null ? D3dConstants.TextureOpSelectArg1 : D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaArg1,
                texture == null ? D3dConstants.TextureArgumentDiffuse : D3dConstants.TextureArgumentTexture);
            SetTextureStageStateChecked(0, D3dConstants.TextureAlphaArg2, D3dConstants.TextureArgumentDiffuse);
            SetTextureStageStateChecked(1, D3dConstants.TextureStageConstant, materialColor);
            SetTextureStageStateChecked(1, D3dConstants.TextureColorOp, D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(1, D3dConstants.TextureColorArg1, D3dConstants.TextureArgumentCurrent);
            SetTextureStageStateChecked(1, D3dConstants.TextureColorArg2, D3dConstants.TextureArgumentConstant);
            SetTextureStageStateChecked(1, D3dConstants.TextureAlphaOp, D3dConstants.TextureOpModulate);
            SetTextureStageStateChecked(1, D3dConstants.TextureAlphaArg1, D3dConstants.TextureArgumentCurrent);
            SetTextureStageStateChecked(1, D3dConstants.TextureAlphaArg2, D3dConstants.TextureArgumentConstant);
            SetTextureStageStateChecked(2, D3dConstants.TextureColorOp, D3dConstants.TextureOpDisable);
            SetSamplerStateChecked(0, D3dConstants.SamplerAddressU, D3dConstants.TextureAddressWrap);
            SetSamplerStateChecked(0, D3dConstants.SamplerAddressV, D3dConstants.TextureAddressWrap);
            SetSamplerStateChecked(0, D3dConstants.SamplerMagFilter, D3dConstants.TextureFilterLinear);
            SetSamplerStateChecked(0, D3dConstants.SamplerMinFilter, D3dConstants.TextureFilterLinear);
            SetSamplerStateChecked(0, D3dConstants.SamplerMipFilter, D3dConstants.TextureFilterNone);

            GCHandle pin = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            try
            {
                DrawPrimitiveUpChecked((uint)(vertices.Length / 3), pin.AddrOfPinnedObject(),
                    (uint)Marshal.SizeOf(typeof(D3dModelVertex)));
            }
            finally
            {
                pin.Free();
            }
        }

        private static D3dTexturedVertex[] CreateQuad(float x, float y, float right, float bottom,
            float u0, float v0, float u1, float v1, uint color)
        {
            return new[] {
                new D3dTexturedVertex(x, y, color, u0, v0),
                new D3dTexturedVertex(right, y, color, u1, v0),
                new D3dTexturedVertex(x, bottom, color, u0, v1),
                new D3dTexturedVertex(right, y, color, u1, v0),
                new D3dTexturedVertex(right, bottom, color, u1, v1),
                new D3dTexturedVertex(x, bottom, color, u0, v1)
            };
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
            int result = reset(device, ref parameters);
            if (result != D3dConstants.D3DOk)
                IsLost = true;
            Check(result, "Reset");
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

        public D3D9Texture CreateTextureRgba(int width, int height, byte[] rgba)
        {
            ThrowIfUnavailable();
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if (rgba == null)
                throw new ArgumentNullException("rgba");
            int byteLength = checked(width * height * 4);
            if (rgba.Length != byteLength)
                throw new ArgumentException("RGBA data length does not match the texture size.", "rgba");

            IntPtr texture = IntPtr.Zero;
            DeviceCreateTextureDelegate create = ComVtable.GetMethod<DeviceCreateTextureDelegate>(
                device, D3dVtableSlots.CreateTexture);
            Check(create(device, (uint)width, (uint)height, 1, 0, D3dConstants.FormatA8R8G8B8,
                D3dConstants.PoolManaged, out texture, IntPtr.Zero), "CreateTexture");
            try
            {
                if (texture == IntPtr.Zero)
                    throw new InvalidOperationException("CreateTexture returned a null texture.");
                byte[] bgra = new byte[byteLength];
                for (int i = 0; i < byteLength; i += 4)
                {
                    bgra[i] = rgba[i + 2];
                    bgra[i + 1] = rgba[i + 1];
                    bgra[i + 2] = rgba[i];
                    bgra[i + 3] = rgba[i + 3];
                }
                D3dLockedRect locked = new D3dLockedRect();
                TextureLockRectDelegate lockRect = ComVtable.GetMethod<TextureLockRectDelegate>(
                    texture, D3dVtableSlots.TextureLockRect);
                Check(lockRect(texture, 0, ref locked, IntPtr.Zero, 0), "Texture LockRect");
                try
                {
                    if (locked.Bits == IntPtr.Zero || locked.Pitch < width * 4)
                        throw new InvalidOperationException("Invalid texture lock result: pitch=" + locked.Pitch);
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr row = new IntPtr(locked.Bits.ToInt64() + (long)y * locked.Pitch);
                        Marshal.Copy(bgra, y * width * 4, row, width * 4);
                    }
                }
                finally
                {
                    TextureUnlockRectDelegate unlock = ComVtable.GetMethod<TextureUnlockRectDelegate>(
                        texture, D3dVtableSlots.TextureUnlockRect);
                    Check(unlock(texture, 0), "Texture UnlockRect");
                }
                D3D9Texture result = new D3D9Texture(texture, width, height);
                texture = IntPtr.Zero;
                return result;
            }
            finally
            {
                if (texture != IntPtr.Zero)
                    Release(ref texture);
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

        private D3dPresentParameters CreatePresentParameters(IntPtr window, int width, int height)
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
                EnableAutoDepthStencil = true,
                AutoDepthStencilFormat = D3dConstants.FormatD24S8,
                Flags = D3dConstants.PresentFlagLockableBackBuffer,
                FullScreenRefreshRateInHz = 0,
                PresentationInterval = verticalSync ? 1u : 0u
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

        private void SetTextureChecked(uint stage, IntPtr texture)
        {
            DeviceSetTextureDelegate set = ComVtable.GetMethod<DeviceSetTextureDelegate>(device, D3dVtableSlots.SetTexture);
            Check(set(device, stage, texture), "SetTexture");
        }

        private void SetTextureStageStateChecked(uint stage, uint type, uint value)
        {
            DeviceSetTextureStageStateDelegate set = ComVtable.GetMethod<DeviceSetTextureStageStateDelegate>(
                device, D3dVtableSlots.SetTextureStageState);
            Check(set(device, stage, type, value), "SetTextureStageState");
        }

        private void SetSamplerStateChecked(uint sampler, uint type, uint value)
        {
            DeviceSetSamplerStateDelegate set = ComVtable.GetMethod<DeviceSetSamplerStateDelegate>(
                device, D3dVtableSlots.SetSamplerState);
            Check(set(device, sampler, type, value), "SetSamplerState");
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
