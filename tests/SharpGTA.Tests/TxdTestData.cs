using System;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    internal static class TxdTestData
    {
        private const uint Version = 0x33002u;
        private const uint RasterFormatPal8 = 0x2000u;
        private const uint RasterFormatPal4 = 0x4000u;
        private const int FixedNameLength = 32;

        internal static byte[] CreateArchive(params TxdTexture[] textures)
        {
            if (textures == null)
                throw new ArgumentNullException("textures");

            byte[] payload;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(Chunk(
                        RenderWareChunkTypes.Struct,
                        Version,
                        CreateDictionaryStruct(textures.Length)));
                    for (int i = 0; i < textures.Length; i++)
                        writer.Write(CreateNative(textures[i]));
                    writer.Write(EmptyExtension());
                }
                payload = stream.ToArray();
            }

            return Chunk(RenderWareChunkTypes.TextureDictionary, Version, payload);
        }

        internal static TxdTexture CreateTexture(
            string name,
            int width,
            int height,
            TxdCompression compression,
            uint rasterFormat,
            byte[][] levels)
        {
            if (levels == null)
                throw new ArgumentNullException("levels");

            TxdMipLevel[] mipLevels = new TxdMipLevel[levels.Length];
            int levelWidth = width;
            int levelHeight = height;
            for (int i = 0; i < levels.Length; i++)
            {
                mipLevels[i] = new TxdMipLevel
                {
                    Width = levelWidth,
                    Height = levelHeight,
                    Data = levels[i]
                };
                levelWidth = Math.Max(1, levelWidth / 2);
                levelHeight = Math.Max(1, levelHeight / 2);
            }

            return new TxdTexture
            {
                Platform = TxdTexture.PlatformD3D8,
                FilterAddressing = 0x00001102u,
                RasterFormat = rasterFormat,
                Name = name,
                MaskName = string.Empty,
                Width = width,
                Height = height,
                Depth = 32,
                RasterType = 4,
                Compression = compression,
                Levels = mipLevels
            };
        }

        internal static byte[] CreateNative(TxdTexture texture)
        {
            if (texture == null)
                throw new ArgumentNullException("texture");

            return Chunk(
                RenderWareChunkTypes.TextureNative,
                Version,
                Concat(
                    Chunk(RenderWareChunkTypes.Struct, Version, CreateNativeStruct(texture)),
                    EmptyExtension()));
        }

        private static byte[] CreateDictionaryStruct(int textureCount)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(checked((short)textureCount));
                    writer.Write((short)0);
                }
                return stream.ToArray();
            }
        }

        private static byte[] CreateNativeStruct(TxdTexture texture)
        {
            TxdMipLevel[] levels = texture.Levels ?? new TxdMipLevel[0];
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(texture.Platform);
                    writer.Write(texture.FilterAddressing);
                    WriteFixedAscii(writer, texture.Name);
                    WriteFixedAscii(writer, texture.MaskName ?? string.Empty);
                    writer.Write(texture.RasterFormat);
                    writer.Write(texture.HasAlpha ? 1 : 0);
                    writer.Write(checked((ushort)texture.Width));
                    writer.Write(checked((ushort)texture.Height));
                    writer.Write(checked((byte)texture.Depth));
                    writer.Write(checked((byte)levels.Length));
                    writer.Write(checked((byte)texture.RasterType));
                    writer.Write(checked((byte)texture.Compression));
                    WritePalette(writer, texture);

                    for (int i = 0; i < levels.Length; i++)
                    {
                        if (levels[i] == null)
                            throw new ArgumentException("Mip levels cannot be null.", "texture");
                        byte[] data = levels[i].Data;
                        if (data == null)
                            throw new ArgumentException("Mip-level data cannot be null.", "texture");
                        writer.Write((uint)data.Length);
                        writer.Write(data);
                    }
                }
                return stream.ToArray();
            }
        }

        private static void WritePalette(BinaryWriter writer, TxdTexture texture)
        {
            int entryCount;
            if ((texture.RasterFormat & RasterFormatPal8) != 0)
                entryCount = 256;
            else if ((texture.RasterFormat & RasterFormatPal4) != 0)
                entryCount = 32;
            else
                return;

            DffColor[] palette = texture.Palette;
            int suppliedCount = palette == null ? 0 : palette.Length;
            if (suppliedCount > entryCount)
                throw new ArgumentException("The palette has too many entries for the raster format.", "texture");

            for (int i = 0; i < entryCount; i++)
            {
                DffColor color = i < suppliedCount ? palette[i] : new DffColor();
                writer.Write(color.R);
                writer.Write(color.G);
                writer.Write(color.B);
                writer.Write(color.A);
            }
        }

        private static void WriteFixedAscii(BinaryWriter writer, string value)
        {
            byte[] bytes = Ascii.GetBytes(value);
            if (bytes.Length > FixedNameLength)
                throw new ArgumentException("Texture names cannot exceed 32 ASCII bytes.", "value");

            writer.Write(bytes);
            writer.Write(new byte[FixedNameLength - bytes.Length]);
        }

        private static byte[] EmptyExtension()
        {
            return Chunk(RenderWareChunkTypes.Extension, Version, new byte[0]);
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
