using System;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class TxdReaderTests
    {
        [Test]
        public void ReadsNamesMipLevelsPalettesAndCaseInsensitiveLookup()
        {
            TxdTexture dxt = TxdTestData.CreateTexture("Brick", 8, 8, TxdCompression.Dxt1,
                0x8500u, new byte[][] { new byte[32], new byte[8] });
            dxt.MaskName = "BrickMask";
            dxt.HasAlpha = true;
            TxdTexture raw = TxdTestData.CreateTexture("flat", 2, 1, TxdCompression.Uncompressed,
                0x0500u, new byte[][] { new byte[] { 3, 2, 1, 4, 30, 20, 10, 40 } });
            TxdTexture paletted = TxdTestData.CreateTexture("palette", 2, 1, TxdCompression.Uncompressed,
                0x2500u, new byte[][] { new byte[] { 0, 1 } });
            paletted.Depth = 8;
            paletted.Palette = new DffColor[256];
            paletted.Palette[0] = new DffColor(1, 2, 3, 4);
            paletted.Palette[1] = new DffColor(5, 6, 7, 8);
            byte[] bytes = TxdTestData.CreateArchive(dxt, raw, paletted);

            TxdArchive archive = TxdReader.Read(bytes);
            Assert.Equal(3, archive.DeclaredTextureCount);
            Assert.Equal(0, archive.SkippedTextureCount);
            Assert.Equal(3, archive.Textures.Length);
            Assert.True(ReferenceEquals(archive.Textures[0], archive.Find("bRiCk")));
            Assert.Equal(2, archive.Find("brick").LevelCount);
            Assert.Equal(8, archive.Find("brick").Levels[0].Width);
            Assert.Equal(4, archive.Find("brick").Levels[1].Width);
            Assert.Equal(32, archive.Find("brick").Levels[0].Data.Length);
            Assert.Equal("BrickMask", archive.Find("brick").MaskName);
            Assert.True(archive.Find("brick").HasAlpha);
            Assert.Equal((byte)10, archive.Find("FLAT").DecodeLevel(0).Rgba[4]);
            Assert.Equal((byte)5, archive.Find("palette").DecodeLevel(0).Rgba[4]);
            Assert.Equal((byte)8, archive.Find("palette").DecodeLevel(0).Rgba[7]);
            Assert.Equal(null, archive.Find("missing"));
        }

        [Test]
        public void DictionaryEndDoesNotInterpretSectorSlack()
        {
            byte[] archive = TxdTestData.CreateArchive(TxdTestData.CreateTexture("one", 1, 1,
                TxdCompression.Uncompressed, 0x0500u, new byte[][] { new byte[4] }));
            byte[] padded = new byte[2048];
            Buffer.BlockCopy(archive, 0, padded, 0, archive.Length);
            for (int i = archive.Length; i < padded.Length; i++)
                padded[i] = 0xA5;
            Assert.Equal(1, TxdReader.Read(padded).Textures.Length);
        }

        [Test]
        public void UnsupportedPlatformsAreExplicitOrSkippedByPolicy()
        {
            TxdTexture ps2 = TxdTestData.CreateTexture("ps2", 1, 1, TxdCompression.Uncompressed,
                0x0500u, new byte[][] { new byte[4] });
            ps2.Platform = TxdTexture.PlatformPs2;
            byte[] bytes = TxdTestData.CreateArchive(ps2);
            Assert.Throws<AssetFormatException>(delegate { TxdReader.Read(bytes); });
            TxdArchive skipped = TxdReader.Read(bytes, true);
            Assert.Equal(1, skipped.DeclaredTextureCount);
            Assert.Equal(1, skipped.SkippedTextureCount);
            Assert.Equal(0, skipped.Textures.Length);
        }

        [Test]
        public void RejectsMalformedCountsHeadersAndMipSizes()
        {
            byte[] bytes = TxdTestData.CreateArchive(TxdTestData.CreateTexture("one", 1, 1,
                TxdCompression.Uncompressed, 0x0500u, new byte[][] { new byte[] { 1, 2, 3, 4 } }));
            byte[] negative = (byte[])bytes.Clone();
            negative[24] = 0xFF;
            negative[25] = 0xFF;
            Assert.Throws<AssetFormatException>(delegate { TxdReader.Read(negative); });
            Assert.Throws<AssetFormatException>(delegate { TxdReader.Read(new byte[12]); });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdReader.Read(bytes, 0, bytes.Length - 1, false);
            });

            byte[] wrongSize = (byte[])bytes.Clone();
            int pixel = FindSequence(wrongSize, new byte[] { 1, 2, 3, 4 });
            Assert.True(pixel >= 4);
            wrongSize[pixel - 4] = 3;
            Assert.Throws<AssetFormatException>(delegate { TxdReader.Read(wrongSize); });

            TxdTexture zeroLevels = TxdTestData.CreateTexture("empty", 1, 1,
                TxdCompression.Uncompressed, 0x0500u, new byte[0][]);
            Assert.Throws<AssetFormatException>(delegate { TxdReader.Read(TxdTestData.CreateArchive(zeroLevels)); });
        }

        private static int FindSequence(byte[] data, byte[] sequence)
        {
            for (int i = 0; i <= data.Length - sequence.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (data[i + j] != sequence[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }
    }
}
