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
        private const int DefaultByteCapacity = 64 * 1024 * 1024;
        private const int ByteWaitTimeoutMilliseconds = 30000;
        private const int MaximumResourceBytes = 32 * 1024 * 1024;
        private readonly GameDataLoader loader;
        private readonly RenderResourceManager resources = new RenderResourceManager();
        private readonly ImgCatalog textures;
        private readonly AsyncByteCache bytes;
        private readonly StreamingPool<string, TxdArchive> txds;
        private readonly StreamingPool<string, ModelResource> models;
        private readonly HashSet<string> knownFailures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private D3D9ModelRenderer renderer;
        private StreamingLoadReport lastLoad;
        private bool waitForBytes;
        private bool disposed;

        internal WorldResourceManager(GameDataLoader loader)
            : this(loader, DefaultModelCapacity, DefaultTxdCapacity, DefaultByteCapacity)
        {
        }

        internal WorldResourceManager(GameDataLoader loader, int modelCapacity)
            : this(loader, modelCapacity, DefaultTxdCapacity, DefaultByteCapacity)
        {
        }

        internal WorldResourceManager(GameDataLoader loader, int modelCapacity, int txdCapacity)
            : this(loader, modelCapacity, txdCapacity, DefaultByteCapacity)
        {
        }

        internal WorldResourceManager(GameDataLoader loader, int modelCapacity, int txdCapacity,
            int byteCapacity)
        {
            if (loader == null)
                throw new ArgumentNullException("loader");
            if (modelCapacity <= 0)
                throw new ArgumentOutOfRangeException("modelCapacity");
            if (txdCapacity <= 0)
                throw new ArgumentOutOfRangeException("txdCapacity");
            if (byteCapacity <= 0)
                throw new ArgumentOutOfRangeException("byteCapacity");
            this.loader = loader;
            textures = OpenTextureCatalog(loader.FileSystem);
            bytes = new AsyncByteCache(byteCapacity, 2, ReadResourceBytes);
            txds = new StreamingPool<string, TxdArchive>(txdCapacity, TryLoadTxd, UnloadTxd,
                StringComparer.OrdinalIgnoreCase);
            models = new StreamingPool<string, ModelResource>(modelCapacity, TryLoadModel, UnloadModel,
                StringComparer.OrdinalIgnoreCase);
            try
            {
                if (loader.FileSystem.FileExists("MODELS/GENERIC.TXD"))
                {
                    if (!bytes.WaitFor(TxdKey("generic"), ByteWaitTimeoutMilliseconds))
                        throw new AssetFormatException("GENERIC.TXD bytes could not be read: " +
                            (bytes.GetFailure(TxdKey("generic")) ?? "timed out"));
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
                bytes.Dispose();
                textures.Dispose();
                throw;
            }
        }

        internal long ByteCacheBytes { get { return bytes.CachedBytes; } }
        internal int ByteCacheCount { get { return bytes.CachedCount; } }
        internal int ByteCachePending { get { return bytes.PendingCount; } }
        internal int ByteCacheEvictions { get { return bytes.EvictedCount; } }
        internal int ByteCacheFailures { get { return bytes.FailedCount; } }
        internal int ByteCacheOversized { get { return bytes.OversizedCount; } }
        internal int ByteCacheCapacity { get { return bytes.MaximumBytes; } }

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
            List<string> failedNames = new List<string>(failures.Keys);
            knownFailures.Clear();
            failures.Clear();
            // Byte read failures are sticky by design; an explicit retry must clear them
            // too, otherwise the model would immediately fail again with the same message.
            for (int i = 0; i < failedNames.Count; i++)
            {
                ModelDefinition definition = loader.Models.Find(failedNames[i]);
                if (definition == null)
                    continue;
                bytes.Forget(DffKey(failedNames[i]));
                if (!string.IsNullOrEmpty(definition.Txd))
                    bytes.Forget(TxdKey(definition.Txd));
            }
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
                    try
                    {
                        bytes.Dispose();
                    }
                    finally
                    {
                        textures.Dispose();
                    }
                }
            }
        }

        internal RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius)
        {
            return BuildVisible(instances, center, radius, false, null);
        }

        /// <summary>
        /// Requests visible models, then their bytes. Worker threads deliver bytes only;
        /// decoding and world mutation stay on this simulation thread. The first frame
        /// waits for its byte deliveries so startup is deterministic.
        /// </summary>
        internal RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, bool waitForBytes)
        {
            return BuildVisible(instances, center, radius, waitForBytes, null);
        }

        internal RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, bool waitForBytes, TimeObjectController timeObjects)
        {
            if (instances == null)
                throw new ArgumentNullException("instances");
            if (radius <= 0.0f || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            if (disposed)
                throw new ObjectDisposedException("WorldResourceManager");

            bytes.Pump();
            models.ReleaseAll();
            this.waitForBytes = waitForBytes;
            try
            {
                HashSet<string> requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < instances.Count; i++)
                {
                    StaticInstance instance = instances[i];
                    if (!StaticWorldScene.IsVisible(instance, center, radius, null, timeObjects))
                        continue;
                    float distanceSquared = (instance.Position - center).LengthSquared();
                    string name = instance.Model.Name;
                    if (knownFailures.Contains(name) || !requested.Add(name))
                        continue;
                    int priority = distanceSquared <= 80.0f * 80.0f ? 3 :
                        distanceSquared <= 160.0f * 160.0f ? 2 : 1;
                    models.Request(name, priority, false);
                }
                RequestBytesForNames(requested);
                lastLoad = models.LoadAllRequested();
                bool requestedFallback = false;
                for (int i = 0; i < instances.Count; i++)
                {
                    StaticInstance instance = instances[i];
                    if (!StaticWorldScene.IsLodFallbackCandidate(instance, center, radius, timeObjects))
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
                {
                    RequestBytesForNames(requested);
                    lastLoad = models.LoadAllRequested();
                }
                PrefetchBytes(instances, center, radius, radius * 1.5f, timeObjects);
                foreach (string name in requested)
                {
                    if (models.GetState(name) != StreamingState.Failed)
                        continue;
                    knownFailures.Add(name);
                    failures[name] = name + ": " + models.GetFailure(name);
                }
            }
            finally
            {
                this.waitForBytes = false;
            }
            return StaticWorldScene.BuildVisible(instances, center, radius, Resolve, ResolveTransforms,
                IsModelLoaded, timeObjects);
        }

        private bool IsModelLoaded(ModelDefinition model)
        {
            return model != null && models.GetState(model.Name) == StreamingState.Loaded;
        }

        /// <summary>
        /// Queues byte reads for the ring just outside the visible radius, so the next
        /// camera move does not have to wait for the disk. Already decoded resources and
        /// permanent read failures are never re-read.
        /// </summary>
        private void PrefetchBytes(IList<StaticInstance> instances, Vector3 center,
            float visibleRadius, float prefetchRadius, TimeObjectController timeObjects)
        {
            // Keep decode headroom: prefetch never fills the whole byte budget, otherwise
            // it can evict bytes that the current frame is about to decode.
            if (bytes.CachedBytes * 4 >= (long)bytes.MaximumBytes * 3)
                return;
            float nearSquared = visibleRadius * visibleRadius;
            float farSquared = prefetchRadius * prefetchRadius;
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (instance == null || instance.Model == null)
                    continue;
                float distanceSquared = (instance.Position - center).LengthSquared();
                if (distanceSquared <= nearSquared || distanceSquared > farSquared)
                    continue;
                if (timeObjects != null && !timeObjects.IsVisible(instance))
                    continue;
                ModelDefinition definition = instance.Model;
                if (!string.IsNullOrEmpty(definition.Txd) &&
                    txds.GetState(definition.Txd) != StreamingState.Loaded)
                    bytes.Request(TxdKey(definition.Txd));
                if (models.GetState(definition.Name) != StreamingState.Loaded &&
                    !knownFailures.Contains(definition.Name))
                    bytes.Request(DffKey(definition.Name));
            }
        }

        /// <summary>
        /// Queues DFF/TXD reads for these models. Bytes arrive on worker threads; the
        /// caller decodes them. Waiting frames wait per model inside the loader instead.
        /// </summary>
        private void RequestBytesForNames(ICollection<string> names)
        {
            foreach (string name in names)
            {
                ModelDefinition definition = loader.Models.Find(name);
                if (definition == null)
                    continue;
                if (!string.IsNullOrEmpty(definition.Txd) &&
                    txds.GetState(definition.Txd) != StreamingState.Loaded)
                    bytes.Request(TxdKey(definition.Txd));
                if (models.GetState(name) != StreamingState.Loaded)
                    bytes.Request(DffKey(name));
            }
        }

        private static string DffKey(string name)
        {
            return "dff:" + name;
        }

        private static string TxdKey(string name)
        {
            return "txd:" + name;
        }

        /// <summary>
        /// Runs on the async worker threads. It only reads bytes; nothing here decodes
        /// assets or mutates pools, caches or world state.
        /// </summary>
        private byte[] ReadResourceBytes(string key)
        {
            if (key.StartsWith("dff:", StringComparison.OrdinalIgnoreCase))
            {
                string dffName = key.Substring(4) + ".dff";
                if (loader.Images.Find(dffName) != null)
                    return loader.Images.Read(dffName);
                if (loader.FileSystem.FileExists("MODELS/" + key.Substring(4) + ".DFF"))
                    return loader.FileSystem.ReadAllBytes("MODELS/" + key.Substring(4) + ".DFF",
                        MaximumResourceBytes);
                throw new KeyNotFoundException("DFF '" + dffName + "' was not found in the game images.");
            }

            string name = key.Substring(4);
            if (name.Equals("generic", StringComparison.OrdinalIgnoreCase) &&
                loader.FileSystem.FileExists("MODELS/GENERIC.TXD"))
                return loader.FileSystem.ReadAllBytes("MODELS/GENERIC.TXD", MaximumResourceBytes);
            if (loader.Images.Find(name + ".txd") != null)
                return loader.Images.Read(name + ".txd");
            if (textures.Find(name + ".txd") != null)
                return textures.Read(name + ".txd");
            if (loader.FileSystem.FileExists("MODELS/" + name + ".TXD"))
                return loader.FileSystem.ReadAllBytes("MODELS/" + name + ".TXD", MaximumResourceBytes);
            throw new KeyNotFoundException("TXD '" + name + "' was not found in the game images.");
        }

        private StreamingLoadOutcome TakeOrRequestBytes(string key, out byte[] data,
            out string failure)
        {
            failure = null;
            if (bytes.TryTake(key, out data))
                return StreamingLoadOutcome.Loaded;
            failure = bytes.GetFailure(key);
            if (failure != null)
                return StreamingLoadOutcome.Failed;
            if (waitForBytes)
            {
                // A waiting frame decodes one model at a time, so a small byte budget
                // never has to hold the whole visible set at once.
                if (bytes.WaitFor(key, ByteWaitTimeoutMilliseconds) &&
                    bytes.TryTake(key, out data))
                {
                    failure = null;
                    return StreamingLoadOutcome.Loaded;
                }
                failure = bytes.GetFailure(key) ??
                    "Bytes for '" + key + "' did not arrive in time.";
                return bytes.GetFailure(key) != null
                    ? StreamingLoadOutcome.Failed
                    : StreamingLoadOutcome.Deferred;
            }
            bytes.Request(key);
            failure = "Bytes for '" + key + "' are still loading.";
            return StreamingLoadOutcome.Deferred;
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
            string key = TxdKey(name);
            byte[] data;
            StreamingLoadOutcome byteOutcome = TakeOrRequestBytes(key, out data, out failure);
            if (byteOutcome != StreamingLoadOutcome.Loaded)
                return byteOutcome;

            bytes.Remove(key);
            try
            {
                archive = TxdReader.Read(data);
                resources.AddDictionary(name, archive);
                if (name.Equals("generic", StringComparison.OrdinalIgnoreCase))
                    resources.AddFallbackDictionary(archive);
                failure = null;
                return StreamingLoadOutcome.Loaded;
            }
            catch (Exception exception)
            {
                if (!IsRecoverable(exception))
                    throw;
                archive = null;
                failure = "TXD '" + name + "' could not be decoded: " + exception.Message;
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

            string key = DffKey(name);
            byte[] data;
            StreamingLoadOutcome byteOutcome = TakeOrRequestBytes(key, out data, out failure);
            if (byteOutcome != StreamingLoadOutcome.Loaded)
            {
                txds.Release(definition.Txd);
                return byteOutcome;
            }

            bytes.Remove(key);
            try
            {
                DffDocument document = DffReader.Read(data);
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