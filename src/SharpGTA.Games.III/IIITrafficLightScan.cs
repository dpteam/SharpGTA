using System;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Games.III
{
    /// <summary>
    /// `CTrafficLights::ScanForLightsOnMap` for III: every placed `trafficlight1` dummy marks the
    /// car links that pass in front of it, and the link knows which of the two junction phases
    /// governs it. re3 tests a band in front of the light (within 15 units towards its base,
    /// less than 3 units to the side, less than 15 units below the node), not an intersection.
    /// </summary>
    internal static class IIITrafficLightScan
    {
        /// <summary>`MI_TRAFFICLIGHTS` in III is the model named `trafficlight1`.</summary>
        internal const string LightModelName = "trafficlight1";

        internal static void Apply(StaticWorld world, PathGraph graph)
        {
            int lights = 0;
            for (int i = 0; i < world.Instances.Count; i++)
            {
                StaticInstance instance = world.Instances[i];
                if (instance.Model == null ||
                    !string.Equals(instance.Model.Name, LightModelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                lights++;
                Vector3 position = instance.Transform.Position;
                Vector3 forward = NormaliseXY(instance.Transform.Forward);
                Vector3 right = NormaliseXY(instance.Transform.Right);
                int type = TrafficLights.SignalType(forward);
                for (int n = 0; n < graph.NodeCount; n++)
                {
                    PathGraphNode node = graph.Nodes[n];
                    if (node.Kind != PathKind.Car)
                        continue;
                    for (int link = 0; link < node.LinkCount; link++)
                    {
                        if (!node.HasCarLink(link))
                            continue;
                        int toNode = node.Links[link];
                        // One physical link is shared by both directions; touch it once.
                        if (n > toNode)
                            continue;
                        Vector3 delta = node.LinkMidpoint(link) - position;
                        float dotY = Math.Abs(Vector3.Dot(delta, forward));
                        float dotX = Vector3.Dot(delta, right);
                        if (!(dotX < 0.0f && dotX > -15.0f && dotY < 3.0f))
                            continue;
                        // The original only checks the upper bound, so a light far below the node
                        // still marks the link.
                        float dz = graph.Nodes[node.LinkOwnerNode(link)].Position.Z - position.Z;
                        if (!(dz < 15.0f))
                            continue;
                        TrafficLights.Mark(graph, node, link, toNode, type);
                    }
                }
            }
            graph.SetTrafficLightObjectCount(lights);
        }

        private static Vector3 NormaliseXY(Vector3 vector)
        {
            float length = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
            if (length <= 0.0001f)
                return new Vector3(0.0f, 1.0f, 0.0f);
            return new Vector3(vector.X / length, vector.Y / length, 0.0f);
        }
    }
}