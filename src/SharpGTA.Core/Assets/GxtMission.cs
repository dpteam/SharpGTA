namespace SharpGTA.Assets
{
    /// <summary>A 12-byte Vice City TABL directory entry.</summary>
    public sealed class GxtMissionDirectoryEntry
    {
        internal GxtMissionDirectoryEntry(string name, uint offset)
        {
            Name = name;
            Offset = offset;
        }

        public string Name { get; private set; }
        public uint Offset { get; private set; }
    }

    /// <summary>A mission table loaded from the absolute offset in a TABL entry.</summary>
    public sealed class GxtMissionTable
    {
        internal GxtMissionTable(string name, uint offset, GxtTable table)
        {
            Name = name;
            Offset = offset;
            Table = table;
        }

        public string Name { get; private set; }
        public uint Offset { get; private set; }
        public GxtTable Table { get; private set; }
    }
}
