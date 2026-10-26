using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.World;

namespace SharpGTA.Assets
{
    /// <summary>Cumulative file/content counters suitable for a headless console summary.</summary>
    public sealed class GameDataLoadStats
    {
        public int DatFileCount { get; internal set; }
        public int IdeFileCount { get; internal set; }
        public int IplFileCount { get; internal set; }
        public int ColFileCount { get; internal set; }
        public int EmbeddedColFileCount { get; internal set; }
        public int ImageCount { get; internal set; }
        public int InstanceCount { get; internal set; }
        public int SkippedInstanceCount { get; internal set; }
        public int IgnoredDirectiveCount { get; internal set; }
        public int ZoneRecordCount { get; internal set; }
        public int MapZoneFileCount { get; internal set; }
        public int CullZoneCount { get; internal set; }
        public int OcclusionVolumeCount { get; internal set; }
        public int PathGroupCount { get; internal set; }
        public int PathNodeCount { get; internal set; }
        public int UnresolvedPathGroupCount { get; internal set; }
        public int DetachedPathGroupCount { get; internal set; }
        public int WaterPathGroupCount { get; internal set; }
    }

    /// <summary>
    /// Minimal ordered DAT content pipeline. It loads metadata, static placements,
    /// collision and IMG catalogs, but intentionally creates no RenderWare objects.
    /// </summary>
    public sealed class GameDataLoader : IDisposable
    {
        private const int MaximumBinaryFileLength = 64 * 1024 * 1024;
        private static readonly string[] NotImplementedDirectives = {
            "TEXDICTION", "MODELFILE", "HIERFILE", "IMAGEPATH", "SPLASH"
        };

        private readonly IGameFileSystem fileSystem;
        private readonly HashSet<string> loadedImagePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool embeddedCollisionLoaded;
        private bool disposed;

        public GameDataLoader(GameDefinition definition, IGameFileSystem fileSystem)
        {
            if (definition == null)
                throw new ArgumentNullException("definition");
            if (fileSystem == null)
                throw new ArgumentNullException("fileSystem");
            Definition = definition;
            this.fileSystem = fileSystem;
            Models = new ModelRegistry(definition);
            Collisions = new CollisionRepository(Models);
            World = new StaticWorld();
            Images = new ImgCatalog();
            Zones = new ZoneWorld(definition);
            Paths = new PathWorld();
            Report = new LoadReport();
            Stats = new GameDataLoadStats();
        }

        public GameDefinition Definition { get; private set; }
        public IGameFileSystem FileSystem { get { return fileSystem; } }
        public ModelRegistry Models { get; private set; }
        public CollisionRepository Collisions { get; private set; }
        public StaticWorld World { get; private set; }
        public ImgCatalog Images { get; private set; }
        public ZoneWorld Zones { get; private set; }
        public PathWorld Paths { get; private set; }
        public LoadReport Report { get; private set; }
        public GameDataLoadStats Stats { get; private set; }

        public void LoadStandard()
        {
            ThrowIfDisposed();
            LoadInitialImage();
            LoadDat(Definition.DefaultDatPath);
            LoadDat(Definition.MainDatPath);
        }

        public void LoadInitialImage()
        {
            ThrowIfDisposed();
            LoadImage(Definition.InitialImagePath);
        }

        /// <summary>Loads and executes a DAT in source order.</summary>
        public void LoadDat(string path)
        {
            ThrowIfDisposed();
            string text = ReadRequiredText(path);
            DatDocument document = new DatReader(Definition.DataSchema).Read(text, path);
            Dictionary<string, DirectiveGroup> notImplemented =
                new Dictionary<string, DirectiveGroup>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DirectiveGroup> unknown =
                new Dictionary<string, DirectiveGroup>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < document.Directives.Count; i++)
            {
                DatDirective directive = document.Directives[i];
                switch (directive.Command)
                {
                    case "IDE":
                        LoadIde(RequireDirectivePath(directive, path));
                        break;
                    case "IPL":
                        LoadIpl(RequireDirectivePath(directive, path));
                        break;
                    case "COLFILE":
                        LoadCollision(RequireDirectivePath(directive, path),
                            ParseCollisionLevel(directive, path));
                        break;
                    case "CDIMAGE":
                        LoadImage(RequireDirectivePath(directive, path));
                        break;
                    case "MAPZONE":
                        if (Definition.DataSchema.SupportsMapZoneDirective)
                            LoadMapZones(RequireDirectivePath(directive, path));
                        else
                            Accumulate(unknown, directive);
                        break;
                    default:
                        if (!directive.IsSupported)
                            Accumulate(unknown, directive);
                        else if (IsNotImplemented(directive.Command))
                            Accumulate(notImplemented, directive);
                        break;
                }
            }

