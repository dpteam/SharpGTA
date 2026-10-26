using System;
using System.Collections.Generic;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>Bounds-checked reader for GTA PC D3D8 RenderWare texture dictionaries.</summary>
    public static class TxdReader
    {
        public const int MaximumInputLength = 256 * 1024 * 1024;
        private const int MaximumLevels = 32;

        public static TxdArchive Read(byte[] data)
        {
            return Read(data, 0, data == null ? 0 : data.Length, false);
        }

        public static TxdArchive Read(byte[] data, bool skipUnsupportedPlatforms)
        {
            return Read(data, 0, data == null ? 0 : data.Length, skipUnsupportedPlatforms);
        }

        public static TxdArchive Read(byte[] data, int offset, int count, bool skipUnsupportedPlatforms)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(offset < 0 ? "offset" : "count");
            if (count > MaximumInputLength)
                throw new AssetFormatException("TXD input exceeds the configured size limit.");
            RenderWareChunkReader root = new RenderWareChunkReader(data, offset, count);
            RenderWareStreamChunk dictionary = root.ReadExpected(RenderWareChunkTypes.TextureDictionary, "texture dictionary");
            RenderWareChunkReader content = new RenderWareChunkReader(data,
                dictionary.PayloadOffset, root.End - dictionary.PayloadOffset);
            RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "textureDictionary.struct");
            short declaredCount = header.Payload.ReadInt16();
            header.Payload.ReadInt16(); // Device ID is informational; each texture has its own platform.
            header.FinishPayload("textureDictionary.struct");
            if (declaredCount < 0)
                throw new AssetFormatException("Negative TXD texture count.", header.ChunkOffset);

            List<TxdTexture> textures = new List<TxdTexture>(declaredCount);
            int skipped = 0;
            for (int i = 0; i < declaredCount; i++)
            {
                RenderWareStreamChunk native = content.ReadExpected(RenderWareChunkTypes.TextureNative,
                    "textureNative[" + i + "]");
                TxdTexture texture;
                if (TryReadNative(native, i, skipUnsupportedPlatforms, out texture))
                    textures.Add(texture);
                else
                    skipped++;
            }
            content.ReadExpected(RenderWareChunkTypes.Extension, "textureDictionary.extension");
            // Do not interpret IMG sector slack after the count-driven dictionary end.
            return new TxdArchive
            {
                Textures = textures.ToArray(),
                DeclaredTextureCount = declaredCount,
                SkippedTextureCount = skipped
            };
        }

        private static bool TryReadNative(RenderWareStreamChunk native, int textureIndex,
            bool skipUnsupportedPlatforms, out TxdTexture texture)
        {
            texture = null;
            RenderWareChunkReader content = new RenderWareChunkReader(native.Buffer,
                native.PayloadOffset, native.PayloadLength);
            RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct,
                "textureNative[" + textureIndex + "].struct");
            uint platform = header.Payload.ReadUInt32();
            if (platform != TxdTexture.PlatformD3D8)
            {
                if (!skipUnsupportedPlatforms)
                {
                    throw new AssetFormatException("Unsupported TXD native platform 0x" +
                        platform.ToString("X8") + " at texture " + textureIndex +
                        "; only PC D3D8 is decoded by this reader.", header.ChunkOffset);
                }
                return false;
            }

            ByteReader reader = header.Payload;
            uint filterAddressing = reader.ReadUInt32();
            string name = reader.ReadFixedAscii(32);
            string maskName = reader.ReadFixedAscii(32);
            uint rasterFormat = reader.ReadUInt32();
            bool hasAlpha = reader.ReadInt32() != 0;
            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();
            int depth = reader.ReadByte();
            int levelCount = reader.ReadByte();
            int rasterType = reader.ReadByte();
            int compressionValue = reader.ReadByte();
            if (width == 0 || height == 0)
                throw Error(textureIndex, name, "Texture dimensions must be positive.", header.ChunkOffset);
            if (levelCount <= 0 || levelCount > MaximumLevels)
                throw Error(textureIndex, name, "Invalid mip level count " + levelCount + ".", header.ChunkOffset);
            if (compressionValue < 0 || compressionValue > (int)TxdCompression.Dxt5)
                throw Error(textureIndex, name, "Unknown compression " + compressionValue + ".", header.ChunkOffset);

            DffColor[] palette = null;
            if ((rasterFormat & 0x4000u) != 0)
                palette = ReadPalette(reader, 32); // D3D8 stores PAL4 as 32 entries, unlike generic Image.
            else if ((rasterFormat & 0x2000u) != 0)
                palette = ReadPalette(reader, 256);

            TxdTexture result = new TxdTexture
            {
                Platform = platform,
                FilterAddressing = filterAddressing,
                RasterFormat = rasterFormat,
                Name = name,
                MaskName = maskName,
                HasAlpha = hasAlpha,
                Width = width,
                Height = height,
                Depth = depth,
                RasterType = rasterType,
                Compression = (TxdCompression)compressionValue,
                Palette = palette,
                Levels = new TxdMipLevel[levelCount]
            };
            for (int i = 0; i < levelCount; i++)
            {
                int levelWidth = Math.Max(1, width >> i);
                int levelHeight = Math.Max(1, height >> i);
                int expectedSize;
                try
                {
                    expectedSize = TxdPixelDecoder.GetExpectedDataSize(result, levelWidth, levelHeight);
                }
                catch (AssetFormatException exception)
                {
                    throw Error(textureIndex, name, "Invalid level " + i + " format: " + exception.Message, header.ChunkOffset);
                }
                catch (NotSupportedException exception)
                {
                    throw new AssetFormatException("TXD texture " + name + " uses an unsupported format: " + exception.Message,
                        header.ChunkOffset, exception);
                }
                catch (OverflowException exception)
                {
                    throw new AssetFormatException("TXD texture " + name + " has an overflowing mip level size.",
                        header.ChunkOffset, exception);
                }
                uint storedSize = reader.ReadUInt32();
                if (storedSize != (uint)expectedSize)
                {
                    throw Error(textureIndex, name, "Mip level " + i + " has " + storedSize +
                        " bytes; expected " + expectedSize + ".", header.ChunkOffset);
                }
                result.Levels[i] = new TxdMipLevel
                {
                    Width = levelWidth,
                    Height = levelHeight,
                    Data = reader.ReadBytes(expectedSize)
                };
            }
            header.FinishPayload("textureNative[" + textureIndex + "].struct");
            content.ReadExpected(RenderWareChunkTypes.Extension,
                "textureNative[" + textureIndex + "].extension");
            content.Finish("textureNative[" + textureIndex + "]");
            texture = result;
            return true;
        }

        private static DffColor[] ReadPalette(ByteReader reader, int count)
        {
            DffColor[] palette = new DffColor[count];
            for (int i = 0; i < count; i++)
            {
                palette[i] = new DffColor(reader.ReadByte(), reader.ReadByte(),
                    reader.ReadByte(), reader.ReadByte());
            }
            return palette;
        }

        private static AssetFormatException Error(int index, string name, string message, int offset)
        {
            return new AssetFormatException("TXD texture " + index + " ('" + name + "'): " + message, offset);
        }
    }
}
