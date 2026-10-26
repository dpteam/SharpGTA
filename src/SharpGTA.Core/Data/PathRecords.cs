using System;
using System.Collections.Generic;
using System.Globalization;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Data
{
    public enum PathKind
    {
        Ped,
        Car,
        Water
    }

    public sealed class PathGroup
    {
        internal PathGroup(int modelId, string modelName, PathKind kind, PathNode[] nodes,
            string sourceName, int? sourceHeaderLine)
        {
            ModelId = modelId;
            ModelName = modelName;
            Kind = kind;
            Nodes = nodes;
            SourceName = sourceName;
            SourceHeaderLine = sourceHeaderLine;
        }

        public int ModelId { get; private set; }
        public string ModelName { get; private set; }
        public PathKind Kind { get; private set; }
        public bool IsDetached { get { return ModelId == -1; } }
        public PathNode[] Nodes { get; private set; }
        public string SourceName { get; private set; }
        public int? SourceHeaderLine { get; private set; }

        /// <summary>The explicitly bound IPL transform; detached groups do not need one.</summary>
        public Matrix? ObjectTransform { get; internal set; }
        public bool HasTransform { get { return ObjectTransform.HasValue; } }
    }

    public sealed class PathNode
    {
        internal PathNode(int nodeIndex, int type, int nextNodeIndex, bool cross,
            Vector3 storedPosition, float width, int numLeftLanes, int numRightLanes,
            int speed, int flags, float spawnRate, bool onlyShortestPath)
        {
            NodeIndex = nodeIndex;
            Type = type;
            NextNodeIndex = nextNodeIndex;
            Cross = cross;
            StoredPosition = storedPosition;
            Width = width;
            NumLeftLanes = numLeftLanes;
            NumRightLanes = numRightLanes;
            Speed = speed;
            Flags = flags;
            SpawnRate = spawnRate;
            IgnoredCollision = (flags & 1) != 0;
            IgnoredLights = (flags & 2) != 0;
            IgnoredTransition = (flags & 4) != 0;
            OnlyShortestPath = onlyShortestPath;
        }

        public int NodeIndex { get; private set; }
        public int Type { get; private set; }
        public int NextNodeIndex { get; private set; }
        public bool Cross { get; private set; }
        public Vector3 StoredPosition { get; private set; }
        public float Width { get; private set; }
        public int NumLeftLanes { get; private set; }
        public int NumRightLanes { get; private set; }
        public int Speed { get; private set; }
        public int Flags { get; private set; }
        public float SpawnRate { get; private set; }
        public bool IgnoredCollision { get; private set; }
        public bool IgnoredTransition { get; private set; }
        public bool IgnoredLights { get; private set; }
        public bool OnlyShortestPath { get; private set; }
    }

    /// <summary>Reads the raw IDE/IPL path section without changing its source ordering.</summary>
    public static class PathRecordParser
    {
        private const int NodesPerGroup = 12;

        public static PathGroup[] ParseGroups(IList<RawDataRecord> pathRecords,
            DataSchema schema, string sourceName)
        {
            if (pathRecords == null)
                throw new ArgumentNullException("pathRecords");
            if (schema == null)
                throw new ArgumentNullException("schema");

            string errorSource = string.IsNullOrEmpty(sourceName) ? "path" : sourceName;
            List<PathGroup> groups = new List<PathGroup>();
            RawDataRecord header = null;
            PathNode[] nodes = null;
            int nodeCount = 0;
            int modelId = 0;
            string modelName = null;
            PathKind kind = PathKind.Ped;

            for (int i = 0; i < pathRecords.Count; i++)
            {
                RawDataRecord record = pathRecords[i];
                if (record == null)
                    throw new ArgumentException("Path records cannot contain null entries.", "pathRecords");

                if (nodes == null)
                {
                    ParseHeader(record, schema, errorSource, out modelId, out modelName, out kind);
                    header = record;
                    nodes = new PathNode[NodesPerGroup];
                    nodeCount = 0;
                    continue;
                }

                nodes[nodeCount] = ParseNode(record, schema, errorSource, kind, nodeCount);
                nodeCount++;
                if (nodeCount == NodesPerGroup)
                {
                    groups.Add(new PathGroup(modelId, modelName, kind, nodes,
                        sourceName, header.LineNumber));
                    header = null;
                    nodes = null;
                    nodeCount = 0;
                }
            }

            if (nodes != null)
            {
                throw Error(errorSource, header, "Path group contains " +
                    nodeCount.ToString(CultureInfo.InvariantCulture) + " nodes; expected exactly " +
                    NodesPerGroup.ToString(CultureInfo.InvariantCulture) + ".");
            }
            return groups.ToArray();
        }

        private static void ParseHeader(RawDataRecord record, DataSchema schema, string sourceName,
            out int modelId, out string modelName, out PathKind kind)
        {
            int count = record.Tokens.Count;
            if (schema.NumericPathTypes)
            {
                if (count != 2 && count != 3)
                {
                    throw Error(sourceName, record, "Expected 2 or 3 fields for a VC path header, but got " +
                        count.ToString(CultureInfo.InvariantCulture) + ".");
                }

                int numericKind = ParseInt32(record, sourceName, 0, "path type");
                switch (numericKind)
                {
                    case 0:
                        kind = PathKind.Ped;
                        break;
                    case 1:
                        kind = PathKind.Car;
                        break;
                    case 2:
                        kind = PathKind.Water;
                        break;
                    default:
                        throw Error(sourceName, record, "Unknown VC path type '" +
                            record.Tokens[0] + "'; expected 0 (ped), 1 (car), or 2 (water).");
                }
                modelId = ParseInt32(record, sourceName, 1, "model id");
                modelName = count == 3 ? record.Tokens[2] : null;
                return;
            }

            if (count != 3)
            {
                throw Error(sourceName, record, "Expected exactly 3 fields for an III path header, but got " +
                    count.ToString(CultureInfo.InvariantCulture) + ".");
            }
            if (string.Equals(record.Tokens[0], "ped", StringComparison.OrdinalIgnoreCase))
                kind = PathKind.Ped;
            else if (string.Equals(record.Tokens[0], "car", StringComparison.OrdinalIgnoreCase))
                kind = PathKind.Car;
            else
                throw Error(sourceName, record, "Unknown III path type '" + record.Tokens[0] +
                    "'; expected 'ped' or 'car'.");

            modelId = ParseInt32(record, sourceName, 1, "model id");
            modelName = record.Tokens[2];
        }

        private static PathNode ParseNode(RawDataRecord record, DataSchema schema,
            string sourceName, PathKind kind, int nodeIndex)
        {
            int count = record.Tokens.Count;
            if (schema.NumericPathTypes)
            {
                if (count != 11 && count != 12)
                {
                    throw Error(sourceName, record, "Expected 11 or 12 fields for a VC path node, but got " +
                        count.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
            else if (kind == PathKind.Ped)
            {
                if (count < 7 || count > 9)
                {
                    throw Error(sourceName, record, "Expected 7 to 9 fields for an III ped path node, but got " +
                        count.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
            else if (count != 9)
            {
                throw Error(sourceName, record, "Expected exactly 9 fields for an III car path node, but got " +
                    count.ToString(CultureInfo.InvariantCulture) + ".");
            }

            int type = ParseInt32(record, sourceName, 0, "node type");
            int next = ParseInt32(record, sourceName, 1, "next node");
            bool cross = ParseInt32(record, sourceName, 2, "cross") != 0;
            Vector3 storedPosition = new Vector3(
                ParseSingle(record, sourceName, 3, "position.x"),
                ParseSingle(record, sourceName, 4, "position.y"),
                ParseSingle(record, sourceName, 5, "position.z"));
            float width = ParseSingle(record, sourceName, 6, "width");
            int numLeft = 0;
            int numRight = 0;
            int speed = 0;
            int flags = 0;
            float spawnRate = 0.0f;

            if (schema.NumericPathTypes)
            {
                if (kind != PathKind.Ped)
                {
                    numLeft = ParseInt32(record, sourceName, 7, "numLeftLanes");
                    numRight = ParseInt32(record, sourceName, 8, "numRightLanes");
                }
                speed = ParseInt32(record, sourceName, 9, "speed");
                flags = ParseInt32(record, sourceName, 10, "flags");
                float storedSpawnRate = count == 12
                    ? ParseSingle(record, sourceName, 11, "spawnRate")
                    : 1.0f;
                spawnRate = storedSpawnRate * 15.0f;
            }
            else
            {
                if (kind == PathKind.Car || count > 7)
                    numLeft = ParseInt32(record, sourceName, 7, "numLeftLanes");
                if (kind == PathKind.Car || count > 8)
                    numRight = ParseInt32(record, sourceName, 8, "numRightLanes");
            }

            // Neither III nor VC text paths contain an only-shortest-path field.
            return new PathNode(nodeIndex, type, next, cross, storedPosition, width,
                numLeft, numRight, speed, flags, spawnRate, false);
        }

        private static int ParseInt32(RawDataRecord record, string sourceName,
            int index, string fieldName)
        {
            string token = record.Tokens[index];
            int value;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw InvalidNumber(record, sourceName, fieldName, token, "integer");
            return value;
        }

        private static float ParseSingle(RawDataRecord record, string sourceName,
            int index, string fieldName)
        {
            string token = record.Tokens[index];
            float value;
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                throw InvalidNumber(record, sourceName, fieldName, token, "floating-point number");
            return value;
        }

        private static AssetFormatException InvalidNumber(RawDataRecord record, string sourceName,
            string fieldName, string token, string expected)
        {
            return Error(sourceName, record, "Invalid " + expected + " for " + fieldName +
                ": '" + token + "'.");
        }

        private static AssetFormatException Error(string sourceName,
            RawDataRecord record, string message)
        {
            return new AssetFormatException(sourceName + " line " +
                record.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }
    }
}
