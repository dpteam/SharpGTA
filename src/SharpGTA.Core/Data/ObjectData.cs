using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Data
{
    /// <summary>One object.dat row; the numeric columns mirror CObjectInfo.</summary>
    public sealed class ObjectDataEntry
    {
        internal ObjectDataEntry(string name, float mass, float turnMass, float airResistance,
            float elasticity, float percentSubmerged, float uprootLimit,
            float collisionDamageMultiplier, int damageEffect, int responseCase, int cameraAvoid)
        {
            Name = name;
            Mass = mass;
            TurnMass = turnMass;
            AirResistance = airResistance;
            Elasticity = elasticity;
            PercentSubmerged = percentSubmerged;
            UprootLimit = uprootLimit;
            CollisionDamageMultiplier = collisionDamageMultiplier;
            DamageEffect = damageEffect;
            ResponseCase = responseCase;
            CameraAvoid = cameraAvoid;
        }

        public string Name { get; private set; }
        public float Mass { get; private set; }
        public float TurnMass { get; private set; }
        public float AirResistance { get; private set; }
        public float Elasticity { get; private set; }
        public float PercentSubmerged { get; private set; }
        public float UprootLimit { get; private set; }
        public float CollisionDamageMultiplier { get; private set; }
        public int DamageEffect { get; private set; }
        public int ResponseCase { get; private set; }
        public int CameraAvoid { get; private set; }
    }

    /// <summary>
    /// Parsed OBJECT.DAT. Membership alone matters for map loading: a model listed here is
    /// created as a CDummyObject instead of a CBuilding, exactly like CObjectData::LoadObjectData
    /// assigning object IDs before the IPLs are read. Entries are unique per model name, while
    /// the original hands out an object-info slot per row, so index-derived IDs would differ
    /// for the few repeated rows; nothing consumes IDs yet.
    /// </summary>
    public sealed class ObjectData
    {
        private readonly Dictionary<string, ObjectDataEntry> entries;
        private readonly List<ObjectDataEntry> ordered;

        public ObjectData()
        {
            entries = new Dictionary<string, ObjectDataEntry>(StringComparer.OrdinalIgnoreCase);
            ordered = new List<ObjectDataEntry>();
        }

        public int Count { get { return ordered.Count; } }
        public IList<ObjectDataEntry> Entries { get { return ordered.AsReadOnly(); } }

        public bool Contains(string modelName)
        {
            return modelName != null && entries.ContainsKey(modelName);
        }

        public bool TryGet(string modelName, out ObjectDataEntry entry)
        {
            if (modelName == null)
            {
                entry = null;
                return false;
            }
            return entries.TryGetValue(modelName, out entry);
        }

        internal void Add(ObjectDataEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");
            ObjectDataEntry existing;
            if (entries.TryGetValue(entry.Name, out existing))
            {
                // Retail OBJECT.DAT repeats a few models; the original assigns a new object ID
                // on every row, so the last row's values win. Membership is unchanged.
                entries[entry.Name] = entry;
                int index = ordered.IndexOf(existing);
                if (index >= 0)
                    ordered[index] = entry;
                return;
            }
            entries.Add(entry.Name, entry);
            ordered.Add(entry);
        }
    }

    /// <summary>Reads OBJECT.DAT rows: name plus ten numeric CObjectInfo columns.</summary>
    public sealed class ObjectDataReader
    {
        private const int FieldCount = 11;

        public ObjectData Read(string text)
        {
            return Read(text, "OBJECT.DAT");
        }

        /// <summary>
        /// Reads OBJECT.DAT and records malformed rows as warnings instead of failing the
        /// whole load: the original's sscanf keeps going with whatever it parsed, so a modded
        /// file must not abort map loading.
        /// </summary>
        public ObjectData Read(string text, string sourceName, LoadReport report)
        {
            if (report == null)
                throw new ArgumentNullException("report");
            return ReadCore(text, sourceName, report);
        }

        public ObjectData Read(string text, string sourceName)
        {
            return ReadCore(text, sourceName, null);
        }

        private ObjectData ReadCore(string text, string sourceName, LoadReport report)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            ObjectData data = new ObjectData();
            using (System.IO.StringReader reader = new System.IO.StringReader(text))
            {
                string line;
                int lineNumber = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    // A comment starts anywhere on the line; the original skips to the next
                    // newline, so trailing text after ';' never reaches sscanf.
                    int comment = line.IndexOf(';');
                    string trimmed = (comment >= 0 ? line.Substring(0, comment) : line).Trim();
                    if (lineNumber == 1 && trimmed.Length > 0 && trimmed[0] == '\uFEFF')
                        trimmed = trimmed.Substring(1).TrimStart();
                    if (trimmed.Length == 0)
                        continue;
                    // The original stops at the '*' sentinel (in OBJECT.DAT it is literally
                    // "* ;end of file"), so nothing after it is parsed.
                    if (trimmed[0] == '*')
                        break;
                    string[] parts = Split(trimmed);
                    if (parts.Length < FieldCount)
                    {
                        string message = sourceName + " line " +
                            lineNumber.ToString(CultureInfo.InvariantCulture) + " has " +
                            parts.Length.ToString(CultureInfo.InvariantCulture) +
                            " fields; expected at least " + FieldCount.ToString(CultureInfo.InvariantCulture) + ".";
                        if (report != null)
                        {
                            report.Add(LoadIssueSeverity.Warning, "ObjectData", sourceName, lineNumber, message);
                            continue;
                        }
                        throw new AssetFormatException(message);
                    }
                    // The original reads the first eleven columns with sscanf and ignores any
                    // trailing ones, which VC's OBJECT.DAT uses.
                    data.Add(new ObjectDataEntry(parts[0],
                        ParseSingle(parts[1], sourceName, lineNumber),
                        ParseSingle(parts[2], sourceName, lineNumber),
                        ParseSingle(parts[3], sourceName, lineNumber),
                        ParseSingle(parts[4], sourceName, lineNumber),
                        ParseSingle(parts[5], sourceName, lineNumber),
                        ParseSingle(parts[6], sourceName, lineNumber),
                        ParseSingle(parts[7], sourceName, lineNumber),
                        ParseInt32(parts[8], sourceName, lineNumber),
                        ParseInt32(parts[9], sourceName, lineNumber),
                        ParseInt32(parts[10], sourceName, lineNumber)));
                }
            }
            return data;
        }

        private static string[] Split(string line)
        {
            List<string> tokens = new List<string>(FieldCount);
            StringBuilder current = new StringBuilder(24);
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ' ' || c == '\t' || c == ',')
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
                tokens.Add(current.ToString());
            return tokens.ToArray();
        }

        private static float ParseSingle(string token, string sourceName, int lineNumber)
        {
            float value;
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new AssetFormatException(sourceName + " line " +
                    lineNumber.ToString(CultureInfo.InvariantCulture) +
                    " has invalid number '" + token + "'.");
            }
            return value;
        }

        private static int ParseInt32(string token, string sourceName, int lineNumber)
        {
            int value;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new AssetFormatException(sourceName + " line " +
                    lineNumber.ToString(CultureInfo.InvariantCulture) +
                    " has invalid integer '" + token + "'.");
            }
            return value;
        }
    }
}