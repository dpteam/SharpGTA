using System;
using System.Text;

namespace SharpGTA.IO
{
    /// <summary>Bounds-checked little-endian reader over an immutable byte range.</summary>
    public sealed class ByteReader
    {
        private readonly byte[] buffer;
        private readonly int start;
        private readonly int end;
        private int position;

        public ByteReader(byte[] buffer)
            : this(buffer, 0, buffer == null ? 0 : buffer.Length)
        {
        }

        public ByteReader(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(offset < 0 ? "offset" : "count");
            this.buffer = buffer;
            start = offset;
            position = offset;
            end = offset + count;
        }

        public int Position
        {
            get { return position - start; }
        }

        public int Length
        {
            get { return end - start; }
        }

        public int Remaining
        {
            get { return end - position; }
        }

        public byte ReadByte()
        {
            Require(1);
            return buffer[position++];
        }

        public short ReadInt16()
        {
            return unchecked((short)ReadUInt16());
        }

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(buffer[position] | (buffer[position + 1] << 8));
            position += 2;
            return value;
        }

        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(buffer[position] |
                (buffer[position + 1] << 8) |
                (buffer[position + 2] << 16) |
                (buffer[position + 3] << 24));
            position += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            ulong low = ReadUInt32();
            return low | ((ulong)ReadUInt32() << 32);
        }

        public float ReadSingle()
        {
            Require(4);
            float value;
            if (BitConverter.IsLittleEndian)
            {
                value = BitConverter.ToSingle(buffer, position);
            }
            else
            {
                byte[] bytes = { buffer[position + 3], buffer[position + 2], buffer[position + 1], buffer[position] };
                value = BitConverter.ToSingle(bytes, 0);
            }
            position += 4;
            return value;
        }

        public byte[] ReadBytes(int count)
        {
            Require(count);
            byte[] result = new byte[count];
            Buffer.BlockCopy(buffer, position, result, 0, count);
            position += count;
            return result;
        }

        public ByteReader ReadSlice(int count)
        {
            Require(count);
            ByteReader result = new ByteReader(buffer, position, count);
            position += count;
            return result;
        }

        public string ReadFixedAscii(int count)
        {
            return Ascii.GetString(ReadBytes(count));
        }

        public string ReadFourCc()
        {
            return Ascii.GetRawString(ReadBytes(4));
        }

        public void Skip(int count)
        {
            Require(count);
            position += count;
        }

        public void Seek(int relativePosition)
        {
            if (relativePosition < 0 || relativePosition > end - start)
                throw new ArgumentOutOfRangeException("relativePosition");
            position = start + relativePosition;
        }

        public void Require(int count)
        {
            if (count < 0 || count > end - position)
                throw new AssetFormatException("Unexpected end of data; required " + count + " bytes, only " + (end - position) + " remain.", position);
        }
    }

    public static class Ascii
    {
        public static string GetString(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            int length = 0;
            while (length < bytes.Length && bytes[length] != 0)
                length++;
            return GetRawString(bytes, 0, length);
        }

        public static string GetRawString(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            return GetRawString(bytes, 0, bytes.Length);
        }

        private static string GetRawString(byte[] bytes, int offset, int count)
        {
            char[] chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[offset + i];
                chars[i] = value <= 0x7F ? (char)value : '?';
            }
            return new string(chars);
        }

        public static byte[] GetBytes(string value)
        {
            if (value == null)
                throw new ArgumentNullException("value");
            byte[] bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c > 0x7F)
                    throw new ArgumentException("Only ASCII is supported.", "value");
                bytes[i] = (byte)c;
            }
            return bytes;
        }
    }
}
