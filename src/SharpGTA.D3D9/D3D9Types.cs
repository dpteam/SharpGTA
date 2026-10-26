using System;
using System.Runtime.InteropServices;

namespace SharpGTA.D3D9
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dMatrix
    {
        internal float M11, M12, M13, M14;
        internal float M21, M22, M23, M24;
        internal float M31, M32, M33, M34;
        internal float M41, M42, M43, M44;

        internal static D3dMatrix Identity()
        {
            D3dMatrix result = new D3dMatrix();
            result.M11 = 1.0f;
            result.M22 = 1.0f;
            result.M33 = 1.0f;
            result.M44 = 1.0f;
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dPresentParameters
    {
        internal uint BackBufferWidth;
        internal uint BackBufferHeight;
        internal uint BackBufferFormat;
        internal uint BackBufferCount;
        internal uint MultiSampleType;
        internal uint MultiSampleQuality;
        internal uint SwapEffect;
        internal IntPtr DeviceWindow;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool Windowed;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool EnableAutoDepthStencil;
        internal uint AutoDepthStencilFormat;
        internal uint Flags;
        internal uint FullScreenRefreshRateInHz;
        internal uint PresentationInterval;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dLockedRect
    {
        internal int Pitch;
        internal IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dVertex
    {
        internal float X;
        internal float Y;
        internal float Z;
        internal float Rhw;
        internal uint Color;
    }

    internal static class D3dConstants
    {
        internal const uint SdkVersion = 32;
        internal const uint DeviceTypeHal = 1;
        internal const uint DeviceTypeRef = 2;
        internal const uint BehaviorSoftwareVertexProcessing = 0x20;
        internal const uint FormatA8R8G8B8 = 21;
        internal const uint SwapEffectDiscard = 1;
        internal const uint PresentFlagLockableBackBuffer = 1;
        internal const uint BackBufferTypeMono = 0;
        internal const uint CullNone = 1;
        internal const uint PrimitiveTriangleList = 4;
        internal const uint FvfXyzRhw = 0x004;
        internal const uint FvfDiffuse = 0x040;
        internal const uint FvfVertex = FvfXyzRhw | FvfDiffuse;
        internal const uint ClearTarget = 1;
        internal const uint StateProjection = 3;
        internal const uint StateView = 2;
        internal const uint StateWorld = 256;
        internal const uint StateLighting = 137;
        internal const uint StateCullMode = 22;
        internal const int D3DOk = 0;
        internal const int D3DErrorDeviceLost = unchecked((int)0x88760868);
        internal const int D3DErrorDeviceNotReset = unchecked((int)0x88760869);
    }
}
