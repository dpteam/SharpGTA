using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Rendering
{
    /// <summary>Shared texture lookup and lazy model conversion; no renderer or GPU ownership.</summary>
    public sealed class RenderResourceManager
    {
        private readonly Dictionary<string, TxdArchive> dictionaries =
            new Dictionary<string, TxdArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TxdArchive> fallbackDictionaries =
            new Dictionary<string, TxdArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TxdArchive> namedDictionaries =
            new Dictionary<string, TxdArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextureData> textures =
            new Dictionary<string, TextureData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<TextureResourceKey, TextureData> contextualTextures =
            new Dictionary<TextureResourceKey, TextureData>();
        private readonly Dictionary<string, GeometryRenderData[]> models =
            new Dictionary<string, GeometryRenderData[]>(StringComparer.OrdinalIgnoreCase);

        public int TextureDictionaryCount { get { return dictionaries.Count; } }
        public int TextureCount { get { return textures.Count + contextualTextures.Count; } }
        public int ModelCount { get { return models.Count; } }
        public long TextureBytes { get; private set; }
        public int MissingTextureCount { get; private set; }

        public void AddDictionary(TxdArchive archive)
        {
            AddDictionary(archive, dictionaries);
        }

        /// <summary>Registers the TXD selected by an IDE model definition.</summary>
        public void AddDictionary(string dictionaryName, TxdArchive archive)
        {
            if (string.IsNullOrEmpty(dictionaryName))
                throw new ArgumentException("A texture dictionary name is required.", "dictionaryName");
            if (archive == null)
                throw new ArgumentNullException("archive");
            namedDictionaries[dictionaryName] = archive;
            AddDictionary(archive);
        }

        /// <summary>Adds textures such as GENERIC.TXD, consulted only after model dictionaries.</summary>
        public void AddFallbackDictionary(TxdArchive archive)
        {
            AddDictionary(archive, fallbackDictionaries);
        }

        private void AddDictionary(TxdArchive archive, Dictionary<string, TxdArchive> target)
        {
            if (archive == null)
                throw new ArgumentNullException("archive");
            for (int i = 0; i < archive.Textures.Length; i++)
            {
                TxdTexture texture = archive.Textures[i];
                if (string.IsNullOrEmpty(texture.Name) || target.ContainsKey(texture.Name))
                    continue; // First registered dictionary wins within one lookup tier.
                target.Add(texture.Name, archive);
                if (ReferenceEquals(target, dictionaries))
                    textures.Remove(texture.Name); // A late model dictionary overrides a cached fallback.
            }
        }

        public TextureData GetTexture(string dictionaryName, string name)
        {
            if (name == null)
                return null;
            TextureResourceKey key = new TextureResourceKey(dictionaryName, name);
            TextureData cached;
            if (contextualTextures.TryGetValue(key, out cached))
                return cached;
            TxdArchive archive;
            if (dictionaryName == null || !namedDictionaries.TryGetValue(dictionaryName, out archive) ||
                archive.Find(name) == null)
            {
                if (!fallbackDictionaries.TryGetValue(name, out archive))
                {
                    MissingTextureCount++;
                    return null;
                }
            }
            cached = Decode(archive, name);
            if (cached == null)
                return null;
            contextualTextures.Add(key, cached);
            return cached;
        }

        public TextureData GetTexture(string name)
        {
            if (name == null)
                return null;
            TextureData cached;
            if (textures.TryGetValue(name, out cached))
                return cached;
            TxdArchive archive;
            if (!dictionaries.TryGetValue(name, out archive) &&
                !fallbackDictionaries.TryGetValue(name, out archive))
            {
                MissingTextureCount++;
                return null;
            }
            cached = Decode(archive, name);
            if (cached != null)
                textures.Add(name, cached);
            return cached;
        }

        private TextureData Decode(TxdArchive archive, string name)
        {
            TxdTexture texture = archive.Find(name);
            if (texture == null || texture.LevelCount == 0)
            {
                MissingTextureCount++;
                return null;
            }
            TxdImage image = texture.DecodeLevel(0);
            TextureData decoded = new TextureData(texture.Name, image.Width, image.Height, image.Rgba);
            TextureBytes += decoded.Rgba.Length;
            return decoded;
        }

        public GeometryRenderData[] GetClump(string name, DffDocument document)
        {
            return GetClump(name, document, null);
        }

        public GeometryRenderData[] GetClump(string name, DffDocument document, string textureDictionary)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (document == null)
                throw new ArgumentNullException("document");
            GeometryRenderData[] cached;
            if (models.TryGetValue(name, out cached))
                return cached;
            if (document.Clumps.Length != 1)
                throw new AssetFormatException("Expected one DFF clump for a model resource.");
            cached = DffRenderConverter.ConvertClump(document.Clumps[0], name);
            for (int i = 0; i < cached.Length; i++)
                cached[i].TextureDictionary = textureDictionary;
            models.Add(name, cached);
            return cached;
        }
    }
}
