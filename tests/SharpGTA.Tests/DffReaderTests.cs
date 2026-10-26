using System;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class DffReaderTests
    {
        [Test]
        public void ReadsStaticGeometryAcrossRetailVersionFamilies()
        {
            uint[] versions = { 0x34003u, 0x33002u, 0x30200u };
            for (int i = 0; i < versions.Length; i++)
                VerifyStaticClump(DffReader.ReadClump(DffTestData.Create(versions[i])), versions[i]);
        }

        [Test]
        public void ReadsMultipleClumpsAndSectorPadding()
        {
            byte[] padded = new byte[2048];
            byte[] clump = DffTestData.Create(0x33002u);
            Buffer.BlockCopy(clump, 0, padded, 0, clump.Length);
            Assert.Equal(1, DffReader.Read(padded).Clumps.Length);

            byte[] first = DffTestData.Create(0x34003u);
            byte[] second = DffTestData.Create(0x30200u);
            byte[] multiple = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, multiple, 0, first.Length);
            Buffer.BlockCopy(second, 0, multiple, first.Length, second.Length);
            DffDocument document = DffReader.Read(multiple);
            Assert.Equal(2, document.Clumps.Length);
            Assert.Equal(0x34003u, document.Clumps[0].Version);
            Assert.Equal(0x30200u, document.Clumps[1].Version);
            Assert.Throws<AssetFormatException>(delegate { DffReader.ReadClump(multiple); });
        }

        [Test]
        public void CountDrivenClumpLengthMismatchIsReportedNotFatal()
        {
            byte[] bytes = DffTestData.Create(0x33002u);
            int declared = BitConverter.ToInt32(bytes, 4);
            byte[] shortened = (byte[])bytes.Clone();
            WriteInt32(shortened, 4, declared - 12);
            DffClump clump = DffReader.ReadClump(shortened);
            Assert.True(clump.HasDeclaredLengthMismatch);
            Assert.Equal(declared + 12, clump.ActualLength);
            Assert.Equal(declared, clump.DeclaredLength);
            Assert.Equal(1, clump.Geometries.Length);
        }

        [Test]
        public void RejectsTruncationNativeGeometryAndWrongRoots()
        {
            byte[] valid = DffTestData.Create(0x34003u);
            int[] cuts = { 0, 4, 11, 12, 100, valid.Length - 1 };
            for (int i = 0; i < cuts.Length; i++)
            {
                int length = cuts[i];
                byte[] truncated = new byte[length];
                Buffer.BlockCopy(valid, 0, truncated, 0, length);
                Assert.Throws<AssetFormatException>(delegate { DffReader.Read(truncated); });
            }

            byte[] native = (byte[])valid.Clone();
            int format = FindSequence(native, new byte[] { 0x9A, 0x00, 0x02, 0x00 });
            Assert.True(format >= 0, "Synthetic geometry format was not found.");
            native[format + 3] |= 1;
            Assert.Throws<AssetFormatException>(delegate { DffReader.Read(native); });
            Assert.Throws<AssetFormatException>(delegate { DffReader.Read(new byte[12]); });
        }

        private static void VerifyStaticClump(DffClump clump, uint version)
        {
            Assert.Equal(version, clump.Version);
            Assert.Equal(0, clump.LightCount);
            Assert.Equal(0, clump.CameraCount);
            Assert.False(clump.HasDeclaredLengthMismatch);
            Assert.Equal(2, clump.Frames.Length);
            Assert.Equal(-1, clump.Frames[0].ParentIndex);
            Assert.Equal(0, clump.Frames[1].ParentIndex);
            Assert.Equal("root", clump.Frames[0].Name);
            Assert.Equal("child", clump.Frames[1].Name);
            Assert.Near(1.0f, clump.Frames[1].LocalTransform.Position.X);
            Assert.Near(2.0f, clump.Frames[1].LocalTransform.Position.Y);
            Assert.Near(3.0f, clump.Frames[1].LocalTransform.Position.Z);
            Assert.Near(1.0f, clump.Frames[0].LocalTransform.Forward.Z);
            Assert.Near(1.0f, clump.Frames[0].LocalTransform.Up.Y);

            Assert.Equal(1, clump.Geometries.Length);
            Assert.Equal(1, clump.Atomics.Length);
            Assert.Equal(1, clump.Atomics[0].FrameIndex);
            Assert.Equal(0, clump.Atomics[0].GeometryIndex);
            Assert.Equal(5, clump.Atomics[0].Flags);
            Assert.Equal(9, clump.Atomics[0].Unused);
            Assert.True(ReferenceEquals(clump.Geometries[0], clump.Atomics[0].Geometry));
            DffGeometry geometry = clump.Geometries[0];
            Assert.Equal(0x0002009Au, geometry.Format);
            Assert.Equal(version < 0x34000u, geometry.HasLegacySurfaceProperties);
            if (version < 0x34000u)
                Assert.Near(0.5f, geometry.LegacySurfaceProperties.Ambient);
            Assert.Equal(3, geometry.PrelitColors.Length);
            Assert.Equal((byte)1, geometry.PrelitColors[0].R);
            Assert.Equal((byte)12, geometry.PrelitColors[2].A);
            Assert.Equal(2, geometry.TexCoords.Length);
            Assert.Equal(3, geometry.TexCoords[0].Length);
            Assert.Near(1.2f, geometry.TexCoords[1][2].Y);

            Assert.Equal(1, geometry.Triangles.Length);
            Assert.Equal((ushort)0, geometry.Triangles[0].A);
            Assert.Equal((ushort)1, geometry.Triangles[0].B);
            Assert.Equal((ushort)2, geometry.Triangles[0].C);
            Assert.Equal(2, geometry.MorphTargets.Length);
            Assert.Near(40.0f, geometry.MorphTargets[0].BoundingSphere.Radius);
            Assert.Near(10.0f, geometry.MorphTargets[0].Vertices[0].X);
            Assert.NotNull(geometry.MorphTargets[0].Normals);
            Assert.Equal(null, geometry.MorphTargets[1].Normals);
            Assert.Near(-10.0f, geometry.MorphTargets[1].Vertices[0].X);

            Assert.Equal(2, geometry.Materials.Length);
            Assert.True(ReferenceEquals(geometry.Materials[0], geometry.Materials[1]));
            DffMaterial material = geometry.Materials[0];
            Assert.Equal(7, material.Flags);
            Assert.Equal(8, material.Unused);
            Assert.Equal((byte)20, material.Color.G);
            Assert.Equal("brick", material.Texture.Name);
            Assert.Equal("mask", material.Texture.MaskName);
            Assert.Equal(0x00001102u, material.Texture.FilterAddressing);
            if (version < 0x30400u)
                Assert.Near(0.25f, material.SurfaceProperties.Specular);
            else
                Assert.Near(0.5f, material.SurfaceProperties.Specular);

            Assert.Equal(1, geometry.Meshes.Length);
            Assert.True(geometry.Meshes[0].TriangleStrip);
            DffTriangle[] triangles = geometry.Meshes[0].Triangulate();
            Assert.Equal(2, triangles.Length);
            Assert.Equal((ushort)0, triangles[0].A);
            Assert.Equal((ushort)1, triangles[0].B);
            Assert.Equal((ushort)2, triangles[0].C);
            Assert.Equal((ushort)1, triangles[1].A);
            Assert.Equal((ushort)0, triangles[1].B);
            Assert.Equal((ushort)2, triangles[1].C);
        }

        private static int FindSequence(byte[] data, byte[] sequence)
        {
            for (int i = 0; i <= data.Length - sequence.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (data[i + j] != sequence[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
