using System;
using System.IO;

namespace SharpGTA.IO
{
    public static class StreamUtilities
    {
        public static byte[] ReadAllBytes(Stream stream, int maximumLength)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (maximumLength < 0)
                throw new ArgumentOutOfRangeException("maximumLength");
            if (stream.CanSeek && stream.Length - stream.Position > maximumLength)
                throw new AssetFormatException("Data exceeds the configured limit of " + maximumLength + " bytes.");

            MemoryStream memory = new MemoryStream();
            byte[] buffer = new byte[16384];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (read < 0 || memory.Length + read > maximumLength)
                    throw new AssetFormatException("Data exceeds the configured limit of " + maximumLength + " bytes.");
                memory.Write(buffer, 0, read);
            }
            return memory.ToArray();
        }

        public static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("offset");
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of stream.");
                total += read;
            }
        }
    }
}
