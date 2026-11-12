using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.World;

namespace SharpGTA.D3D9
{
    /// <summary>Streamed DFF/TXD resolution for static IPL models on the simulation thread.</summary>
    internal sealed class WorldResourceManager : IDisposable
    {
        private sealed class ModelResource
        {
            internal ModelResource(string txd, GeometryRenderData[] atomics, Matrix[] transforms)
            {
                Txd = txd;
                Atomics = atomics;
                Transforms = transforms;
            }

            internal string Txd;
            internal GeometryRenderData[] Atomics;
            internal Matrix[] Transforms;
        }

        private const int DefaultModelCapacity = 512;
        private const int DefaultTxdCapacity = 96;
        private readonly GameDataLoader loader;
        private readonly RenderResourceManager resources = new RenderResourceManager();
        private readonly ImgCatalog textures;
        private readonly StreamingPool<string, TxdArchive> txds;
        private readonly StreamingPool<string, ModelResource> models;
        private readonly HashSet<string> knownFailures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private D3D9ModelRenderer renderer;
        private StreamingLoadReport lastLoad;
        private bool disposed;

        internal WorldResourceManager(GameDataLoader loader)
            : this(loader, DefaultModelCapacity, DefaultTxdCapacity)
        {
        }

        internal WorldResourceManager(GameDataLoader loader, int modelCapacity)
            : this(loader, modelCapacity, DefaultTxdCapacity)
        {
        }

        internal WorldResourceManager(GameDataLoader loader, int modelCapacity, int txdCapacity)
        {
            if (loader == null)
                throw new ArgumentNullException("loader");
            if (modelCapacity <= 0)
                throw new ArgumentOutOfRangeException("modelCapacity");
            if (txdCapacity <= 0)
                throw new ArgumentOutOfRangeException("txdCapacity");
            this.loader = loader;
            textures = OpenTextureCatalog(loader.FileSystem);
            txds = new StreamingPool<string, TxdArchive>(txdCapacity, TryLoadTxd, UnloadTxd,
                StringComparer.OrdinalIgnoreCase);
            models = new StreamingPool<string, ModelResource>(modelCapacity, TryLoadModel, UnloadModel,
                StringComparer.OrdinalIgnoreCase);
            try
            {
                if (loader.FileSystem.FileExists("MODELS/GENERIC.TXD"))
                {
                    txds.Request("generic", int.MaxValue, true);
                    StreamingLoadReport generic = txds.LoadAllRequested();
                    if (generic.FailedCount != 0 || txds.GetState("generic") != StreamingState.Loaded)
                        throw new AssetFormatException("GENERIC.TXD failed: " + txds.GetFailure("generic"));
                    txds.Release("generic");
                }
            }
            catch
            {
                models.Dispose();
                txds.Dispose();
                textures.Dispose();
                throw;
            }
        }

        internal int ModelCount { get { return models.LoadedCount; } }
        internal int TextureDictionaryCount { get { return txds.LoadedCount; } }
        internal int FailedModelCount { get { return knownFailures.Count; } }
        internal int MissingTextureCount { get { return resources.MissingTextureCount; } }
        internal int ModelCapacity { get { return models.MaxLoaded; } }
        internal int TxdCapacity { get { return txds.MaxLoaded; } }
        internal int ModelEvictionCount { get { return models.EvictedCount; } }
        internal int TxdEvictionCount { get { return txds.EvictedCount; } }
        internal StreamingLoadReport LastLoadReport { get { return lastLoad; } }
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

        internal void AttachRenderer(D3D9ModelRenderer value)
        {
            if (value == null)
                throw new ArgumentNullException("value");
            if (renderer != null)
                throw new InvalidOperationException("A renderer is already attached.");
            renderer = value;
        }

        internal void RetryFailedModels()
        {
            knownFailures.Clear();
            failures.Clear();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                models.Dispose();
            }
            finally
            {
                try
                {
                    txds.Dispose();
                }
                finally
                {
                    textures.Dispose();
                }
            }
        }

