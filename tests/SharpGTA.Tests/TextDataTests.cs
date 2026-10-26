using System;
using System.Globalization;
using System.Threading;
using SharpGTA.Data;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class TextDataTests
    {
        [Test]
        public void IIIObjectsAndTimedObjectsUseVariableLodLayouts()
        {
            string text =
                "# object definitions\r\n" +
                "objs\r\n" +
                "100, building, generic, 1, 120.5, 2\r\n" +
                "101, damaged, generic, 2, 100, 80, 0 # two LOD distances\r\n" +
                "end\r\n" +
                "tobj\r\n" +
                "102, clock, generic, 1, 50, 0, 6, 20\r\n" +
                "end\r\n";

            IdeDocument document = new IdeReader(DataSchema.III).Read(text, "iii.ide");

            Assert.Equal(2, document.Objects.Count);
            Assert.Equal(100, document.Objects[0].Id);
            Assert.Equal("building", document.Objects[0].ModelName);
            Assert.Equal("generic", document.Objects[0].TxdName);
            Assert.Equal(1, document.Objects[0].AtomicCount);
            Assert.Near(120.5f, document.Objects[0].LodDistances[0]);
            Assert.Equal(2u, document.Objects[0].Flags);
            Assert.False(document.Objects[0].IsTimed);
            Assert.Equal(2, document.Objects[1].LodDistances.Count);
            Assert.Near(80.0f, document.Objects[1].LodDistances[1]);
            Assert.Equal(1, document.TimedObjects.Count);
            Assert.True(document.TimedObjects[0].IsTimed);
            Assert.Equal(6, document.TimedObjects[0].TimeOn);
            Assert.Equal(20, document.TimedObjects[0].TimeOff);
            Assert.Equal(8, document.TimedObjects[0].RawFields.Count);
        }

        [Test]
        public void IIIInstanceKeepsRawAxisAndQuaternionW()
        {
            string text =
                "inst\n" +
                "100, building, 10.5, 20, -3, 1, 2, 1, 0, 0, 1, 0.7071068\n" +
                "end\n";

            IplDocument document = new IplReader(DataSchema.III).Read(text);
            ObjectInstanceRecord instance = document.Instances[0];

            Assert.Equal(1, document.Instances.Count);
            Assert.Equal(100, instance.Id);
            Assert.Equal("building", instance.ModelName);
            Assert.Near(10.5f, instance.Position.X);
            Assert.Near(20.0f, instance.Position.Y);
            Assert.Near(-3.0f, instance.Position.Z);
            Assert.Near(2.0f, instance.Scale.Y);
            Assert.Near(1.0f, instance.Axis.Z);
            Assert.Near(0.7071068f, instance.QuaternionW);
            Assert.Near(instance.QuaternionW, instance.RawRotation.W);
            Assert.False(instance.HasArea);
            Assert.Near(0.0f, instance.Area);
        }

        [Test]
        public void IIIVehicleAndPedLayoutsOmitViceCityFields()
        {
            string text =
                "cars\n" +
                "90, sentinel, sentinel, car, SENTINEL, SENTINEL, executive, 5, 1, 2b, 4, 0.75\n" +
                "end\n" +
                "peds\n" +
                "7, male01, male01, CIVMALE, STAT_CIVMALE, man, ff\n" +
                "end\n";

            IdeDocument document = new IdeReader(DataSchema.III).Read(text);

            Assert.Equal(1, document.Vehicles.Count);
            Assert.Equal("executive", document.Vehicles[0].VehicleClass);
            Assert.Equal(null, document.Vehicles[0].AnimationFile);
            Assert.Equal(43u, document.Vehicles[0].ComponentRules);
            Assert.Equal(4, document.Vehicles[0].Misc);
            Assert.Near(0.75f, document.Vehicles[0].WheelScale);
            Assert.Equal(1, document.Peds.Count);
            Assert.Equal(255u, document.Peds[0].CarsCanDrive);
            Assert.Equal(null, document.Peds[0].AnimationFile);
            Assert.Equal(-1, document.Peds[0].Radio1);
            Assert.Equal(-1, document.Peds[0].Radio2);
        }

        [Test]
        public void ViceCityVehiclePedAndWeaponLayoutsAreTypedAndRaw()
        {
            string text =
                "cars\n" +
                "130, landstal, landstal, car, LANDSTAL, LANDSTAL, null, normal, 10, 0, 1a, 0, 1.0\n" +
                "end\n" +
                "peds\n" +
                "9, male01, male01, CIVMALE, STAT_CIVMALE, man, 0x12, null, 1, 2\n" +
                "end\n" +
                "weap\n" +
                "170, bat, bat, null, 1, 50.0\n" +
                "end\n";

            IdeDocument document = new IdeReader(DataSchema.VC).Read(text);
            VehicleRecord vehicle = document.Vehicles[0];
            PedRecord ped = document.Peds[0];
            WeaponRecord weapon = document.Weapons[0];

            Assert.Equal(130, vehicle.Id);
            Assert.Equal("car", vehicle.VehicleType);
            Assert.Equal("LANDSTAL", vehicle.GameName);
            Assert.Equal("null", vehicle.AnimationFile);
            Assert.Equal("normal", vehicle.VehicleClass);
            Assert.Equal(10, vehicle.Frequency);
            Assert.Equal(26u, vehicle.ComponentRules);
            Assert.Near(1.0f, vehicle.WheelScale);
            Assert.Equal(13, vehicle.RawFields.Count);

            Assert.Equal("CIVMALE", ped.PedType);
            Assert.Equal("STAT_CIVMALE", ped.PedStats);
            Assert.Equal("man", ped.AnimationGroup);
            Assert.Equal(18u, ped.CarsCanDrive);
            Assert.Equal("null", ped.AnimationFile);
            Assert.Equal(1, ped.Radio1);
            Assert.Equal(2, ped.Radio2);

            Assert.Equal(170, weapon.Id);
            Assert.Equal("null", weapon.AnimationFile);
            Assert.Equal(1, weapon.AtomicCount);
            Assert.Near(50.0f, weapon.LodDistance);
        }

        [Test]
        public void VehicleLayoutsDependOnTypeAndWeaponTrailingFieldIsPreserved()
        {
            string iiiText =
                "cars\n" +
                "90, sentinel, sentinel, car, SENTINEL, SENTINEL, executive, 5, 1, 2b, 4, 0.75\n" +
                "91, predator, predator, boat, PREDATOR, PREDATOR, ignore, 5, 1, 0\n" +
                "92, train, train, train, TRAIN, TRAIN, ignore, 5, 1, 0\n" +
                "93, chopper, chopper, heli, CHOPPER, CHOPPER, ignore, 5, 1, 0\n" +
                "94, airtrain, airtrain, plane, AIRTRAIN, AIRTRAIN, ignore, 5, 1, 0, 95\n" +
                "96, bike, bike, bike, BIKE, BIKE, motorbike, 5, 1, 0, 30, 0.8\n" +
                "end\n";
            IdeDocument iii = new IdeReader(DataSchema.III).Read(iiiText);
            Assert.Equal(6, iii.Vehicles.Count);
            Assert.Equal(12, iii.Vehicles[0].RawFields.Count);
            Assert.Equal(10, iii.Vehicles[1].RawFields.Count);
            Assert.Equal(0, iii.Vehicles[1].Misc);
            Assert.Near(1.0f, iii.Vehicles[1].WheelScale);
            Assert.Equal(10, iii.Vehicles[2].RawFields.Count);
            Assert.Equal(10, iii.Vehicles[3].RawFields.Count);
            Assert.Equal(11, iii.Vehicles[4].RawFields.Count);
            Assert.Equal(95, iii.Vehicles[4].Misc);
            Assert.Near(1.0f, iii.Vehicles[4].WheelScale);
            Assert.Near(0.8f, iii.Vehicles[5].WheelScale);

            string vcText =
                "cars\n" +
                "130, rio, rio, boat, RIO, RIO, null, ignore, 5, 1, 0\n" +
                "131, airtrain, airtrain, plane, AIRTRAIN, AIRTRAIN, null, ignore, 5, 1, 0, 132\n" +
                "end\n" +
                "weap\n" +
                "258, cellphone, cellphone, null, 1, 50, 0\n" +
                "end\n";
            IdeDocument vc = new IdeReader(DataSchema.VC).Read(vcText);
            Assert.Equal(11, vc.Vehicles[0].RawFields.Count);
            Assert.Near(1.0f, vc.Vehicles[0].WheelScale);
            Assert.Equal(12, vc.Vehicles[1].RawFields.Count);
            Assert.Equal(132, vc.Vehicles[1].Misc);
            Assert.Equal(7, vc.Weapons[0].RawFields.Count);
            Assert.Equal("0", vc.Weapons[0].RawFields[6]);
            Assert.Near(50.0f, vc.Weapons[0].LodDistance);
        }

        [Test]
        public void ViceCityInstanceAcceptsOptionalAreaAndOtherSectionsStayRaw()
        {
            string text =
                "inst\n" +
                "200, model, 2.5, 1, 2, 3, 1, 1, 1, 0, 0, 1, 0.5\n" +
                "201, no_area, 4, 5, 6, 1, 1, 1, 0, 1, 0, 0.25\n" +
                "end\n" +
                "zone\n" +
                "DOWNTOWN, 0, 1, 2, 3, 4, 5, 6, 0\n" +
                "end\n" +
                "occl\n" +
                "1, 2, 3, 4, 5, 6, 7\n" +
                "end\n";

            IplDocument document = new IplReader(DataSchema.VC).Read(text);

            Assert.Equal(2, document.Instances.Count);
            Assert.True(document.Instances[0].HasArea);
            Assert.Near(2.5f, document.Instances[0].Area);
            Assert.Near(1.0f, document.Instances[0].Position.X);
            Assert.Near(0.5f, document.Instances[0].RawRotation.W);
            Assert.False(document.Instances[1].HasArea);
            Assert.Near(0.0f, document.Instances[1].Area);
            Assert.Near(4.0f, document.Instances[1].Position.X);
            Assert.Equal(2, document.RawRecords.Count);
            Assert.Equal("zone", document.RawRecords[0].Section);
            Assert.Equal("DOWNTOWN", document.RawRecords[0].Tokens[0]);
            Assert.Equal("occl", document.RawRecords[1].Section);
        }

        [Test]
        public void UnsupportedIdeSectionsArePreservedAsRawRecords()
        {
            string text =
                "hier\n" +
                "300, clump, generic\n" +
                "end\n" +
                "path\n" +
                "ped, 1, route\n" +
                "0, -1, 0, 1, 2, 3, 4\n" +
                "end\n" +
                "2dfx\n" +
                "100, 1, 2, 3, 255, 0, 0, 255, 1\n" +
                "end\n";

            IdeDocument document = new IdeReader(DataSchema.III).Read(text);

            Assert.Equal(4, document.RawRecords.Count);
            Assert.Equal("hier", document.RawRecords[0].Section);
            Assert.Equal("path", document.RawRecords[1].Section);
            Assert.Equal("ped", document.RawRecords[1].RawFields[0]);
            Assert.Equal("path", document.RawRecords[2].Section);
            Assert.Equal("2dfx", document.RawRecords[3].Section);
        }

        [Test]
        public void DatKeepsOrderUnknownDirectivesAndStopsAtExit()
        {
            string text =
                "# loading order matters\n" +
                "ide, DATA\\MAPS\\first.ide\n" +
                "COLFILE 2, DATA\\MAPS\\first.col # collision level and path\n" +
                "mystery one two\n" +
                "CDIMAGE MODELS\\GTA3.IMG\n" +
                "EXIT\n" +
                "IPL DATA\\MAPS\\never.ipl\n";

            DatDocument document = new DatReader(DataSchema.III).Read(text);

            Assert.Equal(4, document.Directives.Count);
            Assert.True(document.TerminatedByExit);
            Assert.Equal("IDE", document.Directives[0].Command);
            Assert.Equal("DATA\\MAPS\\first.ide", document.Directives[0].Path);
            Assert.Equal("COLFILE", document.Directives[1].Command);
            Assert.Equal("2", document.Directives[1].Arguments[0]);
            Assert.Equal("DATA\\MAPS\\first.col", document.Directives[1].Path);
            Assert.Equal("MYSTERY", document.Directives[2].Command);
            Assert.False(document.Directives[2].IsSupported);
            Assert.Equal(2, document.Directives[2].Arguments.Count);
            Assert.True(document.Directives[3].IsSupported);
        }

        [Test]
        public void NumericErrorsAndShortRecordsHaveSourceAndLine()
        {
            AssetFormatException invalidFloat = Assert.Throws<AssetFormatException>(delegate
            {
                new IdeReader(DataSchema.III).Read("objs\n1, model, generic, 1, bad_float, 0\nend\n", "bad.ide");
            });
            Assert.True(invalidFloat.Message.IndexOf("bad.ide line 2", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.True(invalidFloat.Message.IndexOf("bad_float", StringComparison.Ordinal) >= 0);

            AssetFormatException tooShort = Assert.Throws<AssetFormatException>(delegate
            {
                new IplReader(DataSchema.III).Read("inst\n1 model 0 0 0\nend\n", "short.ipl");
            });
            Assert.True(tooShort.Message.IndexOf("short.ipl line 2", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Test]
        public void UnknownSectionsAreReported()
        {
            AssetFormatException ideError = Assert.Throws<AssetFormatException>(delegate
            {
                new IdeReader(DataSchema.III).Read("unknown\n1, 2, 3\nend\n");
            });
            Assert.True(ideError.Message.IndexOf("Unknown section", StringComparison.OrdinalIgnoreCase) >= 0);

            AssetFormatException iplError = Assert.Throws<AssetFormatException>(delegate
            {
                new IplReader(DataSchema.VC).Read("mystery\n1, 2, 3\nend\n");
            });
            Assert.True(iplError.Message.IndexOf("line 1", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Test]
        public void ParsingAlwaysUsesInvariantCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                IdeDocument document = new IdeReader(DataSchema.III).Read(
                    "objs\n1, model, generic, 1, 12.5, 0\nend\n");
                Assert.Near(12.5f, document.Objects[0].LodDistances[0]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ConfigurableBoundsRejectExcessInput()
        {
            DataSchema tiny = new DataSchema(false, false, false, false, false, 64, 2, 1);
            DataSchema shortLines = new DataSchema(false, false, false, false, false, 8, 128, 1);
            Assert.Throws<AssetFormatException>(delegate
            {
                new DataDocumentReader(tiny).Read("one two three\n");
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                new DataDocumentReader(shortLines).Read("123456789\n");
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                new DatReader(tiny).Read("IDE a\nIPL b\n");
            });
        }
    }
}
