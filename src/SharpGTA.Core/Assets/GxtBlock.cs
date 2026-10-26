using SharpGTA.IO;

namespace SharpGTA.Assets
{
    internal sealed class GxtBlock
    {
        internal GxtBlock(string magic, int headerOffset, int payloadOffset, int length)
        {
            Magic = magic;
            HeaderOffset = headerOffset;
            PayloadOffset = payloadOffset;
            Length = length;
        }

        internal string Magic { get; private set; }
        internal int HeaderOffset { get; private set; }
        internal int PayloadOffset { get; private set; }
        internal int Length { get; private set; }

        internal static GxtBlock Read(ByteReader reader, int absoluteOrigin, string context)
        {
            int headerOffset = absoluteOrigin + reader.Position;
            if (reader.Remaining < 8)
                throw new AssetFormatException(context + " has a truncated chunk header.", headerOffset);

            string magic = reader.ReadFourCc();
            uint declaredLength = reader.ReadUInt32();
            if (declaredLength > (uint)reader.Remaining)
                throw new AssetFormatException(context + " chunk '" + magic + "' declares " + declaredLength + " payload bytes, but only " + reader.Remaining + " remain in its bounds.", headerOffset);

            int payloadOffset = absoluteOrigin + reader.Position;
            int length = (int)declaredLength;
            GxtBlock block = new GxtBlock(magic, headerOffset, payloadOffset, length);
            reader.Skip(length);
            return block;
        }
    }
}
