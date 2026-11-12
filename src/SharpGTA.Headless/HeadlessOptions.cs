using System;
using System.Collections.Generic;
using System.Globalization;
using SharpGTA.Data;

namespace SharpGTA.Headless
{
    internal sealed class CommandLineException : Exception
    {
        public CommandLineException(string message)
            : base(message)
        {
        }
    }

    internal sealed class HeadlessOptions
    {
        public HeadlessOptions()
        {
            Game = "iii";
            DatPaths = new List<string>();
            ImagePaths = new List<string>();
            RenderWarePaths = new List<string>();
            RenderWareEntries = new List<string>();
            DffPaths = new List<string>();
            DffEntries = new List<string>();
            TxdPaths = new List<string>();
            TxdEntries = new List<string>();
            PathNearestKind = PathKind.Car;
            PathNearestRadius = 100.0f;
        }

        public string Game { get; set; }
        public string Root { get; set; }
        public bool Standard { get; set; }
        public bool HasExplicitAssetAction { get; set; }
        public bool Strict { get; set; }
        public bool Verbose { get; set; }
        public bool DffScan { get; set; }
        public bool TxdScan { get; set; }
        public bool RenderPreview { get; set; }
        public List<string> TxdPaths { get; private set; }
        public List<string> TxdEntries { get; private set; }
        public bool Help { get; set; }
        public bool Version { get; set; }
        public string GxtPath { get; set; }
        public string GxtKey { get; set; }
        public List<string> DatPaths { get; private set; }
        public List<string> ImagePaths { get; private set; }
        public List<string> RenderWarePaths { get; private set; }
        public List<string> RenderWareEntries { get; private set; }
        public List<string> DffPaths { get; private set; }
        public List<string> DffEntries { get; private set; }
        public ulong Ticks { get; set; }
        public ulong StateAt { get; set; }
        public bool HasStateAt { get; set; }
        public bool HasGroundQuery { get; set; }
        public float GroundX { get; set; }
        public float GroundY { get; set; }
        public float GroundMinZ { get; set; }
        public float GroundMaxZ { get; set; }
        public bool HasZoneQuery { get; set; }
        public float ZoneX { get; set; }
        public float ZoneY { get; set; }
        public float ZoneZ { get; set; }
        public bool HasPathQuery { get; set; }

        public bool HasBuildingQuery { get; set; }
        public int BuildingHandle { get; set; }

        public bool HasPathNearestQuery { get; set; }
        public float PathNearestX { get; set; }
        public float PathNearestY { get; set; }
        public float PathNearestZ { get; set; }
        public PathKind PathNearestKind { get; set; }
        public float PathNearestRadius { get; set; }
        public float PathMergeDistance { get; set; }
        public bool HasPathMergeDistance { get; set; }
        public bool HasPathKindOption { get; set; }
        public bool HasPathRadiusOption { get; set; }

        public bool HasSpawnObjects { get; set; }
        public int SpawnObjectCount { get; set; }
        public string SpawnModel { get; set; }
        public int PathGroupIndex { get; set; }
        public int PathNodeIndex { get; set; }

        public static HeadlessOptions Parse(string[] args)
        {
            HeadlessOptions options = new HeadlessOptions();
            if (args == null)
                throw new ArgumentNullException("args");
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                switch (argument)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                    case "--version":
                        options.Version = true;
                        break;
                    case "--game":
                        options.Game = Value(args, ref i, argument);
                        break;
                    case "--root":
                        options.Root = Value(args, ref i, argument);
                        break;
                    case "--standard":
                        options.Standard = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--dat":
                        options.DatPaths.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--img":
                        options.ImagePaths.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--gxt":
                        options.GxtPath = Value(args, ref i, argument);
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--gxt-key":
                        options.GxtKey = Value(args, ref i, argument);
                        break;
                    case "--rw":
                        options.RenderWarePaths.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--rw-entry":
                        options.RenderWareEntries.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--dff":
                        options.DffPaths.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--dff-entry":
                        options.DffEntries.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--dff-scan":
                        options.DffScan = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--txd":
                        options.TxdPaths.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--txd-entry":
                        options.TxdEntries.Add(Value(args, ref i, argument));
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--txd-scan":
                        options.TxdScan = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--render-preview":
                        options.RenderPreview = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--ticks":
                        options.Ticks = ParseUInt64(Value(args, ref i, argument), argument);
                        break;
                    case "--state-at":
                        options.StateAt = ParseUInt64(Value(args, ref i, argument), argument);
                        options.HasStateAt = true;
                        break;
                    case "--query-ground":
                        options.GroundX = ParseSingle(Value(args, ref i, argument), argument);
                        options.GroundY = ParseSingle(Value(args, ref i, argument), argument);
                        options.GroundMinZ = ParseSingle(Value(args, ref i, argument), argument);
                        options.GroundMaxZ = ParseSingle(Value(args, ref i, argument), argument);
                        options.HasGroundQuery = true;
                        break;
                    case "--query-zone":
                        options.ZoneX = ParseSingle(Value(args, ref i, argument), argument);
                        options.ZoneY = ParseSingle(Value(args, ref i, argument), argument);
                        options.ZoneZ = ParseSingle(Value(args, ref i, argument), argument);
                        options.HasZoneQuery = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--query-path":
                        options.PathGroupIndex = ParseNonNegativeInt32(Value(args, ref i, argument), argument);
                        options.PathNodeIndex = ParseNonNegativeInt32(Value(args, ref i, argument), argument);
                        options.HasPathQuery = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--query-building":
                        options.BuildingHandle = ParseNonNegativeInt32(Value(args, ref i, argument), argument);
                        options.HasBuildingQuery = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--query-path-nearest":
                        options.PathNearestX = ParseSingle(Value(args, ref i, argument), argument);
                        options.PathNearestY = ParseSingle(Value(args, ref i, argument), argument);
                        options.PathNearestZ = ParseSingle(Value(args, ref i, argument), argument);
                        options.HasPathNearestQuery = true;
                        options.HasExplicitAssetAction = true;
                        break;
                    case "--path-kind":
                        options.PathNearestKind = ParsePathKind(Value(args, ref i, argument));
                        options.HasPathKindOption = true;
                        break;
                    case "--path-radius":
                        options.PathNearestRadius = ParseSingle(Value(args, ref i, argument), argument);
                        options.HasPathRadiusOption = true;
                        break;
                    case "--path-merge-distance":
                        // Only changes how the path graph is built from already-requested map
                        // data, so it must not suppress the implicit --standard load.
                        options.PathMergeDistance = ParseSingle(Value(args, ref i, argument), argument);
                        options.HasPathMergeDistance = true;
                        break;
                    case "--spawn-objects":
                        // Spawning needs a loaded world, not a one-shot asset action, so it
                        // does not suppress the implicit --standard load.
                        options.SpawnObjectCount = ParseNonNegativeInt32(Value(args, ref i, argument), argument);
                        options.HasSpawnObjects = true;
                        break;
                    case "--spawn-model":
                        options.SpawnModel = Value(args, ref i, argument);
                        break;
                    case "--strict":
                        options.Strict = true;
                        break;
                    case "--verbose":
                        options.Verbose = true;
                        break;
                    default:
                        throw new CommandLineException("Unknown option: " + argument);
                }
            }

