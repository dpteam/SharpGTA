using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>
    /// Headless RenderWare chunk inventory. It descends only into core chunks with a
    /// documented sequence of child chunk headers. Unknown and plugin payloads remain leaves.
    /// </summary>
    public static class RenderWareInventory
    {
        public const int MaximumDepth = 64;

        public static ReadOnlyCollection<RenderWareChunk> ReadInventory(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            List<RenderWareChunk> chunks = ReadChunks(data, 0, data.Length, 0);
            return chunks.AsReadOnly();
        }

        private static List<RenderWareChunk> ReadChunks(byte[] data, int offset, int length, int depth)
        {
            ByteReader reader = new ByteReader(data, offset, length);
            List<RenderWareChunk> chunks = new List<RenderWareChunk>();
            while (reader.Remaining != 0)
            {
                int chunkOffset = offset + reader.Position;
                // IMG resources are sector-aligned; zero bytes after the final chunk
                // are padding, not additional zero-ID RenderWare chunks.
                if (chunks.Count != 0 && IsZeroPadding(data, chunkOffset, reader.Remaining))
                    break;
                if (reader.Remaining < 12)
                    throw new AssetFormatException("RenderWare chunk header is truncated; 12 bytes are required.", chunkOffset);

                uint id = reader.ReadUInt32();
                uint declaredLength = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                if (declaredLength > (uint)reader.Remaining)
                    throw new AssetFormatException("RenderWare chunk 0x" + id.ToString("X8") + " declares " + declaredLength + " payload bytes, but only " + reader.Remaining + " remain in its bounds.", chunkOffset);

                int payloadOffset = offset + reader.Position;
                int dataLength = (int)declaredLength;
                reader.Skip(dataLength);
                List<RenderWareChunk> children = null;
                if (RenderWareChunkTypes.IsKnownContainer(id))
                {
                    if (dataLength != 0)
                    {
                        if (depth >= MaximumDepth)
                            throw new AssetFormatException("RenderWare container nesting exceeds the configured depth limit of " + MaximumDepth + ".", chunkOffset);
                        children = ReadChunks(data, payloadOffset, dataLength, depth + 1);
                    }
                    else
                    {
                        children = new List<RenderWareChunk>();
                    }
                }
                chunks.Add(new RenderWareChunk(id, version, chunkOffset, payloadOffset, dataLength, children));
            }
            return chunks;
        }

        private static bool IsZeroPadding(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (data[offset + i] != 0)
                    return false;
            }
            return true;
        }
    }
}
