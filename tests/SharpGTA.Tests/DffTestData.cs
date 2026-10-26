using System;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    internal static class DffTestData
    {
        private const uint NodeNameChunk = 0x0253F2FEu;
        private const uint BinMeshChunk = 0x0000050Eu;
        private const uint GeometryFormat = 0x0002009Au;

        internal static byte[] Create(uint version)
        {
            if (version != 0x34003u && version != 0x33002u && version != 0x30200u)
                throw new ArgumentOutOfRangeException("version");

            byte[] clumpStruct = Chunk(
                RenderWareChunkTypes.Struct,
                version,
                version >= 0x33000u ? Int32Payload(1, 0, 0) : Int32Payload(1));
            byte[] frameList = CreateFrameList(version);
            byte[] geometry = CreateGeometry(version);
            byte[] geometryAndAtomics;
            if (version >= 0x30400u)
            {
                byte[] geometryList = Chunk(
                    RenderWareChunkTypes.GeometryList,
                    version,
                    Concat(Chunk(RenderWareChunkTypes.Struct, version, Int32Payload(1)), geometry));
                byte[] atomic = Chunk(
                    RenderWareChunkTypes.Atomic,
                    version,
                    Concat(
                        Chunk(RenderWareChunkTypes.Struct, version, Int32Payload(1, 0, 5, 9)),
                        EmptyExtension(version)));
                geometryAndAtomics = Concat(geometryList, atomic);
            }
            else
            {
                // The old atomic layout is frameIndex, flags, unused; its geometry follows STRUCT.
                byte[] atomic = Chunk(
                    RenderWareChunkTypes.Atomic,
                    version,
                    Concat(
                        Chunk(RenderWareChunkTypes.Struct, version, Int32Payload(1, 5, 9)),
                        geometry,
                        EmptyExtension(version)));
                geometryAndAtomics = atomic;
            }

            return Chunk(
                RenderWareChunkTypes.Clump,
                version,
                Concat(clumpStruct, frameList, geometryAndAtomics, EmptyExtension(version)));
        }

        private static byte[] CreateFrameList(uint version)
        {
            byte[] frameStruct;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(2);
                    WriteFrame(writer, 0.0f, 0.0f, 0.0f, -1);
                    WriteFrame(writer, 1.0f, 2.0f, 3.0f, 0);
                }
                frameStruct = stream.ToArray();
            }

            return Chunk(
                RenderWareChunkTypes.FrameList,
                version,
                Concat(
                    Chunk(RenderWareChunkTypes.Struct, version, frameStruct),
                    CreateFrameExtension(version, "root"),
                    CreateFrameExtension(version, "child")));
        }

        private static void WriteFrame(BinaryWriter writer, float x, float y, float z, int parentIndex)
        {
            // RW streams right, up, at, pos; GTA's Matrix calls up/at Forward/Up respectively.
            WriteVector3(writer, 1.0f, 0.0f, 0.0f);
            WriteVector3(writer, 0.0f, 0.0f, 1.0f);
            WriteVector3(writer, 0.0f, 1.0f, 0.0f);
            WriteVector3(writer, x, y, z);
            writer.Write(parentIndex);
            writer.Write(0);
        }

        private static byte[] CreateFrameExtension(uint version, string name)
        {
            return Chunk(
                RenderWareChunkTypes.Extension,
                version,
                Chunk(NodeNameChunk, version, Ascii.GetBytes(name)));
        }

        private static byte[] CreateGeometry(uint version)
        {
            byte[] geometryStruct = CreateGeometryStruct(version);
            byte[] materialList = CreateMaterialList(version);
            byte[] extension = Chunk(
                RenderWareChunkTypes.Extension,
                version,
                Chunk(BinMeshChunk, version, CreateBinMeshPayload()));
            return Chunk(
                RenderWareChunkTypes.Geometry,
                version,
                Concat(geometryStruct, materialList, extension));
        }

        private static byte[] CreateGeometryStruct(uint version)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(GeometryFormat);
                    writer.Write(1);
                    writer.Write(3);
                    writer.Write(2);
                    if (version < 0x34000u)
                        WriteSurfaceProperties(writer, 0.5f, 0.25f, 1.0f);

                    WriteColor(writer, 1, 2, 3, 4);
                    WriteColor(writer, 5, 6, 7, 8);
                    WriteColor(writer, 9, 10, 11, 12);

                    WriteVector2(writer, 0.1f, 0.2f);
                    WriteVector2(writer, 0.3f, 0.4f);
                    WriteVector2(writer, 0.5f, 0.6f);
                    WriteVector2(writer, 0.7f, 0.8f);
                    WriteVector2(writer, 0.9f, 1.0f);
                    WriteVector2(writer, 1.1f, 1.2f);

                    // On disk: B, A, material, C. The decoded triangle is A=0, B=1, C=2.
                    writer.Write((ushort)1);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)2);

                    WriteMorphTarget(writer, 10.0f, 20.0f, 30.0f, 40.0f, 10, true);
                    WriteMorphTarget(writer, -10.0f, -20.0f, -30.0f, 50.0f, -10, false);
                }
                return Chunk(RenderWareChunkTypes.Struct, version, stream.ToArray());
            }
        }

        private static void WriteMorphTarget(
            BinaryWriter writer,
            float centerX,
            float centerY,
            float centerZ,
            float radius,
            int firstVertexValue,
            bool includeNormals)
        {
            WriteVector3(writer, centerX, centerY, centerZ);
            writer.Write(radius);
            writer.Write(1);
            writer.Write(includeNormals ? 1 : 0);
            for (int i = 0; i < 3; i++)
            {
                float value = firstVertexValue + i * 3;
                WriteVector3(writer, value, value + 1.0f, value + 2.0f);
            }

            if (includeNormals)
            {
                WriteVector3(writer, 1.0f, 0.0f, 0.0f);
                WriteVector3(writer, 0.0f, 1.0f, 0.0f);
                WriteVector3(writer, 0.0f, 0.0f, 1.0f);
            }
        }

        private static byte[] CreateMaterialList(uint version)
        {
            return Chunk(
                RenderWareChunkTypes.MaterialList,
                version,
                Concat(
                    Chunk(RenderWareChunkTypes.Struct, version, Int32Payload(2, -1, 0)),
                    CreateMaterial(version)));
        }

        private static byte[] CreateMaterial(uint version)
        {
            byte[] materialStruct;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(7);
                    WriteColor(writer, 10, 20, 30, 40);
                    writer.Write(8);
                    writer.Write(1);
                    if (version >= 0x30400u)
                        WriteSurfaceProperties(writer, 0.75f, 0.5f, 0.25f);
                }
                materialStruct = stream.ToArray();
            }

            return Chunk(
                RenderWareChunkTypes.Material,
                version,
                Concat(
                    Chunk(RenderWareChunkTypes.Struct, version, materialStruct),
                    CreateTexture(version),
                    EmptyExtension(version)));
        }

        private static byte[] CreateTexture(uint version)
        {
            byte[] filterStruct;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(0x00001102u);
                }
                filterStruct = stream.ToArray();
            }

            return Chunk(
                RenderWareChunkTypes.Texture,
                version,
                Concat(
                    Chunk(RenderWareChunkTypes.Struct, version, filterStruct),
                    Chunk(RenderWareChunkTypes.String, version, OddStringPayload("brick")),
                    Chunk(RenderWareChunkTypes.String, version, OddStringPayload("mask")),
                    EmptyExtension(version)));
        }

        private static byte[] CreateBinMeshPayload()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(1u);
                    writer.Write(1u);
                    writer.Write(4u);
                    writer.Write(4u);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(1);
                    writer.Write(2);
                    writer.Write(0);
                }
                return stream.ToArray();
            }
        }

        private static byte[] OddStringPayload(string value)
        {
            byte[] text = Ascii.GetBytes(value);
            int length = text.Length + 1;
            if ((length & 1) == 0)
                length++;
            byte[] result = new byte[length];
            Buffer.BlockCopy(text, 0, result, 0, text.Length);
            return result;
        }

        private static byte[] EmptyExtension(uint version)
        {
            return Chunk(RenderWareChunkTypes.Extension, version, new byte[0]);
        }

        private static void WriteSurfaceProperties(
            BinaryWriter writer,
            float ambient,
            float specular,
            float diffuse)
        {
            writer.Write(ambient);
            writer.Write(specular);
            writer.Write(diffuse);
        }

        private static void WriteColor(BinaryWriter writer, byte r, byte g, byte b, byte a)
        {
            writer.Write(r);
            writer.Write(g);
            writer.Write(b);
            writer.Write(a);
        }

        private static void WriteVector2(BinaryWriter writer, float x, float y)
        {
            writer.Write(x);
            writer.Write(y);
        }

        private static void WriteVector3(BinaryWriter writer, float x, float y, float z)
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
        }

        private static byte[] Int32Payload(params int[] values)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    for (int i = 0; i < values.Length; i++)
                        writer.Write(values[i]);
                }
                return stream.ToArray();
            }
        }

        private static byte[] Chunk(uint id, uint version, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException("payload");

            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(id);
                    writer.Write(payload.Length);
                    writer.Write(PackVersion(version));
                    writer.Write(payload);
                }
                return stream.ToArray();
            }
        }

        private static uint PackVersion(uint version)
        {
            if (version <= 0x31000u)
                return version >> 8;
            return ((version - 0x30000u) & 0x3FF00u) << 14 |
                (version & 0x3Fu) << 16;
        }

        private static byte[] Concat(params byte[][] chunks)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    for (int i = 0; i < chunks.Length; i++)
                        writer.Write(chunks[i]);
                }
                return stream.ToArray();
            }
        }
    }
}