        internal RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius)
        {
            if (instances == null)
                throw new ArgumentNullException("instances");
            if (radius <= 0.0f || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            if (disposed)
                throw new ObjectDisposedException("WorldResourceManager");

            models.ReleaseAll();
            HashSet<string> requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (!StaticWorldScene.IsVisible(instance, center, radius))
                    continue;
                float distanceSquared = (instance.Position - center).LengthSquared();
                string name = instance.Model.Name;
                if (knownFailures.Contains(name) || !requested.Add(name))
                    continue;
                int priority = distanceSquared <= 80.0f * 80.0f ? 3 :
                    distanceSquared <= 160.0f * 160.0f ? 2 : 1;
                models.Request(name, priority, false);
            }
            StreamingLoadReport visibleLoad = models.LoadAllRequested();
            lastLoad = visibleLoad;
            bool requestedFallback = false;
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (!StaticWorldScene.IsLodFallbackCandidate(instance, center, radius))
                    continue;
                if (IsModelLoaded(instance.Model.RelatedModel))
                    continue;
                string name = instance.Model.Name;
                if (knownFailures.Contains(name) || !requested.Add(name))
                    continue;
                models.Request(name, 0, false);
                requestedFallback = true;
            }
            if (requestedFallback)
                lastLoad = models.LoadAllRequested();
            foreach (string name in requested)
            {
                if (models.GetState(name) != StreamingState.Failed)
                    continue;
                knownFailures.Add(name);
                failures[name] = name + ": " + models.GetFailure(name);
            }
            return StaticWorldScene.BuildVisible(instances, center, radius, Resolve, ResolveTransforms,
                IsModelLoaded);
        }

        private bool IsModelLoaded(ModelDefinition model)
        {
            return model != null && models.GetState(model.Name) == StreamingState.Loaded;
        }

        private GeometryRenderData[] Resolve(StaticInstance instance)
        {
            ModelResource model;
            return models.TryGetResource(instance.Model.Name, out model) ? model.Atomics : null;
        }

        private Matrix[] ResolveTransforms(StaticInstance instance)
        {
            ModelResource model;
            return models.TryGetResource(instance.Model.Name, out model) ? model.Transforms : null;
        }

        private StreamingLoadOutcome TryLoadTxd(string name, out TxdArchive archive, out string failure)
        {
            archive = null;
            failure = null;
            try
            {
                if (name.Equals("generic", StringComparison.OrdinalIgnoreCase) &&
                    loader.FileSystem.FileExists("MODELS/GENERIC.TXD"))
                {
                    archive = loader.LoadTxd("MODELS/GENERIC.TXD");
                }
                else if (loader.Images.Find(name + ".txd") != null)
                {
                    archive = loader.LoadTxdEntry(name + ".txd");
                }
                else if (textures.Find(name + ".txd") != null)
                {
                    archive = TxdReader.Read(textures.Read(name + ".txd"));
                }
                else
                {
                    archive = loader.LoadTxd("MODELS/" + name + ".TXD");
                }
                resources.AddDictionary(name, archive);
                if (name.Equals("generic", StringComparison.OrdinalIgnoreCase))
                    resources.AddFallbackDictionary(archive);
                return StreamingLoadOutcome.Loaded;
            }
            catch (Exception exception)
            {
                if (!IsRecoverable(exception))
                    throw;
                archive = null;
                failure = exception.Message;
                return StreamingLoadOutcome.Failed;
            }
        }

        private void UnloadTxd(string name, TxdArchive archive)
        {
            if (renderer != null)
                renderer.ReleaseTextureDictionary(name);
            resources.RemoveDictionary(name);
        }

        private StreamingLoadOutcome TryLoadModel(string name, out ModelResource model, out string failure)
        {
            model = null;
            failure = null;
            ModelDefinition definition = loader.Models.Find(name);
            if (definition == null)
            {
                failure = "IDE model definition was not found.";
                return StreamingLoadOutcome.Failed;
            }

            txds.Request(definition.Txd, 1, false);
            txds.LoadAllRequested();
            StreamingState txdState = txds.GetState(definition.Txd);
            if (txdState != StreamingState.Loaded)
            {
                failure = "TXD '" + definition.Txd + "' is not loaded: " + txds.GetFailure(definition.Txd);
                txds.Release(definition.Txd);
                return txdState == StreamingState.InQueue
                    ? StreamingLoadOutcome.Deferred
                    : StreamingLoadOutcome.Failed;
            }

            try
            {
                DffDocument document = loader.LoadDffEntry(name + ".dff");
                if (document.Clumps.Length != 1)
                    throw new AssetFormatException("Expected one clump for IPL model " + name + ".");
                GeometryRenderData[] atomics = resources.GetClump(name, document, definition.Txd);
                Matrix[] transforms = DffFrameTransforms.Resolve(document.Clumps[0]);
                for (int i = 0; i < atomics.Length; i++)
                {
                    for (int j = 0; j < atomics[i].Materials.Length; j++)
                    {
                        MaterialData material = atomics[i].Materials[j];
                        if (resources.GetTexture(definition.Txd, material.TextureName) == null &&
                            material.TextureName != null)
                        {
                            missingTextures.Add(name + ": " + definition.Txd + "/" + material.TextureName);
                        }
                    }
                }
                model = new ModelResource(definition.Txd, atomics, transforms);
                return StreamingLoadOutcome.Loaded;
            }
            catch (Exception exception)
            {
                resources.RemoveModel(name);
                txds.Release(definition.Txd);
                if (!IsRecoverable(exception))
                    throw;
                failure = exception.Message;
                return StreamingLoadOutcome.Failed;
            }
        }

        private void UnloadModel(string name, ModelResource model)
        {
            if (renderer != null)
                renderer.ReleaseGeometry(model.Atomics);
            resources.RemoveModel(name);
            txds.Release(model.Txd);
        }

        private static bool IsRecoverable(Exception exception)
        {
            return exception is AssetFormatException ||
                exception is KeyNotFoundException ||
                exception is NotSupportedException ||
                exception is System.IO.IOException;
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
    }
}