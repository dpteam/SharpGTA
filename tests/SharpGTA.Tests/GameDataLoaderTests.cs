using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class GameDataLoaderTests
    {
        [Test]
        public void DatExecutesLoadersInSourceOrderAndAggregatesIgnoredDirectives()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            files.AddText("DATA/A.IDE", "objs\n1, ground, generic, 1, 100, 0\nend\n");
            files.AddBytes("DATA/A.COL", WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f));
            files.AddText("DATA/A.IPL",
                "inst\n1, ground, 10, 20, 3, 5, 5, 5, 0, 0, 1, 1\nend\n");
            files.AddText("DATA/A.ZON", "zone\nTESTZONE, 3, 0, 0, 0, 10, 10, 10, 7\nend\n");

            AddImage(files, "MODELS/EXTRA.IMG", "EXTRA");
            string imageDirectives = "CDIMAGE MODELS/EXTRA.IMG\nCDIMAGE models/extra.img\n";
            files.AddText("DATA/TEST.DAT",
                "IDE DATA/A.IDE\n" +
                "COLFILE 4 DATA/A.COL\n" +
                "IPL DATA/A.IPL\n" +
                imageDirectives +
                "TEXDICTION MODELS/A.TXD\n" +
                "TEXDICTION MODELS/B.TXD\n" +
                "MODELFILE MODELS/A.DFF\n" +
                "HIERFILE DATA/A.HIER\n" +
                "IMAGEPATH MODELS/\n" +
                "MAPZONE DATA/A.ZON\n" +
                "SPLASH LOGO\n" +
                "MYSTERY VALUE\n" +
                "EXIT\n" +
                "IDE DATA/NOT_READ.IDE\n");

            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20), files))
            {
                loader.LoadDat("DATA/TEST.DAT");
                Assert.Equal(1, loader.Stats.DatFileCount);
                Assert.Equal(1, loader.Stats.IdeFileCount);
                Assert.Equal(1, loader.Stats.ColFileCount);
                Assert.Equal(1, loader.Stats.IplFileCount);
                Assert.Equal(1, loader.Stats.ImageCount);
                Assert.Equal(1, loader.Stats.InstanceCount);
                Assert.Equal(0, loader.Stats.SkippedInstanceCount);
                Assert.Equal(7, loader.Stats.IgnoredDirectiveCount);
                Assert.Equal(1, loader.Models.Count);
                Assert.Equal((byte)4, loader.Models.Find(1).Collision.Level);
                Assert.Near(4.0f, loader.World.Instances[0].WorldBounds.Max.X -
                    loader.World.Instances[0].WorldBounds.Min.X);
                Assert.Equal(6, loader.Report.Count);
                Assert.Equal(6, loader.Report.TotalIssueCount);
                Assert.Equal(1, loader.Stats.MapZoneFileCount);
                Assert.Equal(1, loader.Stats.ZoneRecordCount);
                Assert.Equal(7, loader.Zones.GetRawLevelFromPosition(new SharpGTA.Mathematics.Vector3(5.0f, 5.0f, 5.0f)));
                Assert.Equal(0, loader.Report.DroppedIssueCount);

                StaticWorldHit hit;
                Assert.True(loader.World.QueryVertical(10.0f, 20.0f, -5.0f, 10.0f, out hit));
                Assert.Near(3.0f, hit.Position.Z);

                string[] expected = { "DATA/TEST.DAT", "DATA/A.IDE", "DATA/A.COL", "DATA/A.IPL",
                    "MODELS/EXTRA.DIR", "MODELS/EXTRA.IMG", "DATA/A.ZON" };
                AssertReads(files, expected);
            }
        }

        [Test]
        public void StandardLoadUsesInitialImageThenDefaultAndMainDat()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            AddImage(files, "MODELS/GTA3.IMG", "INITIAL");
            files.AddText("DATA/DEFAULT.DAT", "# default\n");
            files.AddText("DATA/MAIN.DAT", "# main\n");

            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20), files))
            {
                loader.LoadStandard();
                Assert.Equal(2, loader.Stats.DatFileCount);
                Assert.Equal(1, loader.Stats.ImageCount);
                AssertReads(files, "MODELS/GTA3.DIR", "MODELS/GTA3.IMG",
                    "DATA/DEFAULT.DAT", "DATA/MAIN.DAT");
            }
        }

        [Test]
        public void InitialImageUsesStreamingWithoutWholeImageFallback()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            AddImage(files, "MODELS/GTA3.IMG", "INITIAL");
            GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20), files);
            loader.LoadInitialImage();
            Assert.False(loader.LoadImage("MODELS//GTA3.IMG"));
            Assert.Equal(1, loader.Images.Count);
            Assert.Equal(1, loader.Stats.ImageCount);
            MemoryGameFileSystem.StreamState imageState = files.LastStreamState("MODELS/GTA3.IMG");
            Assert.NotNull(imageState);
            Assert.Equal(0, imageState.BytesRead);
            byte[] entry = loader.Images.Read("initial");
            Assert.Equal(ImgDirectory.SectorSize, entry.Length);
            Assert.Equal((byte)0x5A, entry[0]);
            Assert.Equal(ImgDirectory.SectorSize, imageState.BytesRead);
            loader.Dispose();
            Assert.True(imageState.Disposed);
            Assert.Throws<ObjectDisposedException>(delegate { loader.LoadInitialImage(); });
        }

        [Test]
        public void ViceCityDefinitionControlsTypedLayoutsAndInstanceArea()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            files.AddText("DATA/VC.IDE",
                "cars\n130, landstal, landstal, car, LANDSTAL, LANDSTAL, null, normal, 10, 0, 1a, 0, 1.0\nend\n" +
                "peds\n9, male01, male01, CIVMALE, STAT_CIVMALE, man, 0x12, null, 1, 2\nend\n" +
                "weap\n170, bat, bat, null, 1, 50.0\nend\n");
            files.AddText("DATA/VC.IPL",
                "inst\n170, bat, 2.5, 1, 2, 3, 1, 1, 1, 0, 0, 1, 1\nend\n");

            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.ViceCity, DataSchema.VC, 200), files))
            {
                loader.LoadIde("DATA/VC.IDE");
                loader.LoadIpl("DATA/VC.IPL");
                Assert.Equal(ModelKind.Vehicle, loader.Models.Find(130).Kind);
                Assert.Equal(ModelKind.Ped, loader.Models.Find(9).Kind);
                Assert.Equal(ModelKind.Weapon, loader.Models.Find(170).Kind);
                Assert.Equal(1, loader.World.Count);
                Assert.True(loader.World.Instances[0].HasArea);
                Assert.Near(2.5f, loader.World.Instances[0].Area);
            }
        }

        [Test]
        public void ViceCityLoadsEmbeddedCollisionBeforeFirstIpl()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            byte[] collision = WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f);
            byte[] directory;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(0u);
                writer.Write(1u);
                writer.Write(Encoding.ASCII.GetBytes("GROUND.COL"));
                writer.Write(new byte[24 - "GROUND.COL".Length]);
                writer.Write(1u);
                writer.Write(1u);
                writer.Write(Encoding.ASCII.GetBytes("SECOND.COL"));
                writer.Write(new byte[24 - "SECOND.COL".Length]);
                writer.Flush();
                directory = stream.ToArray();
            }
            byte[] image = new byte[ImgDirectory.SectorSize * 2];
            byte[] secondCollision = WorldTestAssets.CreateBoxCollision("second", 1.0f, -2.0f, -1.0f);
            Buffer.BlockCopy(collision, 0, image, 0, collision.Length);
            Buffer.BlockCopy(secondCollision, 0, image, ImgDirectory.SectorSize, secondCollision.Length);
            files.AddBytes("MODELS/GTA3.DIR", directory);
            files.AddBytes("MODELS/GTA3.IMG", image);
            files.AddText("DATA/GROUND.IDE", "objs\n1, ground, generic, 1, 100, 0\nend\n");
            files.AddText("DATA/GROUND.IPL", "inst\n1, ground, 10, 20, 3, 1, 1, 1, 0, 0, 1, 1\nend\n");

            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.ViceCity, DataSchema.VC, 20), files))
            {
                loader.LoadInitialImage();
                loader.LoadIde("DATA/GROUND.IDE");
                loader.LoadIpl("DATA/GROUND.IPL");
                Assert.Equal(2, loader.Stats.ColFileCount);
                Assert.Equal(2, loader.Stats.EmbeddedColFileCount);
                Assert.Equal((byte)1, loader.Models.Find(1).Collision.Level);
                Assert.Equal((byte)2, loader.Collisions.Find("second").Level);
                Assert.Equal(0, loader.LoadEmbeddedCollision(), "Initial collision pass is idempotent.");
                StaticWorldHit hit;
                Assert.True(loader.World.QueryVertical(10.0f, 20.0f, -5.0f, 10.0f, out hit));
                Assert.Near(3.0f, hit.Position.Z);
            }
        }

        [Test]
        public void ViceCityBindsTypedZonesOcclusionAndPaths()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            files.AddText("DATA/MAP.IDE", "objs\n1, ground, generic, 1, 100, 0\nend\n");
            string nodes = "";
            for (int i = 0; i < 12; i++)
                nodes += "1, 0, 0, 16, 0, 0, 10, 1, 1, 0, 0, 1\n";
            files.AddText("DATA/MAP.IPL",
                "inst\n1, ground, 10, 20, 3, 1, 1, 1, 0, 0, 1, 1\nend\n" +
                "zone\ndefaultArea, 0, -1, -1, -1, 1, 1, 1, 0\ninfoArea, 2, -2, -2, -2, 2, 2, 2, 0\nend\n" +
                "cull\n0, 0, 0, -1, -1, -1, 1, 1, 1, 3, 2\nend\n" +
                "occl\n0, 0, 0, 4, 6, 8, 90\nend\n" +
                "path\n1, 1, ground\n" + nodes + "end\n");

            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.ViceCity, DataSchema.VC, 20), files))
            {
                loader.LoadIde("DATA/MAP.IDE");
                loader.LoadIpl("DATA/MAP.IPL");
                Assert.Equal(2, loader.Stats.ZoneRecordCount);
                Assert.Equal(1, loader.Stats.CullZoneCount);
                Assert.Equal(1, loader.Stats.OcclusionVolumeCount);
                Assert.Equal(1, loader.Stats.PathGroupCount);
                Assert.Equal(12, loader.Stats.PathNodeCount);
                Assert.Equal(0, loader.Stats.UnresolvedPathGroupCount);
                Assert.Equal(2, loader.Zones.Navigation.Count);
                Assert.Equal(2, loader.Zones.Information.Count);
                Assert.Equal(1, loader.Zones.CullZones.Count);
                Assert.Equal(1, loader.Zones.OcclusionVolumes.Count);
                Assert.Near(4.0f, loader.Zones.OcclusionVolumes[0].Center.Z);
                int wanted;
                Assert.Equal(3, loader.Zones.FindCullAttributes(SharpGTA.Mathematics.Vector3.Zero, out wanted));
                Assert.Equal(2, wanted);
                SharpGTA.Mathematics.Vector3 position = loader.Paths.GetWorldPosition(loader.Paths.Groups[0], loader.Paths.Groups[0].Nodes[0]);
                Assert.Near(11.0f, position.X);
                Assert.Near(20.0f, position.Y);
                Assert.Near(3.0f, position.Z);
            }
        }

        [Test]
        public void MissingAndMalformedFilesThrowWithPathContext()
        {
            MemoryGameFileSystem files = new MemoryGameFileSystem();
            using (GameDataLoader loader = new GameDataLoader(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20), files))
            {
                FileNotFoundException missing = Assert.Throws<FileNotFoundException>(delegate
                {
                    loader.LoadDat("DATA/MISSING.DAT");
                });
                Assert.True(missing.Message.IndexOf("DATA/MISSING.DAT", StringComparison.OrdinalIgnoreCase) >= 0);

                files.AddBytes("DATA/BAD.COL", new byte[] { 1, 2, 3 });
                AssetFormatException malformed = Assert.Throws<AssetFormatException>(delegate
                {
                    loader.LoadCollision("DATA/BAD.COL", 0);
                });
                Assert.True(malformed.Message.IndexOf("DATA/BAD.COL", StringComparison.OrdinalIgnoreCase) >= 0);

                files.AddText("DATA/BADLEVEL.DAT", "COLFILE nope DATA/UNUSED.COL\n");
                malformed = Assert.Throws<AssetFormatException>(delegate
                {
                    loader.LoadDat("DATA/BADLEVEL.DAT");
                });
                Assert.True(malformed.Message.IndexOf("DATA/BADLEVEL.DAT", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        private static void AddImage(MemoryGameFileSystem files, string path, string entryName)
        {
            byte[] directory;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(1u);
                writer.Write(1u);
                byte[] name = Encoding.ASCII.GetBytes(entryName);
                writer.Write(name);
                writer.Write(new byte[24 - name.Length]);
                writer.Flush();
                directory = stream.ToArray();
            }
            byte[] image = new byte[ImgDirectory.SectorSize * 3];
            image[ImgDirectory.SectorSize] = 0x5A;
            files.AddBytes(Path.ChangeExtension(path, ".DIR"), directory);
            files.AddBytes(path, image);
        }

        private static void AssertReads(MemoryGameFileSystem files, params string[] expected)
        {
            Assert.Equal(expected.Length, files.Reads.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], files.Reads[i]);
        }

        private sealed class MemoryGameFileSystem : IGameFileSystem
        {
            internal sealed class StreamState
            {
                internal int BytesRead;
                internal bool Disposed;
            }

            private sealed class TrackedMemoryStream : MemoryStream
            {
                private readonly StreamState state;

                internal TrackedMemoryStream(byte[] bytes, StreamState state)
                    : base(bytes, false)
                {
                    this.state = state;
                }

                public override int Read(byte[] buffer, int offset, int count)
                {
                    int read = base.Read(buffer, offset, count);
                    state.BytesRead += read;
                    return read;
                }

                public override int ReadByte()
                {
                    int value = base.ReadByte();
                    if (value >= 0)
                        state.BytesRead++;
                    return value;
                }

                protected override void Dispose(bool disposing)
                {
                    state.Disposed = true;
                    base.Dispose(disposing);
                }
            }

            private readonly Dictionary<string, byte[]> files =
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, StreamState> streamStates =
                new Dictionary<string, StreamState>(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> opened = new List<string>();
            internal readonly List<string> Reads = new List<string>();

            public string Root { get { return "memory"; } }

            internal void AddText(string path, string text)
            {
                char[] characters = text.ToCharArray();
                byte[] bytes = new byte[characters.Length];
                for (int i = 0; i < characters.Length; i++)
                    bytes[i] = (byte)characters[i];
                AddBytes(path, bytes);
            }

            internal void AddBytes(string path, byte[] bytes)
            {
                files[Normalize(path)] = bytes;
            }

            public bool FileExists(string relativePath)
            {
                return files.ContainsKey(Normalize(relativePath));
            }

            public Stream OpenRead(string relativePath)
            {
                string path = Normalize(relativePath);
                byte[] bytes = Get(path);
                Reads.Add(path);
                opened.Add(path);
                StreamState state = new StreamState();
                streamStates[path] = state;
                return new TrackedMemoryStream(bytes, state);
            }

            public byte[] ReadAllBytes(string relativePath, int maximumLength)
            {
                string path = Normalize(relativePath);
                byte[] bytes = Get(path);
                Reads.Add(path);
                if (bytes.Length > maximumLength)
                    throw new AssetFormatException("Data exceeds the configured limit.");
                return (byte[])bytes.Clone();
            }

            public string ReadAllText(string relativePath, int maximumLength)
            {
                byte[] bytes = ReadAllBytes(relativePath, maximumLength);
                char[] characters = new char[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                    characters[i] = (char)bytes[i];
                return new string(characters);
            }

            internal int OpenCount(string path)
            {
                string normalized = Normalize(path);
                int count = 0;
                for (int i = 0; i < opened.Count; i++)
                {
                    if (string.Equals(opened[i], normalized, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                return count;
            }

            internal StreamState LastStreamState(string path)
            {
                StreamState state;
                return streamStates.TryGetValue(Normalize(path), out state) ? state : null;
            }

            private byte[] Get(string path)
            {
                byte[] bytes;
                if (!files.TryGetValue(path, out bytes))
                    throw new FileNotFoundException("Missing in-memory file: " + path, path);
                return bytes;
            }

            private static string Normalize(string path)
            {
                if (path == null)
                    throw new ArgumentNullException("path");
                return path.Replace('\\', '/');
            }
        }
    }
}
