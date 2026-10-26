using System;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpGTA.D3D9;

namespace SharpGTA.Tests
{
    public sealed class D3D9LayoutTests
    {
        [Test]
        public void NativeStructuresMatchX86AndX64Layouts()
        {
            bool x86 = IntPtr.Size == 4;
            Assert.True(x86 || IntPtr.Size == 8, "Unexpected pointer size.");

            Assert.Equal(64, Marshal.SizeOf(typeof(D3dMatrix)));
            Assert.Equal(0, Offset<D3dMatrix>("M11"));
            Assert.Equal(60, Offset<D3dMatrix>("M44"));
            Assert.Equal(20, Marshal.SizeOf(typeof(D3dVertex)));
            Assert.Equal(12, Offset<D3dVertex>("Rhw"));
            Assert.Equal(16, Offset<D3dVertex>("Color"));
            Assert.Equal(28, Marshal.SizeOf(typeof(D3dTexturedVertex)));
            Assert.Equal(16, Offset<D3dTexturedVertex>("Color"));
            Assert.Equal(20, Offset<D3dTexturedVertex>("U"));
            Assert.Equal(24, Offset<D3dTexturedVertex>("V"));

            Assert.Equal(x86 ? 56 : 64, Marshal.SizeOf(typeof(D3dPresentParameters)));
            Assert.Equal(x86 ? 28 : 32, Offset<D3dPresentParameters>("DeviceWindow"));
            Assert.Equal(x86 ? 32 : 40, Offset<D3dPresentParameters>("Windowed"));
            Assert.Equal(x86 ? 36 : 44, Offset<D3dPresentParameters>("EnableAutoDepthStencil"));
            Assert.Equal(x86 ? 40 : 48, Offset<D3dPresentParameters>("AutoDepthStencilFormat"));
            Assert.Equal(x86 ? 44 : 52, Offset<D3dPresentParameters>("Flags"));
            Assert.Equal(x86 ? 52 : 60, Offset<D3dPresentParameters>("PresentationInterval"));

            Assert.Equal(x86 ? 8 : 16, Marshal.SizeOf(typeof(D3dLockedRect)));
            Assert.Equal(0, Offset<D3dLockedRect>("Pitch"));
            Assert.Equal(x86 ? 4 : 8, Offset<D3dLockedRect>("Bits"));
            Assert.Equal(x86 ? 48 : 80, Marshal.SizeOf(typeof(NativeMethods.WndClassEx)));
            Assert.Equal(8, Offset<NativeMethods.WndClassEx>("WndProc"));
            Assert.Equal(x86 ? 40 : 64, Offset<NativeMethods.WndClassEx>("ClassName"));
            Assert.Equal(x86 ? 44 : 72, Offset<NativeMethods.WndClassEx>("SmallIcon"));
            Assert.Equal(x86 ? 28 : 48, Marshal.SizeOf(typeof(NativeMethods.Message)));
            Assert.Equal(x86 ? 4 : 8, Offset<NativeMethods.Message>("MessageId"));
            Assert.Equal(x86 ? 8 : 16, Offset<NativeMethods.Message>("WParam"));
            Assert.Equal(x86 ? 12 : 24, Offset<NativeMethods.Message>("LParam"));
            Assert.Equal(16, Marshal.SizeOf(typeof(NativeMethods.Rect)));
            Assert.Equal(0, Offset<NativeMethods.Rect>("Left"));
            Assert.Equal(4, Offset<NativeMethods.Rect>("Top"));
            Assert.Equal(8, Offset<NativeMethods.Rect>("Right"));
            Assert.Equal(12, Offset<NativeMethods.Rect>("Bottom"));
        }

        [Test]
        public void PresentParametersUseFourByteWin32Booleans()
        {
            AssertBoolMarshalling("Windowed");
            AssertBoolMarshalling("EnableAutoDepthStencil");
        }

        private static void AssertBoolMarshalling(string fieldName)
        {
            FieldInfo field = typeof(D3dPresentParameters).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            object[] attributes = field.GetCustomAttributes(typeof(MarshalAsAttribute), false);
            Assert.Equal(1, attributes.Length);
            Assert.Equal(UnmanagedType.Bool, ((MarshalAsAttribute)attributes[0]).Value);
        }

        private static int Offset<T>(string fieldName)
        {
            return Marshal.OffsetOf(typeof(T), fieldName).ToInt32();
        }
    }
}
