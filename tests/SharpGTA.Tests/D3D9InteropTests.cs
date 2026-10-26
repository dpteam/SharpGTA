using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpGTA.D3D9;

namespace SharpGTA.Tests
{
    public sealed class D3D9InteropTests
    {
        [Test]
        public void VtableSlotsAndConstantsMatchD3d9Headers()
        {
            Assert.Equal(2, D3dVtableSlots.ComRelease);
            Assert.Equal(3, D3dVtableSlots.TestCooperativeLevel);
            Assert.Equal(13, D3dVtableSlots.SurfaceLockRect);
            Assert.Equal(14, D3dVtableSlots.SurfaceUnlockRect);
            Assert.Equal(16, D3dVtableSlots.CreateDevice);
            Assert.Equal(16, D3dVtableSlots.Reset);
            Assert.Equal(17, D3dVtableSlots.Present);
            Assert.Equal(18, D3dVtableSlots.GetBackBuffer);
            Assert.Equal(19, D3dVtableSlots.TextureLockRect);
            Assert.Equal(20, D3dVtableSlots.TextureUnlockRect);
            Assert.Equal(23, D3dVtableSlots.CreateTexture);
            Assert.Equal(41, D3dVtableSlots.BeginScene);
            Assert.Equal(42, D3dVtableSlots.EndScene);
            Assert.Equal(43, D3dVtableSlots.Clear);
            Assert.Equal(44, D3dVtableSlots.SetTransform);
            Assert.Equal(57, D3dVtableSlots.SetRenderState);
            Assert.Equal(65, D3dVtableSlots.SetTexture);
            Assert.Equal(67, D3dVtableSlots.SetTextureStageState);
            Assert.Equal(69, D3dVtableSlots.SetSamplerState);
            Assert.Equal(83, D3dVtableSlots.DrawPrimitiveUp);
            Assert.Equal(89, D3dVtableSlots.SetFvf);

            Assert.Equal(32u, D3dConstants.SdkVersion);
            Assert.Equal(1u, D3dConstants.DeviceTypeHal);
            Assert.Equal(2u, D3dConstants.DeviceTypeRef);
            Assert.Equal(0x20u, D3dConstants.BehaviorSoftwareVertexProcessing);
            Assert.Equal(21u, D3dConstants.FormatA8R8G8B8);
            Assert.Equal(1u, D3dConstants.SwapEffectDiscard);
            Assert.Equal(1u, D3dConstants.PresentFlagLockableBackBuffer);
            Assert.Equal(0u, D3dConstants.BackBufferTypeMono);
            Assert.Equal(1u, D3dConstants.CullNone);
            Assert.Equal(4u, D3dConstants.PrimitiveTriangleList);
            Assert.Equal(0x004u, D3dConstants.FvfXyzRhw);
            Assert.Equal(0x040u, D3dConstants.FvfDiffuse);
            Assert.Equal(0x044u, D3dConstants.FvfVertex);
            Assert.Equal(0x100u, D3dConstants.FvfTex1);
            Assert.Equal(0x144u, D3dConstants.FvfTexturedVertex);
            Assert.Equal(1u, D3dConstants.PoolManaged);
            Assert.Equal(1u, D3dConstants.ClearTarget);
            Assert.Equal(3u, D3dConstants.StateProjection);
            Assert.Equal(2u, D3dConstants.StateView);
            Assert.Equal(256u, D3dConstants.StateWorld);
            Assert.Equal(137u, D3dConstants.StateLighting);
            Assert.Equal(22u, D3dConstants.StateCullMode);
            Assert.Equal(7u, D3dConstants.StateZEnable);
            Assert.Equal(14u, D3dConstants.StateZWriteEnable);
            Assert.Equal(19u, D3dConstants.StateSourceBlend);
            Assert.Equal(20u, D3dConstants.StateDestinationBlend);
            Assert.Equal(27u, D3dConstants.StateAlphaBlendEnable);
            Assert.Equal(5u, D3dConstants.BlendSourceAlpha);
            Assert.Equal(6u, D3dConstants.BlendInverseSourceAlpha);
            Assert.Equal(1u, D3dConstants.TextureColorOp);
            Assert.Equal(4u, D3dConstants.TextureAlphaOp);
            Assert.Equal(4u, D3dConstants.TextureOpModulate);
            Assert.Equal(2u, D3dConstants.TextureOpSelectArg1);
            Assert.Equal(0u, D3dConstants.TextureArgumentDiffuse);
            Assert.Equal(2u, D3dConstants.TextureArgumentTexture);
            Assert.Equal(3u, D3dConstants.TextureAddressClamp);
            Assert.Equal(2u, D3dConstants.TextureFilterLinear);
            Assert.Equal(0, D3dConstants.D3DOk);
            Assert.Equal(unchecked((int)0x88760868), D3dConstants.D3DErrorDeviceLost);
            Assert.Equal(unchecked((int)0x88760869), D3dConstants.D3DErrorDeviceNotReset);
        }

