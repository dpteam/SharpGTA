using System;
using System.IO;
using System.Text;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class TxdScanTests
    {
        [Test]
        public void ScannerDecodesValidPixelsSkipsPs2AndContinuesAfterFailures()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                TxdTexture good = TxdTestData.CreateTexture("good", 4, 4,
                    TxdCompression.Dxt1, 0x8500u, new byte[][] { new byte[8] });
                byte[] valid = TxdTestData.CreateArchive(good);
                TxdTexture ps2 = TxdTestData.CreateTexture("ps2", 1, 1,
                    TxdCompression.Uncompressed, 0x0500u, new byte[][] { new byte[4] });
                ps2.Platform = TxdTexture.PlatformPs2;
                byte[] unsupported = TxdTestData.CreateArchive(ps2);
                byte[] malformed = new byte[12];
                malformed[0] = 0x16; // Dictionary ID without its required children.

                byte[] directory;
                using (MemoryStream stream = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(stream);
                    WriteEntry(writer, 0, 1, "GOOD.TXD");
                    WriteEntry(writer, 1, 1, "PS2.TXD");
                    WriteEntry(writer, 2, 1, "BAD.TXD");
                    WriteEntry(writer, 3, 0, "EMPTY.TXD");
                    writer.Flush();
                    directory = stream.ToArray();
                }
                byte[] image = new byte[ImgDirectory.SectorSize * 3];
                Buffer.BlockCopy(valid, 0, image, 0, valid.Length);
                Buffer.BlockCopy(unsupported, 0, image, ImgDirectory.SectorSize, unsupported.Length);
                Buffer.BlockCopy(malformed, 0, image, ImgDirectory.SectorSize * 2, malformed.Length);
                temporary.WriteBytes("MODELS/GTA3.DIR", directory);
                temporary.WriteBytes("MODELS/GTA3.IMG", image);

                using (GameDataLoader loader = new GameDataLoader(
                    new WorldTestDefinition(GameId.III, SharpGTA.Data.DataSchema.III, 20),
                    new PhysicalGameFileSystem(temporary.Path)))
                {
                    loader.LoadInitialImage();
                    TxdScanReport report = loader.ScanTxd(true);
                    Assert.Equal(4, report.FileCount);
                    Assert.Equal(1, report.EmptyFileCount);
                    Assert.Equal(1, report.FailedFileCount);
                    Assert.Equal(2, report.DeclaredTextureCount);
                    Assert.Equal(1, report.SkippedTextureCount);
                    Assert.Equal(1, report.TextureCount);
                    Assert.Equal(1, report.LevelCount);
                    Assert.Equal(1, report.DecodedLevelCount);
                    Assert.Equal(16L, report.DecodedPixelCount);
                    Assert.Equal(1, report.Dxt1Count);
                    Assert.Equal(1, report.Failures.Count);
                    Assert.Equal("BAD.TXD", report.Failures[0].Name);
                    Assert.Throws<AssetFormatException>(delegate { loader.LoadTxdEntry("PS2.TXD"); });
                }
            }
        }

        private static void WriteEntry(BinaryWriter writer, uint offset, uint size, string name)
        {
            writer.Write(offset);
            writer.Write(size);
            byte[] bytes = Encoding.ASCII.GetBytes(name);
            writer.Write(bytes);
            writer.Write(new byte[24 - bytes.Length]);
        }
    }
}
