using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    public sealed class PathNodeWorld
    {
        internal PathNodeWorld(PathNode sourceNode, Vector3 worldPosition)
        {
            SourceNode = sourceNode;
            WorldPosition = worldPosition;
        }

        public PathNode SourceNode { get; private set; }
        public Vector3 WorldPosition { get; private set; }
        public int NextNodeIndex { get { return SourceNode.NextNodeIndex; } }
    }

    /// <summary>
    /// Source-ordered path groups and explicit IPL transforms. No instance lookup is
    /// performed until BindInstances is called.
    /// </summary>
    public sealed class PathWorld
    {
        private readonly List<PathGroup> groups = new List<PathGroup>();
        private readonly ReadOnlyCollection<PathGroup> readOnlyGroups;

        public PathWorld()
        {
            readOnlyGroups = groups.AsReadOnly();
        }

        public IList<PathGroup> Groups { get { return readOnlyGroups; } }
        public int GroupCount { get { return groups.Count; } }
        public int NodeCount { get; private set; }
        public int DetachedCount { get; private set; }
        public int WaterCount { get; private set; }
        public int ResolvedCount { get; private set; }
        public int UnresolvedCount { get { return GroupCount - ResolvedCount; } }

        public void AddGroups(IList<PathGroup> pathGroups)
        {
            if (pathGroups == null)
                throw new ArgumentNullException("pathGroups");
            for (int i = 0; i < pathGroups.Count; i++)
            {
                if (pathGroups[i] == null)
                    throw new ArgumentException("Path groups cannot contain null entries.", "pathGroups");
            }
            for (int i = 0; i < pathGroups.Count; i++)
                groups.Add(pathGroups[i]);
            RefreshCounts();
        }

        /// <summary>
        /// Binds attached groups to the first instance with the same raw model ID and,
        /// when present, a case-insensitively equal model name. Detached groups are
        /// resolved without an instance. Returns the number of unresolved groups.
        /// </summary>
        public int BindInstances(IList<StaticInstance> instances)
        {
            if (instances == null)
                throw new ArgumentNullException("instances");

            for (int i = 0; i < groups.Count; i++)
            {
                PathGroup group = groups[i];
                if (group.IsDetached)
                {
                    group.ObjectTransform = null;
                    continue;
                }

                StaticInstance instance = FindFirstInstance(group, instances);
                group.ObjectTransform = instance == null
                    ? null
                    : new Matrix?(instance.Transform);
            }
            RefreshCounts();
            return UnresolvedCount;
        }

        /// <summary>Uses the transform previously stored by BindInstances.</summary>
        public Vector3 GetWorldPosition(PathGroup group, PathNode node)
        {
            if (group == null)
                throw new ArgumentNullException("group");
            return GetWorldPosition(group, node, group.ObjectTransform);
        }

        /// <summary>
        /// Converts compressed path coordinates to world units. Detached groups ignore
        /// the optional object transform; attached groups require one.
        /// </summary>
        public Vector3 GetWorldPosition(PathGroup group, PathNode node, Matrix? objectTransform)
        {
            if (group == null)
                throw new ArgumentNullException("group");
            if (node == null)
                throw new ArgumentNullException("node");

            Vector3 localPosition = node.StoredPosition / 16.0f;
            if (group.IsDetached)
                return localPosition;
            if (!objectTransform.HasValue)
            {
                throw new InvalidOperationException(
                    "An attached path group requires a bound or explicit object transform.");
            }
            return objectTransform.Value.TransformPoint(localPosition);
        }

        public PathNodeWorld[] TransformGroup(PathGroup group, Matrix objectTransform)
        {
            if (group == null)
                throw new ArgumentNullException("group");

            PathNodeWorld[] result = new PathNodeWorld[group.Nodes.Length];
            for (int i = 0; i < result.Length; i++)
            {
                PathNode node = group.Nodes[i];
                result[i] = new PathNodeWorld(node,
                    GetWorldPosition(group, node, new Matrix?(objectTransform)));
            }
            return result;
        }

        private static StaticInstance FindFirstInstance(PathGroup group,
            IList<StaticInstance> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                StaticInstance instance = instances[i];
                if (instance == null || instance.Model == null || instance.Model.Id != group.ModelId)
                    continue;
                if (!string.IsNullOrEmpty(group.ModelName) &&
                    !string.Equals(instance.Model.Name, group.ModelName, StringComparison.OrdinalIgnoreCase))
                    continue;
                return instance;
            }
            return null;
        }

        private void RefreshCounts()
        {
            NodeCount = 0;
            DetachedCount = 0;
            WaterCount = 0;
            ResolvedCount = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                PathGroup group = groups[i];
                NodeCount += group.Nodes.Length;
                if (group.IsDetached)
                    DetachedCount++;
                if (group.Kind == PathKind.Water)
                    WaterCount++;
                if (group.IsDetached || group.HasTransform)
                    ResolvedCount++;
            }
        }
    }
}
