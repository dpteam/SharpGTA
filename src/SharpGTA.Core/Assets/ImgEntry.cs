using System;

namespace SharpGTA.Assets
{
    /// <summary>A single 32-byte GTA III/Vice City IMG directory entry.</summary>
    public sealed class ImgEntry
    {
        private readonly uint rawOffset;
        private readonly uint sizeSectors;
        private readonly string name;

        internal ImgEntry(uint rawOffset, uint sizeSectors, string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            this.rawOffset = rawOffset;
            this.sizeSectors = sizeSectors;
            this.name = name;
        }

        /// <summary>The unmodified offset field: image ID in bits 24-31, sector offset in bits 0-23.</summary>
        public uint RawOffset
        {
            get { return rawOffset; }
        }

        public uint OffsetSectors
        {
            get { return rawOffset & 0x00FFFFFFu; }
        }

        public uint SizeSectors
        {
            get { return sizeSectors; }
        }

        public int ImageIndex
        {
            get { return (int)(rawOffset >> 24); }
        }

        public long ByteOffset
        {
            get { return OffsetSectors * (long)ImgDirectory.SectorSize; }
        }

        /// <summary>The sector-rounded length stored in the directory, including IMG padding.</summary>
        public long ByteLength
        {
            get { return sizeSectors * (long)ImgDirectory.SectorSize; }
        }

        public string Name
        {
            get { return name; }
        }

        internal ImgEntry WithImageIndex(int imageIndex)
        {
            if (imageIndex < 0 || imageIndex > 255)
                throw new ArgumentOutOfRangeException("imageIndex");
            return new ImgEntry(OffsetSectors | ((uint)imageIndex << 24), sizeSectors, name);
        }
    }
}
