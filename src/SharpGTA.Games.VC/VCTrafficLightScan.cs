using System;
using SharpGTA.Data;
using SharpGTA.Mathematics;
using SharpGTA.World;

namespace SharpGTA.Games.VC
{
    /// <summary>
    /// `CTrafficLights::ScanForLightsOnMap` for VC: a light's own 17/-15 axis is intersected with
    /// the car link, which is a different test from III's proximity band. VC also has four light
    /// models, and its direction flag is a bool instead of the III 0x80 bit.
    /// </summary>
    internal static class VCTrafficLightScan
    {
        /// <summary>`IsTrafficLight` from reVC's ModelIndices.h.</summary>
        private static readonly string[] LightModelNames = { "trafficlight1", "MTraffic4", "MTraffic1", "MTraffic2" };

        internal static void Apply(StaticWorld world, PathGraph graph)
        {
            int lights = 0;
            for (int i = 0; i < world.Instances.Count; i++)
            {
                StaticInstance instance = world.Instances[i];
                if (instance.Model == null || !IsLight(instance.Model.Name))
                    continue;
                lights++;
                // `light->GetMatrix() * CVector(17, 0, 0)` and the -15 end of the axis.
                Vector3 axisStart = instance.Transform.TransformPoint(new Vector3(17.0f, 0.0f, 0.0f));
                Vector3 axisEnd = instance.Transform.TransformPoint(new Vector3(-15.0f, 0.0f, 0.0f));
                int type = TrafficLights.SignalType(NormaliseXY(instance.Transform.Forward));
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
                        if (n > toNode)
                            continue;
                        Vector3 other = graph.Nodes[toNode].Position;
                        float midpointZ = (node.Position.Z + other.Z) * 0.5f;
                        if (!(Math.Abs(axisStart.Z - midpointZ) < 10.0f))
                            continue;
                        if (!SegmentsIntersect(axisStart.X, axisStart.Y, axisEnd.X, axisEnd.Y,
                            node.Position.X, node.Position.Y, other.X, other.Y))
                        {
                            continue;
                        }
                        TrafficLights.Mark(graph, node, link, toNode, type);
                    }
                }
                // `Check peds`: same walk-phase crossings as III, measured from the light position.
                for (int n = 0; n < graph.NodeCount; n++)
                {
                    PathGraphNode node = graph.Nodes[n];
                    if (node.Kind != PathKind.Ped)
                        continue;
                    Vector3 lightPosition = instance.Position;
                    float dist1 = Math.Abs(node.Position.X - lightPosition.X) +
                        Math.Abs(node.Position.Y - lightPosition.Y);
                    if (!(dist1 < 50.0f))
                        continue;
                    for (int link = 0; link < node.LinkCount; link++)
                    {
                        if (!node.LinkCrossesRoad(link))
                            continue;
                        int toNode = node.Links[link];
                        Vector3 other = graph.Nodes[toNode].Position;
                        float dist2 = Math.Abs(other.X - lightPosition.X) +
                            Math.Abs(other.Y - lightPosition.Y);
                        if (dist1 < 15.0f || dist2 < 15.0f)
                            MarkPedCrossing(graph, node, link, toNode);
                    }
                }
            }
            graph.SetTrafficLightObjectCount(lights);
        }

        private static void MarkPedCrossing(PathGraph graph, PathGraphNode node, int link, int toNode)
        {
            node.SetLinkPedTrafficLight(link, true);
            PathGraphNode other = graph.Nodes[toNode];
            int back = other.FindLink(node.GlobalIndex);
            if (back >= 0)
                other.SetLinkPedTrafficLight(back, true);
        }

        private static bool IsLight(string name)
        {
            for (int i = 0; i < LightModelNames.Length; i++)
            {
                if (string.Equals(name, LightModelNames[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static Vector3 NormaliseXY(Vector3 vector)
        {
            float length = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
            if (length <= 0.0001f)
                return new Vector3(0.0f, 1.0f, 0.0f);
            return new Vector3(vector.X / length, vector.Y / length, 0.0f);
        }

        /// <summary>`DoesLineSegmentIntersect` exactly as reVC writes it.</summary>
        private static bool SegmentsIntersect(float l1x1, float l1y1, float l1x2, float l1y2,
            float l2x1, float l2y1, float l2x2, float l2y2)
        {
            return ((l2y2 - l1y1) * (l1x2 - l1x1) + (l1x1 - l2x2) * (l1y2 - l1y1)) *
                ((l2y1 - l1y1) * (l1x2 - l1x1) + (l1x1 - l2x1) * (l1y2 - l1y1)) <= 0.0f &&
                ((l1y2 - l2y1) * (l2x2 - l2x1) + (l2y2 - l2y1) * (l2x1 - l1x2)) *
                ((l1y1 - l2y1) * (l2x2 - l2x1) + (l2y2 - l2y1) * (l2x1 - l1x1)) <= 0.0f;
        }
    }
}