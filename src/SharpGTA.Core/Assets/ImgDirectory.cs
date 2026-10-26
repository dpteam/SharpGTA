using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>A parsed GTA III/Vice City PC DIR file.</summary>
    public sealed class ImgDirectory
    {
        public const int SectorSize = 2048;
        public const int EntrySize = 32;
        public const int MaximumLength = 64 * 1024 * 1024;

        private readonly ReadOnlyCollection<ImgEntry> entries;

        private ImgDirectory(List<ImgEntry> entries)
        {
            this.entries = entries.AsReadOnly();
        }

        public ReadOnlyCollection<ImgEntry> Entries
        {
            get { return entries; }
        }

        public static ImgDirectory Read(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            return FromBytes(StreamUtilities.ReadAllBytes(stream, MaximumLength));
        }

        public static ImgDirectory FromBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (data.Length > MaximumLength)
                throw new AssetFormatException("IMG directory exceeds the configured limit of " + MaximumLength + " bytes.");
            if (data.Length % EntrySize != 0)
                throw new AssetFormatException("IMG directory has " + (data.Length % EntrySize) + " trailing byte(s).", data.Length - data.Length % EntrySize);

            List<ImgEntry> entries = new List<ImgEntry>(data.Length / EntrySize);
            ByteReader reader = new ByteReader(data);
            while (reader.Remaining != 0)
            {
                uint rawOffset = reader.ReadUInt32();
                uint sizeSectors = reader.ReadUInt32();
                string name = reader.ReadFixedAscii(24);
                entries.Add(new ImgEntry(rawOffset, sizeSectors, name));
            }
            return new ImgDirectory(entries);
        }

        /// <summary>Finds the first entry using ASCII-only, case-insensitive name comparison.</summary>
        public ImgEntry Find(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            for (int i = 0; i < entries.Count; i++)
            {
                if (ImgNameEquals(entries[i].Name, name))
                    return entries[i];
            }
            return null;
        }

        internal static bool ImgNameEquals(string left, string right)
        {
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                char a = left[i];
                char b = right[i];
                if (a >= 'a' && a <= 'z')
                    a = (char)(a - ('a' - 'A'));
                if (b >= 'a' && b <= 'z')
                    b = (char)(b - ('a' - 'A'));
                if (a != b)
                    return false;
            }
            return true;
        }
    }
}
