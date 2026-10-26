using System;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>Decodes non-native PC TXD mip levels to tightly packed RGBA8 pixels.</summary>
    public static class TxdPixelDecoder
    {
        public const int MaximumDecodedBytes = 256 * 1024 * 1024;

        private const uint C1555 = 0x0100u;
        private const uint C565 = 0x0200u;
        private const uint C4444 = 0x0300u;
        private const uint LUM8 = 0x0400u;
        private const uint C8888 = 0x0500u;
        private const uint C888 = 0x0600u;
        private const uint C555 = 0x0A00u;
        private const uint PixelFormatMask = 0x0F00u;
        private const uint PAL8 = 0x2000u;
        private const uint PAL4 = 0x4000u;
        private const TxdCompression Uncompressed = (TxdCompression)0;

        public static TxdImage Decode(TxdTexture texture, TxdMipLevel level)
        {
            if (texture == null)
                throw new ArgumentNullException("texture");
            if (level == null)
                throw new ArgumentNullException("level");
            if (level.Data == null)
                throw new AssetFormatException("TXD mip level has no pixel data.");
            if (level.Width <= 0 || level.Height <= 0)
                throw new AssetFormatException("TXD mip level dimensions must be positive.");

            int width = level.Width;
            int height = level.Height;
            int pixelCount;
            int expectedLength;
            int outputLength;
            try
            {
                pixelCount = checked(width * height);
                outputLength = checked(pixelCount * 4);
                expectedLength = GetExpectedLength(texture, width, height, pixelCount);
            }
            catch (OverflowException exception)
            {
                throw new AssetFormatException("TXD mip level dimensions or depth are too large.", -1, exception);
            }

            if (level.Data.Length != expectedLength)
            {
                throw new AssetFormatException("TXD mip level data length " + level.Data.Length +
                    " does not match the expected length " + expectedLength + ".");
            }

            if (outputLength > MaximumDecodedBytes)
                throw new AssetFormatException("Decoded TXD image exceeds the configured RGBA size limit.");
            if (texture.Compression == TxdCompression.Dxt2 || texture.Compression == TxdCompression.Dxt4)
            {
                throw new NotSupportedException("DXT2/DXT4 premultiplied-alpha CPU decoding is not implemented; raw levels remain available.");
            }

            byte[] rgba = new byte[outputLength];
            if (texture.Compression == Uncompressed)
                DecodeUncompressed(texture, level.Data, pixelCount, rgba);
            else
                DecodeDxt(texture.Compression, level.Data, width, height, rgba);
            return new TxdImage(width, height, rgba);
        }

        public static int GetExpectedDataSize(TxdTexture texture, int width, int height)
        {
            if (texture == null)
                throw new ArgumentNullException("texture");
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(width <= 0 ? "width" : "height");
            int pixelCount = checked(width * height);
            switch (texture.Compression)
            {
                case Uncompressed:
                    return GetUncompressedLength(texture, pixelCount);
                case TxdCompression.Dxt1:
                    return GetDxtLength(width, height, 8);
                case TxdCompression.Dxt2:
                case TxdCompression.Dxt3:
                case TxdCompression.Dxt4:
                case TxdCompression.Dxt5:
                    return GetDxtLength(width, height, 16);
                default:
                    throw new AssetFormatException("Unknown TXD compression value " + (int)texture.Compression + ".");
            }
        }

        private static int GetExpectedLength(TxdTexture texture, int width, int height, int pixelCount)
        {
            switch (texture.Compression)
            {
                case Uncompressed:
                    return GetUncompressedLength(texture, pixelCount);
                case TxdCompression.Dxt1:
                    return GetDxtLength(width, height, 8);
                case TxdCompression.Dxt2:
                    return GetDxtLength(width, height, 16);
                case TxdCompression.Dxt3:
                    return GetDxtLength(width, height, 16);
                case TxdCompression.Dxt4:
                    return GetDxtLength(width, height, 16);
                case TxdCompression.Dxt5:
                    return GetDxtLength(width, height, 16);
                default:
                    throw new AssetFormatException("Unknown TXD compression value " + (int)texture.Compression + ".");
            }
        }

        private static int GetDxtLength(int width, int height, int blockBytes)
        {
            int blockWidth = width / 4 + (width % 4 == 0 ? 0 : 1);
            int blockHeight = height / 4 + (height % 4 == 0 ? 0 : 1);
            int blockCount = checked(blockWidth * blockHeight);
            return checked(blockCount * blockBytes);
        }

        private static int GetUncompressedLength(TxdTexture texture, int pixelCount)
        {
            uint format = texture.RasterFormat;
            if ((format & PAL4) != 0)
                throw new NotSupportedException("PAL4 TXD textures are not supported.");
            if (texture.Depth <= 0)
                throw new AssetFormatException("TXD texture depth must be positive.");

            long bitCount = checked((long)pixelCount * texture.Depth);
            if ((bitCount & 7L) != 0)
                throw new AssetFormatException("TXD mip level pixel data is not byte-aligned.");
            int expectedLength = checked((int)(bitCount / 8L));

            uint pixelFormat = format & PixelFormatMask;
            if (!IsKnownPixelFormat(pixelFormat))
                throw new AssetFormatException("Unknown TXD raster format 0x" + pixelFormat.ToString("X4") + ".");

            if ((format & PAL8) != 0)
            {
                RequireDepth(texture.Depth, 8, "PAL8");
                return expectedLength;
            }

            switch (pixelFormat)
            {
                case C8888:
                    RequireDepth(texture.Depth, 32, "C8888");
                    break;
                case C888:
                    if (texture.Depth != 24 && texture.Depth != 32)
                        throw InvalidDepth(texture.Depth, "C888 (24 or 32)");
                    break;
                case C1555:
                case C565:
                case C4444:
                case C555:
                    RequireDepth(texture.Depth, 16, "16-bit");
                    break;
                case LUM8:
                    RequireDepth(texture.Depth, 8, "LUM8");
                    break;
            }
            return expectedLength;
        }

        private static bool IsKnownPixelFormat(uint pixelFormat)
        {
            return pixelFormat == C1555 || pixelFormat == C565 || pixelFormat == C4444 ||
                pixelFormat == LUM8 || pixelFormat == C8888 || pixelFormat == C888 ||
                pixelFormat == C555;
        }

        private static void RequireDepth(int actual, int expected, string format)
        {
            if (actual != expected)
                throw InvalidDepth(actual, format + " (" + expected + ")");
        }

        private static AssetFormatException InvalidDepth(int actual, string format)
        {
            return new AssetFormatException("TXD depth " + actual + " is invalid for " + format + " raster data.");
        }

        private static void DecodeUncompressed(TxdTexture texture, byte[] data, int pixelCount, byte[] rgba)
        {
            uint pixelFormat = texture.RasterFormat & PixelFormatMask;
            if ((texture.RasterFormat & PAL8) != 0)
            {
                DecodePalette(texture, data, pixelCount, rgba);
                return;
            }

            switch (pixelFormat)
            {
                case C8888:
                    DecodeBgra(data, pixelCount, rgba);
                    break;
                case C888:
                    DecodeBgr(data, pixelCount, texture.Depth == 32, rgba);
                    break;
                case C1555:
                case C565:
                case C4444:
                case C555:
                    Decode16(pixelFormat, data, pixelCount, rgba);
                    break;
                case LUM8:
                    DecodeLuminance(data, pixelCount, rgba);
                    break;
            }
        }

        private static void DecodePalette(TxdTexture texture, byte[] data, int pixelCount, byte[] rgba)
        {
            DffColor[] palette = texture.Palette;
            if (palette == null)
                throw new AssetFormatException("PAL8 TXD texture has no palette.");

            bool paletteHasAlpha = FormatHasAlpha(texture.RasterFormat);
            int destination = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int index = data[i];
                if (index >= palette.Length)
                    throw new AssetFormatException("PAL8 index " + index + " is outside the palette.");

                DffColor color = palette[index];
                rgba[destination] = color.R;
                rgba[destination + 1] = color.G;
                rgba[destination + 2] = color.B;
                rgba[destination + 3] = paletteHasAlpha ? color.A : (byte)0xFF;
                destination += 4;
            }
        }

        private static bool FormatHasAlpha(uint format)
        {
            uint pixelFormat = format & PixelFormatMask;
            return pixelFormat == C8888 || pixelFormat == C1555 || pixelFormat == C4444;
        }

        private static void DecodeBgra(byte[] data, int pixelCount, byte[] rgba)
        {
            int source = 0;
            int destination = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                rgba[destination] = data[source + 2];
                rgba[destination + 1] = data[source + 1];
                rgba[destination + 2] = data[source];
                rgba[destination + 3] = data[source + 3];
                source += 4;
                destination += 4;
            }
        }

        private static void DecodeBgr(byte[] data, int pixelCount, bool hasPadding, byte[] rgba)
        {
            int source = 0;
            int destination = 0;
            int sourceStep = hasPadding ? 4 : 3;
            for (int i = 0; i < pixelCount; i++)
            {
                rgba[destination] = data[source + 2];
                rgba[destination + 1] = data[source + 1];
                rgba[destination + 2] = data[source];
                rgba[destination + 3] = 0xFF;
                source += sourceStep;
                destination += 4;
            }
        }

        private static void Decode16(uint pixelFormat, byte[] data, int pixelCount, byte[] rgba)
        {
            int source = 0;
            int destination = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int value = data[source] | data[source + 1] << 8;
                switch (pixelFormat)
                {
                    case C1555:
                        Write1555(value, true, destination, rgba);
                        break;
                    case C555:
                        Write1555(value, false, destination, rgba);
                        break;
                    case C565:
                        rgba[destination] = Expand5((value >> 11) & 0x1F);
                        rgba[destination + 1] = Expand6((value >> 5) & 0x3F);
                        rgba[destination + 2] = Expand5(value & 0x1F);
                        rgba[destination + 3] = 0xFF;
                        break;
                    case C4444:
                        rgba[destination] = Expand4((value >> 8) & 0x0F);
                        rgba[destination + 1] = Expand4((value >> 4) & 0x0F);
                        rgba[destination + 2] = Expand4(value & 0x0F);
                        rgba[destination + 3] = Expand4((value >> 12) & 0x0F);
                        break;
                }
                source += 2;
                destination += 4;
            }
        }

        private static void Write1555(int value, bool hasAlpha, int destination, byte[] rgba)
        {
            rgba[destination] = Expand5((value >> 10) & 0x1F);
            rgba[destination + 1] = Expand5((value >> 5) & 0x1F);
            rgba[destination + 2] = Expand5(value & 0x1F);
            rgba[destination + 3] = !hasAlpha || (value & 0x8000) != 0 ? (byte)0xFF : (byte)0;
        }

        private static void DecodeLuminance(byte[] data, int pixelCount, byte[] rgba)
        {
            int destination = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                byte luminance = data[i];
                rgba[destination] = luminance;
                rgba[destination + 1] = luminance;
                rgba[destination + 2] = luminance;
                rgba[destination + 3] = 0xFF;
                destination += 4;
            }
        }

        private static void DecodeDxt(TxdCompression compression, byte[] data,
            int width, int height, byte[] rgba)
        {
            int blockWidth = width / 4 + (width % 4 == 0 ? 0 : 1);
            int blockHeight = height / 4 + (height % 4 == 0 ? 0 : 1);
            int blockBytes = compression == TxdCompression.Dxt1 ? 8 : 16;
            byte[] colors = new byte[16];
            byte[] alphaTable = new byte[8];
            int blockOffset = 0;

            for (int blockY = 0; blockY < blockHeight; blockY++)
            {
                for (int blockX = 0; blockX < blockWidth; blockX++)
                {
                    bool isDxt1 = compression == TxdCompression.Dxt1;
                    bool isDxt3 = compression == TxdCompression.Dxt3;
                    int colorOffset = blockOffset + (isDxt1 ? 0 : 8);
                    BuildColorTable(data, colorOffset, colors, isDxt3, isDxt1);
                    uint colorIndices = ReadUInt32(data, colorOffset + 4);

                    ulong alphaIndices = 0;
                    if (compression == TxdCompression.Dxt5)
                    {
                        BuildDxt5AlphaTable(data, blockOffset, alphaTable);
                        for (int i = 0; i < 6; i++)
                            alphaIndices |= (ulong)data[blockOffset + 2 + i] << (i * 8);
                    }

                    for (int y = 0; y < 4; y++)
                    {
                        int outputY = blockY * 4 + y;
                        for (int x = 0; x < 4; x++)
                        {
                            int outputX = blockX * 4 + x;
                            if (outputX >= width || outputY >= height)
                                continue;

                            int pixel = y * 4 + x;
                            int colorIndex = (int)((colorIndices >> (pixel * 2)) & 3u);
                            int color = colorIndex * 4;
                            int destination = (outputY * width + outputX) * 4;
                            rgba[destination] = colors[color];
                            rgba[destination + 1] = colors[color + 1];
                            rgba[destination + 2] = colors[color + 2];

                            if (isDxt1)
                            {
                                rgba[destination + 3] = colors[color + 3];
                            }
                            else if (isDxt3)
                            {
                                int packedAlpha = data[blockOffset + pixel / 2];
                                int nibble = (packedAlpha >> ((pixel & 1) * 4)) & 0x0F;
                                rgba[destination + 3] = (byte)(nibble * 17);
                            }
                            else
                            {
                                int alphaIndex = (int)((alphaIndices >> (pixel * 3)) & 7ul);
                                rgba[destination + 3] = alphaTable[alphaIndex];
                            }
                        }
                    }
                    blockOffset += blockBytes;
                }
            }
        }

        private static void BuildColorTable(byte[] data, int offset, byte[] colors,
            bool unconditionalFourColor, bool transparentThreeColor)
        {
            int color0 = ReadUInt16(data, offset);
            int color1 = ReadUInt16(data, offset + 2);
            WriteExpandedColor(color0, 0, colors);
            WriteExpandedColor(color1, 4, colors);

            if (unconditionalFourColor || color0 > color1)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    colors[8 + channel] = (byte)((2 * colors[channel] + colors[4 + channel]) / 3);
                    colors[12 + channel] = (byte)((colors[channel] + 2 * colors[4 + channel]) / 3);
                }
                colors[11] = 0xFF;
                colors[15] = 0xFF;
            }
            else
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    colors[8 + channel] = (byte)((colors[channel] + colors[4 + channel]) / 2);
                    colors[12 + channel] = 0;
                }
                colors[11] = 0xFF;
                colors[15] = transparentThreeColor ? (byte)0 : (byte)0xFF;
            }
        }

        private static void WriteExpandedColor(int value, int destination, byte[] colors)
        {
            colors[destination] = Expand5((value >> 11) & 0x1F);
            colors[destination + 1] = Expand6((value >> 5) & 0x3F);
            colors[destination + 2] = Expand5(value & 0x1F);
            colors[destination + 3] = 0xFF;
        }

        private static void BuildDxt5AlphaTable(byte[] data, int offset, byte[] alpha)
        {
            int alpha0 = data[offset];
            int alpha1 = data[offset + 1];
            alpha[0] = (byte)alpha0;
            alpha[1] = (byte)alpha1;
            if (alpha0 > alpha1)
            {
                alpha[2] = (byte)((6 * alpha0 + alpha1) / 7);
                alpha[3] = (byte)((5 * alpha0 + 2 * alpha1) / 7);
                alpha[4] = (byte)((4 * alpha0 + 3 * alpha1) / 7);
                alpha[5] = (byte)((3 * alpha0 + 4 * alpha1) / 7);
                alpha[6] = (byte)((2 * alpha0 + 5 * alpha1) / 7);
                alpha[7] = (byte)((alpha0 + 6 * alpha1) / 7);
            }
            else
            {
                alpha[2] = (byte)((4 * alpha0 + alpha1) / 5);
                alpha[3] = (byte)((3 * alpha0 + 2 * alpha1) / 5);
                alpha[4] = (byte)((2 * alpha0 + 3 * alpha1) / 5);
                alpha[5] = (byte)((alpha0 + 4 * alpha1) / 5);
                alpha[6] = 0;
                alpha[7] = 0xFF;
            }
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | data[offset + 1] << 8;
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 |
                data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static byte Expand4(int value)
        {
            return (byte)(value * 255 / 15);
        }

        private static byte Expand5(int value)
        {
            return (byte)(value * 255 / 31);
        }

        private static byte Expand6(int value)
        {
            return (byte)(value * 255 / 63);
        }
    }
}
