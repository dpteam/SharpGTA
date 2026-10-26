using System;
using System.Collections.Generic;
using System.IO;
using SharpGTA.Collision;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class CollisionTests
    {
        [Test]
        public void ReadsLegacyModelAndPreservesLines()
        {
            List<ColModel> models = ColFile.Read(CreateSyntheticFile());
            Assert.Equal(1, models.Count);
            ColModel model = models[0];
            Assert.Equal("legacy_model", model.ModelName);
            Assert.Near(20.0f, model.BoundingSphere.Radius);
            AssertVector(new Vector3(1.0f, 2.0f, 3.0f), model.BoundingSphere.Center);
            AssertVector(new Vector3(-10.0f, -10.0f, -10.0f), model.BoundingBox.Min);
            AssertVector(new Vector3(10.0f, 10.0f, 10.0f), model.BoundingBox.Max);

            Assert.Equal(1, model.Spheres.Length);
            Assert.Near(1.25f, model.Spheres[0].Radius);
            AssertVector(new Vector3(4.0f, 5.0f, 6.0f), model.Spheres[0].Center);
            Assert.Equal((byte)17, model.Spheres[0].Surface);
            Assert.Equal((byte)2, model.Spheres[0].Piece);

            Assert.Equal(1, model.Lines.Length);
            AssertVector(new Vector3(-1.0f, -2.0f, -3.0f), model.Lines[0].P0);
            AssertVector(new Vector3(7.0f, 8.0f, 9.0f), model.Lines[0].P1);

            Assert.Equal(1, model.Boxes.Length);
            AssertVector(new Vector3(-2.0f, -3.0f, -4.0f), model.Boxes[0].Min);
            AssertVector(new Vector3(2.0f, 3.0f, 4.0f), model.Boxes[0].Max);
            Assert.Equal((byte)23, model.Boxes[0].Surface);
            Assert.Equal((byte)4, model.Boxes[0].Piece);

            Assert.Equal(3, model.Vertices.Length);
            AssertVector(new Vector3(0.0f, 0.0f, 0.0f), model.Vertices[0]);
            AssertVector(new Vector3(2.0f, 0.0f, 0.0f), model.Vertices[1]);
            AssertVector(new Vector3(0.0f, 2.0f, 0.0f), model.Vertices[2]);
            Assert.Equal(1, model.Triangles.Length);
            Assert.Equal((ushort)0, model.Triangles[0].A);
            Assert.Equal((ushort)1, model.Triangles[0].B);
            Assert.Equal((ushort)2, model.Triangles[0].C);
            Assert.Equal((byte)42, model.Triangles[0].Surface);

            model.Level = 7;
            Assert.Equal((byte)7, model.Slot);
            model.Slot = 9;
            Assert.Equal((byte)9, model.Level);
        }

        [Test]
        public void ReadsMultipleModelsAndAllowsZeroPadding()
        {
            byte[] file = Concat(CreateSyntheticFile(), CreateTriangleFile(
                new Vector3[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 1.0f, 0.0f)
                },
                new ColTriangle[] { new ColTriangle(0, 1, 2, 42) }), new byte[2048]);
            List<ColModel> models = ColFile.Read(file);
            Assert.Equal(2, models.Count);
            Assert.Equal("legacy_model", models[0].ModelName);
            Assert.Equal("triangles", models[1].ModelName);
        }

        [Test]
        public void RayCastReturnsClosestTriangleAndVerticalHit()
        {
            ColModel model = CreateTwoPlaneModel();
            Ray ray = new Ray(new Vector3(0.25f, 0.25f, 10.0f),
                new Vector3(0.0f, 0.0f, -2.0f), 100.0f);
            ColHit hit;
            Assert.True(model.RayCast(ray, out hit));
            Assert.Equal(ColShapeType.Triangle, hit.ShapeType);
            Assert.Equal(1, hit.ShapeIndex);
            Assert.Equal((byte)77, hit.Surface);
            Assert.Equal((byte)0, hit.Piece);
            Assert.Near(7.0f, hit.Distance);
            AssertVector(new Vector3(0.25f, 0.25f, 3.0f), hit.Position);
            AssertVector(new Vector3(0.0f, 0.0f, -1.0f), hit.Normal);

            Assert.True(model.QueryVertical(0.25f, 0.25f, -5.0f, 10.0f, out hit));
            Assert.Equal(ColShapeType.Triangle, hit.ShapeType);
            Assert.Equal((byte)77, hit.Surface);
            Assert.Near(7.0f, hit.Distance);
            Assert.Near(3.0f, hit.Position.Z);
        }

        [Test]
        public void RayCastRejectsBehindParallelOutsideAndLimitedRanges()
        {
            ColModel model = CreateTwoPlaneModel();
            ColHit hit;
            Assert.False(model.RayCast(new Ray(new Vector3(0.25f, 0.25f, 10.0f),
                new Vector3(0.0f, 0.0f, 1.0f), 100.0f), out hit), "Triangle behind the ray was hit.");
            Assert.False(model.RayCast(new Ray(new Vector3(0.25f, 0.25f, 10.0f),
                new Vector3(1.0f, 0.0f, 0.0f), 100.0f), out hit), "Parallel ray was hit.");
            Assert.False(model.RayCast(new Ray(new Vector3(3.0f, 3.0f, 10.0f),
                new Vector3(0.0f, 0.0f, -1.0f), 100.0f), out hit), "Point outside the triangle was hit.");
            Assert.False(model.RayCast(new Ray(new Vector3(0.25f, 0.25f, 10.0f),
                new Vector3(0.0f, 0.0f, -1.0f), 6.5f), out hit), "Ray exceeded MaxDistance.");
        }

        [Test]
        public void RayCastHitsSpheresAndBoxes()
        {
            ColModel model = ColFile.Read(CreateSyntheticFile())[0];
            ColHit hit;
            Assert.True(model.RayCast(new Ray(new Vector3(4.0f, 5.0f, 10.0f),
                new Vector3(0.0f, 0.0f, -1.0f), 20.0f), out hit));
            Assert.Equal(ColShapeType.Sphere, hit.ShapeType);
            Assert.Equal(0, hit.ShapeIndex);
            Assert.Equal((byte)17, hit.Surface);
            Assert.Equal((byte)2, hit.Piece);
            Assert.Near(2.75f, hit.Distance);
            Assert.Near(7.25f, hit.Position.Z);
            AssertVector(new Vector3(0.0f, 0.0f, 1.0f), hit.Normal);

            Assert.True(model.QueryVertical(4.0f, 5.0f, 0.0f, 10.0f, out hit));
            Assert.Equal(ColShapeType.Sphere, hit.ShapeType);
            Assert.Near(2.75f, hit.Distance);

            Assert.True(model.RayCast(new Ray(new Vector3(0.0f, 0.0f, 10.0f),
                new Vector3(0.0f, 0.0f, -1.0f), 20.0f), out hit));
            Assert.Equal(ColShapeType.Box, hit.ShapeType);
            Assert.Equal(0, hit.ShapeIndex);
            Assert.Equal((byte)23, hit.Surface);
            Assert.Equal((byte)4, hit.Piece);
            Assert.Near(6.0f, hit.Distance);
            Assert.Near(4.0f, hit.Position.Z);
            AssertVector(new Vector3(0.0f, 0.0f, 1.0f), hit.Normal);
        }

        [Test]
        public void RejectsMalformedContainersAndModels()
        {
            byte[] valid = CreateSyntheticFile();
            byte[] malformed = Clone(valid);
            malformed[0] = 0;
            ExpectInvalid(malformed);

            ExpectInvalid(Prefix(valid, 4));
            ExpectInvalid(Prefix(valid, valid.Length - 1));
            ExpectInvalid(Concat(valid, new byte[] { 1 }));

            malformed = Clone(valid);
            WriteUInt32At(malformed, 4, uint.MaxValue);
            ExpectInvalid(malformed);

            malformed = Clone(valid);
            WriteInt16At(malformed, 72, -1); // name[24] + bounds[40] + header[8]
            ExpectInvalid(malformed);

            malformed = Clone(valid);
            WriteInt32At(malformed, 200, 3); // first triangle index; vertex count is three
            ExpectInvalid(malformed);

            malformed = Clone(valid);
            WriteInt32At(malformed, 204, -1);
            ExpectInvalid(malformed);

            malformed = Concat(valid, new byte[] { 0 });
            WriteUInt32At(malformed, 4, (uint)(malformed.Length - 8));
            ExpectInvalid(malformed); // non-payload bytes inside the declared model size
        }

        [Test]
        public void DegenerateTrianglesAreSafe()
        {
            ColTrianglePlane plane = CollisionGeometry.CalculateTrianglePlane(
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(2.0f, 0.0f, 0.0f));
            Assert.True(plane.IsDegenerate);
            AssertVector(Vector3.Zero, plane.Normal);
            Assert.Near(0.0f, plane.Distance);
            Assert.False(float.IsNaN(plane.Evaluate(new Vector3(1.0f, 2.0f, 3.0f))));

            ColModel model = ColFile.Read(CreateTriangleFile(
                new Vector3[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.0f, 0.0f),
                    new Vector3(2.0f, 0.0f, 0.0f)
                },
                new ColTriangle[] { new ColTriangle(0, 1, 2, 42) }))[0];
            ColHit hit;
            Assert.False(model.RayCast(new Ray(new Vector3(0.5f, 0.0f, 1.0f),
                new Vector3(0.0f, 0.0f, -1.0f), 10.0f), out hit));
        }

        private static ColModel CreateTwoPlaneModel()
        {
            return ColFile.Read(CreateTriangleFile(
                new Vector3[]
                {
                    new Vector3(0.0f, 0.0f, 0.0f),
                    new Vector3(2.0f, 0.0f, 0.0f),
                    new Vector3(0.0f, 2.0f, 0.0f),
                    new Vector3(0.0f, 0.0f, 3.0f),
                    new Vector3(2.0f, 0.0f, 3.0f),
                    new Vector3(0.0f, 2.0f, 3.0f)
                },
                new ColTriangle[]
                {
                    new ColTriangle(0, 1, 2, 42),
                    new ColTriangle(3, 4, 5, 77)
                }))[0];
        }

        private static byte[] CreateSyntheticFile()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BeginModel(stream);
                WriteName(stream, "legacy_model");
                WriteSingle(stream, 20.0f); // bounding radius precedes its center on disk
                WriteVector(stream, new Vector3(1.0f, 2.0f, 3.0f));
                WriteVector(stream, new Vector3(-10.0f, -10.0f, -10.0f));
                WriteVector(stream, new Vector3(10.0f, 10.0f, 10.0f));

                WriteCount(stream, 1);
                WriteSingle(stream, 1.25f);
                WriteVector(stream, new Vector3(4.0f, 5.0f, 6.0f));
                stream.WriteByte(17);
                stream.WriteByte(2);
                WriteUInt16(stream, 0xF00D);

                WriteCount(stream, 1);
                WriteVector(stream, new Vector3(-1.0f, -2.0f, -3.0f));
                WriteVector(stream, new Vector3(7.0f, 8.0f, 9.0f));

                WriteCount(stream, 1);
                WriteVector(stream, new Vector3(-2.0f, -3.0f, -4.0f));
                WriteVector(stream, new Vector3(2.0f, 3.0f, 4.0f));
                stream.WriteByte(23);
                stream.WriteByte(4);
                WriteUInt16(stream, 0xC33C);

                WriteCount(stream, 3);
                WriteVector(stream, new Vector3(0.0f, 0.0f, 0.0f));
                WriteVector(stream, new Vector3(2.0f, 0.0f, 0.0f));
                WriteVector(stream, new Vector3(0.0f, 2.0f, 0.0f));

                WriteCount(stream, 1);
                WriteInt32(stream, 0);
                WriteInt32(stream, 1);
                WriteInt32(stream, 2);
                stream.WriteByte(42);
                stream.WriteByte(0xD1);
                stream.WriteByte(0xD2);
                stream.WriteByte(0xD3);
                return FinishModel(stream);
            }
        }

        private static byte[] CreateTriangleFile(Vector3[] vertices, ColTriangle[] triangles)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BeginModel(stream);
                WriteName(stream, "triangles");
                WriteSingle(stream, 200.0f);
                WriteVector(stream, Vector3.Zero);
                WriteVector(stream, new Vector3(-100.0f, -100.0f, -100.0f));
                WriteVector(stream, new Vector3(100.0f, 100.0f, 100.0f));
                WriteCount(stream, 0);
                WriteCount(stream, 0);
                WriteCount(stream, 0);
                WriteCount(stream, (short)vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                    WriteVector(stream, vertices[i]);
                WriteCount(stream, (short)triangles.Length);
                for (int i = 0; i < triangles.Length; i++)
                {
                    WriteInt32(stream, triangles[i].A);
                    WriteInt32(stream, triangles[i].B);
                    WriteInt32(stream, triangles[i].C);
                    stream.WriteByte(triangles[i].Surface);
                    WriteUInt16(stream, 0);
                    stream.WriteByte(0);
                }
                return FinishModel(stream);
            }
        }

        private static void BeginModel(Stream stream)
        {
            WriteUInt32(stream, ColFile.ModelIdent);
            WriteUInt32(stream, 0);
        }

        private static byte[] FinishModel(MemoryStream stream)
        {
            long end = stream.Length;
            stream.Position = 4;
            WriteUInt32(stream, (uint)(end - 8));
            stream.Position = end;
            return stream.ToArray();
        }

        private static void WriteName(Stream stream, string name)
        {
            for (int i = 0; i < 24; i++)
                stream.WriteByte(i < name.Length ? (byte)name[i] : (byte)0);
        }

        private static void WriteCount(Stream stream, short count)
        {
            WriteInt16(stream, count);
            WriteUInt16(stream, 0xA55A); // ignored alignment bytes need not be zero
        }

        private static void WriteVector(Stream stream, Vector3 value)
        {
            WriteSingle(stream, value.X);
            WriteSingle(stream, value.Y);
            WriteSingle(stream, value.Z);
        }

        private static void WriteSingle(Stream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteInt16(Stream stream, short value)
        {
            WriteUInt16(stream, unchecked((ushort)value));
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteInt32(Stream stream, int value)
        {
            WriteUInt32(stream, unchecked((uint)value));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void ExpectInvalid(byte[] data)
        {
            Assert.Throws<AssetFormatException>(delegate { ColFile.Read(data); });
        }

        private static byte[] Clone(byte[] source)
        {
            return (byte[])source.Clone();
        }

        private static byte[] Prefix(byte[] source, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(source, 0, result, 0, length);
            return result;
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            int length = 0;
            for (int i = 0; i < arrays.Length; i++)
                length += arrays[i].Length;
            byte[] result = new byte[length];
            int offset = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
                offset += arrays[i].Length;
            }
            return result;
        }

        private static void WriteInt16At(byte[] data, int offset, short value)
        {
            ushort bits = unchecked((ushort)value);
            data[offset] = (byte)bits;
            data[offset + 1] = (byte)(bits >> 8);
        }

        private static void WriteInt32At(byte[] data, int offset, int value)
        {
            WriteUInt32At(data, offset, unchecked((uint)value));
        }

        private static void WriteUInt32At(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X);
            Assert.Near(expected.Y, actual.Y);
            Assert.Near(expected.Z, actual.Z);
        }
    }
}
