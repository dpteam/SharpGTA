using System;
using System.Globalization;
using System.Threading;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class ZoneTests
    {
        [Test]
        public void IIIHierarchyReparentsNestedZonesAndNormalizesRecords()
        {
            ZoneWorld world = new ZoneWorld(GameId.III, DataSchema.III);
            ZoneRecord inner = Zone("inner, 2, 20, 30, 40, 40, 50, 60, 8");
            ZoneRecord outer = Zone("outerLongName, 0, 110, 120, 130, 10, 20, 30, 7");

            // Insert the smaller zone first to exercise the original child-moving step.
            world.AddZone(inner);
            world.AddZone(outer);

            ZoneRecord root = world.Navigation[0];
            Assert.Equal("CITYZON", root.Name);
            Assert.Equal(ZoneWorld.GenericRawLevel, root.RawLevel);
            Assert.Equal(3, world.Navigation.Count);
            Assert.Equal(3, world.Information.Count);
            Assert.Equal("OUTERLO", outer.Name);
            AssertVector(new Vector3(10.0f, 20.0f, 30.0f), outer.Bounds.Min);
            AssertVector(new Vector3(110.0f, 120.0f, 130.0f), outer.Bounds.Max);
            Assert.Equal(2, root.ChildIndex);
            Assert.Equal(1, outer.ChildIndex);
            Assert.Equal(2, inner.ParentIndex);
            Assert.Equal(-1, outer.NextIndex);
            Assert.Equal(outer, world.GetZone(2));

            Assert.Equal(inner, world.FindSmallestZone(new Vector3(25.0f, 35.0f, 45.0f)));
            Assert.Equal(outer, world.FindSmallestZone(new Vector3(50.0f, 60.0f, 70.0f)));
            Assert.Equal(root, world.FindSmallestZone(new Vector3(2000.0f, 0.0f, 0.0f)));
            Assert.Equal(root, world.FindSmallestZone(new Vector3(5000.0f, 0.0f, 0.0f)));
            // III information lookup also descends through Default/Navig/Info zones.
            Assert.Equal(inner, world.FindInformationZone(new Vector3(25.0f, 35.0f, 45.0f)));
            Assert.Equal(outer, world.FindInformationZone(new Vector3(50.0f, 60.0f, 70.0f)));
        }

        [Test]
        public void IIIMapZonesKeepProfileLevelsOutOfNavigation()
        {
            ZoneWorld world = new ZoneWorld(GameId.III, DataSchema.III);
            ZoneRecord first = Zone("Zone01, 3, 0, 0, 0, 10, 10, 10, 42");
            ZoneRecord second = Zone("Zone02, 3, 0, 0, 0, 10, 10, 10, 7");
            world.AddMapZone(first);
            world.AddZone(second);

            Assert.Equal(3, world.MapZones.Count);
            Assert.Equal(1, world.Navigation.Count);
            Assert.Equal(1, world.Information.Count);
            Assert.Equal(-1, world.Navigation[0].ChildIndex);
            Assert.Equal(42, world.GetRawLevelFromPosition(new Vector3(5.0f, 5.0f, 5.0f)));
            Assert.Equal(ZoneWorld.GenericRawLevel,
                world.GetRawLevelFromPosition(new Vector3(100.0f, 100.0f, 0.0f)));
            Assert.Equal("CITYZON", world.FindSmallestZone(new Vector3(5.0f, 5.0f, 5.0f)).Name);
        }

        [Test]
        public void ViceCityUsesSeparateArraysAndOriginalZoneQueries()
        {
            ZoneWorld world = new ZoneWorld(GameId.ViceCity, DataSchema.VC);
            Assert.Equal("VICE_C", world.Navigation[0].Name);
            Assert.Equal("CITYINF", world.Information[0].Name);
            Assert.Equal("THEMAP", world.MapZones[0].Name);
            Assert.Near(-2400.0f, world.Navigation[0].Bounds.Min.X);
            Assert.Near(1600.0f, world.Navigation[0].Bounds.Max.X);
            Assert.Near(-2000.0f, world.Information[0].Bounds.Min.Y);
            Assert.Near(500.0f, world.Information[0].Bounds.Max.Z);

            ZoneRecord defaultZone = Zone("def, 0, -100, -100, 0, 100, 100, 100, 1");
            ZoneRecord navigZone = Zone("nav, 1, -50, -50, 0, 50, 50, 100, 1");
            ZoneRecord firstInfo = Zone("first, 2, -10, -10, 0, 10, 10, 100, 1");
            ZoneRecord secondInfo = Zone("second, 2, -10, -10, 0, 10, 10, 100, 1");
            world.AddZone(defaultZone);
            world.AddZone(navigZone);
            world.AddZone(firstInfo);
            world.AddZone(secondInfo);

            Assert.Equal(3, world.Navigation.Count);
            Assert.Equal(3, world.Information.Count);
            Assert.Equal(defaultZone, world.FindSmallestNavigationZone(Vector3.Zero, true, false));
            Assert.Equal(navigZone, world.FindSmallestNavigationZone(Vector3.Zero, false, true));
            Assert.Equal(navigZone, world.FindSmallestNavigationZone(Vector3.Zero, true, true));
            Assert.Equal(null, world.FindSmallestNavigationZone(Vector3.Zero, false, false));
            Assert.Equal(navigZone, world.FindSmallestZone(Vector3.Zero));

            Vector3 outside = new Vector3(2000.0f, 0.0f, 0.0f);
            Assert.Equal(world.Navigation[0], world.FindSmallestNavigationZone(outside, true, false));
            Assert.Equal(null, world.FindSmallestNavigationZone(outside, false, true));
            Assert.Equal(firstInfo, world.FindInformationZone(Vector3.Zero));
            Assert.Equal(world.Information[0],
                world.FindInformationZone(new Vector3(1000.0f, 1000.0f, 50.0f)));
        }

        [Test]
        public void CullQueriesOrAllFlagsAndTakeMaximumWantedDrop()
        {
            ZoneWorld world = new ZoneWorld(GameId.ViceCity, DataSchema.VC);
            CullZoneRecord first = Cull("1, 2, 3, -10, -10, -10, 10, 10, 10, 1, 2");
            world.AddCull(first);
            world.AddCull(Cull("0, 0, 0, -5, -5, -5, 5, 5, 5, 4, 5"));
            world.AddCull(Cull("25, 25, 0, 20, 20, -5, 30, 30, 5, 8, 6"));

            AssertVector(new Vector3(1.0f, 2.0f, 3.0f), first.Position);
            int wantedLevelDrop;
            Assert.Equal(5, world.FindCullAttributes(Vector3.Zero, out wantedLevelDrop));
            Assert.Equal(5, wantedLevelDrop);
            Assert.Equal(8, world.FindCullAttributes(new Vector3(25.0f, 25.0f, 0.0f), out wantedLevelDrop));
            Assert.Equal(6, wantedLevelDrop);
            Assert.Equal(0, world.FindCullAttributes(new Vector3(100.0f, 0.0f, 0.0f), out wantedLevelDrop));
            Assert.Equal(0, wantedLevelDrop);
        }

        [Test]
        public void OcclusionParsingCentersHeightNormalizesAngleAndChecksProfile()
        {
            OcclusionVolumeRecord volume = Occlusion("10, 20, 30, 4, 6, 8, -90");
            AssertVector(new Vector3(10.0f, 20.0f, 34.0f), volume.Center);
            AssertVector(new Vector3(10.0f, 20.0f, 30.0f), volume.PositionBeforeCenter);
            Assert.Near(10.0f, volume.RawX);
            Assert.Near(20.0f, volume.RawY);
            Assert.Near(30.0f, volume.RawZ);
            Assert.Near(4.0f, volume.Width);
            Assert.Near(6.0f, volume.Length);
            Assert.Near(8.0f, volume.Height);
            Assert.Near(270.0f, volume.AngleDegrees);
            Assert.Equal((ushort)49151, volume.QuantizedAngle);

            OcclusionVolumeRecord fullTurn = Occlusion("0, 0, 0, 1, 1, 1, 360");
            Assert.Near(360.0f, fullTurn.AngleDegrees);
            Assert.Equal((ushort)65535, fullTurn.QuantizedAngle);
            OcclusionVolumeRecord wrapped = Occlusion("0, 0, 0, 1, 1, 1, 450");
            Assert.Near(90.0f, wrapped.AngleDegrees);
            Assert.Equal((ushort)16383, wrapped.QuantizedAngle);

            ZoneWorld vc = new ZoneWorld(GameId.ViceCity, DataSchema.VC);
            vc.AddOcclusion(volume);
            Assert.Equal(1, vc.OcclusionVolumes.Count);
            Assert.Equal(volume, vc.OcclusionVolumes[0]);

            ZoneWorld iii = new ZoneWorld(GameId.III, DataSchema.III);
            Assert.Throws<NotSupportedException>(delegate { iii.AddOcclusion(volume); });
            Assert.Equal(0, iii.OcclusionVolumes.Count);
        }

        [Test]
        public void ZoneParsersRejectMalformedRecords()
        {
            AssetFormatException shortZone = Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseZone(Raw("zone", 19, "A 0 0 0 0 1 1 1"));
            });
            Assert.True(shortZone.Message.IndexOf("zone line 19", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseCull(Raw("cull", 20, "0 0 0 1 1 1 2 2 2 0"));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseOcclusion(Raw("occl", 21, "0 0 0 1 1 1"));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseZone(Raw("zone", 22, "A 4 0 0 0 1 1 1 0"));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseZone(Raw("zone", 23, "Zöné 0 0 0 0 1 1 1 0"));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseCull(Raw("cull", 24, "0 0 0 -1 -1 -1 1 1 1 nope 0"));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                ZoneRecordParser.ParseOcclusion(Raw("occl", 25, "0 0 0 1 1 NaN 0"));
            });
        }

        [Test]
        public void ZoneParsingUsesInvariantCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                ZoneRecord zone = ZoneRecordParser.ParseZone(Raw("zone", 30,
                    "decimal 0 1.5 2.5 3.5 4.5 5.5 6.5 2"));
                OcclusionVolumeRecord volume = ZoneRecordParser.ParseOcclusion(Raw("occl", 31,
                    "1.5 2.5 3.5 4.5 5.5 6.5 45.5"));
                Assert.Near(1.5f, zone.Bounds.Min.X);
                Assert.Near(6.5f, zone.Bounds.Max.Z);
                Assert.Near(6.75f, volume.Center.Z);
                Assert.Near(45.5f, volume.AngleDegrees);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        private static ZoneRecord Zone(string text)
        {
            return ZoneRecordParser.ParseZone(Raw("zone", 1, text));
        }

        private static CullZoneRecord Cull(string text)
        {
            return ZoneRecordParser.ParseCull(Raw("cull", 1, text));
        }

        private static OcclusionVolumeRecord Occlusion(string text)
        {
            return ZoneRecordParser.ParseOcclusion(Raw("occl", 1, text));
        }

        private static RawDataRecord Raw(string section, int lineNumber, string text)
        {
            return new RawDataRecord(section, lineNumber, text,
                DataTokenizer.Tokenize(text, lineNumber, DataSchema.DefaultMaxTokens));
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X);
            Assert.Near(expected.Y, actual.Y);
            Assert.Near(expected.Z, actual.Z);
        }
    }
}
