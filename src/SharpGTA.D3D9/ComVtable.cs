using System;
using System.Runtime.InteropServices;

namespace SharpGTA.D3D9
{
    /// <summary>Zero-based slots from d3d9.h, including the three IUnknown methods.</summary>
    /// <summary>Zero-based slots from d3d9.h, including the three IUnknown methods.</summary>
    internal static class D3dVtableSlots
    {
        internal const int ComRelease = 2;
        internal const int CreateDevice = 16;
        internal const int Reset = 16;
        internal const int Present = 17;
        internal const int GetBackBuffer = 18;
        internal const int TestCooperativeLevel = 3;
        internal const int BeginScene = 41;
        internal const int EndScene = 42;
        internal const int Clear = 43;
        internal const int SetTransform = 44;
        internal const int SetRenderState = 57;
        internal const int DrawPrimitiveUp = 83;
        internal const int SetFvf = 89;
        internal const int SurfaceLockRect = 13;
        internal const int SurfaceUnlockRect = 14;
    }

    internal static class ComVtable
    {
        internal static TDelegate GetMethod<TDelegate>(IntPtr instance, int slot)
        {
            if (instance == IntPtr.Zero)
                throw new ArgumentNullException("instance");
            if (slot < 0)
                throw new ArgumentOutOfRangeException("slot");
            IntPtr table = Marshal.ReadIntPtr(instance);
            IntPtr method = Marshal.ReadIntPtr(table, checked(slot * IntPtr.Size));
            return (TDelegate)(object)Marshal.GetDelegateForFunctionPointer(method, typeof(TDelegate));
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int CreateDeviceNativeDelegate(IntPtr d3d, uint adapter, uint type,
        IntPtr focusWindow, uint flags, ref D3dPresentParameters parameters, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceResetDelegate(IntPtr device, ref D3dPresentParameters parameters);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceTestCooperativeLevelDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceBeginSceneDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceEndSceneDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DevicePresentDelegate(IntPtr device, IntPtr sourceRect, IntPtr destinationRect,
        IntPtr destinationWindow, IntPtr dirtyRegion);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceClearDelegate(IntPtr device, uint rectangleCount, IntPtr rectangles,
        uint flags, uint color, float z, uint stencil);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceSetTransformDelegate(IntPtr device, uint state, ref D3dMatrix matrix);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceSetRenderStateDelegate(IntPtr device, uint state, uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceSetFvfDelegate(IntPtr device, uint fvf);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceDrawPrimitiveUpDelegate(IntPtr device, uint primitiveType,
        uint primitiveCount, IntPtr data, uint stride);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int DeviceGetBackBufferDelegate(IntPtr device, uint swapChain,
        uint backBuffer, uint type, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SurfaceLockRectDelegate(IntPtr surface, ref D3dLockedRect lockedRect,
        IntPtr rectangle, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SurfaceUnlockRectDelegate(IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate uint ComReleaseDelegate(IntPtr instance);
}