        [Test]
        public void DelegatesAndPInvokeDeclarationsHaveTheExpectedAbi()
        {
            Type[] delegates = {
                typeof(CreateDeviceNativeDelegate), typeof(DeviceResetDelegate),
                typeof(DeviceTestCooperativeLevelDelegate), typeof(DeviceBeginSceneDelegate),
                typeof(DeviceEndSceneDelegate), typeof(DevicePresentDelegate),
                typeof(DeviceClearDelegate), typeof(DeviceSetTransformDelegate),
                typeof(DeviceSetRenderStateDelegate), typeof(DeviceSetFvfDelegate),
                typeof(DeviceDrawPrimitiveUpDelegate), typeof(DeviceGetBackBufferDelegate),
                typeof(DeviceCreateTextureDelegate), typeof(DeviceSetTextureDelegate),
                typeof(DeviceSetTextureStageStateDelegate), typeof(DeviceSetSamplerStateDelegate),
                typeof(SurfaceLockRectDelegate), typeof(SurfaceUnlockRectDelegate),
                typeof(TextureLockRectDelegate), typeof(TextureUnlockRectDelegate),
                typeof(ComReleaseDelegate), typeof(NativeMethods.WndProc)
            };
            foreach (Type type in delegates)
            {
                object[] attributes = type.GetCustomAttributes(typeof(UnmanagedFunctionPointerAttribute), false);
                Assert.Equal(1, attributes.Length, type.FullName);
                Assert.Equal(CallingConvention.StdCall,
                    ((UnmanagedFunctionPointerAttribute)attributes[0]).CallingConvention, type.FullName);
            }

            Dictionary<string, string> expected = ExpectedImports();
            MethodInfo[] methods = typeof(NativeMethods).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            int importCount = 0;
            foreach (MethodInfo method in methods)
            {
                object[] attributes = method.GetCustomAttributes(typeof(DllImportAttribute), false);
                if (attributes.Length == 0)
                    continue;
                importCount++;
                DllImportAttribute import = (DllImportAttribute)attributes[0];
                string expectedImport;
                Assert.True(expected.TryGetValue(method.Name, out expectedImport), "Unexpected P/Invoke: " + method.Name);
                Assert.Equal(expectedImport, import.Value + "!" + import.EntryPoint, method.Name);
                Assert.True(import.ExactSpelling, method.Name + " must use ExactSpelling.");
                if (import.EntryPoint.EndsWith("W", StringComparison.Ordinal))
                    Assert.Equal(CharSet.Unicode, import.CharSet, method.Name);
                bool readsLastError = method.Name == "RegisterClassEx" || method.Name == "CreateWindowEx" ||
                    method.Name == "AdjustWindowRect" || method.Name == "SetWindowPos" || method.Name == "GetModuleHandle";
                Assert.Equal(readsLastError, import.SetLastError, method.Name + " SetLastError");
                expected.Remove(method.Name);
            }
            Assert.Equal(18, importCount);
            Assert.Equal(0, expected.Count, "Missing P/Invoke declarations.");
        }

