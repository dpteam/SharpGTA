using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SharpGTA.Data
{
    public class RawDataRecord
    {
        private readonly IList<string> tokens;

        public RawDataRecord(string section, int lineNumber, string text, IList<string> tokens)
        {
            if (section == null)
                throw new ArgumentNullException("section");
            if (lineNumber < 0)
                throw new ArgumentOutOfRangeException("lineNumber");
            if (text == null)
                throw new ArgumentNullException("text");
            if (tokens == null)
                throw new ArgumentNullException("tokens");

            Section = section;
            LineNumber = lineNumber;
            Text = text;
            this.tokens = new List<string>(tokens).AsReadOnly();
        }

        internal RawDataRecord(string section, DataLine line)
            : this(section, line.LineNumber, line.Text, line.Tokens)
        {
        }

        public string Section { get; private set; }
        public int LineNumber { get; private set; }
        public string Text { get; private set; }

        public IList<string> Tokens
        {
            get { return tokens; }
        }

        public IList<string> RawFields
        {
            get { return tokens; }
        }
    }

    public sealed class SimpleObjectRecord : RawDataRecord
    {
        private readonly IList<float> lodDistances;

        internal SimpleObjectRecord(
            string section, DataLine line, bool isTimed, int id, string modelName, string txdName,
            int atomicCount, float[] lodDistances, uint flags, int timeOn, int timeOff)
            : base(section, line)
        {
            IsTimed = isTimed;
            Id = id;
            ModelName = modelName;
            TxdName = txdName;
            AtomicCount = atomicCount;
            this.lodDistances = Array.AsReadOnly((float[])lodDistances.Clone());
            Flags = flags;
            TimeOn = timeOn;
            TimeOff = timeOff;
        }

        public int Id { get; private set; }
        public string ModelName { get; private set; }
        public string Name { get { return ModelName; } }
        public string TxdName { get; private set; }
        public string Txd { get { return TxdName; } }
        public int AtomicCount { get; private set; }
        public int NumObjs { get { return AtomicCount; } }
        public uint Flags { get; private set; }
        public bool IsTimed { get; private set; }
        public int TimeOn { get; private set; }
        public int TimeOff { get; private set; }

        public IList<float> LodDistances
        {
            get { return lodDistances; }
        }
    }

    public sealed class VehicleRecord : RawDataRecord
    {
        internal VehicleRecord(
            DataLine line, int id, string modelName, string txdName, string vehicleType,
            string handlingId, string gameName, string animationFile, string vehicleClass,
            int frequency, int level, uint componentRules, int misc, float wheelScale)
            : base("cars", line)
        {
            Id = id;
            ModelName = modelName;
            TxdName = txdName;
            VehicleType = vehicleType;
            HandlingId = handlingId;
            GameName = gameName;
            AnimationFile = animationFile;
            VehicleClass = vehicleClass;
            Frequency = frequency;
            Level = level;
            ComponentRules = componentRules;
            Misc = misc;
            WheelScale = wheelScale;
        }

        public int Id { get; private set; }
        public string ModelName { get; private set; }
        public string Name { get { return ModelName; } }
        public string TxdName { get; private set; }
        public string Txd { get { return TxdName; } }
        public string VehicleType { get; private set; }
        public string HandlingId { get; private set; }
        public string GameName { get; private set; }
        public string AnimationFile { get; private set; }
        public string VehicleClass { get; private set; }
        public int Frequency { get; private set; }
        public int Level { get; private set; }
        public uint ComponentRules { get; private set; }
        public int Misc { get; private set; }
        public float WheelScale { get; private set; }
    }

    public sealed class PedRecord : RawDataRecord
    {
        internal PedRecord(
            DataLine line, int id, string modelName, string txdName, string pedType,
            string pedStats, string animationGroup, uint carsCanDrive, string animationFile,
            int radio1, int radio2)
            : base("peds", line)
        {
            Id = id;
            ModelName = modelName;
            TxdName = txdName;
            PedType = pedType;
            PedStats = pedStats;
            AnimationGroup = animationGroup;
            CarsCanDrive = carsCanDrive;
            AnimationFile = animationFile;
            Radio1 = radio1;
            Radio2 = radio2;
        }

        public int Id { get; private set; }
        public string ModelName { get; private set; }
        public string Name { get { return ModelName; } }
        public string TxdName { get; private set; }
        public string Txd { get { return TxdName; } }
        public string PedType { get; private set; }
        public string PedStats { get; private set; }
        public string AnimationGroup { get; private set; }
        public uint CarsCanDrive { get; private set; }
        public string AnimationFile { get; private set; }
        public int Radio1 { get; private set; }
        public int Radio2 { get; private set; }
    }

    public sealed class WeaponRecord : RawDataRecord
    {
        internal WeaponRecord(
            DataLine line, int id, string modelName, string txdName, string animationFile,
            int atomicCount, float lodDistance)
            : base("weap", line)
        {
            Id = id;
            ModelName = modelName;
            TxdName = txdName;
            AnimationFile = animationFile;
            AtomicCount = atomicCount;
            LodDistance = lodDistance;
        }

        public int Id { get; private set; }
        public string ModelName { get; private set; }
        public string Name { get { return ModelName; } }
        public string TxdName { get; private set; }
        public string Txd { get { return TxdName; } }
        public string AnimationFile { get; private set; }
        public int AtomicCount { get; private set; }
        public int NumObjs { get { return AtomicCount; } }
        public float LodDistance { get; private set; }
    }

    public sealed class IdeDocument
    {
        private readonly IList<SimpleObjectRecord> objects;
        private readonly IList<SimpleObjectRecord> timedObjects;
        private readonly IList<VehicleRecord> vehicles;
        private readonly IList<PedRecord> peds;
        private readonly IList<WeaponRecord> weapons;
        private readonly IList<RawDataRecord> rawRecords;

        internal IdeDocument(
            IList<SimpleObjectRecord> objects,
            IList<SimpleObjectRecord> timedObjects,
            IList<VehicleRecord> vehicles,
            IList<PedRecord> peds,
            IList<WeaponRecord> weapons,
            IList<RawDataRecord> rawRecords)
        {
            this.objects = new List<SimpleObjectRecord>(objects).AsReadOnly();
            this.timedObjects = new List<SimpleObjectRecord>(timedObjects).AsReadOnly();
            this.vehicles = new List<VehicleRecord>(vehicles).AsReadOnly();
            this.peds = new List<PedRecord>(peds).AsReadOnly();
            this.weapons = new List<WeaponRecord>(weapons).AsReadOnly();
            this.rawRecords = new List<RawDataRecord>(rawRecords).AsReadOnly();
        }

        public IList<SimpleObjectRecord> Objects { get { return objects; } }
        public IList<SimpleObjectRecord> TimedObjects { get { return timedObjects; } }
        public IList<VehicleRecord> Vehicles { get { return vehicles; } }
        public IList<PedRecord> Peds { get { return peds; } }
        public IList<WeaponRecord> Weapons { get { return weapons; } }
        public IList<RawDataRecord> RawRecords { get { return rawRecords; } }
    }

    public sealed class IdeReader
    {
        private readonly DataSchema schema;

        public IdeReader()
            : this(DataSchema.III)
        {
        }

        public IdeReader(DataSchema schema)
        {
            if (schema == null)
                throw new ArgumentNullException("schema");
            this.schema = schema;
        }

        public IdeDocument Read(string text)
        {
            return Read(text, "IDE");
        }

        public IdeDocument Read(string text, string sourceName)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            using (StringReader reader = new StringReader(text))
                return Read(reader, sourceName);
        }

        public IdeDocument Read(TextReader reader)
        {
            return Read(reader, "IDE");
        }

        public IdeDocument Read(TextReader reader, string sourceName)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");
            string source = string.IsNullOrEmpty(sourceName) ? "IDE" : sourceName;
            List<SimpleObjectRecord> objects = new List<SimpleObjectRecord>();
            List<SimpleObjectRecord> timedObjects = new List<SimpleObjectRecord>();
            List<VehicleRecord> vehicles = new List<VehicleRecord>();
            List<PedRecord> peds = new List<PedRecord>();
            List<WeaponRecord> weapons = new List<WeaponRecord>();
            List<RawDataRecord> rawRecords = new List<RawDataRecord>();
            DataDocumentReader documentReader = new DataDocumentReader(schema, source);
            string section = null;
            int recordCount = 0;

            foreach (DataLine line in documentReader.ReadLines(reader))
            {
                if (section == null)
                {
                    section = ReadSection(source, line);
                    continue;
                }
                if (IsEnd(line))
                {
                    section = null;
                    continue;
                }

                if (recordCount == schema.MaxRecords)
                {
                    throw DataParsing.Error(source, line, "Too many records; the maximum is " +
                        schema.MaxRecords.ToString(CultureInfo.InvariantCulture) + ".");
                }
                recordCount++;

                switch (section)
                {
                    case "objs":
                        objects.Add(ParseSimpleObject(source, line, false));
                        break;
                    case "tobj":
                        timedObjects.Add(ParseSimpleObject(source, line, true));
                        break;
                    case "cars":
                        vehicles.Add(ParseVehicle(source, line));
                        break;
                    case "peds":
                        peds.Add(ParsePed(source, line));
                        break;
                    case "weap":
                        weapons.Add(ParseWeapon(source, line));
                        break;
                    default:
                        rawRecords.Add(new RawDataRecord(section, line));
                        break;
                }
            }
            return new IdeDocument(objects, timedObjects, vehicles, peds, weapons, rawRecords);
        }

        private string ReadSection(string sourceName, DataLine line)
        {
            if (line.Tokens.Count != 1)
                throw DataParsing.Error(sourceName, line, "Unknown section '" + line.Text.Trim() + "'.");

            string section = line.Tokens[0].ToLowerInvariant();
            switch (section)
            {
                case "objs":
                case "tobj":
                case "hier":
                case "cars":
                case "peds":
                case "path":
                case "2dfx":
                    return section;
                case "weap":
                    if (!schema.SupportsWeaponSection)
                        throw DataParsing.Error(sourceName, line, "Section 'weap' is not supported by this data schema.");
                    return section;
                default:
                    throw DataParsing.Error(sourceName, line, "Unknown section '" + line.Tokens[0] + "'.");
            }
        }

        private static bool IsEnd(DataLine line)
        {
            return line.Tokens.Count == 1 && string.Equals(line.Tokens[0], "end", StringComparison.OrdinalIgnoreCase);
        }

        private static SimpleObjectRecord ParseSimpleObject(string sourceName, DataLine line, bool timed)
        {
            string layout = timed ? "tobj" : "objs";
            if (line.Tokens.Count < 4)
            {
                throw DataParsing.Error(sourceName, line, "Expected at least 4 fields for " + layout +
                    ", but got " + line.Tokens.Count.ToString(CultureInfo.InvariantCulture) + ".");
            }

            int id = DataParsing.ParseInt32(sourceName, line, 0, "id");
            int atomicCount = DataParsing.ParseInt32(sourceName, line, 3, "numObjs");
            if (atomicCount < 1 || atomicCount > 3)
                throw DataParsing.Error(sourceName, line, "numObjs must be between 1 and 3.");

            int expected = 5 + atomicCount + (timed ? 2 : 0);
            DataParsing.RequireCount(sourceName, line, expected, layout + " with numObjs=" +
                atomicCount.ToString(CultureInfo.InvariantCulture));
            float[] distances = new float[atomicCount];
            for (int i = 0; i < distances.Length; i++)
                distances[i] = DataParsing.ParseSingle(sourceName, line, 4 + i, "lodDistances[" + i.ToString(CultureInfo.InvariantCulture) + "]");

            int flagsIndex = 4 + atomicCount;
            uint flags = DataParsing.ParseUInt32(sourceName, line, flagsIndex, "flags");
            int timeOn = -1;
            int timeOff = -1;
            if (timed)
            {
                timeOn = DataParsing.ParseInt32(sourceName, line, flagsIndex + 1, "timeOn");
                timeOff = DataParsing.ParseInt32(sourceName, line, flagsIndex + 2, "timeOff");
            }
            return new SimpleObjectRecord(layout, line, timed, id, line.Tokens[1], line.Tokens[2],
                atomicCount, distances, flags, timeOn, timeOff);
        }

        private VehicleRecord ParseVehicle(string sourceName, DataLine line)
        {
            string vehicleType = line.Tokens.Count > 3 ? line.Tokens[3] : "";
            int baseCount = schema.VehicleHasAnimFile ? 11 : 10;
            int expected;
            switch (vehicleType)
            {
                case "car":
                case "bike":
                    expected = baseCount + 2; // misc (wheel/steer) and wheelScale.
                    break;
                case "plane":
                    expected = baseCount + 1; // misc is the LOD model ID.
                    break;
                case "boat":
                case "train":
                case "heli":
                    expected = baseCount;
                    break;
                default:
                    throw DataParsing.Error(sourceName, line, "Unknown vehicle type '" + vehicleType + "'.");
            }
            DataParsing.RequireCount(sourceName, line, expected,
                (schema.VehicleHasAnimFile ? "VC " : "III ") + vehicleType + " cars");
            int id = DataParsing.ParseInt32(sourceName, line, 0, "id");
            string animationFile = schema.VehicleHasAnimFile ? line.Tokens[6] : null;
            int classIndex = schema.VehicleHasAnimFile ? 7 : 6;
            int numericIndex = classIndex + 1;
            int frequency = DataParsing.ParseInt32(sourceName, line, numericIndex, "frequency");
            int level = DataParsing.ParseInt32(sourceName, line, numericIndex + 1, "level");
            uint componentRules = DataParsing.ParseUInt32Hex(sourceName, line, numericIndex + 2, "compRules");
            int misc = expected > baseCount ? DataParsing.ParseInt32(sourceName, line, numericIndex + 3, "misc") : 0;
            float wheelScale = expected > baseCount + 1 ? DataParsing.ParseSingle(sourceName, line, numericIndex + 4, "wheelScale") : 1.0f;

            return new VehicleRecord(line, id, line.Tokens[1], line.Tokens[2], vehicleType,
                line.Tokens[4], line.Tokens[5], animationFile, line.Tokens[classIndex], frequency,
                level, componentRules, misc, wheelScale);
        }

        private PedRecord ParsePed(string sourceName, DataLine line)
        {
            int expected = schema.PedHasAnimAndRadio ? 10 : 7;
            DataParsing.RequireCount(sourceName, line, expected, schema.PedHasAnimAndRadio ? "VC peds" : "III peds");
            int id = DataParsing.ParseInt32(sourceName, line, 0, "id");
            uint carsCanDrive = DataParsing.ParseUInt32Hex(sourceName, line, 6, "carsCanDrive");
            string animationFile = schema.PedHasAnimAndRadio ? line.Tokens[7] : null;
            int radio1 = -1;
            int radio2 = -1;
            if (schema.PedHasAnimAndRadio)
            {
                radio1 = DataParsing.ParseInt32(sourceName, line, 8, "radio1");
                radio2 = DataParsing.ParseInt32(sourceName, line, 9, "radio2");
            }
            return new PedRecord(line, id, line.Tokens[1], line.Tokens[2], line.Tokens[3],
                line.Tokens[4], line.Tokens[5], carsCanDrive, animationFile, radio1, radio2);
        }

        private static WeaponRecord ParseWeapon(string sourceName, DataLine line)
        {
            if (line.Tokens.Count != 6 && line.Tokens.Count != 7)
                throw DataParsing.Error(sourceName, line, "Expected 6 or 7 fields for VC weap, but got " +
                    line.Tokens.Count.ToString(CultureInfo.InvariantCulture) + ". The optional trailing field is preserved in RawFields.");
            int id = DataParsing.ParseInt32(sourceName, line, 0, "id");
            int atomicCount = DataParsing.ParseInt32(sourceName, line, 4, "numObjs");
            if (atomicCount != 1)
                throw DataParsing.Error(sourceName, line, "VC weap numObjs must be 1.");
            float lodDistance = DataParsing.ParseSingle(sourceName, line, 5, "lodDistance");
            return new WeaponRecord(line, id, line.Tokens[1], line.Tokens[2], line.Tokens[3],
                atomicCount, lodDistance);
        }
    }
}