            ReportGroups(notImplemented, path, "UnsupportedDirective",
                "Supported DAT directive '{0}' is not implemented; {1} occurrence(s) ignored.");
            ReportGroups(unknown, path, "UnknownDirective",
                "Unknown DAT directive '{0}'; {1} occurrence(s) ignored.");
            Stats.DatFileCount++;
        }

        public void LoadIde(string path)
        {
            ThrowIfDisposed();
            string text = ReadRequiredText(path);
            IdeDocument document = new IdeReader(Definition.DataSchema).Read(text, path);
            try
            {
                Models.Bind(document);
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
            Stats.IdeFileCount++;
            BindPathRecords(document.RawRecords, path);
        }

        public void LoadIpl(string path)
        {
            ThrowIfDisposed();
            string text = ReadRequiredText(path);
            IplDocument document = new IplReader(Definition.DataSchema).Read(text, path);
            if (Definition.CollisionStorage == CollisionStorageKind.StreamedSlots && !embeddedCollisionLoaded)
                LoadEmbeddedCollision();
            int added = World.Bind(document, Models, Report, path);
            Stats.IplFileCount++;
            Stats.InstanceCount += added;
            Stats.SkippedInstanceCount += document.Instances.Count - added;
            BindZoneRecords(document.RawRecords, path, false);
            BindPathRecords(document.RawRecords, path);
        }

        public void LoadMapZones(string path)
        {
            ThrowIfDisposed();
            if (!Definition.DataSchema.SupportsMapZoneDirective)
                throw new NotSupportedException("MAPZONE is not supported by this game data profile.");
            string text = ReadRequiredText(path);
            IplDocument document = new IplReader(Definition.DataSchema).Read(text, path);
            BindZoneRecords(document.RawRecords, path, true);
            Stats.MapZoneFileCount++;
        }

        private void BindZoneRecords(IList<RawDataRecord> records, string path, bool mapZoneOnly)
        {
            for (int i = 0; i < records.Count; i++)
            {
                RawDataRecord record = records[i];
                if (record.Section == "zone")
                {
                    ZoneRecord zone = ZoneRecordParser.ParseZone(record);
                    if (mapZoneOnly || zone.Kind == ZoneKind.MapZone)
                        Zones.AddMapZone(zone);
                    else
                        Zones.AddZone(zone);
                    Stats.ZoneRecordCount++;
                }
                else if (!mapZoneOnly && record.Section == "cull")
                {
                    Zones.AddCull(ZoneRecordParser.ParseCull(record));
                    Stats.CullZoneCount++;
                }
                else if (!mapZoneOnly && record.Section == "occl")
                {
                    Zones.AddOcclusion(ZoneRecordParser.ParseOcclusion(record));
                    Stats.OcclusionVolumeCount++;
                }
            }
        }

        private void BindPathRecords(IList<RawDataRecord> records, string path)
        {
            List<RawDataRecord> pathRecords = new List<RawDataRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Section == "path")
                    pathRecords.Add(records[i]);
            }
            if (pathRecords.Count == 0)
            {
                RefreshPathStats();
                return;
            }
            PathGroup[] groups = PathRecordParser.ParseGroups(pathRecords, Definition.DataSchema, path);
            Paths.AddGroups(groups);
            Stats.PathGroupCount += groups.Length;
            for (int i = 0; i < groups.Length; i++)
            {
                Stats.PathNodeCount += groups[i].Nodes.Length;
                if (groups[i].IsDetached)
                    Stats.DetachedPathGroupCount++;
                if (groups[i].Kind == PathKind.Water)
                    Stats.WaterPathGroupCount++;
            }
            RefreshPathStats();
        }

        private void RefreshPathStats()
        {
            Stats.UnresolvedPathGroupCount = Paths.BindInstances(World.Instances);
        }

        public void LoadCollision(string path, byte level)
        {
            ThrowIfDisposed();
            byte[] bytes;
            try
            {
                bytes = ReadRequiredBytes(path);
                Collisions.Add(bytes, level);
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
            Stats.ColFileCount++;
        }

        /// <summary>
        /// Adds an IMG and its sibling DIR once per case-insensitive path. The image
        /// remains on disk and only requested entries are read into memory.
        /// </summary>
        public bool LoadImage(string path)
        {
            ThrowIfDisposed();
            if (path == null)
                throw new ArgumentNullException("path");
            if (path.Length == 0)
                throw new ArgumentException("Image path is empty.", "path");
            string[] pathParts = path.Replace('\\', '/').Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string key = string.Join("/", pathParts);
            if (loadedImagePaths.Contains(key))
                return false;

            string directoryPath = Path.ChangeExtension(path, ".DIR");
            RequireFile(directoryPath);
            RequireFile(path);
            ImgDirectory directory;
            try
            {
                using (Stream stream = fileSystem.OpenRead(directoryPath))
                    directory = ImgDirectory.Read(stream);
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(directoryPath, exception);
            }

            Stream imageStream = fileSystem.OpenRead(path);
            ImgArchive archive = null;
            bool added = false;
            try
            {
                archive = new ImgArchive(imageStream, directory, false);
                imageStream = null; // Ownership transferred to the archive.
                Images.Add(archive);
                added = true;
            }
            finally
            {
                if (imageStream != null)
                    imageStream.Dispose();
                if (!added && archive != null)
                    archive.Dispose();
            }

            loadedImagePaths.Add(key);
            Stats.ImageCount++;
            return true;
        }

        /// <summary>
        /// Synchronously loads VC COLL resources from registered images in CColStore
        /// slot order. Streaming/eviction will replace this initial headless pass later.
        /// </summary>
        public int LoadEmbeddedCollision()
        {
            ThrowIfDisposed();
            if (embeddedCollisionLoaded)
                return 0;
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nextSlot = 1; // Slot zero is reserved for generic loose collision.
            int fileCount = 0;
            for (int imageIndex = Images.Count - 1; imageIndex >= 0; imageIndex--)
            {
                ImgArchive archive = Images.GetImage(imageIndex);
                for (int i = 0; i < archive.Directory.Entries.Count; i++)
                {
                    ImgEntry entry = archive.Directory.Entries[i];
                    if (!string.Equals(Path.GetExtension(entry.Name), ".COL", StringComparison.OrdinalIgnoreCase) ||
                        !names.Add(entry.Name))
                        continue;
                    if (nextSlot > byte.MaxValue)
                        throw new AssetFormatException("More than 255 streamed collision slots are not supported by the current headless loader.");
                    try
                    {
                        List<SharpGTA.Collision.ColModel> models = SharpGTA.Collision.ColFile.Read(archive.Read(entry));
                        byte slot = (byte)nextSlot++;
                        Collisions.Add(models, slot);
                    }
                    catch (AssetFormatException exception)
                    {
                        throw WithPath("IMG:" + entry.Name, exception);
                    }
                    fileCount++;
                }
            }
            embeddedCollisionLoaded = true;
            Stats.ColFileCount += fileCount;
            Stats.EmbeddedColFileCount += fileCount;
            return fileCount;
        }

        /// <summary>Decodes every DFF entry once for diagnostics without retaining all models.</summary>
        public DffScanReport ScanDff()
        {
            ThrowIfDisposed();
            DffScanReport report = new DffScanReport();
            for (int i = 0; i < Images.Entries.Count; i++)
            {
                ImgEntry entry = Images.Entries[i];
                if (!string.Equals(Path.GetExtension(entry.Name), ".DFF", StringComparison.OrdinalIgnoreCase))
                    continue;
                report.FileCount++;
                if (entry.ByteLength == 0)
                {
                    // Retail III DIR contains zero-sized placeholder DFF entries.
                    report.EmptyFileCount++;
                    continue;
                }
                try
                {
                    DffDocument document = DffReader.Read(Images.Read(entry));
                    report.ClumpCount += document.Clumps.Length;
                    for (int j = 0; j < document.Clumps.Length; j++)
                    {
                        DffClump clump = document.Clumps[j];
                        report.GeometryCount += clump.Geometries.Length;
                        report.FrameCount += clump.Frames.Length;
                        report.AtomicCount += clump.Atomics.Length;
                        for (int frame = 0; frame < clump.Frames.Length; frame++)
                        {
                            if (clump.Frames[frame].HAnim != null)
                                report.HAnimCount++;
                        }
                        for (int atomic = 0; atomic < clump.Atomics.Length; atomic++)
                        {
                            if (clump.Atomics[atomic].HasMatFx)
                                report.AtomicMatFxCount++;
                            if (clump.Atomics[atomic].LegacySkin != null)
                                report.LegacyAtomicSkinCount++;
                        }
                        if (clump.HasDeclaredLengthMismatch)
                            report.LengthMismatchCount++;
                        for (int k = 0; k < clump.Geometries.Length; k++)
                        {
                            report.TriangleCount += clump.Geometries[k].Triangles.Length;
                            report.MaterialCount += clump.Geometries[k].Materials.Length;
                            DffGeometry geometry = clump.Geometries[k];
                            if (geometry.Skin != null)
                            {
                                report.SkinCount++;
                                if (geometry.Skin.IsOldFormat)
                                    report.OldSkinCount++;
                                else
                                    report.SplitSkinCount++;
                            }
                            for (int material = 0; material < geometry.Materials.Length; material++)
                            {
                                if (geometry.Materials[material].MatFx != null)
                                    report.MatFxMaterialCount++;
                            }
                        }
                    }
                }
                catch (AssetFormatException exception)
                {
                    report.AddFailure(entry.Name, exception.Message);
                }
            }
            return report;
        }

        public DffDocument LoadDff(string path)
        {
            ThrowIfDisposed();
            RequireFile(path);
            try
            {
                return DffReader.Read(ReadRequiredBytes(path));
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
        }

        public DffDocument LoadDffEntry(string name)
        {
            ThrowIfDisposed();
            if (name == null)
                throw new ArgumentNullException("name");
            try
            {
                return DffReader.Read(Images.Read(name));
            }
            catch (AssetFormatException exception)
            {
                throw WithPath("IMG:" + name, exception);
            }
        }

        public TxdArchive LoadTxd(string path)
        {
            ThrowIfDisposed();
            RequireFile(path);
            try
            {
                return TxdReader.Read(ReadRequiredBytes(path));
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
        }

        public TxdArchive LoadTxdEntry(string name)
        {
            ThrowIfDisposed();
            if (name == null)
                throw new ArgumentNullException("name");
            try
            {
                return TxdReader.Read(Images.Read(name));
            }
            catch (AssetFormatException exception)
            {
                throw WithPath("IMG:" + name, exception);
            }
        }

        /// <summary>Reads every TXD entry once; decoded pixels are discarded after validation.</summary>
        public TxdScanReport ScanTxd(bool decodePixels)
        {
            ThrowIfDisposed();
            TxdScanReport report = new TxdScanReport();
            for (int i = 0; i < Images.Entries.Count; i++)
            {
                ImgEntry entry = Images.Entries[i];
                if (!string.Equals(Path.GetExtension(entry.Name), ".TXD", StringComparison.OrdinalIgnoreCase))
                    continue;
                report.FileCount++;
                if (entry.ByteLength == 0)
                {
                    report.EmptyFileCount++;
                    continue;
                }
                try
                {
                    TxdArchive archive = TxdReader.Read(Images.Read(entry), true);
                    report.DeclaredTextureCount += archive.DeclaredTextureCount;
                    report.SkippedTextureCount += archive.SkippedTextureCount;
                    for (int j = 0; j < archive.Textures.Length; j++)
                    {
                        TxdTexture texture = archive.Textures[j];
                        report.TextureCount++;
                        report.LevelCount += texture.LevelCount;
                        switch (texture.Compression)
                        {
                            case TxdCompression.Uncompressed:
                                report.UncompressedCount++;
                                break;
                            case TxdCompression.Dxt1:
                                report.Dxt1Count++;
                                break;
                            case TxdCompression.Dxt3:
                                report.Dxt3Count++;
                                break;
                            default:
                                report.OtherCompressionCount++;
                                break;
                        }
                        if (!decodePixels)
                            continue;
                        for (int level = 0; level < texture.LevelCount; level++)
                        {
                            TxdImage image = texture.DecodeLevel(level);
                            report.DecodedLevelCount++;
                            report.DecodedPixelCount += (long)image.Width * image.Height;
                        }
                    }
                }
                catch (AssetFormatException exception)
                {
                    report.AddFailure(entry.Name, exception.Message);
                }
                catch (NotSupportedException exception)
                {
                    report.AddFailure(entry.Name, exception.Message);
                }
                catch (OverflowException exception)
                {
                    report.AddFailure(entry.Name, exception.Message);
                }
            }
            return report;
        }

        public GxtArchive LoadGxt(string path)
        {
            ThrowIfDisposed();
            RequireFile(path);
            try
            {
                using (Stream stream = fileSystem.OpenRead(path))
                    return GxtArchive.Read(stream);
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            Images.Dispose();
        }

        private string ReadRequiredText(string path)
        {
            RequireFile(path);
            try
            {
                return fileSystem.ReadAllText(path, PhysicalGameFileSystem.DefaultTextLimit);
            }
            catch (AssetFormatException exception)
            {
                throw WithPath(path, exception);
            }
        }

        private byte[] ReadRequiredBytes(string path)
        {
            RequireFile(path);
            return fileSystem.ReadAllBytes(path, MaximumBinaryFileLength);
        }

        private void RequireFile(string path)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (!fileSystem.FileExists(path))
                throw new FileNotFoundException("Game file was not found: " + path, path);
        }

        private static AssetFormatException WithPath(string path, AssetFormatException exception)
        {
            return new AssetFormatException(path + ": " + exception.Message,
                exception, exception.Offset);
        }

        private static string RequireDirectivePath(DatDirective directive, string datPath)
        {
            if (string.IsNullOrEmpty(directive.Path))
                throw DirectiveError(datPath, directive, "A file path is required.");
            return directive.Path;
        }

        private static byte ParseCollisionLevel(DatDirective directive, string datPath)
        {
            if (directive.Arguments.Count < 2)
                throw DirectiveError(datPath, directive, "COLFILE requires a level and a file path.");
            byte level;
            if (!byte.TryParse(directive.Arguments[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out level))
            {
                throw DirectiveError(datPath, directive,
                    "Invalid COLFILE level '" + directive.Arguments[0] + "'.");
            }
            return level;
        }

        private static AssetFormatException DirectiveError(
            string datPath, DatDirective directive, string message)
        {
            return new AssetFormatException(datPath + " line " +
                directive.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }

        private static bool IsNotImplemented(string command)
        {
            for (int i = 0; i < NotImplementedDirectives.Length; i++)
            {
                if (string.Equals(command, NotImplementedDirectives[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void Accumulate(
            Dictionary<string, DirectiveGroup> groups, DatDirective directive)
        {
            DirectiveGroup group;
            if (!groups.TryGetValue(directive.Command, out group))
            {
                group = new DirectiveGroup();
                group.FirstLine = directive.LineNumber;
                groups.Add(directive.Command, group);
            }
            group.Count++;
        }

        private void ReportGroups(Dictionary<string, DirectiveGroup> groups, string path,
            string category, string message)
        {
            foreach (KeyValuePair<string, DirectiveGroup> pair in groups)
            {
                Report.Add(LoadIssueSeverity.Warning, category, path, pair.Value.FirstLine,
                    string.Format(CultureInfo.InvariantCulture, message, pair.Key,
                        pair.Value.Count.ToString(CultureInfo.InvariantCulture)));
                Stats.IgnoredDirectiveCount += pair.Value.Count;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("GameDataLoader");
        }

        private sealed class DirectiveGroup
        {
            internal int FirstLine;
            internal int Count;
        }
    }
}
