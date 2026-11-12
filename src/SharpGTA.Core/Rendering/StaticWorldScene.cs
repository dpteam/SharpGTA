using System;
using System.Collections.Generic;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Rendering
{
    /// <summary>Bounded static placements; geometry is resolved lazily by the presentation host.</summary>
    public static class StaticWorldScene
    {
        /// <summary>
        /// Applies model draw distance and the original LOD/non-LOD handoff. LOD
        /// proxies appear only beyond the related model's largest draw distance.
        /// </summary>
        public static bool IsVisible(StaticInstance instance, Vector3 center, float radius)
        {
            return IsVisible(instance, center, radius, null);
        }

        public static bool IsVisible(StaticInstance instance, Vector3 center, float radius,
            Func<ModelDefinition, bool> relatedAvailable)
        {
            if (instance == null || instance.Model == null)
                return false;
            float distanceSquared = (instance.Position - center).LengthSquared();
            if (distanceSquared > radius * radius)
                return false;
            ModelDefinition model = instance.Model;
            float distance = (float)Math.Sqrt(distanceSquared);
            if (model.RelatedModel != null && distance < model.LodNearDistance &&
                (relatedAvailable == null || relatedAvailable(model.RelatedModel)))
                return false;
            float largestDistance = model.LargestLodDistance;
            return largestDistance >= 2.0f && distance <= largestDistance;
        }

        /// <summary>True when a near LOD can replace an unavailable detailed model.</summary>
        public static bool IsLodFallbackCandidate(StaticInstance instance,
            Vector3 center, float radius)
        {
            if (instance == null || instance.Model == null ||
                instance.Model.RelatedModel == null)
                return false;
            float distanceSquared = (instance.Position - center).LengthSquared();
            if (distanceSquared > radius * radius)
                return false;
            float distance = (float)Math.Sqrt(distanceSquared);
            return distance < instance.Model.LodNearDistance &&
                instance.Model.LargestLodDistance >= 2.0f &&
                distance <= instance.Model.LargestLodDistance;
        }

        public static RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, Func<StaticInstance, GeometryRenderData[]> modelResolver,
            Func<StaticInstance, Matrix[]> transformResolver)
        {
            return BuildVisible(instances, center, radius, modelResolver, transformResolver, null);
        }

        public static RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, Func<StaticInstance, GeometryRenderData[]> modelResolver,
            Func<StaticInstance, Matrix[]> transformResolver,
            Func<ModelDefinition, bool> relatedAvailable)
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
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (!IsVisible(instance, center, radius, relatedAvailable))
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
