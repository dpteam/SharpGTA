using System;
using System.Collections.Generic;
using System.Globalization;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.IO;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class ObjectDataTests
    {
        private const string Row =
            "magnet\t\t\t99999.0,\t99999.0\t0.99,\t\t0.1,\t\t50.0,\t\t125.0,\t1.0,\t0,\t0,\t0\n";

        [Test]
        public void ParsesRetailRowsAndStopsAtTheSentinel()
        {
            ObjectData data = new ObjectDataReader().Read(
                ";\n; Object.dat\n;\n" +
                Row +
                "coffee\t\t\t2000.0,\t2000.0\t0.99,\t\t0.1,\t\t50.0,\t\t99999.0,\t0.52,\t1,\t0,\t0\n" +
                "* ;end of file\n" +
                Row.Replace("magnet", "ignored_after_sentinel"));

            Assert.Equal(2, data.Count);
            Assert.True(data.Contains("MAGNET"));
            Assert.False(data.Contains("ignored_after_sentinel"));
            ObjectDataEntry entry;
            Assert.True(data.TryGet("coffee", out entry));
            Assert.Near(2000.0f, entry.Mass);
            Assert.Near(0.52f, entry.CollisionDamageMultiplier);
            Assert.Equal(1, entry.DamageEffect);
            Assert.False(data.TryGet(null, out entry));
            Assert.False(data.Contains(null));
        }

        [Test]
        public void DuplicateRowsKeepTheLastValuesAndStaySingle()
        {
            ObjectData data = new ObjectDataReader().Read(
                "vheistlocdoor\t100.0,\t100.0\t0.99,\t0.1,\t50.0,\t0.0,\t1.0,\t0,\t4,\t1\n" +
                "vheistlocdoor\t200.0,\t300.0\t0.5,\t0.6,\t40.0,\t7.0,\t2.0,\t3,\t4,\t1\n");
            Assert.Equal(1, data.Count);
            ObjectDataEntry entry;
            Assert.True(data.TryGet("vheistlocdoor", out entry));
            Assert.Near(200.0f, entry.Mass);
            Assert.Near(7.0f, entry.UprootLimit);
            Assert.Equal(3, entry.DamageEffect);
        }

        [Test]
        public void ExtraTrailingColumnsAreIgnoredButShortRowsFail()
        {
            ObjectData data = new ObjectDataReader().Read(
                "extra\t1.0,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\t0,\t0,\t0,\t9.9,\t123,\tsomething\n");
            Assert.Equal(1, data.Count);
            ObjectDataEntry entry;
            Assert.True(data.TryGet("extra", out entry));
            Assert.Near(4.0f, entry.UprootLimit);

            Assert.Throws<AssetFormatException>(delegate
            {
                new ObjectDataReader().Read("short\t1.0,\t2.0\t0.99\n");
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                new ObjectDataReader().Read("bad\tX,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\t0,\t0,\t0\n");
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                new ObjectDataReader().Read("bad\t1.0,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\tq,\t0,\t0\n");
            });
        }

        [Test]
        public void CommentsAnywhereOnALineAndBomDoNotBreakParsing()
        {
            ObjectData data = new ObjectDataReader().Read(
                "\uFEFFmagnet\t99999.0,\t99999.0\t0.99,\t0.1,\t50.0,\t125.0,\t1.0,\t0,\t0,\t0\r\n" +
                "coffee\t2000.0,\t2000.0\t0.99,\t0.1,\t50.0,\t99999.0,\t0.52,\t1,\t0,\t0 ; trailing note\r\n");
            Assert.Equal(2, data.Count);
            Assert.True(data.Contains("magnet"));
            ObjectDataEntry entry;
            Assert.True(data.TryGet("coffee", out entry));
            Assert.Near(2000.0f, entry.Mass);
        }

        [Test]
        public void MalformedRowsBecomeWarningsWhenAReportIsSupplied()
        {
            LoadReport report = new LoadReport();
            ObjectData data = new ObjectDataReader().Read(
                "good\t1.0,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\t0,\t0,\t0\n" +
                "short\t1.0,\t2.0\n" +
                "good2\t1.0,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\t0,\t0,\t0\n",
                "OBJECT.DAT", report);
            // The broken row is skipped but the valid rows still load, like sscanf continuing.
            Assert.Equal(2, data.Count);
            Assert.True(data.Contains("good"));
            Assert.True(data.Contains("good2"));
            Assert.Equal(1, report.Count);
            Assert.Equal(LoadIssueSeverity.Warning, report.Items[0].Severity);
            Assert.Equal("ObjectData", report.Items[0].Category);
            Assert.Equal(2, report.Items[0].Line);
        }

        [Test]
        public void EntityLimitsMatchTheOriginalPoolSizes()
        {
            GameLimits iii = SharpGTA.Games.III.IIIDefinition.Instance.Limits;
            Assert.Equal(5500, iii.Buildings);
            Assert.Equal(450, iii.Objects);
            Assert.Equal(140, iii.Peds);
            Assert.Equal(110, iii.Vehicles);
            Assert.Equal(2802, iii.Dummies);
            Assert.Equal(1214, iii.Treadables);

            GameLimits vc = SharpGTA.Games.VC.VCDefinition.Instance.Limits;
            Assert.Equal(7000, vc.Buildings);
            Assert.Equal(460, vc.Objects);
            Assert.Equal(140, vc.Peds);
            Assert.Equal(110, vc.Vehicles);
            Assert.Equal(2340, vc.Dummies);
            Assert.Equal(1, vc.Treadables);
        }
    }

    public sealed class WorldEntityTests
    {
        private static readonly string BuildRow =
            "objmodel\t100.0,\t100.0\t0.99,\t0.1,\t50.0,\t0.0,\t1.0,\t0,\t0,\t0\n";

        [Test]
        public void PlacementsAreClassifiedByObjectDataAndPathModels()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n" +
                "1, plain, generic, 1, 300, 0\n" +
                "2, objmodel, generic, 1, 300, 0\n" +
                "3, pathmodel, generic, 1, 300, 0\n" +
                "end\n"));

            ObjectData objects = new ObjectDataReader().Read(BuildRow);
            PathWorld paths = new PathWorld();
            paths.AddGroups(ParsePathGroups("ped, 3, pathmodel\n" + Repeat("1, 0, 0, 16, 32, 48, 160", 12)));

            StaticWorld world = new StaticWorld();
            world.SetEntityClassification(objects, paths);
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n" +
                "1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "2, objmodel, 10, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "3, pathmodel, 20, 0, 0, 1, 1, 1, 0, 0, 0, 1\n" +
                "end\n"), registry, new LoadReport());

            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Building));
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Dummy));
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Treadable));
            Assert.Equal(0, world.Entities.Count(WorldEntityKind.Object));
            Assert.Equal(0, world.InstancesWithoutHandle);

            StaticInstance plain = Find(world, "plain");
            StaticInstance dummy = Find(world, "objmodel");
            StaticInstance treadable = Find(world, "pathmodel");
            Assert.True(plain.Handle > 0 && dummy.Handle > 0 && treadable.Handle > 0);

            WorldEntity resolved;
            Assert.True(world.TryGetEntity(WorldEntityKind.Building, plain.Handle, out resolved));
            Assert.True(ReferenceEquals(plain, resolved));
            Assert.True(world.TryGetEntity(WorldEntityKind.Dummy, dummy.Handle, out resolved));
            Assert.True(ReferenceEquals(dummy, resolved));
            Assert.True(world.TryGetEntity(WorldEntityKind.Treadable, treadable.Handle, out resolved));
            Assert.True(ReferenceEquals(treadable, resolved));
            // Raw handles are per-pool, exactly like CPools::GetObject vs GetBuilding: the
            // same numeric handle means something else in another pool, so the kind is part
            // of the lookup and a cross-pool read must not return the dummy.
            Assert.True(world.TryGetEntity(WorldEntityKind.Building, dummy.Handle, out resolved));
            Assert.False(ReferenceEquals(dummy, resolved));
            StaticInstance building;
            Assert.True(world.TryGetBuilding(plain.Handle, out building));
            Assert.True(ReferenceEquals(plain, building));
        }

        [Test]
        public void PoolExhaustionIsReportedPerKindWithoutThrowing()
        {
            GameLimits limits = new GameLimits(32, 16, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1);
            WorldEntities entities = new WorldEntities(limits);
            List<StaticInstance> instances = new List<StaticInstance>();
            StaticInstance first = CreateInstance();
            StaticInstance second = CreateInstance();
            StaticInstance third = CreateInstance();
            instances.Add(first);
            instances.Add(second);
            instances.Add(third);

            int firstHandle = entities.Create(WorldEntityKind.Building, first);
            int secondHandle = entities.Create(WorldEntityKind.Building, second);
            Assert.True(firstHandle > 0 && secondHandle > 0);
            Assert.Equal(-1, entities.Create(WorldEntityKind.Building, third));
            Assert.Equal(1, entities.RejectedCount(WorldEntityKind.Building));
            Assert.Equal(2, entities.Count(WorldEntityKind.Building));
            Assert.Equal(1, entities.TotalRejectedCount);
            Assert.Equal(2, entities.Capacity(WorldEntityKind.Building));

            WorldEntity resolved;
            Assert.True(entities.TryGet(WorldEntityKind.Building, firstHandle, out resolved));
            Assert.True(entities.Release(WorldEntityKind.Building, firstHandle));
            Assert.False(entities.TryGet(WorldEntityKind.Building, firstHandle, out resolved));
            // Reuse of the slot bumps the generation, so the released handle stays dead.
            int reused = entities.Create(WorldEntityKind.Building, third);
            Assert.True(reused > 0);
            Assert.False(entities.TryGet(WorldEntityKind.Building, firstHandle, out resolved));

            entities.Clear();
            Assert.Equal(0, entities.Count(WorldEntityKind.Building));
            Assert.Equal(0, entities.TotalRejectedCount);
            Assert.Equal(1, entities.Capacity(WorldEntityKind.Ped));
            Assert.Equal(1, entities.Capacity(WorldEntityKind.Vehicle));
        }

        [Test]
        public void ObjectsWithoutClassificationBecomeBuildings()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Building));
            Assert.Equal(0, world.Entities.Count(WorldEntityKind.Dummy));
            Assert.Equal(0, world.Entities.Count(WorldEntityKind.Treadable));
            StaticInstance building;
            Assert.True(world.TryGetBuilding(world.Instances[0].Handle, out building));
            Assert.True(ReferenceEquals(world.Instances[0], building));
        }

        [Test]
        public void OneWorldCannotMixGameDefinitions()
        {
            StaticWorld world = new StaticWorld();
            ModelRegistry iii = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            iii.Bind(new IdeReader(DataSchema.III).Read("objs\n1, plain, generic, 1, 300, 0\nend\n"));
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"), iii, new LoadReport());

            ModelRegistry vc = new ModelRegistry(
                new WorldTestDefinition(GameId.ViceCity, DataSchema.VC, 16));
            vc.Bind(new IdeReader(DataSchema.VC).Read("objs\n1, plain, generic, 1, 300, 0\nend\n"));
            Assert.Throws<InvalidOperationException>(delegate
            {
                world.Bind(new IplReader(DataSchema.VC).Read(
                    "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"), vc, new LoadReport());
            });
        }

        [Test]
        public void ObjectDataWinsOverAPathHeaderForTheSameModel()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, both, generic, 1, 300, 0\nend\n"));
            ObjectData objects = new ObjectDataReader().Read(
                "both\t100.0,\t100.0\t0.99,\t0.1,\t50.0,\t0.0,\t1.0,\t0,\t0,\t0\n");
            PathWorld paths = new PathWorld();
            paths.AddGroups(ParsePathGroups("ped, 1, both\n" + Repeat("1, 0, 0, 16, 32, 48, 160", 12)));

            StaticWorld world = new StaticWorld();
            world.SetEntityClassification(objects, paths);
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, both, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"), registry, new LoadReport());
            // The original checks GetObjectID before IsPathObject, so object.dat wins.
            Assert.Equal(1, world.Entities.Count(WorldEntityKind.Dummy));
            Assert.Equal(0, world.Entities.Count(WorldEntityKind.Treadable));
        }

        [Test]
        public void PathObjectNamesAreCaseInsensitiveAndNullSafe()
        {
            PathWorld paths = new PathWorld();
            paths.AddGroups(ParsePathGroups("ped, 7, Road_A\n" + Repeat("1, 0, 0, 16, 32, 48, 160", 12)));
            Assert.True(paths.IsPathObject("road_a"));
            Assert.True(paths.IsPathObject("ROAD_A"));
            Assert.False(paths.IsPathObject("other"));
            Assert.False(paths.IsPathObject(null));
        }

        [Test]
        public void StaticWorldReportsPlacementsWithoutAHandleWhenThePoolIsFull()
        {
            // The synthetic definition allows 64 buildings; bind 70 plain placements.
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            System.Text.StringBuilder ipl = new System.Text.StringBuilder("inst\n");
            for (int i = 0; i < 70; i++)
                ipl.Append("1, plain, ").Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append(", 0, 0, 1, 1, 1, 0, 0, 0, 1\n");
            ipl.Append("end\n");
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(ipl.ToString()), registry, new LoadReport());

            Assert.Equal(70, world.Count);
            Assert.Equal(64, world.Entities.Count(WorldEntityKind.Building));
            Assert.Equal(6, world.InstancesWithoutHandle);
            int without = 0;
            for (int i = 0; i < world.Instances.Count; i++)
            {
                if (world.Instances[i].Handle == -1)
                    without++;
            }
            Assert.Equal(6, without);
        }

        [Test]
        public void HandlesSurviveACollisionRevisionRebuild()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            CollisionRepository collisions = new CollisionRepository(registry);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            int handle = world.Instances[0].Handle;
            Assert.True(handle > 0);

            // Adding COL bumps CollisionRevision, so the next query rebuilds the sector grid.
            collisions.Add(WorldTestAssets.CreateBoxCollision("plain", 20.0f, -1.0f, 1.0f), 0);
            List<StaticInstance> results = new List<StaticInstance>();
            Assert.Equal(1, world.QuerySectors(Vector3.Zero, 50.0f, results));
            Assert.Equal(handle, world.Instances[0].Handle);
            StaticInstance building;
            Assert.True(world.TryGetBuilding(handle, out building));
            Assert.True(ReferenceEquals(world.Instances[0], building));
        }

        [Test]
        public void ClassificationCannotChangeAfterTheFirstBind()
        {
            StaticWorld world = new StaticWorld();
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            Assert.Throws<InvalidOperationException>(delegate
            {
                world.SetEntityClassification(new ObjectDataReader().Read(
                    "plain\t1.0,\t2.0\t0.99,\t0.1,\t50.0,\t4.0,\t0.5,\t0,\t0,\t0\n"), null);
            });
        }

        private static PathGroup[] ParsePathGroups(string body)
        {
            const string sourceName = "entity-paths.ide";
            IdeDocument document = new IdeReader(DataSchema.III).Read(
                "path\n" + body + "end\n", sourceName);
            return PathRecordParser.ParseGroups(document.RawRecords, DataSchema.III, sourceName);
        }

        private static string Repeat(string value, int count)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
                builder.Append(value).Append('\n');
            return builder.ToString();
        }

        private static StaticInstance CreateInstance()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 16));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            return world.Instances[0];
        }

        private static StaticInstance Find(StaticWorld world, string name)
        {
            for (int i = 0; i < world.Instances.Count; i++)
            {
                if (string.Equals(world.Instances[i].Model.Name, name, StringComparison.OrdinalIgnoreCase))
                    return world.Instances[i];
            }
            throw new InvalidOperationException("Synthetic instance was not found: " + name);
        }
    }
}