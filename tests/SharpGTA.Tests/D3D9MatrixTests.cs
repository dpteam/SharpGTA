using SharpGTA.D3D9;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class D3D9MatrixTests
    {
        [Test]
        public void GtaRowsBecomeDirect3DWorldMatrixAndRgbaBecomesArgb()
        {
            Matrix source = new Matrix(
                new Vector3(1, 2, 3), new Vector3(4, 5, 6),
                new Vector3(7, 8, 9), new Vector3(10, 11, 12));
            D3dMatrix converted = D3dMatrix.FromGtaMatrix(source);
            Assert.Near(1, converted.M11);
            Assert.Near(2, converted.M12);
            Assert.Near(3, converted.M13);
            Assert.Near(4, converted.M21);
            Assert.Near(5, converted.M22);
            Assert.Near(6, converted.M23);
            Assert.Near(7, converted.M31);
            Assert.Near(8, converted.M32);
            Assert.Near(9, converted.M33);
            Assert.Near(10, converted.M41);
            Assert.Near(11, converted.M42);
            Assert.Near(12, converted.M43);
            Assert.Near(1, converted.M44);
            Assert.Equal(0x33FF1122u, D3D9ModelRenderer.ToD3dColor(0x332211FFu));
        }

        [Test]
        public void LookAtAndPerspectiveProducePositiveDepthAndCenteredProjection()
        {
            D3dMatrix view = D3dMatrix.LookAt(new Vector3(0, -5, 0), Vector3.Zero, Vector3.UnitZ);
            float x, y, z, w;
            view.TransformCoordinate(0, 0, 0, out x, out y, out z, out w);
            Assert.Near(0, x);
            Assert.Near(0, y);
            Assert.Near(5, z);
            Assert.Near(1, w);

            D3dMatrix projection = D3dMatrix.PerspectiveFov(
                (float)(System.Math.PI / 2.0), 4.0f / 3.0f, 1.0f, 100.0f);
            projection.TransformCoordinate(x, y, z, out x, out y, out z, out w);
            Assert.Near(0, x);
            Assert.Near(0, y);
            Assert.Near(5, w);
            Assert.Near(80.0f / 99.0f, z / w);
        }
    }
}
