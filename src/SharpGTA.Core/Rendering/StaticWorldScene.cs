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
        /// Applies timed-object visibility, model draw distance and the original LOD/
        /// non-LOD handoff. LOD proxies appear only beyond the related draw distance.
        /// </summary>
        public static bool IsVisible(StaticInstance instance, Vector3 center, float radius)
        {
            return IsVisible(instance, center, radius, null, null);
        }

        public static bool IsVisible(StaticInstance instance, Vector3 center, float radius,
            Func<ModelDefinition, bool> relatedAvailable)
        {
            return IsVisible(instance, center, radius, relatedAvailable, null);
        }

        public static bool IsVisible(StaticInstance instance, Vector3 center, float radius,
            Func<ModelDefinition, bool> relatedAvailable, TimeObjectController timeObjects)
        {
            if (instance == null || instance.Model == null)
                return false;
            if (timeObjects != null && !timeObjects.IsVisible(instance))
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
            return IsLodFallbackCandidate(instance, center, radius, null);
        }

        public static bool IsLodFallbackCandidate(StaticInstance instance,
            Vector3 center, float radius, TimeObjectController timeObjects)
        {
            if (instance == null || instance.Model == null ||
                instance.Model.RelatedModel == null)
                return false;
            if (timeObjects != null && !timeObjects.IsVisible(instance))
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
            return BuildVisible(instances, center, radius, modelResolver, transformResolver,
                null, null);
        }

        public static RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, Func<StaticInstance, GeometryRenderData[]> modelResolver,
            Func<StaticInstance, Matrix[]> transformResolver,
            Func<ModelDefinition, bool> relatedAvailable)
        {
            return BuildVisible(instances, center, radius, modelResolver, transformResolver,
                relatedAvailable, null);
        }

        public static RenderSceneSnapshot BuildVisible(IList<StaticInstance> instances,
            Vector3 center, float radius, Func<StaticInstance, GeometryRenderData[]> modelResolver,
            Func<StaticInstance, Matrix[]> transformResolver,
            Func<ModelDefinition, bool> relatedAvailable, TimeObjectController timeObjects)
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
                if (!IsVisible(instance, center, radius, relatedAvailable, timeObjects))
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
