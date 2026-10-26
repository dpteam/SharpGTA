using System;
using System.Globalization;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.Mathematics;
using SharpGTA.IO;
using SharpGTA.Rendering;

namespace SharpGTA.Headless
{
    internal static class RenderPreviewRunner
    {
        internal static void Run(GameDataLoader loader, HeadlessOptions options, TextWriter output)
        {
            RenderResourceManager resources = new RenderResourceManager();
            for (int i = 0; i < options.TxdPaths.Count; i++)
                resources.AddDictionary(loader.LoadTxd(options.TxdPaths[i]));
            for (int i = 0; i < options.TxdEntries.Count; i++)
                resources.AddDictionary(loader.LoadTxdEntry(options.TxdEntries[i]));

            int textureCount = 0;
            long textureBytes = 0;
            LoadStandardTextures(loader, resources);
            for (int i = 0; i < options.DffPaths.Count; i++)
                RunModel(loader.LoadDff(options.DffPaths[i]), options.DffPaths[i],
                    resources, ref textureCount, ref textureBytes, output);
            for (int i = 0; i < options.DffEntries.Count; i++)
                RunModel(loader.LoadDffEntry(options.DffEntries[i]), options.DffEntries[i],
                    resources, ref textureCount, ref textureBytes, output);

            output.WriteLine("Render-Resources: dictionaries=" + resources.TextureDictionaryCount.ToString(CultureInfo.InvariantCulture) +
                " textures=" + textureCount.ToString(CultureInfo.InvariantCulture) +
                " textureBytes=" + textureBytes.ToString(CultureInfo.InvariantCulture) +
                " missing=" + resources.MissingTextureCount.ToString(CultureInfo.InvariantCulture));
        }

        private static void LoadStandardTextures(GameDataLoader loader, RenderResourceManager resources)
        {
            // Player models use their model TXD when present. Other model textures are
            // resolved by the existing dictionaries; no whole-IMG TXD preload is needed.
            if (loader.Images.Find("player.txd") != null)
                resources.AddDictionary(loader.LoadTxdEntry("player.txd"));
        }

        private static void RunModel(DffDocument document, string source,
            RenderResourceManager resources, ref int textureCount, ref long textureBytes, TextWriter output)
        {
            if (document.Clumps.Length == 0)
                throw new AssetFormatException("No clumps in render preview source: " + source);
            GeometryRenderData[] geometry = resources.GetClump(source, document);
            for (int i = 0; i < geometry.Length; i++)
            {
                for (int j = 0; j < geometry[i].Materials.Length; j++)
                {
                    string textureName = geometry[i].Materials[j].TextureName;
                    if (textureName != null && resources.GetTexture(textureName) != null)
                    {
                        textureCount++;
                        textureBytes += resources.GetTexture(textureName).Rgba.Length;
                    }
                }
            }

            // Use a unit transform: preview validates resource conversion, not entity placement.
            RenderSceneSnapshot snapshot;
            if (geometry.Length == 0)
                snapshot = new RenderSceneSnapshot(new RenderDrawCall[0]);
            else
            {
                RenderSceneBuilderHelper helper = new RenderSceneBuilderHelper();
                snapshot = helper.Build(geometry);
            }

            using (NullRenderDevice device = new NullRenderDevice())
            {
                device.Render(snapshot);
                output.WriteLine("Render-Preview: source=" + source + " backend=" + device.Name +
                    " atomics=" + geometry.Length.ToString(CultureInfo.InvariantCulture) +
                    " drawCalls=" + device.LastStatistics.DrawCallCount.ToString(CultureInfo.InvariantCulture) +
                    " triangles=" + device.LastStatistics.TriangleCount.ToString(CultureInfo.InvariantCulture) +
                    " invalid=" + device.LastStatistics.InvalidReferenceCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        private sealed class RenderSceneBuilderHelper
        {
            internal RenderSceneSnapshot Build(GeometryRenderData[] geometry)
            {
                System.Collections.Generic.List<RenderDrawCall> calls =
                    new System.Collections.Generic.List<RenderDrawCall>();
                for (int i = 0; i < geometry.Length; i++)
                {
                    for (int j = 0; j < geometry[i].Meshes.Length; j++)
                    {
                        MeshData mesh = geometry[i].Meshes[j];
                        MaterialData material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < geometry[i].Materials.Length
                            ? geometry[i].Materials[mesh.MaterialIndex]
                            : null;
                        calls.Add(new RenderDrawCall(geometry[i], mesh, material, Matrix.Identity));
                    }
                }
                return new RenderSceneSnapshot(calls);
            }
        }
    }
}
