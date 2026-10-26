using System;
using System.IO;

namespace SharpGTA.D3D9
{
    internal static class BmpWriter
    {
        internal static void WriteBgra(string path, int width, int height, byte[] bgra)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if (bgra == null)
                throw new ArgumentNullException("bgra");
            int stride = checked(width * 4);
            int pixelBytes = checked(stride * height);
            if (bgra.Length != pixelBytes)
                throw new ArgumentException("BGRA data length does not match the image size.", "bgra");
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0x4D42);
                writer.Write(checked(54 + pixelBytes));
                writer.Write(0);
                writer.Write(54);
                writer.Write(40);
                writer.Write(width);
                writer.Write(-height); // Top-down image, already in presentation order.
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write(pixelBytes);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);
                writer.Write(bgra);
            }
        }
    }
}
