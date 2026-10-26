using System;
using System.IO;
using SharpGTA.D3D9;

namespace SharpGTA.Tests
{
    public sealed class D3D9BmpWriterTests
    {
        [Test]
        public void WritesTopDownBgraBmpWithoutReorderingPixels()
        {
            byte[] pixels = {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 128
            };
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                string path = Path.Combine(temporary.Path, "image.bmp");
                BmpWriter.WriteBgra(path, 2, 2, pixels);
                byte[] bytes = File.ReadAllBytes(path);
                Assert.Equal(70, bytes.Length);
                Assert.Equal(0x4D42, ReadUInt16(bytes, 0));
                Assert.Equal(70, ReadInt32(bytes, 2));
                Assert.Equal(0, ReadInt32(bytes, 6));
                Assert.Equal(54, ReadInt32(bytes, 10));
                Assert.Equal(40, ReadInt32(bytes, 14));
                Assert.Equal(2, ReadInt32(bytes, 18));
                Assert.Equal(-2, ReadInt32(bytes, 22));
                Assert.Equal(1, ReadUInt16(bytes, 26));
                Assert.Equal(32, ReadUInt16(bytes, 28));
                Assert.Equal(0, ReadInt32(bytes, 30));
                Assert.Equal(16, ReadInt32(bytes, 34));
                Assert.Equal(2835, ReadInt32(bytes, 38));
                Assert.Equal(2835, ReadInt32(bytes, 42));
                Assert.Equal(0, ReadInt32(bytes, 46));
                Assert.Equal(0, ReadInt32(bytes, 50));
                for (int i = 0; i < pixels.Length; i++)
                    Assert.Equal(pixels[i], bytes[54 + i], "Pixel byte " + i);
            }
        }

        [Test]
        public void RejectsInvalidArgumentsAndBufferLengths()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                string path = Path.Combine(temporary.Path, "invalid.bmp");
                byte[] pixels = new byte[16];
                Assert.Throws<ArgumentNullException>(delegate { BmpWriter.WriteBgra(null, 2, 2, pixels); });
                Assert.Throws<ArgumentNullException>(delegate { BmpWriter.WriteBgra(path, 2, 2, null); });
                Assert.Throws<ArgumentOutOfRangeException>(delegate { BmpWriter.WriteBgra(path, 0, 2, pixels); });
                Assert.Throws<ArgumentOutOfRangeException>(delegate { BmpWriter.WriteBgra(path, 2, -1, pixels); });
                Assert.Throws<ArgumentException>(delegate { BmpWriter.WriteBgra(path, 2, 2, new byte[15]); });
                Assert.False(File.Exists(path));
            }
        }

        private static int ReadUInt16(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8;
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8 |
                bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        }
    }
}
