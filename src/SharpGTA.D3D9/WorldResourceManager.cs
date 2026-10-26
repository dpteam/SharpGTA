using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.World;

namespace SharpGTA.D3D9
{
    /// <summary>Lazy DFF/TXD resolution for IPL models, with explicit bounds and failures.</summary>
    internal sealed class WorldResourceManager
    {
        private readonly GameDataLoader loader;
        private readonly RenderResourceManager resources = new RenderResourceManager();
        private readonly ImgCatalog textures;
        private readonly HashSet<string> dictionaries =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Matrix[]> transforms =
            new Dictionary<string, Matrix[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeometryRenderData[]> models =
            new Dictionary<string, GeometryRenderData[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal WorldResourceManager(GameDataLoader loader)
        {
            if (loader == null)
                throw new ArgumentNullException("loader");
            this.loader = loader;
            textures = OpenTextureCatalog(loader.FileSystem);
            try
            {
                if (loader.FileSystem.FileExists("MODELS/GENERIC.TXD"))
                {
                    TxdArchive generic = loader.LoadTxd("MODELS/GENERIC.TXD");
                    resources.AddDictionary("generic", generic);
                    resources.AddFallbackDictionary(generic);
                    dictionaries.Add("generic");
                }
            }
            catch
            {
                textures.Dispose();
                throw;
            }
        }

        private static ImgCatalog OpenTextureCatalog(IGameFileSystem files)
        {
            ImgCatalog catalog = new ImgCatalog();
            bool success = false;
            try
            {
                if (files.FileExists("MODELS/TXD.IMG") && files.FileExists("MODELS/TXD.DIR"))
                {
                    ImgDirectory directory;
                    using (System.IO.Stream stream = files.OpenRead("MODELS/TXD.DIR"))
                        directory = ImgDirectory.Read(stream);
                    catalog.Add(new ImgArchive(files.OpenRead("MODELS/TXD.IMG"), directory, false));
                }
                success = true;
                return catalog;
            }
            finally
            {
                if (!success)
                    catalog.Dispose();
            }
        }

        internal int ModelCount { get { return models.Count; } }
        internal int TextureDictionaryCount { get { return dictionaries.Count; } }
        internal int FailedModelCount { get { return failed.Count; } }
        internal int MissingTextureCount { get { return resources.MissingTextureCount; } }
        internal string[] MissingTextureDetails
        {
            get
            {
                string[] result = new string[missingTextures.Count];
                missingTextures.CopyTo(result);
                Array.Sort(result, StringComparer.OrdinalIgnoreCase);
                return result;
            }
        }
        internal string FirstFailure
        {
            get
            {
                foreach (string message in failures.Values)
                    return message;
                return null;
            }
        }
        internal RenderResourceManager Textures { get { return resources; } }

        internal void Dispose()
        {
            textures.Dispose();
        }

        internal GeometryRenderData[] Resolve(StaticInstance instance)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            string name = instance.Model.Name;
            GeometryRenderData[] atomics;
            if (failed.Contains(name))
                return null;
            if (models.TryGetValue(name, out atomics))
                return atomics;
            try
            {
                EnsureDictionary(instance.Model.Txd);
                DffDocument document = loader.LoadDffEntry(name + ".dff");
                if (document.Clumps.Length != 1)
                    throw new AssetFormatException("Expected one clump for IPL model " + name + ".");
                atomics = resources.GetClump(name, document, instance.Model.Txd);
                Matrix[] frameTransforms = DffFrameTransforms.Resolve(document.Clumps[0]);
                for (int i = 0; i < atomics.Length; i++)
                {
                    for (int j = 0; j < atomics[i].Materials.Length; j++)
                    {
                        MaterialData material = atomics[i].Materials[j];
                        if (resources.GetTexture(instance.Model.Txd, material.TextureName) == null &&
                            material.TextureName != null)
                        {
                            missingTextures.Add(name + ": " + instance.Model.Txd + "/" + material.TextureName);
                        }
                    }
                }
                transforms.Add(name, frameTransforms);
                models.Add(name, atomics);
                return atomics;
            }
            catch (Exception exception)
            {
                if (!(exception is AssetFormatException) && !(exception is KeyNotFoundException) &&
                    !(exception is NotSupportedException) && !(exception is System.IO.IOException))
                    throw;
                failed.Add(name);
                failures[name] = name + ": " + exception.Message;
                return null;
            }
        }

        internal RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius)
        {
            return StaticWorldScene.BuildVisible(instances, center, radius, Resolve, ResolveTransforms);
        }

        private Matrix[] ResolveTransforms(StaticInstance instance)
        {
            return transforms[instance.Model.Name];
        }

        private void EnsureDictionary(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (!dictionaries.Add(name))
                return;
            TxdArchive archive;
            if (loader.Images.Find(name + ".txd") != null)
                archive = loader.LoadTxdEntry(name + ".txd");
            else if (textures.Find(name + ".txd") != null)
                archive = TxdReader.Read(textures.Read(name + ".txd"));
            else
                archive = loader.LoadTxd("MODELS/" + name + ".TXD");
            resources.AddDictionary(name, archive);
        }
    }
}
