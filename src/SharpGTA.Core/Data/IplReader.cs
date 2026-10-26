using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Data
{
    public sealed class ObjectInstanceRecord : RawDataRecord
    {
        internal ObjectInstanceRecord(
            DataLine line, int id, string modelName, Vector3 position, Vector3 scale,
            Vector3 axis, float quaternionW, bool hasArea, float area)
            : base("inst", line)
        {
            Id = id;
            ModelName = modelName;
            Position = position;
            Scale = scale;
            Axis = axis;
            QuaternionW = quaternionW;
            HasArea = hasArea;
            Area = area;

            Quaternion rotation = Quaternion.Identity;
            rotation.X = axis.X;
            rotation.Y = axis.Y;
            rotation.Z = axis.Z;
            rotation.W = quaternionW;
            RawRotation = rotation;
        }

        public int Id { get; private set; }
        public string ModelName { get; private set; }
        public string Name { get { return ModelName; } }
        public Vector3 Position { get; private set; }
        public Vector3 Scale { get; private set; }
        public Vector3 Axis { get; private set; }
        public Vector3 RotationAxis { get { return Axis; } }
        public float QuaternionW { get; private set; }
        /// <summary>Raw axis components and w as stored; not a normalized quaternion.</summary>
        public Quaternion RawRotation { get; private set; }
        public bool HasArea { get; private set; }
        public float Area { get; private set; }
    }

    public sealed class IplDocument
    {
        private readonly IList<ObjectInstanceRecord> instances;
        private readonly IList<RawDataRecord> rawRecords;

        internal IplDocument(IList<ObjectInstanceRecord> instances, IList<RawDataRecord> rawRecords)
        {
            this.instances = new List<ObjectInstanceRecord>(instances).AsReadOnly();
            this.rawRecords = new List<RawDataRecord>(rawRecords).AsReadOnly();
        }

        public IList<ObjectInstanceRecord> Instances { get { return instances; } }
        public IList<RawDataRecord> RawRecords { get { return rawRecords; } }
    }

    public sealed class IplReader
    {
        private readonly DataSchema schema;

        public IplReader()
            : this(DataSchema.III)
        {
        }

        public IplReader(DataSchema schema)
        {
            if (schema == null)
                throw new ArgumentNullException("schema");
            this.schema = schema;
        }

        public IplDocument Read(string text)
        {
            return Read(text, "IPL");
        }

        public IplDocument Read(string text, string sourceName)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            using (StringReader reader = new StringReader(text))
                return Read(reader, sourceName);
        }

        public IplDocument Read(TextReader reader)
        {
            return Read(reader, "IPL");
        }

        public IplDocument Read(TextReader reader, string sourceName)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");
            string source = string.IsNullOrEmpty(sourceName) ? "IPL" : sourceName;
            List<ObjectInstanceRecord> instances = new List<ObjectInstanceRecord>();
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

                if (section == "inst")
                    instances.Add(ParseInstance(source, line));
                else
                    rawRecords.Add(new RawDataRecord(section, line));
            }
            return new IplDocument(instances, rawRecords);
        }

        private string ReadSection(string sourceName, DataLine line)
        {
            if (line.Tokens.Count != 1)
                throw DataParsing.Error(sourceName, line, "Unknown section '" + line.Text.Trim() + "'.");

            string section = line.Tokens[0].ToLowerInvariant();
            switch (section)
            {
                case "inst":
                case "zone":
                case "cull":
                case "pick":
                case "path":
                    return section;
                case "occl":
                    if (!schema.SupportsOcclusion)
                        throw DataParsing.Error(sourceName, line, "Section 'occl' is not supported by this data schema.");
                    return section;
                default:
                    throw DataParsing.Error(sourceName, line, "Unknown section '" + line.Tokens[0] + "'.");
            }
        }

        private static bool IsEnd(DataLine line)
        {
            return line.Tokens.Count == 1 && string.Equals(line.Tokens[0], "end", StringComparison.OrdinalIgnoreCase);
        }

        private ObjectInstanceRecord ParseInstance(string sourceName, DataLine line)
        {
            bool hasArea;
            if (line.Tokens.Count == 12)
            {
                hasArea = false;
            }
            else if (line.Tokens.Count == 13 && schema.AllowInstanceArea)
            {
                hasArea = true;
            }
            else
            {
                string expected = schema.AllowInstanceArea ? "12 or 13" : "exactly 12";
                throw DataParsing.Error(sourceName, line, "Expected " + expected + " fields for inst, but got " +
                    line.Tokens.Count.ToString(CultureInfo.InvariantCulture) + ".");
            }

            int id = DataParsing.ParseInt32(sourceName, line, 0, "id");
            int offset = hasArea ? 1 : 0;
            float area = hasArea ? DataParsing.ParseSingle(sourceName, line, 2, "area") : 0.0f;
            int positionIndex = 2 + offset;
            Vector3 position = new Vector3(
                DataParsing.ParseSingle(sourceName, line, positionIndex, "position.x"),
                DataParsing.ParseSingle(sourceName, line, positionIndex + 1, "position.y"),
                DataParsing.ParseSingle(sourceName, line, positionIndex + 2, "position.z"));
            int scaleIndex = positionIndex + 3;
            Vector3 scale = new Vector3(
                DataParsing.ParseSingle(sourceName, line, scaleIndex, "scale.x"),
                DataParsing.ParseSingle(sourceName, line, scaleIndex + 1, "scale.y"),
                DataParsing.ParseSingle(sourceName, line, scaleIndex + 2, "scale.z"));
            int axisIndex = scaleIndex + 3;
            Vector3 axis = new Vector3(
                DataParsing.ParseSingle(sourceName, line, axisIndex, "axis.x"),
                DataParsing.ParseSingle(sourceName, line, axisIndex + 1, "axis.y"),
                DataParsing.ParseSingle(sourceName, line, axisIndex + 2, "axis.z"));
            float quaternionW = DataParsing.ParseSingle(sourceName, line, axisIndex + 3, "quaternionW");

            return new ObjectInstanceRecord(line, id, line.Tokens[1], position, scale, axis,
                quaternionW, hasArea, area);
        }
    }
}
