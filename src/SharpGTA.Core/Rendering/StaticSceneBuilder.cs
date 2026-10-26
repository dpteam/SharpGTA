using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.World;

namespace SharpGTA.Rendering
{
    public sealed class StaticSceneBuilder
    {
        public RenderSceneSnapshot Build(StaticInstance instance, GeometryRenderData[] atomics)
        {
            if (instance == null)
                throw new ArgumentNullException("instance");
            if (atomics == null)
                throw new ArgumentNullException("atomics");
            List<RenderDrawCall> calls = new List<RenderDrawCall>();
            for (int i = 0; i < atomics.Length; i++)
            {
                GeometryRenderData model = atomics[i];
                for (int j = 0; j < model.Meshes.Length; j++)
                {
                    MeshData mesh = model.Meshes[j];
                    MaterialData material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Length
                        ? model.Materials[mesh.MaterialIndex]
                        : null;
                    calls.Add(new RenderDrawCall(model, mesh, material, instance.Transform));
                }
            }
            return new RenderSceneSnapshot(calls);
        }
    }
}
