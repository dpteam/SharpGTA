using System;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class TxdPixelDecoderTests
    {
        private const uint C1555 = 0x0100u;
        private const uint C565 = 0x0200u;
        private const uint C4444 = 0x0300u;
        private const uint LUM8 = 0x0400u;
        private const uint C8888 = 0x0500u;
        private const uint C888 = 0x0600u;
        private const uint C555 = 0x0A00u;
        private const uint PAL8 = 0x2000u;
        private const uint PAL4 = 0x4000u;
        private const TxdCompression Uncompressed = (TxdCompression)0;

        [Test]
        public void Dxt1DecodesFourColorBranchWithIntegerExpansion()
        {
            byte[] data =
            {
                0x00, 0xF8, 0x1F, 0x00, 0xE4, 0, 0, 0
            };
            byte[] expected =
            {
                255, 0, 0, 255,
                0, 0, 255, 255,
                170, 0, 85, 255,
                85, 0, 170, 255
            };

            AssertImage(DecodeUncompressedOrDxt(C8888, 32, TxdCompression.Dxt1, 4, 1, data),
                4, 1, expected);
        }

        [Test]
        public void Dxt1DecodesThreeColorBranchAndTransparencyWithoutHasAlpha()
        {
            byte[] data =
            {
                0x00, 0x00, 0xFF, 0xFF, 0xE4, 0, 0, 0
            };
            byte[] expected =
            {
                0, 0, 0, 255,
                255, 255, 255, 255,
                127, 127, 127, 255,
                0, 0, 0, 0
            };
            TxdTexture texture = CreateTexture(C8888, 32, TxdCompression.Dxt1);
            texture.HasAlpha = false;

            AssertImage(TxdPixelDecoder.Decode(texture, CreateLevel(4, 1, data)), 4, 1, expected);
        }

        [Test]
        public void Dxt1ClipsThreeByFiveEdgeBlocksToTheRequestedImage()
        {
            byte[] data = new byte[16];
            byte[] redBlock = { 0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0 };
            byte[] greenBlock = { 0xE0, 0x07, 0x00, 0x00, 0, 0, 0, 0 };
            Buffer.BlockCopy(redBlock, 0, data, 0, redBlock.Length);
            Buffer.BlockCopy(greenBlock, 0, data, 8, greenBlock.Length);
            byte[] expected = new byte[3 * 5 * 4];
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    int offset = (y * 3 + x) * 4;
                    expected[offset + 1] = y < 4 ? (byte)0 : (byte)255;
                    expected[offset] = y < 4 ? (byte)255 : (byte)0;
                    expected[offset + 2] = 0;
                    expected[offset + 3] = 255;
                }
            }

            AssertImage(DecodeUncompressedOrDxt(C8888, 32, TxdCompression.Dxt1, 3, 5, data),
                3, 5, expected);
        }

        [Test]
        public void Dxt3UsesUnconditionalFourColorsAndFourBitAlpha()
        {
            byte[] data =
            {
                0x50, 0xFA, 0, 0, 0, 0, 0, 0,
                0x00, 0x00, 0xFF, 0xFF, 0xE4, 0, 0, 0
            };
            byte[] expected =
            {
                0, 0, 0, 0,
                255, 255, 255, 85,
                85, 85, 85, 170,
                170, 170, 170, 255
            };

            AssertImage(DecodeUncompressedOrDxt(C8888, 32, TxdCompression.Dxt3, 4, 1, data),
                4, 1, expected);
        }

        [Test]
        public void Dxt5DecodesBothAlphaInterpolationBranches()
        {
            VerifyDxt5Alpha(255, 0, new byte[] { 255, 0, 218, 182, 145, 109, 72, 36 });
            VerifyDxt5Alpha(10, 20, new byte[] { 10, 20, 12, 14, 16, 18, 0, 255 });
        }

        [Test]
        public void DecodesBgraAndBothBgrStorageDepths()
        {
            AssertImage(DecodeUncompressedOrDxt(C8888, 32, Uncompressed,
                2, 1, new byte[] { 0x33, 0x22, 0x11, 0x44, 0x00, 0x80, 0xFF, 0x7F }),
                2, 1, new byte[]
                {
                    0x11, 0x22, 0x33, 0x44,
                    0xFF, 0x80, 0x00, 0x7F
                });
            AssertImage(DecodeUncompressedOrDxt(C888, 24, Uncompressed,
                1, 1, new byte[] { 3, 2, 1 }), 1, 1, new byte[] { 1, 2, 3, 255 });
            AssertImage(DecodeUncompressedOrDxt(C888, 32, Uncompressed,
                1, 1, new byte[] { 30, 20, 10, 99 }), 1, 1, new byte[] { 10, 20, 30, 255 });
        }

        [Test]
        public void DecodesSixteenBitColorFormatsAsLittleEndian()
        {
            AssertImage(DecodeUncompressedOrDxt(C1555, 16, Uncompressed,
                2, 1, new byte[] { 0x00, 0xFC, 0xFF, 0x03 }), 2, 1, new byte[]
                {
                    255, 0, 0, 255,
                    0, 255, 255, 0
                });
            AssertImage(DecodeUncompressedOrDxt(C565, 16, Uncompressed,
                3, 1, new byte[] { 0x00, 0xF8, 0xE0, 0x07, 0x1F, 0x00 }), 3, 1, new byte[]
                {
                    255, 0, 0, 255,
                    0, 255, 0, 255,
                    0, 0, 255, 255
                });
            AssertImage(DecodeUncompressedOrDxt(C4444, 16, Uncompressed,
                1, 1, new byte[] { 0xAF, 0xF1 }), 1, 1, new byte[] { 17, 170, 255, 255 });
            AssertImage(DecodeUncompressedOrDxt(C555, 16, Uncompressed,
                1, 1, new byte[] { 0x10, 0x42 }), 1, 1, new byte[] { 131, 131, 131, 255 });
        }

        [Test]
        public void DecodesLuminanceAsOpaqueGray()
        {
            AssertImage(DecodeUncompressedOrDxt(LUM8, 8, Uncompressed,
                3, 1, new byte[] { 0, 127, 255 }), 3, 1, new byte[]
                {
                    0, 0, 0, 255,
                    127, 127, 127, 255,
                    255, 255, 255, 255
                });
        }

        [Test]
        public void Pal8UsesRgbaPaletteAndFormatDrivenAlphaRules()
        {
            DffColor[] palette =
            {
                new DffColor(1, 2, 3, 4),
                new DffColor(5, 6, 7, 8)
            };
            TxdTexture withAlpha = CreateTexture(PAL8 | C8888, 8, Uncompressed);
            withAlpha.Palette = palette;
            AssertImage(TxdPixelDecoder.Decode(withAlpha, CreateLevel(2, 1, new byte[] { 0, 1 })),
                2, 1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            TxdTexture withoutAlpha = CreateTexture(PAL8 | C888, 8, Uncompressed);
            withoutAlpha.Palette = palette;
            AssertImage(TxdPixelDecoder.Decode(withoutAlpha, CreateLevel(1, 1, new byte[] { 1 })),
                1, 1, new byte[] { 5, 6, 7, 255 });

            TxdTexture missingPalette = CreateTexture(PAL8 | C8888, 8, Uncompressed);
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(missingPalette, CreateLevel(1, 1, new byte[] { 0 }));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(withAlpha, CreateLevel(1, 1, new byte[] { 2 }));
            });
        }

        [Test]
        public void RejectsInvalidArgumentsDimensionsDepthsAndFormats()
        {
            TxdTexture texture = CreateTexture(C8888, 32, Uncompressed);
            Assert.Throws<ArgumentNullException>(delegate
            {
                TxdPixelDecoder.Decode(null, CreateLevel(1, 1, new byte[4]));
            });
            Assert.Throws<ArgumentNullException>(delegate { TxdPixelDecoder.Decode(texture, null); });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(texture, CreateLevel(1, 1, null));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(texture, CreateLevel(0, 1, new byte[0]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(texture, CreateLevel(-1, 1, new byte[0]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(texture, CreateLevel(int.MaxValue, int.MaxValue, new byte[0]));
            });

            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(C8888, 24, Uncompressed),
                    CreateLevel(1, 1, new byte[3]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(C8888, 1, Uncompressed),
                    CreateLevel(1, 1, new byte[0]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(0x0900u, 16, Uncompressed),
                    CreateLevel(1, 1, new byte[2]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(C8888, 32, (TxdCompression)99),
                    CreateLevel(1, 1, new byte[0]));
            });
        }

        [Test]
        public void RequiresExactMipDataLengths()
        {
            TxdTexture uncompressed = CreateTexture(C8888, 32, Uncompressed);
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(uncompressed, CreateLevel(1, 1, new byte[3]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(uncompressed, CreateLevel(1, 1, new byte[5]));
            });

            TxdTexture dxt1 = CreateTexture(C8888, 32, TxdCompression.Dxt1);
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(dxt1, CreateLevel(3, 5, new byte[15]));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                TxdPixelDecoder.Decode(dxt1, CreateLevel(3, 5, new byte[17]));
            });
        }

        [Test]
        public void RejectsExplicitlyUnsupportedPal4Dxt2AndDxt4()
        {
            Assert.Throws<NotSupportedException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(PAL4 | C8888, 4, Uncompressed),
                    CreateLevel(1, 1, new byte[0]));
            });
            Assert.Throws<NotSupportedException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(C8888, 32, TxdCompression.Dxt2),
                    CreateLevel(4, 4, new byte[16]));
            });
            Assert.Throws<NotSupportedException>(delegate
            {
                TxdPixelDecoder.Decode(CreateTexture(C8888, 32, TxdCompression.Dxt4),
                    CreateLevel(4, 4, new byte[16]));
            });
        }

        private static void VerifyDxt5Alpha(byte alpha0, byte alpha1, byte[] expectedAlpha)
        {
            byte[] data = new byte[16];
            data[0] = alpha0;
            data[1] = alpha1;
            // Alpha indices for the first eight texels: 0, 1, 2, 3, 4, 5, 6, 7.
            data[2] = 0x88;
            data[3] = 0xC6;
            data[4] = 0xFA;
            data[8] = 0x00;
            data[9] = 0xF8;
            data[10] = 0x1F;
            data[11] = 0x00;

            byte[] expected = new byte[8 * 4];
            for (int i = 0; i < expectedAlpha.Length; i++)
            {
                expected[i * 4] = 255;
                expected[i * 4 + 1] = 0;
                expected[i * 4 + 2] = 0;
                expected[i * 4 + 3] = expectedAlpha[i];
            }
            AssertImage(DecodeUncompressedOrDxt(C8888, 32, TxdCompression.Dxt5, 4, 2, data),
                4, 2, expected);
        }

        private static TxdImage DecodeUncompressedOrDxt(uint format, int depth,
            TxdCompression compression, int width, int height, byte[] data)
        {
            return TxdPixelDecoder.Decode(CreateTexture(format, depth, compression),
                CreateLevel(width, height, data));
        }

        private static TxdTexture CreateTexture(uint format, int depth, TxdCompression compression)
        {
            TxdTexture texture = new TxdTexture();
            texture.RasterFormat = format;
            texture.Depth = depth;
            texture.Compression = compression;
            texture.HasAlpha = true;
            return texture;
        }

        private static TxdMipLevel CreateLevel(int width, int height, byte[] data)
        {
            TxdMipLevel level = new TxdMipLevel();
            level.Width = width;
            level.Height = height;
            level.Data = data;
            return level;
        }

        private static void AssertImage(TxdImage image, int width, int height, byte[] expected)
        {
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(expected.Length, image.Rgba.Length);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], image.Rgba[i], "RGBA byte " + i + " differs.");
        }
    }
}
