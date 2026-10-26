using System.IO;
using SharpGTA.Assets;

namespace SharpGTA.Tests
{
    internal static class DffPluginTestData
    {
        internal static byte[] CreateWithPlugins(uint version, bool oldSkin)
        {
            return DffTestData.CreateWithPlugins(version, oldSkin);
        }

        internal static byte[] CreateGeometrySkinPayload(int vertexCount, bool oldFormat)
        {
            byte[] indices = { 0, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0 };
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write((byte)2);
                    writer.Write((byte)(oldFormat ? 0 : 2));
                    writer.Write((byte)(oldFormat ? 0 : 4));
                    writer.Write((byte)0);
                    if (!oldFormat)
                    {
                        writer.Write((byte)0);
                        writer.Write((byte)1);
                    }

                    for (int i = 0; i < vertexCount * 4; i++)
                        writer.Write(indices[i % indices.Length]);
                    for (int i = 0; i < vertexCount; i++)
                    {
                        writer.Write(0.5f);
                        writer.Write(0.5f);
                        writer.Write(0.0f);
                        writer.Write(0.0f);
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        if (oldFormat)
                            writer.Write(0xDEADDEADu);
                        float offset = i * 30.0f;
                        WriteInverseMatrix(writer, 10.0f + offset, 20.0f + offset, 30.0f + offset);
                    }

                    if (!oldFormat)
                    {
                        writer.Write(4);
                        writer.Write(1);
                        writer.Write(1);
                        writer.Write((byte)1);
                        writer.Write((byte)0);
                        writer.Write((byte)0);
                        writer.Write((byte)1);
                        writer.Write((byte)0);
                        writer.Write((byte)1);
                    }
                }
                return stream.ToArray();
            }
        }

        internal static byte[] CreateHAnimPayload()
        {
            return DffTestData.Int32Payload(
                0x100,
                7,
                2,
                0x10,
                36,
                10, 0, 1,
                11, 1, 2);
        }

        internal static byte[] CreateMaterialMatFxPayload(uint version)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(2u);
                    writer.Write(2u);
                    writer.Write(0.5f);
                    writer.Write(1);
                    writer.Write(1);
                    writer.Write(CreateTexture(version, "envtex", "mask"));
                    writer.Write(0u);
                }
                return stream.ToArray();
            }
        }

        internal static byte[] CreateAtomicMatFxPayload()
        {
            return DffTestData.Int32Payload(1);
        }

        private static void WriteInverseMatrix(BinaryWriter writer, float x, float y, float z)
        {
            writer.Write(1.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(1.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(1.0f);
            writer.Write(0.0f);
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
            writer.Write(1.0f);
        }

        private static byte[] CreateTexture(uint version, string name, string maskName)
        {
            return DffTestData.Chunk(
                RenderWareChunkTypes.Texture,
                version,
                DffTestData.Concat(
                    DffTestData.Chunk(
                        RenderWareChunkTypes.Struct,
                        version,
                        DffTestData.Int32Payload(0x00001102)),
                    DffTestData.Chunk(
                        RenderWareChunkTypes.String,
                        version,
                        DffTestData.OddStringPayload(name)),
                    DffTestData.Chunk(
                        RenderWareChunkTypes.String,
                        version,
                        DffTestData.OddStringPayload(maskName)),
                    DffTestData.Chunk(RenderWareChunkTypes.Extension, version, new byte[0])));
        }
    }
}
