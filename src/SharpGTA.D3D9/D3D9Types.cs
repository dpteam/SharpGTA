using System;
using System.Runtime.InteropServices;
using SharpGTA.Mathematics;

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

        internal static D3dMatrix FromGtaMatrix(Matrix value)
        {
            D3dMatrix result = new D3dMatrix();
            result.M11 = value.Right.X;
            result.M12 = value.Right.Y;
            result.M13 = value.Right.Z;
            result.M21 = value.Forward.X;
            result.M22 = value.Forward.Y;
            result.M23 = value.Forward.Z;
            result.M31 = value.Up.X;
            result.M32 = value.Up.Y;
            result.M33 = value.Up.Z;
            result.M41 = value.Position.X;
            result.M42 = value.Position.Y;
            result.M43 = value.Position.Z;
            result.M44 = 1.0f;
            return result;
        }

        internal static D3dMatrix LookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            Vector3 zAxis = (target - eye).Normalize();
            Vector3 xAxis = Vector3.Cross(up, zAxis).Normalize();
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis);
            D3dMatrix result = new D3dMatrix();
            result.M11 = xAxis.X;
            result.M12 = yAxis.X;
            result.M13 = zAxis.X;
            result.M21 = xAxis.Y;
            result.M22 = yAxis.Y;
            result.M23 = zAxis.Y;
            result.M31 = xAxis.Z;
            result.M32 = yAxis.Z;
            result.M33 = zAxis.Z;
            result.M41 = -Vector3.Dot(xAxis, eye);
            result.M42 = -Vector3.Dot(yAxis, eye);
            result.M43 = -Vector3.Dot(zAxis, eye);
            result.M44 = 1.0f;
            return result;
        }

        internal static D3dMatrix PerspectiveFov(float fovY, float aspect, float nearPlane, float farPlane)
        {
            if (fovY <= 0.0f || fovY >= (float)Math.PI)
                throw new ArgumentOutOfRangeException("fovY");
            if (aspect <= 0.0f || float.IsNaN(aspect) || float.IsInfinity(aspect))
                throw new ArgumentOutOfRangeException("aspect");
            if (nearPlane <= 0.0f || farPlane <= nearPlane)
                throw new ArgumentOutOfRangeException(farPlane <= nearPlane ? "farPlane" : "nearPlane");
            float yScale = 1.0f / (float)Math.Tan(fovY * 0.5f);
            D3dMatrix result = new D3dMatrix();
            result.M11 = yScale / aspect;
            result.M22 = yScale;
            result.M33 = farPlane / (farPlane - nearPlane);
            result.M34 = 1.0f;
            result.M43 = -nearPlane * farPlane / (farPlane - nearPlane);
            return result;
        }

        internal void TransformCoordinate(float x, float y, float z,
            out float clipX, out float clipY, out float clipZ, out float clipW)
        {
            clipX = M11 * x + M21 * y + M31 * z + M41;
            clipY = M12 * x + M22 * y + M32 * z + M42;
            clipZ = M13 * x + M23 * y + M33 * z + M43;
            clipW = M14 * x + M24 * y + M34 * z + M44;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dTexturedVertex
    {
        internal float X;
        internal float Y;
        internal float Z;
        internal float Rhw;
        internal uint Color;
        internal float U;
        internal float V;

        internal D3dTexturedVertex(float x, float y, uint color, float u, float v)
        {
            X = x;
            Y = y;
            Z = 0.0f;
            Rhw = 1.0f;
            Color = color;
            U = u;
            V = v;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dModelVertex
    {
        internal float X;
        internal float Y;
        internal float Z;
        internal float NormalX;
        internal float NormalY;
        internal float NormalZ;
        internal uint Color;
        internal float U;
        internal float V;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dColorValue
    {
        internal float Red;
        internal float Green;
        internal float Blue;
        internal float Alpha;

        internal D3dColorValue(float red, float green, float blue, float alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dLight
    {
        internal uint Type;
        internal D3dColorValue Diffuse;
        internal D3dColorValue Specular;
        internal D3dColorValue Ambient;
        internal D3dVector Position;
        internal D3dVector Direction;
        internal float Range;
        internal float Falloff;
        internal float Attenuation0;
        internal float Attenuation1;
        internal float Attenuation2;
        internal float Theta;
        internal float Phi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3dVector
    {
        internal float X;
        internal float Y;
        internal float Z;

        internal D3dVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    internal static class D3dConstants
    {
        internal const uint SdkVersion = 32;
        internal const uint DeviceTypeHal = 1;
        internal const uint DeviceTypeRef = 2;
        internal const uint BehaviorSoftwareVertexProcessing = 0x20;
        internal const uint PoolManaged = 1;
        internal const uint FormatA8R8G8B8 = 21;
        internal const uint FormatD24S8 = 75;
        internal const uint FormatIndex16 = 101;
        internal const uint SwapEffectDiscard = 1;
        internal const uint PresentFlagLockableBackBuffer = 1;
        internal const uint BackBufferTypeMono = 0;
        internal const uint CullNone = 1;
        internal const uint PrimitiveTriangleList = 4;
        internal const uint FvfXyz = 0x002;
        internal const uint FvfXyzRhw = 0x004;
        internal const uint FvfNormal = 0x010;
        internal const uint FvfDiffuse = 0x040;
        internal const uint FvfVertex = FvfXyzRhw | FvfDiffuse;
        internal const uint FvfTex1 = 0x100;
        internal const uint FvfTexturedVertex = FvfVertex | FvfTex1;
        internal const uint FvfModelVertex = FvfXyz | FvfNormal | FvfDiffuse | FvfTex1;
        internal const uint ClearTarget = 1;
        internal const uint ClearZBuffer = 2;
        internal const uint StateProjection = 3;
        internal const uint StateView = 2;
        internal const uint StateWorld = 256;
        internal const uint StateLighting = 137;
        internal const uint StateCullMode = 22;
        internal const uint StateZEnable = 7;
        internal const uint StateZWriteEnable = 14;
        internal const uint StateAlphaTestEnable = 15;
        internal const uint StateSourceBlend = 19;
        internal const uint StateDestinationBlend = 20;
        internal const uint StateAlphaRef = 24;
        internal const uint StateAlphaFunc = 25;
        internal const uint StateAlphaBlendEnable = 27;
        internal const uint StateAmbient = 139;
        internal const uint StateColorVertex = 141;
        internal const uint StateDiffuseMaterialSource = 145;
        internal const uint StateAmbientMaterialSource = 147;
        internal const uint MaterialSourceVertexColor = 1;
        internal const uint LightDirectional = 3;
        internal const uint BlendSourceAlpha = 5;
        internal const uint BlendInverseSourceAlpha = 6;
        internal const uint CompareAlways = 8;
        internal const uint CompareGreater = 7;
        internal const uint TextureColorOp = 1;
        internal const uint TextureColorArg1 = 2;
        internal const uint TextureColorArg2 = 3;
        internal const uint TextureAlphaOp = 4;
        internal const uint TextureAlphaArg1 = 5;
        internal const uint TextureAlphaArg2 = 6;
        internal const uint TextureOpDisable = 1;
        internal const uint TextureOpSelectArg1 = 2;
        internal const uint TextureOpModulate = 4;
        internal const uint TextureArgumentCurrent = 1;
        internal const uint TextureArgumentTexture = 2;
        internal const uint TextureArgumentConstant = 6;
        internal const uint TextureArgumentDiffuse = 0;
        internal const uint TextureStageConstant = 32;
        internal const uint SamplerAddressU = 1;
        internal const uint SamplerAddressV = 2;
        internal const uint SamplerMagFilter = 5;
        internal const uint SamplerMinFilter = 6;
        internal const uint SamplerMipFilter = 7;
        internal const uint TextureAddressWrap = 1;
        internal const uint TextureAddressClamp = 3;
        internal const uint TextureFilterPoint = 1;
        internal const uint TextureFilterLinear = 2;
        internal const uint TextureFilterNone = 0;
        internal const int D3DOk = 0;
        internal const int D3DErrorDeviceLost = unchecked((int)0x88760868);
        internal const int D3DErrorDeviceNotReset = unchecked((int)0x88760869);
    }
}
