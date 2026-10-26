using System;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    internal sealed class RenderWareStreamChunk
    {
        internal RenderWareStreamChunk(uint id, int payloadLength, int chunkOffset,
            int payloadOffset, uint libraryId, uint version, uint build, byte[] buffer)
        {
            Buffer = buffer;
            Id = id;
            PayloadLength = payloadLength;
            ChunkOffset = chunkOffset;
            PayloadOffset = payloadOffset;
            LibraryId = libraryId;
            Version = version;
            Build = build;
            Payload = new ByteReader(buffer, payloadOffset, payloadLength);
        }

        internal byte[] Buffer { get; private set; }
        internal uint Id { get; private set; }
        internal int PayloadLength { get; private set; }
        internal int ChunkOffset { get; private set; }
        internal int PayloadOffset { get; private set; }
        internal uint LibraryId { get; private set; }
        internal uint Version { get; private set; }
        internal uint Build { get; private set; }
        internal ByteReader Payload { get; private set; }

        internal int DeclaredEnd
        {
            get { return PayloadOffset + PayloadLength; }
        }

        internal void FinishPayload(string context)
        {
            if (Payload.Remaining != 0)
            {
                throw new AssetFormatException(context + " has " + Payload.Remaining +
                    " unconsumed byte(s) in its STRUCT payload.", PayloadOffset + Payload.Position);
            }
        }
    }

    /// <summary>Bounded sibling chunk sequence; there is deliberately no Align4 operation.</summary>
    internal sealed class RenderWareChunkReader
    {
        private readonly byte[] buffer;
        private readonly int start;
        private readonly int end;
        private int position;

        internal RenderWareChunkReader(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("offset");
            this.buffer = buffer;
            start = offset;
            position = offset;
            end = offset + count;
        }

        internal int Position { get { return position; } }
        internal int Remaining { get { return end - position; } }
        internal int End { get { return end; } }

        internal void Seek(int newPosition)
        {
            if (newPosition < start || newPosition > end)
                throw new ArgumentOutOfRangeException("newPosition");
            position = newPosition;
        }

        internal uint? PeekId()
        {
            if (Remaining < 4)
                return null;
            return (uint)(buffer[position] | buffer[position + 1] << 8 |
                buffer[position + 2] << 16 | buffer[position + 3] << 24);
        }

        internal bool IsZeroPadding()
        {
            for (int i = position; i < end; i++)
            {
                if (buffer[i] != 0)
                    return false;
            }
            return true;
        }

        internal RenderWareStreamChunk ReadExpected(uint expectedId, string context)
        {
            if (Remaining < 12)
                throw new AssetFormatException("Truncated RenderWare chunk header while reading " + context + ".", position);
            uint actualId = PeekId().Value;
            if (actualId != expectedId)
            {
                throw new AssetFormatException("Expected RenderWare chunk 0x" + expectedId.ToString("X8") +
                    " for " + context + ", found 0x" + actualId.ToString("X8") + ".", position);
            }
            return Read(context);
        }

        internal RenderWareStreamChunk Read(string context)
        {
            if (Remaining < 12)
                throw new AssetFormatException("Truncated RenderWare chunk header while reading " + context + ".", position);
            int chunkOffset = position;
            uint id = ReadUInt32();
            uint declaredLength = ReadUInt32();
            uint libraryId = ReadUInt32();
            if (declaredLength > (uint)(end - position))
            {
                throw new AssetFormatException("RenderWare chunk 0x" + id.ToString("X8") +
                    " exceeds its containing bounds while reading " + context + ".", chunkOffset);
            }
            int payloadOffset = position;
            int payloadLength = (int)declaredLength;
            position += payloadLength;
            uint version;
            uint build;
            UnpackLibraryId(libraryId, out version, out build);
            return new RenderWareStreamChunk(id, payloadLength, chunkOffset, payloadOffset,
                libraryId, version, build, buffer);
        }

        internal void Skip(string context)
        {
            Read(context);
        }

        internal void Finish(string context)
        {
            if (Remaining != 0)
                throw new AssetFormatException(context + " has " + Remaining + " unconsumed byte(s).", position);
        }

        internal static void UnpackLibraryId(uint libraryId, out uint version, out uint build)
        {
            if ((libraryId & 0xFFFF0000u) != 0)
            {
                version = (((libraryId >> 14) & 0x3FF00u) + 0x30000u) |
                    ((libraryId >> 16) & 0x3Fu);
                build = libraryId & 0xFFFFu;
            }
            else
            {
                version = libraryId << 8;
                build = 0;
            }
        }

        private uint ReadUInt32()
        {
            uint value = (uint)(buffer[position] | buffer[position + 1] << 8 |
                buffer[position + 2] << 16 | buffer[position + 3] << 24);
            position += 4;
            return value;
        }
    }
}
