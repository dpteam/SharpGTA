using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.Games.III;
using SharpGTA.Games.VC;
using SharpGTA.IO;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Headless
{
    public static class HeadlessApplication
    {
        private const int MaximumLooseAssetLength = 256 * 1024 * 1024;

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            if (error == null)
                throw new ArgumentNullException("error");
            try
            {
                HeadlessOptions options = HeadlessOptions.Parse(args);
                if (options.Help)
                {
                    WriteBanner(output);
                    WriteUsage(output);
                    return 0;
                }
                if (options.Version)
                {
                    output.WriteLine(Branding.Title);
                    return 0;
                }

                GameDefinition definition = SelectGame(options.Game);
                WriteBanner(output);
                output.WriteLine("Game: " + options.Game.ToLowerInvariant() + " (" + definition.DisplayName + ")");
                GameClock gameClock = null;
                using (GameDataLoader loader = CreateLoader(definition, options))
                {
                    if (loader != null)
                    {
                        LoadAssets(loader, options, output);
                        if (options.HasGroundQuery)
                            WriteGroundQuery(loader.World, options, output);
                        if (options.Verbose)
                            WriteIssues(loader.Report, output);
                    }

                    SimulationRuntime runtime = new SimulationRuntime(definition);
                    gameClock = new GameClock();
                    runtime.AddModule(gameClock);
                    if (options.HasStateAt && options.StateAt == 0 && options.Ticks != 0)
                        output.WriteLine("State@0: " + runtime.ComputeStateHash());
                    while (runtime.Clock.Tick < options.Ticks)
                    {
                        runtime.Step();
                        if (options.HasStateAt && options.StateAt == runtime.Clock.Tick && options.StateAt != options.Ticks)
                        {
                            output.WriteLine("State@" + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture) +
                                ": " + runtime.ComputeStateHash());
                        }
                    }
                    output.WriteLine("Ticks: " + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture));
                    output.WriteLine("Clock: day=" + gameClock.DaysPassed.ToString(CultureInfo.InvariantCulture) +
                        " time=" + gameClock.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                        gameClock.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                        gameClock.Seconds.ToString("00", CultureInfo.InvariantCulture));
                    output.WriteLine("State@" + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture) + ": " + runtime.ComputeStateHash());
                    if (options.Strict && loader != null && loader.Report.TotalIssueCount != 0)
                    {
                        error.WriteLine("STRICT: " + loader.Report.TotalIssueCount.ToString(CultureInfo.InvariantCulture) + " asset issue(s) were reported.");
                        return 3;
                    }
                }
                return 0;
            }
            catch (CommandLineException exception)
            {
                error.WriteLine("USAGE: " + exception.Message);
                error.WriteLine("Use --help for the command line reference.");
                return 2;
            }
            catch (Exception exception)
            {
                error.WriteLine("ERROR: " + exception.Message);
                return 1;
            }
        }

        private static GameDefinition SelectGame(string value)
        {
            if (string.Equals(value, "iii", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "gta3", StringComparison.OrdinalIgnoreCase) || value == "3")
                return IIIDefinition.Instance;
            if (string.Equals(value, "vc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "vicecity", StringComparison.OrdinalIgnoreCase) || value == "6")
                return VCDefinition.Instance;
            throw new CommandLineException("Unknown game '" + value + "'; expected iii or vc.");
        }

        private static GameDataLoader CreateLoader(GameDefinition definition, HeadlessOptions options)
        {
            if (options.Root == null)
                return null;
            PhysicalGameFileSystem files = new PhysicalGameFileSystem(options.Root);
            return new GameDataLoader(definition, files);
        }

        private static void LoadAssets(GameDataLoader loader, HeadlessOptions options, TextWriter output)
        {
            if (options.Standard)
                loader.LoadStandard();
            for (int i = 0; i < options.ImagePaths.Count; i++)
                loader.LoadImage(options.ImagePaths[i]);
            for (int i = 0; i < options.DatPaths.Count; i++)
                loader.LoadDat(options.DatPaths[i]);
            if (options.GxtPath != null)
            {
                GxtArchive gxt = loader.LoadGxt(options.GxtPath);
                output.WriteLine("GXT: source=" + options.GxtPath + " main=" + gxt.Main.Count.ToString(CultureInfo.InvariantCulture) +
                    " missions=" + gxt.MissionTables.Count.ToString(CultureInfo.InvariantCulture));
                if (options.GxtKey != null)
                {
                    string value;
                    output.WriteLine("GXT-Key: " + options.GxtKey + "=" +
                        (gxt.Main.TryGet(options.GxtKey, out value) ? value : "<missing>"));
                }
            }
            for (int i = 0; i < options.RenderWarePaths.Count; i++)
            {
                byte[] bytes = loader.FileSystem.ReadAllBytes(options.RenderWarePaths[i], MaximumLooseAssetLength);
                WriteRenderWare(options.RenderWarePaths[i], bytes, output);
            }
            for (int i = 0; i < options.RenderWareEntries.Count; i++)
            {
                string name = options.RenderWareEntries[i];
                WriteRenderWare("IMG:" + name, loader.Images.Read(name), output);
            }

            if (options.DffScan)
            {
                if (loader.Images.Count == 0)
                    loader.LoadInitialImage();
                DffScanReport scan = loader.ScanDff();
                output.WriteLine("DFF-Scan: files=" + scan.FileCount.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + scan.FailedFileCount.ToString(CultureInfo.InvariantCulture) +
                    " empty=" + scan.EmptyFileCount.ToString(CultureInfo.InvariantCulture) +
                    " clumps=" + scan.ClumpCount.ToString(CultureInfo.InvariantCulture) +
                    " geometries=" + scan.GeometryCount.ToString(CultureInfo.InvariantCulture) +
                    " frames=" + scan.FrameCount.ToString(CultureInfo.InvariantCulture) +
                    " atomics=" + scan.AtomicCount.ToString(CultureInfo.InvariantCulture) +
                    " triangles=" + scan.TriangleCount.ToString(CultureInfo.InvariantCulture) +
                    " materials=" + scan.MaterialCount.ToString(CultureInfo.InvariantCulture) +
                    " lengthMismatches=" + scan.LengthMismatchCount.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < scan.Failures.Count; i++)
                    output.WriteLine("DFF-Error: " + scan.Failures[i].Name + " - " + scan.Failures[i].Message);
            }
            if (options.TxdScan)
            {
                if (loader.Images.Count == 0)
                    loader.LoadInitialImage();
                TxdScanReport scan = loader.ScanTxd(true);
                output.WriteLine("TXD-Scan: files=" + scan.FileCount.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + scan.FailedFileCount.ToString(CultureInfo.InvariantCulture) +
                    " empty=" + scan.EmptyFileCount.ToString(CultureInfo.InvariantCulture) +
                    " textures=" + scan.TextureCount.ToString(CultureInfo.InvariantCulture) +
                    " skipped=" + scan.SkippedTextureCount.ToString(CultureInfo.InvariantCulture) +
                    " levels=" + scan.LevelCount.ToString(CultureInfo.InvariantCulture) +
                    " decodedLevels=" + scan.DecodedLevelCount.ToString(CultureInfo.InvariantCulture) +
                    " pixels=" + scan.DecodedPixelCount.ToString(CultureInfo.InvariantCulture) +
                    " raw=" + scan.UncompressedCount.ToString(CultureInfo.InvariantCulture) +
                    " dxt1=" + scan.Dxt1Count.ToString(CultureInfo.InvariantCulture) +
                    " dxt3=" + scan.Dxt3Count.ToString(CultureInfo.InvariantCulture) +
                    " other=" + scan.OtherCompressionCount.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < scan.Failures.Count; i++)
                    output.WriteLine("TXD-Error: " + scan.Failures[i].Name + " - " + scan.Failures[i].Message);
            }
            for (int i = 0; i < options.TxdPaths.Count; i++)
                WriteTxd(options.TxdPaths[i], loader.LoadTxd(options.TxdPaths[i]), output);
            for (int i = 0; i < options.TxdEntries.Count; i++)
                WriteTxd("IMG:" + options.TxdEntries[i], loader.LoadTxdEntry(options.TxdEntries[i]), output);
            for (int i = 0; i < options.DffPaths.Count; i++)
                WriteDff(options.DffPaths[i], loader.LoadDff(options.DffPaths[i]), output);
            for (int i = 0; i < options.DffEntries.Count; i++)
                WriteDff("IMG:" + options.DffEntries[i], loader.LoadDffEntry(options.DffEntries[i]), output);

            GameDataLoadStats stats = loader.Stats;
            output.WriteLine("Assets: dat=" + stats.DatFileCount.ToString(CultureInfo.InvariantCulture) +
                " ide=" + stats.IdeFileCount.ToString(CultureInfo.InvariantCulture) +
                " ipl=" + stats.IplFileCount.ToString(CultureInfo.InvariantCulture) +
                " col=" + stats.ColFileCount.ToString(CultureInfo.InvariantCulture) +
                " img=" + stats.ImageCount.ToString(CultureInfo.InvariantCulture) +
                " models=" + loader.Models.Count.ToString(CultureInfo.InvariantCulture) +
                " collisions=" + loader.Collisions.Count.ToString(CultureInfo.InvariantCulture) +
                " instances=" + loader.World.Count.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("Asset-Issues: total=" + loader.Report.TotalIssueCount.ToString(CultureInfo.InvariantCulture) +
                " retained=" + loader.Report.Count.ToString(CultureInfo.InvariantCulture) +
                " dropped=" + loader.Report.DroppedIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteGroundQuery(StaticWorld world, HeadlessOptions options, TextWriter output)
        {
            StaticWorldHit hit;
            if (!world.QueryVertical(options.GroundX, options.GroundY, options.GroundMinZ, options.GroundMaxZ, out hit))
            {
                output.WriteLine("Ground: hit=false");
                return;
            }
            output.WriteLine("Ground: hit=true model=" + hit.Instance.Model.Name +
                " position=" + Vector(hit.Position) + " normal=" + Vector(hit.Normal) +
                " surface=" + hit.Surface.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteRenderWare(string source, byte[] bytes, TextWriter output)
        {
            IList<RenderWareChunk> roots = RenderWareInventory.ReadInventory(bytes);
            int count = 0;
            int depth = 0;
            for (int i = 0; i < roots.Count; i++)
                CountChunks(roots[i], 1, ref count, ref depth);
            output.WriteLine("RW: source=" + source + " roots=" + roots.Count.ToString(CultureInfo.InvariantCulture) +
                " chunks=" + count.ToString(CultureInfo.InvariantCulture) + " depth=" + depth.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteTxd(string source, TxdArchive archive, TextWriter output)
        {
            int levels = 0;
            long pixels = 0;
            int raw = 0;
            int dxt1 = 0;
            int dxt3 = 0;
            for (int i = 0; i < archive.Textures.Length; i++)
            {
                TxdTexture texture = archive.Textures[i];
                levels += texture.LevelCount;
                if (texture.Compression == TxdCompression.Uncompressed)
                    raw++;
                else if (texture.Compression == TxdCompression.Dxt1)
                    dxt1++;
                else if (texture.Compression == TxdCompression.Dxt3)
                    dxt3++;
                for (int level = 0; level < texture.LevelCount; level++)
                {
                    TxdImage image = texture.DecodeLevel(level);
                    pixels += (long)image.Width * image.Height;
                }
            }
            output.WriteLine("TXD: source=" + source + " declared=" + archive.DeclaredTextureCount.ToString(CultureInfo.InvariantCulture) +
                " textures=" + archive.Textures.Length.ToString(CultureInfo.InvariantCulture) +
                " skipped=" + archive.SkippedTextureCount.ToString(CultureInfo.InvariantCulture) +
                " levels=" + levels.ToString(CultureInfo.InvariantCulture) +
                " pixels=" + pixels.ToString(CultureInfo.InvariantCulture) +
                " raw=" + raw.ToString(CultureInfo.InvariantCulture) +
                " dxt1=" + dxt1.ToString(CultureInfo.InvariantCulture) +
                " dxt3=" + dxt3.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteDff(string source, DffDocument document, TextWriter output)
        {
            int geometries = 0;
            int frames = 0;
            int atomics = 0;
            int triangles = 0;
            int materials = 0;
            int meshes = 0;
            int vertices = 0;
            int mismatches = 0;
            for (int i = 0; i < document.Clumps.Length; i++)
            {
                DffClump clump = document.Clumps[i];
                frames += clump.Frames.Length;
                atomics += clump.Atomics.Length;
                geometries += clump.Geometries.Length;
                if (clump.HasDeclaredLengthMismatch)
                    mismatches++;
                for (int j = 0; j < clump.Geometries.Length; j++)
                {
                    DffGeometry geometry = clump.Geometries[j];
                    triangles += geometry.Triangles.Length;
                    materials += geometry.Materials.Length;
                    meshes += geometry.Meshes.Length;
                    if (geometry.MorphTargets.Length != 0 && geometry.MorphTargets[0].Vertices != null)
                        vertices += geometry.MorphTargets[0].Vertices.Length;
                }
            }
            output.WriteLine("DFF: source=" + source + " clumps=" + document.Clumps.Length.ToString(CultureInfo.InvariantCulture) +
                " geometries=" + geometries.ToString(CultureInfo.InvariantCulture) +
                " frames=" + frames.ToString(CultureInfo.InvariantCulture) +
                " atomics=" + atomics.ToString(CultureInfo.InvariantCulture) +
                " vertices=" + vertices.ToString(CultureInfo.InvariantCulture) +
                " triangles=" + triangles.ToString(CultureInfo.InvariantCulture) +
                " materials=" + materials.ToString(CultureInfo.InvariantCulture) +
                " meshes=" + meshes.ToString(CultureInfo.InvariantCulture) +
                " lengthMismatches=" + mismatches.ToString(CultureInfo.InvariantCulture));
        }

        private static void CountChunks(RenderWareChunk chunk, int level, ref int count, ref int depth)
        {
            count++;
            if (level > depth)
                depth = level;
            for (int i = 0; i < chunk.Children.Count; i++)
                CountChunks(chunk.Children[i], level + 1, ref count, ref depth);
        }

        private static void WriteIssues(LoadReport report, TextWriter output)
        {
            for (int i = 0; i < report.Items.Count; i++)
            {
                LoadIssue issue = report.Items[i];
                output.WriteLine("Issue: " + issue.Severity.ToString().ToUpperInvariant() + " " + issue.Category +
                    " " + issue.Path + (issue.Line == 0 ? "" : ":" + issue.Line.ToString(CultureInfo.InvariantCulture)) +
                    " - " + issue.Message);
            }
        }

        private static string Vector(SharpGTA.Mathematics.Vector3 value)
        {
            return "(" + value.X.ToString("R", CultureInfo.InvariantCulture) + "," +
                value.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
                value.Z.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static void WriteBanner(TextWriter output)
        {
            output.WriteLine(Branding.Title);
            output.WriteLine(Branding.Tagline);
        }

        private static void WriteUsage(TextWriter output)
        {
            output.WriteLine("Usage: " + Branding.Name + ".Headless.exe [options]");
            output.WriteLine("  --game iii|vc             Select the independent game data profile (default iii).");
            output.WriteLine("  --root PATH               Original game root; alone it implies --standard.");
            output.WriteLine("  --standard                Load initial IMG, DEFAULT.DAT, then the main DAT.");
            output.WriteLine("  --dat PATH                Execute an additional DAT in source order; repeatable.");
            output.WriteLine("  --img PATH                Register an IMG and sibling DIR; repeatable.");
            output.WriteLine("  --gxt PATH                Load a GXT and print table counts.");
            output.WriteLine("  --gxt-key KEY             Print one case-sensitive main GXT value.");
            output.WriteLine("  --rw PATH                 Inventory a loose DFF/TXD stream; repeatable.");
            output.WriteLine("  --rw-entry NAME           Inventory a named entry from loaded IMG files.");
            output.WriteLine("  --dff PATH                Decode static non-native DFF data; repeatable.");
            output.WriteLine("  --dff-entry NAME          Decode a named DFF entry from loaded IMG files.");
            output.WriteLine("  --dff-scan                Decode every DFF entry and print aggregate diagnostics.");
            output.WriteLine("  --txd PATH                Read D3D8 TXD and decode all its mip levels; repeatable.");
            output.WriteLine("  --txd-entry NAME          Read a named D3D8 TXD entry from loaded IMG files.");
            output.WriteLine("  --txd-scan                Decode every D3D8 TXD texture and mip level in IMG.");
            output.WriteLine("  --query-ground X Y Z0 Z1  Query static COLL ground between two Z values.");
            output.WriteLine("  --ticks N                 Run N deterministic 30 Hz simulation ticks.");
            output.WriteLine("  --state-at N              Also print the diagnostic state hash at tick N.");
            output.WriteLine("  --verbose                 Print retained asset issues.");
            output.WriteLine("  --strict                  Return 3 when any asset issue was reported.");
            output.WriteLine("  --version                 Print the branded version only.");
            output.WriteLine("  --help                    Show this text.");
        }
    }
}
