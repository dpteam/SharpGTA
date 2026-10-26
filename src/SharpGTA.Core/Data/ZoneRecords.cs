using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Data
{
    public enum ZoneKind
    {
        Default = 0,
        Navig = 1,
        Info = 2,
        MapZone = 3
    }

    public sealed class ZoneRecord
    {
        internal ZoneRecord(int lineNumber, string name, ZoneKind kind, BoundingBox bounds, int rawLevel)
        {
            LineNumber = lineNumber;
            Name = name;
            Kind = kind;
            Bounds = bounds;
            RawLevel = rawLevel;
            ChildIndex = -1;
            ParentIndex = -1;
            NextIndex = -1;
        }

        public int LineNumber { get; private set; }
        public string Name { get; private set; }
        public ZoneKind Kind { get; private set; }
        public BoundingBox Bounds { get; private set; }

        /// <summary>
        /// A numeric level ID in the owning game profile, not a shared III/VC enum.
        /// Both PC profiles use 0 for LEVEL_GENERIC; other values are profile-local.
        /// </summary>
        public int RawLevel { get; private set; }

        /// <summary>Indices in the containing navigation/normal-zone array; -1 means null.</summary>
        public int ChildIndex { get; internal set; }
        public int ParentIndex { get; internal set; }
        public int NextIndex { get; internal set; }
    }

    public sealed class CullZoneRecord
    {
        internal CullZoneRecord(int lineNumber, Vector3 position, BoundingBox bounds,
            int flags, int wantedLevelDrop)
        {
            LineNumber = lineNumber;
            Position = position;
            Bounds = bounds;
            Flags = flags;
            WantedLevelDrop = wantedLevelDrop;
        }

        public int LineNumber { get; private set; }
        public Vector3 Position { get; private set; }
        public BoundingBox Bounds { get; private set; }
        public int Flags { get; private set; }
        public int WantedLevelDrop { get; private set; }
    }

    public sealed class OcclusionVolumeRecord
    {
        internal OcclusionVolumeRecord(int lineNumber, Vector3 positionBeforeCenter,
            Vector3 center, float width, float length, float height,
            float angleDegrees, ushort quantizedAngle)
        {
            LineNumber = lineNumber;
            PositionBeforeCenter = positionBeforeCenter;
            Center = center;
            Width = width;
            Length = length;
            Height = height;
            AngleDegrees = angleDegrees;
            QuantizedAngle = quantizedAngle;
        }

        public int LineNumber { get; private set; }

        /// <summary>The original IPL position, before FileLoader adds half the height to Z.</summary>
        public Vector3 PositionBeforeCenter { get; private set; }
        public float RawX { get { return PositionBeforeCenter.X; } }
        public float RawY { get { return PositionBeforeCenter.Y; } }
        public float RawZ { get { return PositionBeforeCenter.Z; } }
        public Vector3 Center { get; private set; }
        public float Width { get; private set; }
        public float Length { get; private set; }
        public float Height { get; private set; }
        public float AngleDegrees { get; private set; }
        public ushort QuantizedAngle { get; private set; }
    }

    public static class ZoneRecordParser
    {
        public static ZoneRecord ParseZone(RawDataRecord record)
        {
            DataLine line = GetLine(record);
            string sourceName = GetSourceName(record);
            DataParsing.RequireCount(sourceName, line, 9, "zone");

            string name = NormalizeName(sourceName, line, line.Tokens[0]);
            int rawKind = DataParsing.ParseInt32(sourceName, line, 1, "zone type");
            if (rawKind < (int)ZoneKind.Default || rawKind > (int)ZoneKind.MapZone)
            {
                throw DataParsing.Error(sourceName, line,
                    "Unknown zone type " + line.Tokens[1] + "; expected a value from 0 through 3.");
            }

            Vector3 first = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 2, "min.x"),
                DataParsing.ParseSingle(sourceName, line, 3, "min.y"),
                DataParsing.ParseSingle(sourceName, line, 4, "min.z"));
            Vector3 second = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 5, "max.x"),
                DataParsing.ParseSingle(sourceName, line, 6, "max.y"),
                DataParsing.ParseSingle(sourceName, line, 7, "max.z"));
            int rawLevel = DataParsing.ParseInt32(sourceName, line, 8, "level");

            // CTheZones::CreateZone swaps each reversed pair independently.
            BoundingBox bounds = new BoundingBox(Vector3.Min(first, second), Vector3.Max(first, second));
            return new ZoneRecord(line.LineNumber, name, (ZoneKind)rawKind, bounds, rawLevel);
        }

        public static CullZoneRecord ParseCull(RawDataRecord record)
        {
            DataLine line = GetLine(record);
            string sourceName = GetSourceName(record);
            DataParsing.RequireCount(sourceName, line, 11, "cull");

            Vector3 position = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 0, "position.x"),
                DataParsing.ParseSingle(sourceName, line, 1, "position.y"),
                DataParsing.ParseSingle(sourceName, line, 2, "position.z"));
            Vector3 minimum = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 3, "min.x"),
                DataParsing.ParseSingle(sourceName, line, 4, "min.y"),
                DataParsing.ParseSingle(sourceName, line, 5, "min.z"));
            Vector3 maximum = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 6, "max.x"),
                DataParsing.ParseSingle(sourceName, line, 7, "max.y"),
                DataParsing.ParseSingle(sourceName, line, 8, "max.z"));
            int flags = DataParsing.ParseInt32(sourceName, line, 9, "flags");
            int wantedLevelDrop = DataParsing.ParseInt32(sourceName, line, 10, "wanted level drop");

            // Unlike CreateZone, AddCullZone does not reorder its input bounds.
            return new CullZoneRecord(line.LineNumber, position,
                new BoundingBox(minimum, maximum), flags, wantedLevelDrop);
        }

        public static OcclusionVolumeRecord ParseOcclusion(RawDataRecord record)
        {
            DataLine line = GetLine(record);
            string sourceName = GetSourceName(record);
            DataParsing.RequireCount(sourceName, line, 7, "occl");

            Vector3 originalPosition = new Vector3(
                DataParsing.ParseSingle(sourceName, line, 0, "position.x"),
                DataParsing.ParseSingle(sourceName, line, 1, "position.y"),
                DataParsing.ParseSingle(sourceName, line, 2, "position.z"));
            float width = DataParsing.ParseSingle(sourceName, line, 3, "width");
            float length = DataParsing.ParseSingle(sourceName, line, 4, "length");
            float height = DataParsing.ParseSingle(sourceName, line, 5, "height");
            float angle = DataParsing.ParseSingle(sourceName, line, 6, "angle");
            float normalizedAngle = NormalizeAngle(angle);
            ushort quantizedAngle = (ushort)(normalizedAngle / 360.0f * 65535.0f);
            Vector3 center = new Vector3(originalPosition.X, originalPosition.Y,
                originalPosition.Z + height / 2.0f);

            return new OcclusionVolumeRecord(line.LineNumber, originalPosition, center,
                width, length, height, normalizedAngle, quantizedAngle);
        }

        private static DataLine GetLine(RawDataRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");
            return new DataLine(record.LineNumber, string.Empty, record.Tokens);
        }

        private static string GetSourceName(RawDataRecord record)
        {
            return string.IsNullOrEmpty(record.Section) ? "IPL" : record.Section;
        }

        private static string NormalizeName(string sourceName, DataLine line, string name)
        {
            int length = Math.Min(name.Length, 7);
            char[] normalized = new char[length];
            for (int i = 0; i < name.Length; i++)
            {
                char character = name[i];
                if (character > 0x7f)
                {
                    throw DataParsing.Error(sourceName, line,
                        "Zone names must contain ASCII characters only.");
                }
                if (i < length)
                {
                    if (character >= 'a' && character <= 'z')
                        character = (char)(character - 'a' + 'A');
                    normalized[i] = character;
                }
            }
            return new string(normalized);
        }

        private static float NormalizeAngle(float angle)
        {
            // COcclusion::AddOne keeps 360 (and positive exact multiples reduce to
            // 360), while negative exact multiples reduce to zero. Remainder avoids
            // unbounded loops for malformed but finite, very large input angles.
            float normalized = angle % 360.0f;
            if (normalized < 0.0f)
                normalized += 360.0f;
            if (normalized == 0.0f)
                return angle > 0.0f ? 360.0f : 0.0f;
            return normalized;
        }
    }
}
