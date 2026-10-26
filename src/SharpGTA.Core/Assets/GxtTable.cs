using System;
using System.Collections.Generic;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>A matched TKEY/TDAT table. Lookup keys are ordinal and case-sensitive.</summary>
    public sealed class GxtTable
    {
        private sealed class ValueEntry
        {
            internal int Offset;
            internal int Length;
        }

        private readonly byte[] buffer;
        private readonly int dataOffset;
        private readonly int dataLength;
        private readonly List<ValueEntry> values;
        private readonly Dictionary<string, int> keyIndices;

        internal GxtTable(byte[] buffer, GxtBlock keyBlock, GxtBlock dataBlock, string tableName)
        {
            if (keyBlock.Length % 12 != 0)
                throw new AssetFormatException(tableName + " TKEY payload has " + (keyBlock.Length % 12) + " trailing byte(s).", keyBlock.PayloadOffset + keyBlock.Length - keyBlock.Length % 12);
            if (dataBlock.Length % 2 != 0)
                throw new AssetFormatException(tableName + " TDAT payload has an odd byte length.", dataBlock.PayloadOffset + dataBlock.Length - 1);

            this.buffer = buffer;
            dataOffset = dataBlock.PayloadOffset;
            dataLength = dataBlock.Length;
            int count = keyBlock.Length / 12;
            values = new List<ValueEntry>(count);
            keyIndices = new Dictionary<string, int>(count, StringComparer.Ordinal);

            ByteReader keys = new ByteReader(buffer, keyBlock.PayloadOffset, keyBlock.Length);
            for (int i = 0; i < count; i++)
            {
                int entryOffset = keyBlock.PayloadOffset + keys.Position;
                uint valueOffset = keys.ReadUInt32();
                string key = keys.ReadFixedAscii(8);
                ValueEntry value = ReadValue(valueOffset, key, tableName, entryOffset);
                if (!keyIndices.ContainsKey(key))
                    keyIndices.Add(key, values.Count);
                values.Add(value);
            }
        }

        public int Count
        {
            get { return values.Count; }
        }

        /// <summary>
        /// Maps each stored 16-bit code unit directly to a System.Char. This deliberately
        /// performs no Unicode decoding or game-codepage conversion.
        /// </summary>
        public bool TryGet(string key, out string value)
        {
            ushort[] codeUnits;
            if (!TryGetCodeUnits(key, out codeUnits))
            {
                value = null;
                return false;
            }

            char[] characters = new char[codeUnits.Length];
            for (int i = 0; i < codeUnits.Length; i++)
                characters[i] = (char)codeUnits[i];
            value = new string(characters);
            return true;
        }

        /// <summary>Returns a copy of the NUL-terminated value, excluding its terminator.</summary>
        public bool TryGetCodeUnits(string key, out ushort[] value)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            int index;
            if (!keyIndices.TryGetValue(key, out index))
            {
                value = null;
                return false;
            }

            ValueEntry entry = values[index];
            ushort[] result = new ushort[entry.Length];
            int position = dataOffset + entry.Offset;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (ushort)(buffer[position] | (buffer[position + 1] << 8));
                position += 2;
            }
            value = result;
            return true;
        }

        private ValueEntry ReadValue(uint valueOffset, string key, string tableName, int keyEntryOffset)
        {
            if ((valueOffset & 1u) != 0 || (ulong)valueOffset + 2u > (ulong)dataLength)
                throw new AssetFormatException(tableName + " key '" + key + "' has an out-of-bounds or odd TDAT offset " + valueOffset + ".", keyEntryOffset);

            int start = dataOffset + (int)valueOffset;
            int end = dataOffset + dataLength;
            int position = start;
            while (position < end && (buffer[position] != 0 || buffer[position + 1] != 0))
                position += 2;
            if (position == end)
                throw new AssetFormatException(tableName + " key '" + key + "' has no NUL terminator within TDAT.", start);

            ValueEntry entry = new ValueEntry();
            entry.Offset = (int)valueOffset;
            entry.Length = (position - start) / 2;
            return entry;
        }
    }
}
