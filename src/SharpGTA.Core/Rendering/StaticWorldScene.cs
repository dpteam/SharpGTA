using System;
using System.Collections.Generic;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Rendering
{
    /// <summary>Bounded static placements; geometry is resolved lazily by the presentation host.</summary>
    public static class StaticWorldScene
    {
        public static RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, Func<StaticInstance, GeometryRenderData[]> modelResolver,
            Func<StaticInstance, Matrix[]> transformResolver)
        {
            if (instances == null)
                throw new ArgumentNullException("instances");
            if (modelResolver == null)
                throw new ArgumentNullException("modelResolver");
            if (transformResolver == null)
                throw new ArgumentNullException("transformResolver");
            if (radius <= 0.0f || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException("radius");
            List<RenderDrawCall> calls = new List<RenderDrawCall>();
            float radiusSquared = radius * radius;
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (instance == null || instance.Model == null)
                    continue;
                Vector3 delta = instance.Position - center;
                if (delta.LengthSquared() > radiusSquared)
                    continue;
                GeometryRenderData[] atomics = modelResolver(instance);
                if (atomics == null)
                    continue;
                calls.AddRange(DffRenderSceneBuilder.Build(atomics,
                    transformResolver(instance), instance.Transform).DrawCalls);
            }
            return new RenderSceneSnapshot(calls);
        }
    }
}