        [Test]
        public void VtableDispatchUsesTheRequestedSlotAndPreservesHresult()
        {
            IntPtr table = Marshal.AllocHGlobal(checked((D3dVtableSlots.BeginScene + 1) * IntPtr.Size));
            IntPtr instance = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr observed = IntPtr.Zero;
            DeviceBeginSceneDelegate callback = delegate(IntPtr self)
            {
                observed = self;
                return D3dConstants.D3DErrorDeviceLost;
            };
            try
            {
                for (int i = 0; i <= D3dVtableSlots.BeginScene; i++)
                    Marshal.WriteIntPtr(table, i * IntPtr.Size, IntPtr.Zero);
                Marshal.WriteIntPtr(table, D3dVtableSlots.BeginScene * IntPtr.Size,
                    Marshal.GetFunctionPointerForDelegate(callback));
                Marshal.WriteIntPtr(instance, table);
                DeviceBeginSceneDelegate method = ComVtable.GetMethod<DeviceBeginSceneDelegate>(
                    instance, D3dVtableSlots.BeginScene);
                Assert.Equal(D3dConstants.D3DErrorDeviceLost, method(instance));
                Assert.Equal(instance, observed);
                GC.KeepAlive(callback);
            }
            finally
            {
                Marshal.FreeHGlobal(instance);
                Marshal.FreeHGlobal(table);
            }
        }

        [Test]
        public void InvalidVtableAndDeviceCallsFailBeforeNativeAccess()
        {
            Assert.Throws<ArgumentNullException>(delegate
            {
                ComVtable.GetMethod<ComReleaseDelegate>(IntPtr.Zero, D3dVtableSlots.ComRelease);
            });
            Assert.Throws<ArgumentOutOfRangeException>(delegate
            {
                ComVtable.GetMethod<ComReleaseDelegate>(new IntPtr(1), -1);
            });

            D3D9Device device = new D3D9Device();
            Assert.Throws<InvalidOperationException>(delegate { device.DrawTestTriangle(); });
            Assert.Throws<InvalidOperationException>(delegate { device.Present(); });
            device.Dispose();
            device.Dispose();
            Assert.Throws<ObjectDisposedException>(delegate { device.Present(); });
            Assert.Throws<ObjectDisposedException>(delegate { device.Create(new IntPtr(1), 640, 480); });
        }

        private static Dictionary<string, string> ExpectedImports()
        {
            Dictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal);
            expected.Add("RegisterClassEx", "user32.dll!RegisterClassExW");
            expected.Add("UnregisterClass", "user32.dll!UnregisterClassW");
            expected.Add("CreateWindowEx", "user32.dll!CreateWindowExW");
            expected.Add("DefWindowProc", "user32.dll!DefWindowProcW");
            expected.Add("DestroyWindow", "user32.dll!DestroyWindow");
            expected.Add("ShowWindow", "user32.dll!ShowWindow");
            expected.Add("UpdateWindow", "user32.dll!UpdateWindow");
            expected.Add("PeekMessage", "user32.dll!PeekMessageW");
            expected.Add("TranslateMessage", "user32.dll!TranslateMessage");
            expected.Add("DispatchMessage", "user32.dll!DispatchMessageW");
            expected.Add("PostQuitMessage", "user32.dll!PostQuitMessage");
            expected.Add("GetClientRect", "user32.dll!GetClientRect");
            expected.Add("AdjustWindowRect", "user32.dll!AdjustWindowRect");
            expected.Add("SetWindowPos", "user32.dll!SetWindowPos");
            expected.Add("ShowCursor", "user32.dll!ShowCursor");
            expected.Add("LoadCursor", "user32.dll!LoadCursorW");
            expected.Add("GetModuleHandle", "kernel32.dll!GetModuleHandleW");
            expected.Add("Direct3DCreate9", "d3d9.dll!Direct3DCreate9");
            return expected;
        }
    }
}
