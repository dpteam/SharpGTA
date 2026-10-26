using System;
using System.IO;
using System.Text;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class DffScanTests
    {
        [Test]
        public void ScannerSeparatesValidEmptyAndMalformedEntriesAndContinues()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                byte[] valid = DffTestData.Create(0x33002u);
                byte[] directory;
                using (MemoryStream stream = new MemoryStream())
                {
                    BinaryWriter writer = new BinaryWriter(stream);
                    WriteEntry(writer, 0, 1, "GOOD.DFF");
                    WriteEntry(writer, 1, 1, "BAD.DFF");
                    WriteEntry(writer, 2, 0, "EMPTY.DFF");
                    writer.Flush();
                    directory = stream.ToArray();
                }
                byte[] image = new byte[ImgDirectory.SectorSize * 2];
                Buffer.BlockCopy(valid, 0, image, 0, valid.Length);
                byte[] malformed = new byte[12];
                malformed[0] = 0x10; // Clump with no required children.
                Buffer.BlockCopy(malformed, 0, image, ImgDirectory.SectorSize, malformed.Length);
                temporary.WriteBytes("MODELS/GTA3.DIR", directory);
                temporary.WriteBytes("MODELS/GTA3.IMG", image);

                using (GameDataLoader loader = new GameDataLoader(
                    new WorldTestDefinition(GameId.III, SharpGTA.Data.DataSchema.III, 20),
                    new PhysicalGameFileSystem(temporary.Path)))
                {
                    loader.LoadInitialImage();
                    DffScanReport report = loader.ScanDff();
                    Assert.Equal(3, report.FileCount);
                    Assert.Equal(1, report.EmptyFileCount);
                    Assert.Equal(1, report.FailedFileCount);
                    Assert.Equal(1, report.ClumpCount);
                    Assert.Equal(1, report.GeometryCount);
                    Assert.Equal(1, report.Failures.Count);
                    Assert.Equal("BAD.DFF", report.Failures[0].Name);
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
