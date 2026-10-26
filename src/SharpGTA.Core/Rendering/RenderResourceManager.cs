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
        private readonly Dictionary<string, TextureData> textures =
            new Dictionary<string, TextureData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeometryRenderData[]> models =
            new Dictionary<string, GeometryRenderData[]>(StringComparer.OrdinalIgnoreCase);

        public int TextureDictionaryCount { get { return dictionaries.Count; } }
        public int TextureCount { get { return textures.Count; } }
        public int ModelCount { get { return models.Count; } }
        public long TextureBytes { get; private set; }
        public int MissingTextureCount { get; private set; }

        public void AddDictionary(TxdArchive archive)
        {
            if (archive == null)
                throw new ArgumentNullException("archive");
            for (int i = 0; i < archive.Textures.Length; i++)
            {
                TxdTexture texture = archive.Textures[i];
                if (string.IsNullOrEmpty(texture.Name) || dictionaries.ContainsKey(texture.Name))
                    continue; // First registered dictionary wins, matching TxdStore lookup order.
                dictionaries.Add(texture.Name, archive);
            }
        }

        public TextureData GetTexture(string name)
        {
            if (name == null)
                return null;
            TextureData cached;
            if (textures.TryGetValue(name, out cached))
                return cached;
            TxdArchive archive;
            if (!dictionaries.TryGetValue(name, out archive))
            {
                MissingTextureCount++;
                return null;
            }
            TxdTexture texture = archive.Find(name);
            if (texture == null || texture.LevelCount == 0)
            {
                MissingTextureCount++;
                return null;
            }
            TxdImage image = texture.DecodeLevel(0);
            cached = new TextureData(texture.Name, image.Width, image.Height, image.Rgba);
            textures.Add(name, cached);
            TextureBytes += cached.Rgba.Length;
            return cached;
        }

        public GeometryRenderData[] GetClump(string name, DffDocument document)
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
            models.Add(name, cached);
            return cached;
        }
    }
}
