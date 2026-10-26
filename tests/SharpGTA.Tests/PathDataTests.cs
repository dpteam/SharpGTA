using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    internal sealed class PathTestDefinition : GameDefinition
    {
        internal PathTestDefinition()
            : base(GameId.III, "Synthetic path test", "DATA/MAIN.DAT", "test", 24, 4,
                CollisionStorageKind.LevelBased, DataSchema.III,
                new GameLimits(20, 16, 1, 1, 1, 1, 1, 1))
        {
        }
    }

    public sealed class PathDataTests
    {
        private const string IiiCarNode = "1, 0, 0, 16, 32, 48, 160, 1, 1";
        private const string VcCarNode = "1, 0, 0, 16, 32, 48, 10, 1, 1, 0, 0";

        [Test]
        public void IIIParsesPedAndCarGroupsWithTwelveRealLayoutNodes()
        {
            string body =
                "car, 2084, custom_rd4_ug\n" +
                "2, 2, 0, -398, 98, -81, 160, 1, 1\n" +
                Repeat("1, 0, 0, -228, -1479, -82, 160, 2, 2", 11) +
                "ped, 2096, com_park2\n" +
                "2, 9, 0, -59, 146, -72, 160, 1, 1\n" +
                "2, 2, 0, -926, 468, 16, 160\n" +
                "2, 3, 0, -844, 734, -65, 160, 2\n" +
                Repeat("1, -1, 0, 41, 157, -70, 160", 9);

            PathGroup[] groups = ParseIII(body);

            Assert.Equal(2, groups.Length);
            Assert.Equal("iii-paths.ide", groups[0].SourceName);
            Assert.Equal(2, groups[0].SourceHeaderLine.Value);
            Assert.Equal(15, groups[1].SourceHeaderLine.Value);
            Assert.Equal(PathKind.Car, groups[0].Kind);
            Assert.Equal(2084, groups[0].ModelId);
            Assert.Equal("custom_rd4_ug", groups[0].ModelName);
            Assert.False(groups[0].IsDetached);
            Assert.Equal(PathKind.Ped, groups[1].Kind);
            Assert.Equal(2096, groups[1].ModelId);
            Assert.Equal(12, groups[0].Nodes.Length);
            Assert.Equal(12, groups[1].Nodes.Length);
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(i, groups[0].Nodes[i].NodeIndex);
                Assert.Equal(i, groups[1].Nodes[i].NodeIndex);
            }

            PathNode car = groups[0].Nodes[0];
            Assert.Equal(2, car.Type);
            Assert.Equal(2, car.NextNodeIndex);
            Assert.False(car.Cross);
            AssertVector(new Vector3(-398.0f, 98.0f, -81.0f), car.StoredPosition);
            Assert.Near(160.0f, car.Width);
            Assert.Equal(1, car.NumLeftLanes);
            Assert.Equal(1, car.NumRightLanes);
            Assert.Equal(0, car.Speed);
            Assert.Equal(0, car.Flags);
            Assert.Near(0.0f, car.SpawnRate);

            PathNode pedWithLanes = groups[1].Nodes[0];
            Assert.Equal(9, pedWithLanes.NextNodeIndex);
            Assert.Equal(1, pedWithLanes.NumLeftLanes);
            Assert.Equal(1, pedWithLanes.NumRightLanes);
            Assert.Equal(0, groups[1].Nodes[1].NumLeftLanes);
            Assert.Equal(0, groups[1].Nodes[1].NumRightLanes);
            Assert.Equal(2, groups[1].Nodes[2].NumLeftLanes);
            Assert.Equal(0, groups[1].Nodes[2].NumRightLanes);
        }

        [Test]
        public void VCParsesDetachedPedCarWaterAndOptionalSpawnRate()
        {
            string body =
                "0, -1\n" +
                "2, 1, 1, 160, -32, 48, 12, 9, 8, 3, 7\n" +
                "1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.5\n" +
                Repeat("1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0", 10) +
                "1, -1, roads\n" +
                "2, 5, 1, 160, -32, 48, 10, 2, 3, 2, 2, 0.25\n" +
                Repeat("1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 1", 11) +
                "2, -1\n" +
                "2, -1, 0, -12950, -14283.8, 158.174, 10, 2, 2, 0, 0\n" +
                Repeat("1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0", 11);

            PathGroup[] groups = ParseVC(body);

            Assert.Equal(3, groups.Length);
            Assert.Equal(PathKind.Ped, groups[0].Kind);
            Assert.Equal(PathKind.Car, groups[1].Kind);
            Assert.Equal(PathKind.Water, groups[2].Kind);
            Assert.Equal(2, groups[0].SourceHeaderLine.Value);
            Assert.Equal(15, groups[1].SourceHeaderLine.Value);
            Assert.Equal(28, groups[2].SourceHeaderLine.Value);
            Assert.Equal("vc-paths.ipl", groups[2].SourceName);
            Assert.Equal(null, groups[0].ModelName);
            Assert.Equal("roads", groups[1].ModelName);
            Assert.Equal(null, groups[2].ModelName);
            for (int i = 0; i < groups.Length; i++)
            {
                Assert.True(groups[i].IsDetached);
                Assert.Equal(-1, groups[i].ModelId);
                Assert.Equal(12, groups[i].Nodes.Length);
            }

            PathNode ped = groups[0].Nodes[0];
            Assert.True(ped.Cross);
            Assert.Equal(0, ped.NumLeftLanes);
            Assert.Equal(0, ped.NumRightLanes);
            Assert.Equal(3, ped.Speed);
            Assert.Equal(7, ped.Flags);
            Assert.True(ped.IgnoredCollision);
            Assert.True(ped.IgnoredLights);
            Assert.True(ped.IgnoredTransition);
            Assert.Near(15.0f, ped.SpawnRate);
            Assert.Near(7.5f, groups[0].Nodes[1].SpawnRate);

            PathNode car = groups[1].Nodes[0];
            Assert.Equal(2, car.NumLeftLanes);
            Assert.Equal(3, car.NumRightLanes);
            Assert.Equal(2, car.Speed);
            Assert.False(car.IgnoredCollision);
            Assert.True(car.IgnoredLights);
            Assert.False(car.IgnoredTransition);
            Assert.Near(3.75f, car.SpawnRate);

            PathNode water = groups[2].Nodes[0];
            Assert.Equal(2, water.NumLeftLanes);
            Assert.Equal(2, water.NumRightLanes);
            Assert.Near(15.0f, water.SpawnRate);
            AssertVector(new Vector3(-12950.0f, -14283.8f, 158.174f), water.StoredPosition);

            PathWorld world = new PathWorld();
            world.AddGroups(groups);
            Assert.Equal(3, world.GroupCount);
            Assert.Equal(36, world.NodeCount);
            Assert.Equal(3, world.DetachedCount);
            Assert.Equal(1, world.WaterCount);
            Assert.Equal(3, world.ResolvedCount);
            Assert.Equal(0, world.UnresolvedCount);
        }

        [Test]
        public void WorldPositionsUseCompressedCoordinatesAndIgnoreDetachedTransforms()
        {
            PathGroup attached = ParseIII("car, 10, road\n" + Repeat(IiiCarNode, 12))[0];
            PathGroup detached = ParseIII("car, -1, unused\n" + Repeat(IiiCarNode, 12))[0];
            PathWorld world = new PathWorld();
            world.AddGroups(new PathGroup[] { attached, detached });
            Matrix transform = Matrix.CreateScale(2.0f);
            transform.Position = new Vector3(10.0f, 20.0f, 30.0f);

            AssertVector(new Vector3(12.0f, 24.0f, 36.0f),
                world.GetWorldPosition(attached, attached.Nodes[0], transform));
            AssertVector(new Vector3(1.0f, 2.0f, 3.0f),
                world.GetWorldPosition(detached, detached.Nodes[0], transform));

            PathNodeWorld[] transformed = world.TransformGroup(attached, transform);
            Assert.Equal(12, transformed.Length);
            Assert.Equal(attached.Nodes[0], transformed[0].SourceNode);
            Assert.Equal(0, transformed[0].NextNodeIndex);
            AssertVector(new Vector3(12.0f, 24.0f, 36.0f), transformed[0].WorldPosition);
            PathNodeWorld[] detachedNodes = world.TransformGroup(detached, transform);
            AssertVector(new Vector3(1.0f, 2.0f, 3.0f), detachedNodes[0].WorldPosition);
            Assert.Equal(2, world.GroupCount);
            Assert.Equal(24, world.NodeCount);
            Assert.Equal(1, world.DetachedCount);
            Assert.Equal(1, world.ResolvedCount);
            Assert.Equal(1, world.UnresolvedCount);
        }

        [Test]
        public void BindInstancesUsesFirstCaseInsensitiveModelAndKeepsEveryGroup()
        {
            PathGroup[] groups = ParseIII(
                "car, 10, ROAD\n" + Repeat(IiiCarNode, 12) +
                "car, 10, road\n" + Repeat(IiiCarNode, 12) +
                "car, 11, missing\n" + Repeat(IiiCarNode, 12) +
                "car, -1, unused\n" + Repeat(IiiCarNode, 12));
            ModelRegistry registry = new ModelRegistry(new PathTestDefinition());
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n10, road, generic, 1, 100, 0\nend\n"));
            IplDocument placements = new IplReader(DataSchema.III).Read(
                "inst\n" +
                "10, road, 100, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "10, ROAD, 200, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "end\n");
            StaticWorld staticWorld = new StaticWorld();
            staticWorld.Bind(placements, registry, new LoadReport());
            PathWorld world = new PathWorld();
            world.AddGroups(groups);

            Assert.Throws<InvalidOperationException>(delegate
            {
                world.GetWorldPosition(groups[0], groups[0].Nodes[0]);
            });
            int unresolved = world.BindInstances(staticWorld.Instances);

            Assert.Equal(1, unresolved);
            Assert.Equal(1, world.UnresolvedCount);
            Assert.Equal(3, world.ResolvedCount);
            Assert.Equal(4, world.GroupCount);
            Assert.Equal(48, world.NodeCount);
            Assert.True(groups[0].HasTransform);
            Assert.True(groups[1].HasTransform);
            Assert.False(groups[2].HasTransform);
            Assert.False(groups[3].HasTransform);
            Assert.Near(100.0f, groups[0].ObjectTransform.Value.Position.X);
            Assert.Near(100.0f, groups[1].ObjectTransform.Value.Position.X);
            AssertVector(new Vector3(101.0f, 2.0f, 3.0f),
                world.GetWorldPosition(groups[0], groups[0].Nodes[0]));
            AssertVector(new Vector3(1.0f, 2.0f, 3.0f),
                world.GetWorldPosition(groups[3], groups[3].Nodes[0]));
            for (int i = 0; i < groups.Length; i++)
                Assert.Equal(groups[i], world.Groups[i]);
        }

        [Test]
        public void EmptySectionsAndMalformedCyclesAreHandledStrictly()
        {
            Assert.Equal(0, ParseIII("").Length);
            Assert.Equal(0, ParseVC("").Length);

            AssetFormatException tooFew = Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("car, 1, road\n" + Repeat(IiiCarNode, 11));
            });
            Assert.True(tooFew.Message.IndexOf("iii-paths.ide line 2", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("car, 1, road\n" + Repeat(IiiCarNode, 13));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII(Repeat(IiiCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("car, 1, road\n");
            });
        }

        [Test]
        public void InvalidHeadersTypesAndNodeLayoutsAreRejected()
        {
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("water, 1, road\n" + Repeat(IiiCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("car, 1\n" + Repeat(IiiCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseVC("3, -1\n" + Repeat(VcCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseVC("not_numeric, -1\n" + Repeat(VcCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseVC("0\n" + Repeat(VcCarNode, 12));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseIII("car, 1, road\n1, bad_next, 0, 1, 2, 3, 4, 1, 1\n" +
                    Repeat(IiiCarNode, 11));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ParseVC("1, -1\n1, 0, 0, 1, 2, 3, 4, 1, 1, 0\n" +
                    Repeat(VcCarNode, 11));
            });
        }

        private static PathGroup[] ParseIII(string body)
        {
            const string sourceName = "iii-paths.ide";
            IdeDocument document = new IdeReader(DataSchema.III).Read(
                "path\n" + body + "end\n", sourceName);
            return PathRecordParser.ParseGroups(document.RawRecords, DataSchema.III, sourceName);
        }

        private static PathGroup[] ParseVC(string body)
        {
            const string sourceName = "vc-paths.ipl";
            IplDocument document = new IplReader(DataSchema.VC).Read(
                "path\n" + body + "end\n", sourceName);
            return PathRecordParser.ParseGroups(document.RawRecords, DataSchema.VC, sourceName);
        }

        private static string Repeat(string line, int count)
        {
            string result = "";
            for (int i = 0; i < count; i++)
                result += line + "\n";
            return result;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X, 0.0001f);
            Assert.Near(expected.Y, actual.Y, 0.0001f);
            Assert.Near(expected.Z, actual.Z, 0.0001f);
        }
    }
}
