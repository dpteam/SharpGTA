using System;
using System.Collections.Generic;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Collision
{
    /// <summary>Reader for the PC GTA III/Vice City legacy COLL container.</summary>
    public static class ColFile
    {
        public const uint ModelIdent = 0x4C4C4F43; // "COLL" in little-endian byte order.

        private const int ModelNameSize = 24;
        private const int MinimumPayloadSize = 84;

        public static List<ColModel> Read(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            List<ColModel> models = new List<ColModel>();
            ByteReader reader = new ByteReader(data);
            while (reader.Remaining > 0)
            {
                int headerOffset = reader.Position;
                if (reader.Remaining < 8)
                {
                    if (IsZeroPadding(data, headerOffset))
                        break;
                    throw new AssetFormatException("Truncated COLL model header.", headerOffset);
                }

                uint ident = reader.ReadUInt32();
                if (ident != ModelIdent)
                {
                    if (IsZeroPadding(data, headerOffset))
                        break;
                    throw new AssetFormatException("Invalid COLL model identifier; expected 0x4C4C4F43.", headerOffset);
                }

                uint payloadSize = reader.ReadUInt32();
                if (payloadSize > int.MaxValue)
                    throw new AssetFormatException("COLL model payload size exceeds the supported range.", headerOffset + 4);
                if (payloadSize > (uint)reader.Remaining)
                    throw new AssetFormatException("Truncated COLL model payload.", reader.Position);
                if (payloadSize < MinimumPayloadSize)
                    throw new AssetFormatException("COLL model payload is too small.", headerOffset + 4);

                int payloadOffset = reader.Position;
                ByteReader payload = reader.ReadSlice((int)payloadSize);
                try
                {
                    models.Add(ReadModel(payload));
                }
                catch (AssetFormatException exception)
                {
                    long offset = exception.Offset >= 0 ? payloadOffset + exception.Offset : payloadOffset;
                    throw new AssetFormatException("Invalid COLL model payload.", offset, exception);
                }
            }
            return models;
        }

        private static ColModel ReadModel(ByteReader reader)
        {
            string modelName = reader.ReadFixedAscii(ModelNameSize);

            // The on-disk sphere is radius first, then center (unlike CSphere in memory).
            float boundingRadius = reader.ReadSingle();
            Vector3 boundingCenter = ReadVector3(reader);
            Vector3 boundingMin = ReadVector3(reader);
            Vector3 boundingMax = ReadVector3(reader);

            int sphereCount = ReadCount(reader, "sphere");
            RequireRecords(reader, sphereCount, 20, "spheres");
            ColSphere[] spheres = new ColSphere[sphereCount];
            for (int i = 0; i < spheres.Length; i++)
            {
                float radius = reader.ReadSingle();
                Vector3 center = ReadVector3(reader);
                byte surface = reader.ReadByte();
                byte piece = reader.ReadByte();
                reader.Skip(2);
                spheres[i] = new ColSphere(radius, center, surface, piece);
            }

            int lineCount = ReadCount(reader, "line");
            RequireRecords(reader, lineCount, 24, "lines");
            ColLine[] lines = new ColLine[lineCount];
            for (int i = 0; i < lines.Length; i++)
            {
                // reVC discards these records. Keep both endpoints instead of losing them.
                lines[i] = new ColLine(ReadVector3(reader), ReadVector3(reader));
            }

            int boxCount = ReadCount(reader, "box");
            RequireRecords(reader, boxCount, 28, "boxes");
            ColBox[] boxes = new ColBox[boxCount];
            for (int i = 0; i < boxes.Length; i++)
            {
                Vector3 min = ReadVector3(reader);
                Vector3 max = ReadVector3(reader);
                byte surface = reader.ReadByte();
                byte piece = reader.ReadByte();
                reader.Skip(2);
                boxes[i] = new ColBox(min, max, surface, piece);
            }

            int vertexCount = ReadCount(reader, "vertex");
            RequireRecords(reader, vertexCount, 12, "vertices");
            Vector3[] vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = ReadVector3(reader);

            int triangleCount = ReadCount(reader, "triangle");
            RequireRecords(reader, triangleCount, 16, "triangles");
            ColTriangle[] triangles = new ColTriangle[triangleCount];
            for (int i = 0; i < triangles.Length; i++)
            {
                int triangleOffset = reader.Position;
                int a = reader.ReadInt32();
                int b = reader.ReadInt32();
                int c = reader.ReadInt32();
                byte surface = reader.ReadByte();
                reader.Skip(3);
                ValidateIndex(a, vertices.Length, triangleOffset);
                ValidateIndex(b, vertices.Length, triangleOffset + 4);
                ValidateIndex(c, vertices.Length, triangleOffset + 8);
                triangles[i] = new ColTriangle((ushort)a, (ushort)b, (ushort)c, surface);
            }

            if (reader.Remaining != 0)
                throw new AssetFormatException("COLL model payload has " + reader.Remaining + " unexpected trailing bytes.", reader.Position);

            return new ColModel(modelName,
                new BoundingSphere(boundingCenter, boundingRadius),
                new BoundingBox(boundingMin, boundingMax),
                spheres, lines, boxes, vertices, triangles);
        }

        private static int ReadCount(ByteReader reader, string description)
        {
            int offset = reader.Position;
            short count = reader.ReadInt16();
            reader.Skip(2); // Every on-disk count occupies a four-byte aligned slot.
            if (count < 0)
                throw new AssetFormatException("Negative COLL " + description + " count.", offset);
            return count;
        }

        private static void RequireRecords(ByteReader reader, int count, int recordSize, string description)
        {
            int bytes = checked(count * recordSize);
            if (bytes > reader.Remaining)
                throw new AssetFormatException("Truncated COLL " + description + " array.", reader.Position);
        }

        private static void ValidateIndex(int index, int vertexCount, int offset)
        {
            if (index < 0 || index > ushort.MaxValue || index >= vertexCount)
                throw new AssetFormatException("COLL triangle vertex index is outside the uint16/vertex-array range.", offset);
        }

        private static Vector3 ReadVector3(ByteReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, y, z);
        }

        private static bool IsZeroPadding(byte[] data, int offset)
        {
            for (int i = offset; i < data.Length; i++)
                if (data[i] != 0)
                    return false;
            return true;
        }
    }
}
