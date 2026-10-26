using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>
    /// A GTA III/Vice City GXT archive. Main chunks are scanned until their TKEY/TDAT
    /// pair is complete, as in the games; Vice City mission chunks are then read through TABL offsets.
    /// </summary>
    public sealed class GxtArchive
    {
        public const int MaximumLength = 64 * 1024 * 1024;
        public const int MaximumMissionTables = 4096;
        public const int MaximumKeyEntries = 1000000;

        private sealed class MissionDefinition
        {
            internal GxtMissionDirectoryEntry Entry;
            internal int RawNameOffset;
        }

        private readonly GxtTable main;
        private readonly ReadOnlyCollection<GxtMissionDirectoryEntry> missionDirectory;
        private readonly ReadOnlyCollection<GxtMissionTable> missionTables;

        private GxtArchive(GxtTable main, List<GxtMissionDirectoryEntry> missionDirectory, List<GxtMissionTable> missionTables)
        {
            this.main = main;
            this.missionDirectory = missionDirectory.AsReadOnly();
            this.missionTables = missionTables.AsReadOnly();
        }

        public GxtTable Main
        {
            get { return main; }
        }

        public ReadOnlyCollection<GxtMissionDirectoryEntry> MissionDirectory
        {
            get { return missionDirectory; }
        }

        public ReadOnlyCollection<GxtMissionTable> MissionTables
        {
            get { return missionTables; }
        }

        public static GxtArchive Read(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            return FromBytes(StreamUtilities.ReadAllBytes(stream, MaximumLength));
        }

        public static GxtArchive Read(byte[] data)
        {
            return FromBytes(data);
        }

        public static GxtArchive FromBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (data.Length > MaximumLength)
                throw new AssetFormatException("GXT archive exceeds the configured limit of " + MaximumLength + " bytes.");

            GxtBlock mainKeys = null;
            GxtBlock mainData = null;
            List<MissionDefinition> definitions = new List<MissionDefinition>();
            List<GxtMissionDirectoryEntry> directory = new List<GxtMissionDirectoryEntry>();
            ByteReader reader = new ByteReader(data);

            while (mainKeys == null || mainData == null)
            {
                if (reader.Remaining == 0)
                    throw new AssetFormatException("GXT archive has no complete main TKEY/TDAT pair.", reader.Position);
                GxtBlock block = GxtBlock.Read(reader, 0, "GXT main table");
                if (block.Magic == "TABL")
                {
                    ReadMissionDirectory(data, block, definitions, directory);
                }
                else if (block.Magic == "TKEY")
                {
                    if (mainKeys != null)
                        throw new AssetFormatException("GXT main table contains duplicate TKEY chunks.", block.HeaderOffset);
                    mainKeys = block;
                }
                else if (block.Magic == "TDAT")
                {
                    if (mainData != null)
                        throw new AssetFormatException("GXT main table contains duplicate TDAT chunks.", block.HeaderOffset);
                    mainData = block;
                }
                // All other chunk types are intentionally skipped.
            }

            int totalKeyEntries = 0;
            CheckKeyEntryBudget(mainKeys, ref totalKeyEntries);
            GxtTable main = new GxtTable(data, mainKeys, mainData, "GXT main table");
            List<GxtMissionTable> missions = new List<GxtMissionTable>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                // Retail VC TABL includes MAIN, pointing directly at the main TKEY.
                // It is a directory alias, not a name-prefixed mission table.
                if (definitions[i].Entry.Offset == (uint)mainKeys.HeaderOffset)
                    continue;
                GxtMissionTable mission = ReadMission(data, definitions, i, ref totalKeyEntries);
                missions.Add(mission);
            }
            return new GxtArchive(main, directory, missions);
        }

        /// <summary>Returns the first exactly named mission table (ordinal, case-sensitive).</summary>
        public GxtMissionTable FindMission(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            for (int i = 0; i < missionTables.Count; i++)
            {
                if (string.Equals(missionTables[i].Name, name, StringComparison.Ordinal))
                    return missionTables[i];
            }
            return null;
        }

        public GxtTable LoadMission(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            GxtMissionTable selected = null;
            // VC compares strlen(storedName) bytes: stored name is a prefix of the query.
            for (int i = 0; i < missionTables.Count; i++)
            {
                if (name.StartsWith(missionTables[i].Name, StringComparison.Ordinal))
                {
                    selected = missionTables[i];
                    break;
                }
            }
            if (selected == null)
                throw new KeyNotFoundException("GXT mission table '" + name + "' was not found.");
            return selected.Table;
        }

        private static void ReadMissionDirectory(byte[] data, GxtBlock block, List<MissionDefinition> definitions, List<GxtMissionDirectoryEntry> directory)
        {
            if (block.Length % 12 != 0)
                throw new AssetFormatException("GXT TABL payload has " + (block.Length % 12) + " trailing byte(s).", block.PayloadOffset + block.Length - block.Length % 12);
            int count = block.Length / 12;
            if (count > MaximumMissionTables - definitions.Count)
                throw new AssetFormatException("GXT mission directory exceeds the configured limit of " + MaximumMissionTables + " entries.", block.HeaderOffset);

            ByteReader reader = new ByteReader(data, block.PayloadOffset, block.Length);
            for (int i = 0; i < count; i++)
            {
                int nameOffset = block.PayloadOffset + reader.Position;
                string name = reader.ReadFixedAscii(8);
                uint offset = reader.ReadUInt32();
                GxtMissionDirectoryEntry entry = new GxtMissionDirectoryEntry(name, offset);
                definitions.Add(new MissionDefinition { Entry = entry, RawNameOffset = nameOffset });
                directory.Add(entry);
            }
        }

        private static GxtMissionTable ReadMission(byte[] data, List<MissionDefinition> definitions, int index, ref int totalKeyEntries)
        {
            MissionDefinition definition = definitions[index];
            uint rawOffset = definition.Entry.Offset;
            if ((ulong)rawOffset + 8u > (ulong)data.Length)
                throw new AssetFormatException("GXT mission '" + definition.Entry.Name + "' points outside the archive.", rawOffset);
            int missionOffset = (int)rawOffset;

            int boundary = data.Length;
            for (int i = 0; i < definitions.Count; i++)
            {
                uint candidate = definitions[i].Entry.Offset;
                if (candidate > rawOffset && candidate < (uint)boundary)
                    boundary = (int)candidate;
            }
            if (missionOffset > boundary - 8)
                throw new AssetFormatException("GXT mission '" + definition.Entry.Name + "' overlaps the next mission directory offset.", rawOffset);

            for (int i = 0; i < 8; i++)
            {
                if (data[definition.RawNameOffset + i] != data[missionOffset + i])
                    throw new AssetFormatException("GXT mission '" + definition.Entry.Name + "' does not match the 8-byte table name at its TABL offset.", missionOffset);
            }

            ByteReader reader = new ByteReader(data, missionOffset + 8, boundary - missionOffset - 8);
            GxtBlock keys = null;
            GxtBlock values = null;
            string context = "GXT mission '" + definition.Entry.Name + "'";
            while (keys == null || values == null)
            {
                if (reader.Remaining == 0)
                    throw new AssetFormatException(context + " has no complete TKEY/TDAT pair.", missionOffset);
                GxtBlock block = GxtBlock.Read(reader, missionOffset + 8, context);
                if (block.Magic == "TKEY")
                {
                    if (keys != null)
                        throw new AssetFormatException(context + " contains duplicate TKEY chunks.", block.HeaderOffset);
                    keys = block;
                }
                else if (block.Magic == "TDAT")
                {
                    if (values != null)
                        throw new AssetFormatException(context + " contains duplicate TDAT chunks.", block.HeaderOffset);
                    values = block;
                }
            }

            CheckKeyEntryBudget(keys, ref totalKeyEntries);
            GxtTable table = new GxtTable(data, keys, values, context);
            return new GxtMissionTable(definition.Entry.Name, rawOffset, table);
        }

        private static void CheckKeyEntryBudget(GxtBlock keys, ref int totalKeyEntries)
        {
            int count = keys.Length / 12;
            if (count > MaximumKeyEntries - totalKeyEntries)
                throw new AssetFormatException("GXT archive exceeds the configured limit of " + MaximumKeyEntries + " total key entries.", keys.HeaderOffset);
            totalKeyEntries += count;
        }
    }
}
