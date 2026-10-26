using System;
using System.Globalization;
using System.Text;

namespace SharpGTA.Runtime
{
    /// <summary>Order-sensitive FNV-1a state digest for regression testing, not security.</summary>
    public sealed class StateWriter
    {
        private ulong hash = 14695981039346656037UL;

        public ulong Digest
        {
            get { return hash; }
        }

        public string ToHex()
        {
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public void WriteByte(byte value)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        public void WriteBytes(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException("bytes");
            for (int i = 0; i < bytes.Length; i++)
                WriteByte(bytes[i]);
        }

        public void WriteUInt32(uint value)
        {
            WriteByte((byte)value);
            WriteByte((byte)(value >> 8));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 24));
        }

        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)value);
            WriteUInt32((uint)(value >> 32));
        }

        public void WriteInt64(long value)
        {
            WriteUInt64(unchecked((ulong)value));
        }

        public void WriteSingle(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            WriteBytes(bytes);
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteInt32(-1);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt32(bytes.Length);
            WriteBytes(bytes);
        }
    }
}
