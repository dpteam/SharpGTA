using System;

namespace SharpGTA.Assets
{
    public enum TxdCompression
    {
        Uncompressed = 0,
        Dxt1 = 1,
        Dxt2 = 2,
        Dxt3 = 3,
        Dxt4 = 4,
        Dxt5 = 5
    }

    public sealed class TxdArchive
    {
        public TxdTexture[] Textures { get; internal set; }
        public int DeclaredTextureCount { get; internal set; }
        public int SkippedTextureCount { get; internal set; }

        public TxdTexture Find(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (Textures == null)
                return null;

            for (int i = 0; i < Textures.Length; i++)
            {
                TxdTexture texture = Textures[i];
                if (texture != null && string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase))
                    return texture;
            }
            return null;
        }
    }

    public sealed class TxdTexture
    {
        public const uint PlatformD3D8 = 8;
        public const uint PlatformPs2 = 0x00325350;

        public uint Platform { get; set; }
        public uint FilterAddressing { get; set; }
        public uint RasterFormat { get; set; }
        public string Name { get; set; }
        public string MaskName { get; set; }
        public bool HasAlpha { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public int RasterType { get; set; }
        public TxdCompression Compression { get; set; }
        public DffColor[] Palette { get; set; }
        public TxdMipLevel[] Levels { get; set; }

        public int LevelCount
        {
            get { return Levels.Length; }
        }

        public TxdImage DecodeLevel(int index)
        {
            return TxdPixelDecoder.Decode(this, Levels[index]);
        }
    }

    public sealed class TxdMipLevel
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Data { get; set; }
    }

    public sealed class TxdImage
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Rgba { get; private set; }

        public TxdImage(int width, int height, byte[] rgba)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if (rgba == null)
                throw new ArgumentNullException("rgba");

            int expectedLength = checked(width * height * 4);
            if (rgba.Length != expectedLength)
                throw new ArgumentException("RGBA data length must equal width * height * 4.", "rgba");

            Width = width;
            Height = height;
            Rgba = rgba;
        }
    }
}
