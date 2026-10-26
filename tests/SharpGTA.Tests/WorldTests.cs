using System;
using System.Globalization;
using System.IO;
using System.Text;
using SharpGTA.Assets;
using SharpGTA.Collision;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    internal sealed class WorldTestDefinition : GameDefinition
    {
        internal WorldTestDefinition(GameId id, DataSchema schema, int modelCapacity)
            : base(id, "Synthetic " + id, "DATA/MAIN.DAT", "test", 24, 4,
                id == GameId.ViceCity ? CollisionStorageKind.StreamedSlots : CollisionStorageKind.LevelBased, schema,
                new GameLimits(modelCapacity, 16, 1, 1, 1, 1, 1, 1))
        {
        }
    }

    internal static class WorldTestAssets
    {
        internal static byte[] CreateBoxCollision(string modelName, float halfExtent,
            float minimumZ, float maximumZ)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(ColFile.ModelIdent);
                writer.Write(0u);
                WriteName(writer, modelName);
                writer.Write(halfExtent * 4.0f);
                WriteVector(writer, Vector3.Zero);
                Vector3 minimum = new Vector3(-halfExtent, -halfExtent, minimumZ);
                Vector3 maximum = new Vector3(halfExtent, halfExtent, maximumZ);
                WriteVector(writer, minimum);
                WriteVector(writer, maximum);
                WriteCount(writer, 0); // spheres
                WriteCount(writer, 0); // lines
                WriteCount(writer, 1); // boxes
                WriteVector(writer, minimum);
                WriteVector(writer, maximum);
                writer.Write((byte)17);
                writer.Write((byte)3);
                writer.Write((ushort)0);
                WriteCount(writer, 0); // vertices
                WriteCount(writer, 0); // triangles

                long end = stream.Position;
                stream.Position = 4;
                writer.Write((uint)(end - 8));
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteName(BinaryWriter writer, string name)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name);
            if (bytes.Length > 24)
                throw new ArgumentException("Synthetic collision name is too long.", "name");
            writer.Write(bytes);
            writer.Write(new byte[24 - bytes.Length]);
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private static void WriteCount(BinaryWriter writer, short count)
        {
            writer.Write(count);
            writer.Write((short)0);
        }
    }

    public sealed class WorldTests
    {
        [Test]
        public void BindsRawIdsAndAllModelKindsIndependently()
        {
            WorldTestDefinition definition = new WorldTestDefinition(GameId.ViceCity, DataSchema.VC, 200);
            ModelRegistry registry = new ModelRegistry(definition);
            registry.Bind(new IdeReader(DataSchema.VC).Read(
                "objs\n10, building, generic, 1, 120.5, 2\nend\n" +
                "tobj\n11, clock, generic, 1, 50, 3, 6, 20\nend\n" +
                "hier\n12, clump, clump_txd\nend\n" +
                "cars\n130, landstal, landstal, car, LANDSTAL, LANDSTAL, null, normal, 10, 0, 1a, 0, 1.0\nend\n" +
                "peds\n9, male01, male01, CIVMALE, STAT_CIVMALE, man, 0x12, null, 1, 2\nend\n" +
                "weap\n170, bat, bat, null, 1, 50.0\nend\n"));

            Assert.Equal(6, registry.Count);
            Assert.Equal(ModelKind.Simple, registry.Find(10).Kind);
            Assert.Equal(ModelKind.Timed, registry.Find(11).Kind);
            Assert.Equal(ModelKind.Clump, registry.Find(12).Kind);
            Assert.Equal(ModelKind.Vehicle, registry.Find(130).Kind);
            Assert.Equal(ModelKind.Ped, registry.Find(9).Kind);
            Assert.Equal(ModelKind.Weapon, registry.Find(170).Kind);
            Assert.Equal(registry.Find(10), registry.Find("BuIlDiNg"));
            Assert.Equal("generic", registry.Find(10).Txd);
            Assert.Equal(2u, registry.Find(10).Flags);
            Assert.Near(120.5f, registry.Find(10).LodDistances[0]);
            Assert.Near(50.0f, registry.Find(170).LodDistances[0]);

            ModelRegistry other = new ModelRegistry(definition);
            other.Bind(new IdeReader(DataSchema.VC).Read(
                "objs\n10, unrelated, generic, 1, 10, 0\nend\n"));
            Assert.Equal("building", registry.Find(10).Name);
            Assert.Equal("unrelated", other.Find(10).Name);
        }

        [Test]
        public void RejectsDuplicateAndInvalidIdsWithoutPartialBindings()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, building, generic, 1, 10, 0\nend\n"));

            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate
            {
                registry.Bind(new IdeReader(DataSchema.III).Read(
                    "objs\n1, other, generic, 1, 10, 0\nend\n"));
            });
            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate
            {
                registry.Bind(new IdeReader(DataSchema.III).Read(
                    "objs\n2, BUILDING, generic, 1, 10, 0\nend\n"));
            });
            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate
            {
                registry.Bind(new IdeReader(DataSchema.III).Read(
                    "objs\n20, outside, generic, 1, 10, 0\nend\n"));
            });
            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate
            {
                registry.Bind(new IdeReader(DataSchema.III).Read(
                    "hier\nnot_an_id, clump, txd\nend\n"));
            });
            Assert.Equal(1, registry.Count);
            Assert.Equal(null, registry.Find(2));
            Assert.Equal(null, registry.Find(20));
        }

        [Test]
        public void CollisionLastWinsAndAttachesBeforeOrAfterModelBinding()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            CollisionRepository collisions = new CollisionRepository(registry);
            collisions.Add(WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f), 2);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, GROUND, generic, 1, 10, 0\nend\n"));
            Assert.Equal((byte)2, registry.Find(1).Collision.Level);

            collisions.Add(WorldTestAssets.CreateBoxCollision("GrOuNd", 2.0f, -1.0f, 4.0f), 7);
            Assert.Equal(1, collisions.ModelCount);
            Assert.Equal(2, collisions.LoadedModelCount);
            Assert.Equal(1, collisions.ReplacementCount);
            Assert.Equal(2, collisions.FileCount);
            Assert.Equal((byte)7, registry.Find(1).Collision.Level);
            Assert.Near(4.0f, registry.Find(1).Collision.BoundingBox.Max.Z);
            Assert.Equal(registry.Find(1).Collision, collisions.Find("GROUND"));
        }

        [Test]
        public void RecreatesOriginalIplRotationAndIgnoresScale()
        {
            ModelRegistry registry = CreateGroundRegistry(false);
            float w = (float)Math.Cos(Math.PI / 4.0);
            IplDocument document = new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 10, 20, 30, 7, 8, 9, 0, 0, 1, " +
                w.ToString("R", CultureInfo.InvariantCulture) + "\nend\n");
            StaticWorld world = new StaticWorld();
            world.Bind(document, registry, new LoadReport());
            StaticInstance instance = world.Instances[0];

            AssertVector(new Vector3(7.0f, 8.0f, 9.0f), instance.Scale);
            AssertVector(new Vector3(10.0f, 20.0f, 30.0f), instance.Position);
            AssertVector(new Vector3(10.0f, 19.0f, 30.0f),
                instance.Transform.TransformPoint(Vector3.UnitX));
            Assert.Near(-w, instance.Rotation.Z);
            Assert.Near(w, instance.Rotation.W);
            Assert.Near(1.0f, instance.Transform.Right.Length());
        }

        [Test]
        public void VerticalQueryTransformsTiltedGroundRayAndNormal()
        {
            ModelRegistry registry = CreateGroundRegistry(true, 10.0f, -0.25f, 0.0f);
            float w = (float)Math.Cos(Math.PI / 8.0); // angle = -45 degrees
            IplDocument document = new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 100, 200, 10, 9, 9, 9, 1, 0, 0, " +
                w.ToString("R", CultureInfo.InvariantCulture) + "\nend\n");
            StaticWorld world = new StaticWorld();
            world.Bind(document, registry, new LoadReport());

            StaticWorldHit hit;
            Assert.True(world.QueryVertical(101.0f, 201.0f, 30.0f, -30.0f, out hit));
            Assert.Equal(world.Instances[0], hit.Instance);
            AssertVector(new Vector3(101.0f, 201.0f, 9.0f), hit.Position, 0.0005f);
            AssertVector(new Vector3(0.0f, 0.7071068f, 0.7071068f), hit.Normal, 0.0005f);
            Assert.Near(21.0f, hit.Distance, 0.0005f);
            Assert.Equal((byte)17, hit.Surface);
            Assert.Equal(ColShapeType.Box, hit.ShapeType);
        }

        [Test]
        public void LateCollisionAndReplacementRefreshStaticBroadPhase()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            CollisionRepository collisions = new CollisionRepository(registry);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 100, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, ground, -1, -1, 3, 1, 1, 1, 0, 0, 1, 1\nend\n"),
                registry, new LoadReport());

            StaticWorldHit hit;
            Assert.False(world.QueryVertical(1.0f, 1.0f, -10.0f, 10.0f, out hit));
            collisions.Add(WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f), 0);
            Assert.True(world.QueryVertical(1.0f, 1.0f, -10.0f, 10.0f, out hit));
            Assert.Near(3.0f, hit.Position.Z);

            collisions.Add(WorldTestAssets.CreateBoxCollision("GROUND", 2.0f, -1.0f, 2.0f), 1);
            Assert.True(world.QueryVertical(1.0f, 1.0f, -10.0f, 10.0f, out hit));
            Assert.Near(5.0f, hit.Position.Z);
            Assert.Equal((byte)1, hit.Instance.Model.Collision.Level);
        }

        [Test]
        public void MissingModelsAreSkippedAndReportOverflowIsBounded()
        {
            ModelRegistry registry = CreateGroundRegistry(false);
            StringBuilder text = new StringBuilder("inst\n");
            for (int i = 0; i < 140; i++)
                text.Append("999, missing, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\n");
            text.Append("1, ground, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\nend\n");
            IplDocument document = new IplReader(DataSchema.III).Read(text.ToString(), "missing.ipl");
            LoadReport report = new LoadReport();
            StaticWorld world = new StaticWorld();

            Assert.Equal(1, world.Bind(document, registry, report, "missing.ipl"));
            Assert.Equal(128, report.Count);
            Assert.Equal(140, report.TotalIssueCount);
            Assert.Equal(12, report.DroppedIssueCount);
            Assert.Equal(12, report.OverflowCount);
            Assert.Equal("MissingModel", report.Items[0].Category);
            Assert.Equal("missing.ipl", report.Items[0].Path);
            report.Clear();
            Assert.Equal(0, report.Count);
            Assert.Equal(0, report.TotalIssueCount);
        }

        [Test]
        public void OutsideGridInstancesRemainQueryable()
        {
            ModelRegistry registry = CreateGroundRegistry(true);
            IplDocument document = new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 5000, 0, 2, 1, 1, 1, 0, 0, 1, 1\nend\n");
            StaticWorld world = new StaticWorld();
            world.Bind(document, registry, new LoadReport());

            StaticWorldHit hit;
            Assert.Equal(1, world.UnindexedCount);
            Assert.True(world.QueryVertical(5000.0f, 0.0f, -10.0f, 10.0f, out hit));
            Assert.Near(2.0f, hit.Position.Z);
            Assert.Equal(world.Instances[0], hit.Instance);
        }

        private static ModelRegistry CreateGroundRegistry(bool withCollision)
        {
            return CreateGroundRegistry(withCollision, 2.0f, -1.0f, 0.0f);
        }

        private static ModelRegistry CreateGroundRegistry(bool withCollision,
            float halfExtent, float minimumZ, float maximumZ)
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 20));
            CollisionRepository collisions = new CollisionRepository(registry);
            if (withCollision)
                collisions.Add(WorldTestAssets.CreateBoxCollision("ground", halfExtent, minimumZ, maximumZ), 0);
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 100, 0\nend\n"));
            return registry;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            AssertVector(expected, actual, 0.0001f);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float epsilon)
        {
            Assert.Near(expected.X, actual.X, epsilon);
            Assert.Near(expected.Y, actual.Y, epsilon);
            Assert.Near(expected.Z, actual.Z, epsilon);
        }
    }
}