            if (!options.Help && !options.Version)
            {
                if (options.HasStateAt && options.StateAt > options.Ticks)
                    throw new CommandLineException("--state-at cannot be greater than --ticks.");
                if ((options.HasPathKindOption || options.HasPathRadiusOption) && !options.HasPathNearestQuery)
                    throw new CommandLineException("--path-kind and --path-radius require --query-path-nearest.");
                if (options.HasPathNearestQuery && !(options.PathNearestRadius > 0.0f))
                    throw new CommandLineException("--path-radius must be a positive number.");
                if (options.HasPathMergeDistance && !(options.PathMergeDistance > 0.0f))
                    throw new CommandLineException("--path-merge-distance must be a positive number.");
                if (options.GxtKey != null && options.GxtPath == null)
                    throw new CommandLineException("--gxt-key requires --gxt.");
                if (options.RenderPreview && options.DffPaths.Count + options.DffEntries.Count == 0)
                    throw new CommandLineException("--render-preview requires at least one --dff or --dff-entry.");
                if (options.HasGroundQuery)
                    options.HasExplicitAssetAction = true;
                bool needsRoot = options.Standard || options.DatPaths.Count != 0 || options.ImagePaths.Count != 0 ||
                    options.GxtPath != null || options.RenderWarePaths.Count != 0 || options.RenderWareEntries.Count != 0 ||
                    options.DffPaths.Count != 0 || options.DffEntries.Count != 0 || options.DffScan ||
                    options.TxdPaths.Count != 0 || options.TxdEntries.Count != 0 || options.TxdScan ||
                    options.HasGroundQuery || options.HasZoneQuery || options.HasPathQuery ||
                    options.HasBuildingQuery || options.HasSpawnObjects || options.RenderPreview ||
                    options.HasPathNearestQuery || options.HasPathMergeDistance;
                if (needsRoot && options.Root == null)
                    throw new CommandLineException("Asset loading and ground queries require --root PATH.");
                if (options.Root != null && !options.HasExplicitAssetAction)
                    options.Standard = true;
            }
            return options;
        }

        private static string Value(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
                throw new CommandLineException("Missing value for " + option + ".");
            index++;
            return args[index];
        }

        private static ulong ParseUInt64(string value, string option)
        {
            ulong result;
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result))
                throw new CommandLineException("Invalid non-negative integer for " + option + ": " + value);
            return result;
        }

        private static int ParseNonNegativeInt32(string value, string option)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result < 0)
                throw new CommandLineException("Invalid non-negative integer for " + option + ": " + value);
            return result;
        }

        private static float ParseSingle(string value, string option)
        {
            float result;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.IsNaN(result) || float.IsInfinity(result))
                throw new CommandLineException("Invalid finite number for " + option + ": " + value);
            return result;
        }

        private static PathKind ParsePathKind(string value)
        {
            if (string.Equals(value, "car", StringComparison.OrdinalIgnoreCase))
                return PathKind.Car;
            if (string.Equals(value, "ped", StringComparison.OrdinalIgnoreCase))
                return PathKind.Ped;
            if (string.Equals(value, "water", StringComparison.OrdinalIgnoreCase))
                return PathKind.Water;
            throw new CommandLineException("Invalid path kind for --path-kind: " + value +
                " (expected car, ped or water).");
        }
    }
}
