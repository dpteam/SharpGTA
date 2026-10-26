using System;
using System.Collections.Generic;
using System.Globalization;

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
        }

        public string Game { get; set; }
        public string Root { get; set; }
        public bool Standard { get; set; }
        public bool HasExplicitAssetAction { get; set; }
        public bool Strict { get; set; }
        public bool Verbose { get; set; }
        public bool DffScan { get; set; }
        public bool TxdScan { get; set; }
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
                if (options.GxtKey != null && options.GxtPath == null)
                    throw new CommandLineException("--gxt-key requires --gxt.");
                if (options.HasGroundQuery)
                    options.HasExplicitAssetAction = true;
                bool needsRoot = options.Standard || options.DatPaths.Count != 0 || options.ImagePaths.Count != 0 ||
                    options.GxtPath != null || options.RenderWarePaths.Count != 0 || options.RenderWareEntries.Count != 0 ||
                    options.DffPaths.Count != 0 || options.DffEntries.Count != 0 || options.DffScan ||
                    options.TxdPaths.Count != 0 || options.TxdEntries.Count != 0 || options.TxdScan || options.HasGroundQuery;
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

        private static float ParseSingle(string value, string option)
        {
            float result;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.IsNaN(result) || float.IsInfinity(result))
                throw new CommandLineException("Invalid finite number for " + option + ": " + value);
            return result;
        }
    }
}
