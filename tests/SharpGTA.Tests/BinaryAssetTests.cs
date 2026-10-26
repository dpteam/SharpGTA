using System;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class BinaryAssetTests
    {
        private sealed class TestKey
        {
            internal string Name;
            internal uint Offset;

            internal TestKey(string name, uint offset)
            {
                Name = name;
                Offset = offset;
            }
        }

        [Test]
        public void ImgDirectoryParsesNamesAndRejectsTrailingBytes()
        {
            byte[] bytes = new byte[ImgDirectory.EntrySize * 2];
            WriteDirectoryEntry(bytes, 0, 0x02000003u, 2, "Player.DFF");
            WriteDirectoryEntry(bytes, 1, 7, 1, "ABCDEFGHIJKLMNOPQRSTUVWX");

            ImgDirectory directory = ImgDirectory.Read(new MemoryStream(bytes));
            Assert.Equal(2, directory.Entries.Count);
            Assert.Equal(0x02000003u, directory.Entries[0].RawOffset);
            Assert.Equal(3u, directory.Entries[0].OffsetSectors);
            Assert.Equal(2, directory.Entries[0].ImageIndex);
            Assert.Equal(3L * ImgDirectory.SectorSize, directory.Entries[0].ByteOffset);
            Assert.Equal(2L * ImgDirectory.SectorSize, directory.Entries[0].ByteLength);
            Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWX", directory.Entries[1].Name);
            Assert.Equal(directory.Entries[0], directory.Find("pLaYeR.dFf"));
            Assert.Equal(null, directory.Find("PLAYER"));

            Assert.Throws<AssetFormatException>(delegate
            {
                ImgDirectory.FromBytes(new byte[ImgDirectory.EntrySize + 1]);
            });
        }

        [Test]
        public void ImgArchiveReadsSectorPaddingAndChecksDuplicatesAndRanges()
        {
            byte[] bytes = new byte[ImgDirectory.EntrySize * 3];
            WriteDirectoryEntry(bytes, 0, 0x7F000000u, 1, "MODEL");
            WriteDirectoryEntry(bytes, 1, 1, 1, "model");
            WriteDirectoryEntry(bytes, 2, 2, 1, "BROKEN");
            ImgDirectory directory = ImgDirectory.FromBytes(bytes);

            byte[] image = new byte[ImgDirectory.SectorSize * 2];
            image[0] = 0x11;
            image[ImgDirectory.SectorSize - 1] = 0x12;
            image[ImgDirectory.SectorSize] = 0x22;
            ImgArchive archive = new ImgArchive(image, directory);

            byte[] first = archive.Read("MoDeL");
            Assert.Equal(ImgDirectory.SectorSize, first.Length);
            Assert.Equal((byte)0x11, first[0]);
            Assert.Equal((byte)0x12, first[ImgDirectory.SectorSize - 1]);
            Assert.Throws<AssetFormatException>(delegate { archive.Read("broken"); });
            Assert.Throws<AssetFormatException>(delegate
            {
                ImgArchive truncated = new ImgArchive(new byte[ImgDirectory.SectorSize], directory);
                truncated.Read(directory.Entries[1]);
            });
        }

        [Test]
        public void ImgCatalogAssignsIdsAndRoutesUsingOnlyLow24OffsetBits()
        {
            byte[] firstBytes = new byte[ImgDirectory.EntrySize];
            WriteDirectoryEntry(firstBytes, 0, 0, 1, "FIRST");
            byte[] secondBytes = new byte[ImgDirectory.EntrySize * 3];
            WriteDirectoryEntry(secondBytes, 0, 0xFF000001u, 1, "SECOND");
            WriteDirectoryEntry(secondBytes, 1, 0, 1, "first");
            WriteDirectoryEntry(secondBytes, 2, 1, 1, "FIRST");

            byte[] firstImage = new byte[ImgDirectory.SectorSize];
            firstImage[0] = 0x31;
            byte[] secondImage = new byte[ImgDirectory.SectorSize * 2];
            secondImage[ImgDirectory.SectorSize] = 0x42;
            secondImage[0] = 0x52;

            ImgCatalog catalog = new ImgCatalog();
            Assert.Equal(0, catalog.Add(new ImgArchive(firstImage, ImgDirectory.FromBytes(firstBytes))));
            Assert.Equal(1, catalog.Add(new ImgArchive(secondImage, ImgDirectory.FromBytes(secondBytes))));
            ImgEntry second = catalog.Find("second");
            Assert.Equal(1, second.ImageIndex);
            Assert.Equal(1u, second.OffsetSectors);
            Assert.Equal(1u, second.RawOffset >> 24);
            Assert.Equal((byte)0x42, catalog.Read("SeCoNd")[0]);
            Assert.Equal((byte)0x52, catalog.Read("FIRST")[0], "The last image wins across images and the first duplicate within its DIR wins.");

            ImgCatalog full = new ImgCatalog();
            ImgDirectory empty = ImgDirectory.FromBytes(new byte[0]);
            for (int i = 0; i < 256; i++)
                Assert.Equal(i, full.Add(new ImgArchive(new byte[0], empty)));
            Assert.Throws<InvalidOperationException>(delegate
            {
                full.Add(new ImgArchive(new byte[0], empty));
            });
        }

        [Test]
        public void GxtMainSkipsUnknownChunksSupportsEitherOrderAndUsesOrdinalKeys()
        {
            byte[] archiveBytes = Concat(
                Chunk("JUNK", new byte[] { 1, 2, 3 }),
                Chunk("TDAT", Data(0xD800, 0x0041, 0)),
                Chunk("SKIP", new byte[0]),
                Chunk("TKEY", Keys(new TestKey("HELLO", 0))));
            GxtArchive archive = GxtArchive.FromBytes(archiveBytes);

            string text;
            Assert.True(archive.Main.TryGet("HELLO", out text));
            Assert.Equal(new string(new char[] { '\uD800', 'A' }), text);
            Assert.False(archive.Main.TryGet("hello", out text));
            Assert.Equal(null, text);

            ushort[] units;
            Assert.True(archive.Main.TryGetCodeUnits("HELLO", out units));
            Assert.Equal(2, units.Length);
            Assert.Equal((ushort)0xD800, units[0]);
            Assert.Equal((ushort)0x0041, units[1]);
            units[1] = 0x42;
            Assert.True(archive.Main.TryGetCodeUnits("HELLO", out units));
            Assert.Equal((ushort)0x0041, units[1], "Returned code units must be a copy.");
            Assert.Equal(0, archive.MissionTables.Count);
        }

        [Test]
        public void GxtRejectsMissingPairsMalformedValuesAndChunkLengths()
        {
            byte[] key = Keys(new TestKey("KEY", 0));
            Assert.Throws<AssetFormatException>(delegate
            {
                GxtArchive.FromBytes(Chunk("TKEY", key));
            });

            byte[][] invalidValues = new byte[][]
            {
                Concat(Chunk("TKEY", Keys(new TestKey("KEY", 1))), Chunk("TDAT", Data(0x41, 0))),
                Concat(Chunk("TKEY", Keys(new TestKey("KEY", 4))), Chunk("TDAT", Data(0x41, 0))),
                Concat(Chunk("TKEY", key), Chunk("TDAT", Data(0x41))),
                Concat(Chunk("TKEY", key), Chunk("TDAT", new byte[] { 0x41 }))
            };
            for (int i = 0; i < invalidValues.Length; i++)
            {
                byte[] candidate = invalidValues[i];
                Assert.Throws<AssetFormatException>(delegate { GxtArchive.FromBytes(candidate); });
            }

            byte[] badChunk = Chunk("TKEY", key);
            WriteUInt32(badChunk, 4, 1000);
            Assert.Throws<AssetFormatException>(delegate { GxtArchive.FromBytes(badChunk); });
            Assert.Throws<AssetFormatException>(delegate
            {
                GxtArchive.FromBytes(Concat(Chunk("TABL", new byte[13]), Chunk("TKEY", new byte[0]), Chunk("TDAT", new byte[0])));
            });
        }

        [Test]
        public void GxtViceCityTablLoadsEveryMissionByAbsoluteOffset()
        {
            byte[] mainKeys = Chunk("TKEY", Keys(new TestKey("MAIN", 0)));
            byte[] mainData = Chunk("TDAT", Data(0x4D, 0));
            byte[] firstBody = Concat(
                FixedName("M1"),
                Chunk("TKEY", Keys(new TestKey("ONE", 0))),
                Chunk("JUNK", new byte[] { 9 }),
                Chunk("TDAT", Data(0x31, 0)));
            byte[] secondBody = Concat(
                FixedName("M2"),
                Chunk("TDAT", Data(0x32, 0)),
                Chunk("TKEY", Keys(new TestKey("TWO", 0))));

            const int directoryBlockLength = 8 + 24;
            int firstOffset = directoryBlockLength + mainKeys.Length + mainData.Length;
            int secondOffset = firstOffset + firstBody.Length;
            byte[] directory = new byte[24];
            WriteFixedName(directory, 0, "M2", 8);
            WriteUInt32(directory, 8, (uint)secondOffset);
            WriteFixedName(directory, 12, "M1", 8);
            WriteUInt32(directory, 20, (uint)firstOffset);

            GxtArchive archive = GxtArchive.Read(Concat(
                Chunk("TABL", directory), mainKeys, mainData, firstBody, secondBody));
            Assert.Equal(2, archive.MissionDirectory.Count);
            Assert.Equal(2, archive.MissionTables.Count);
            Assert.Equal("M2", archive.MissionTables[0].Name);
            Assert.Equal((uint)firstOffset, archive.MissionDirectory[1].Offset);

            string value;
            Assert.True(archive.Main.TryGet("MAIN", out value));
            Assert.Equal("M", value);
            Assert.True(archive.LoadMission("M1").TryGet("ONE", out value));
            Assert.Equal("1", value);
            Assert.True(archive.LoadMission("M2").TryGet("TWO", out value));
            Assert.Equal("2", value);
            Assert.Equal(null, archive.FindMission("m1"));
            Assert.Equal(null, archive.FindMission("M1_EXTRA"));
            Assert.True(archive.LoadMission("M1_EXTRA").TryGet("ONE", out value));
            Assert.Equal("1", value);
        }

        [Test]
        public void GxtViceCityMainTablAliasDoesNotBecomeAMission()
        {
            byte[] mainKeys = Chunk("TKEY", Keys(new TestKey("MAIN", 0)));
            byte[] mainData = Chunk("TDAT", Data(0x4D, 0));
            byte[] missionBody = Concat(
                FixedName("M1"),
                Chunk("TKEY", Keys(new TestKey("ONE", 0))),
                Chunk("TDAT", Data(0x31, 0)));
            int mainOffset = 8 + 24;
            int missionOffset = mainOffset + mainKeys.Length + mainData.Length;
            byte[] directory = new byte[24];
            WriteFixedName(directory, 0, "MAIN", 8);
            WriteUInt32(directory, 8, (uint)mainOffset);
            WriteFixedName(directory, 12, "M1", 8);
            WriteUInt32(directory, 20, (uint)missionOffset);

            GxtArchive archive = GxtArchive.FromBytes(Concat(
                Chunk("TABL", directory), mainKeys, mainData, missionBody));
            Assert.Equal(2, archive.MissionDirectory.Count);
            Assert.Equal(1, archive.MissionTables.Count);
            Assert.Equal(null, archive.FindMission("MAIN"));
            string value;
            Assert.True(archive.Main.TryGet("MAIN", out value));
            Assert.Equal("M", value);
            Assert.True(archive.LoadMission("M1").TryGet("ONE", out value));
            Assert.Equal("1", value);
        }

        [Test]
        public void GxtViceCityRejectsBadMissionOffsetsNamesAndMissingPairs()
        {
            byte[] mainKeys = Chunk("TKEY", Keys(new TestKey("MAIN", 0)));
            byte[] mainData = Chunk("TDAT", Data(0));
            byte[] directory = new byte[12];
            WriteFixedName(directory, 0, "M1", 8);
            WriteUInt32(directory, 8, uint.MaxValue);
            Assert.Throws<AssetFormatException>(delegate
            {
                GxtArchive.FromBytes(Concat(Chunk("TABL", directory), mainKeys, mainData));
            });

            int missionOffset = 8 + directory.Length + mainKeys.Length + mainData.Length;
            WriteUInt32(directory, 8, (uint)missionOffset);
            byte[] prefix = Concat(Chunk("TABL", directory), mainKeys, mainData);
            byte[] missingData = Concat(prefix, FixedName("M1"), Chunk("TKEY", Keys(new TestKey("KEY", 0))));
            Assert.Throws<AssetFormatException>(delegate { GxtArchive.FromBytes(missingData); });

            byte[] wrongName = Concat(prefix, FixedName("XX"), Chunk("TKEY", Keys(new TestKey("KEY", 0))), Chunk("TDAT", Data(0)));
            Assert.Throws<AssetFormatException>(delegate { GxtArchive.FromBytes(wrongName); });
        }

        [Test]
        public void RenderWareInventoryRecursesOnlyKnownChunkContainers()
        {
            byte[] structChunk = RwChunk(RenderWareChunkTypes.Struct, RwChunk(0x123, new byte[0]));
            byte[] unknownChunk = RwChunk(0xCAFEBABEu, RwChunk(RenderWareChunkTypes.Clump, new byte[0]));
            byte[] extension = RwChunk(RenderWareChunkTypes.Extension, RwChunk(0x253F2FEu, new byte[] { 1, 2, 3 }));
            byte[] clump = RwChunk(RenderWareChunkTypes.Clump, Concat(structChunk, unknownChunk, extension));

            RenderWareChunk root = RenderWareInventory.ReadInventory(clump)[0];
            Assert.Equal(RenderWareChunkTypes.Clump, root.Id);
            Assert.Equal(0x12345678u, root.Version);
            Assert.Equal(0, root.ChunkOffset);
            Assert.Equal(12, root.PayloadOffset);
            Assert.Equal(clump.Length - 12, root.DataLength);
            Assert.True(root.IsKnownContainer);
            Assert.Equal(3, root.Children.Count);
            Assert.False(root.Children[0].IsKnownContainer);
            Assert.Equal(0, root.Children[0].Children.Count, "STRUCT payload is not a generic child sequence.");
            Assert.False(root.Children[1].IsKnownContainer);
            Assert.Equal(0, root.Children[1].Children.Count, "Unknown nesting must not be guessed.");
            Assert.Equal(1, root.Children[2].Children.Count);
            Assert.Equal(0x253F2FEu, root.Children[2].Children[0].Id);
            Assert.Equal(0, root.Children[2].Children[0].Children.Count);
        }

        [Test]
        public void RenderWareInventoryAcceptsSectorPaddingOnlyAfterAChunk()
        {
            byte[] clump = RwChunk(RenderWareChunkTypes.Clump,
                RwChunk(RenderWareChunkTypes.Struct, new byte[12]));
            byte[] padded = Concat(clump, new byte[2048 - clump.Length]);
            Assert.Equal(1, RenderWareInventory.ReadInventory(padded).Count);
            Assert.Throws<AssetFormatException>(delegate
            {
                RenderWareInventory.ReadInventory(Concat(clump, new byte[] { 0, 0, 1 }));
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                RenderWareInventory.ReadInventory(new byte[11]);
            });
        }

        [Test]
        public void RenderWareInventoryRejectsTruncatedHeadersAndLengthsOutsideParentBounds()
        {
            Assert.Throws<AssetFormatException>(delegate
            {
                RenderWareInventory.ReadInventory(new byte[11]);
            });
            byte[] badLength = RwChunk(RenderWareChunkTypes.Struct, new byte[0]);
            WriteUInt32(badLength, 4, 100);
            Assert.Throws<AssetFormatException>(delegate { RenderWareInventory.ReadInventory(badLength); });

            byte[] child = RwChunk(RenderWareChunkTypes.Struct, new byte[0]);
            WriteUInt32(child, 4, 1);
            byte[] parentWithTrailingByte = Concat(RwChunk(RenderWareChunkTypes.Clump, child), new byte[1]);
            Assert.Throws<AssetFormatException>(delegate
            {
                RenderWareInventory.ReadInventory(parentWithTrailingByte);
            });
        }

        private static byte[] Keys(params TestKey[] keys)
        {
            byte[] result = new byte[keys.Length * 12];
            for (int i = 0; i < keys.Length; i++)
            {
                WriteUInt32(result, i * 12, keys[i].Offset);
                WriteFixedName(result, i * 12 + 4, keys[i].Name, 8);
            }
            return result;
        }

        private static byte[] Data(params ushort[] units)
        {
            byte[] result = new byte[units.Length * 2];
            for (int i = 0; i < units.Length; i++)
            {
                result[i * 2] = (byte)units[i];
                result[i * 2 + 1] = (byte)(units[i] >> 8);
            }
            return result;
        }

        private static byte[] Chunk(string magic, byte[] payload)
        {
            byte[] result = new byte[8 + payload.Length];
            WriteFixedName(result, 0, magic, 4);
            WriteUInt32(result, 4, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, result, 8, payload.Length);
            return result;
        }

        private static byte[] RwChunk(uint id, byte[] payload)
        {
            byte[] result = new byte[12 + payload.Length];
            WriteUInt32(result, 0, id);
            WriteUInt32(result, 4, (uint)payload.Length);
            WriteUInt32(result, 8, 0x12345678u);
            Buffer.BlockCopy(payload, 0, result, 12, payload.Length);
            return result;
        }

        private static byte[] FixedName(string name)
        {
            return FixedName(name, 8);
        }

        private static byte[] FixedName(string name, int width)
        {
            byte[] result = new byte[width];
            WriteFixedName(result, 0, name, width);
            return result;
        }

        private static void WriteDirectoryEntry(byte[] buffer, int index, uint rawOffset, uint size, string name)
        {
            int offset = index * ImgDirectory.EntrySize;
            WriteUInt32(buffer, offset, rawOffset);
            WriteUInt32(buffer, offset + 4, size);
            WriteFixedName(buffer, offset + 8, name, 24);
        }

        private static void WriteFixedName(byte[] buffer, int offset, string value, int width)
        {
            if (value.Length > width)
                throw new ArgumentException("Test name is too long.", "value");
            for (int i = 0; i < width; i++)
                buffer[offset + i] = i < value.Length ? (byte)value[i] : (byte)0;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            int length = 0;
            for (int i = 0; i < arrays.Length; i++)
                length += arrays[i].Length;
            byte[] result = new byte[length];
            int offset = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                Buffer.BlockCopy(arrays[i], 0, result, offset, arrays[i].Length);
                offset += arrays[i].Length;
            }
            return result;
        }
    }
}
