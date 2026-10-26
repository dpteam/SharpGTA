using System;
using System.Collections.Generic;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.Frontend;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class FrontendAssetTests
    {
        [Test]
        public void LanguageSelectionLoadsTheMatchingOriginalGxtFile()
        {
            MemoryFiles files = new MemoryFiles();
            AddLanguage(files, "AMERICAN", "MAIN MENU");
            AddLanguage(files, "FRENCH", "MENU PRINCIPAL");
            AddLanguage(files, "GERMAN", "HAUPTMENU");
            AddLanguage(files, "ITALIAN", "MENU PRINCIPALE");
            AddLanguage(files, "SPANISH", "MENU PRINCIPAL");
            FrontendText text = new FrontendText(files, FrontendLanguage.English);
            Assert.Equal("MAIN MENU", text.Get("FEM_MM"));
            text.SetLanguage(FrontendLanguage.French);
            Assert.Equal("MENU PRINCIPAL", text.Get("FEM_MM"));
            text.SetLanguage(FrontendLanguage.German);
            Assert.Equal("HAUPTMENU", text.Get("FEM_MM"));
            text.SetLanguage(FrontendLanguage.Italian);
            Assert.Equal("MENU PRINCIPALE", text.Get("FEM_MM"));
            text.SetLanguage(FrontendLanguage.Spanish);
            Assert.Equal("MENU PRINCIPAL", text.Get("FEM_MM"));
            Assert.Throws<KeyNotFoundException>(delegate { text.Get("MISSING"); });
        }

        [Test]
        public void FrontendTextureLoaderSeparatesMenuAndFontDictionariesAndCachesDecodedImages()
        {
            MemoryFiles files = new MemoryFiles();
            files.Add("MODELS/MENU.TXD", TxdTestData.CreateArchive(
                Texture("mainmenu24", new byte[] { 30, 20, 10, 255 })));
            files.Add("MODELS/FONTS.TXD", TxdTestData.CreateArchive(
                Texture("font2", new byte[] { 3, 2, 1, 128 })));
            IIIFrontendAssets assets = IIIFrontendAssets.Load(files);
            TxdImage background = assets.GetImage("MAINMENU24");
            Assert.Equal(10, background.Rgba[0]);
            Assert.Equal(20, background.Rgba[1]);
            Assert.Equal(30, background.Rgba[2]);
            TxdImage font = assets.GetImage("font2");
            Assert.Equal(1, font.Rgba[0]);
            Assert.Equal(2, font.Rgba[1]);
            Assert.Equal(3, font.Rgba[2]);
            Assert.Equal(128, font.Rgba[3]);
            Assert.True(ReferenceEquals(font, assets.GetImage("FONT2")));
            Assert.Throws<KeyNotFoundException>(delegate { assets.GetImage("missing"); });
        }

        private static void AddLanguage(MemoryFiles files, string name, string value)
        {
            files.Add("TEXT/" + name + ".GXT", CreateGxt("FEM_MM", value));
        }

        private static TxdTexture Texture(string name, byte[] bgra)
        {
            return TxdTestData.CreateTexture(name, 1, 1, TxdCompression.Uncompressed,
                0x0500u, new byte[][] { bgra });
        }

        private static byte[] CreateGxt(string key, string value)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                byte[] keyPayload;
                using (MemoryStream keys = new MemoryStream())
                using (BinaryWriter keyWriter = new BinaryWriter(keys))
                {
                    keyWriter.Write(0u);
                    byte[] name = new byte[8];
                    for (int i = 0; i < key.Length; i++)
                        name[i] = (byte)key[i];
                    keyWriter.Write(name);
                    keyPayload = keys.ToArray();
                }
                WriteChunk(writer, "TKEY", keyPayload);
                using (MemoryStream values = new MemoryStream())
                using (BinaryWriter valueWriter = new BinaryWriter(values))
                {
                    for (int i = 0; i < value.Length; i++)
                        valueWriter.Write((ushort)value[i]);
                    valueWriter.Write((ushort)0);
                    WriteChunk(writer, "TDAT", values.ToArray());
                }
                return stream.ToArray();
            }
        }

        private static void WriteChunk(BinaryWriter writer, string magic, byte[] payload)
        {
            for (int i = 0; i < 4; i++)
                writer.Write((byte)magic[i]);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        private sealed class MemoryFiles : IGameFileSystem
        {
            private readonly Dictionary<string, byte[]> files =
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public string Root { get { return "memory"; } }

            internal void Add(string path, byte[] bytes) { files.Add(path, bytes); }

            public bool FileExists(string relativePath) { return files.ContainsKey(relativePath); }

            public Stream OpenRead(string relativePath)
            {
                return new MemoryStream(files[relativePath], false);
            }

            public byte[] ReadAllBytes(string relativePath, int maximumLength)
            {
                byte[] bytes = files[relativePath];
                if (bytes.Length > maximumLength)
                    throw new InvalidDataException("Synthetic input exceeds limit.");
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
        }
    }
}
