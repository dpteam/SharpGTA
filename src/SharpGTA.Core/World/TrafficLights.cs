using System;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>`CAR_LIGHTS_*` from TrafficLights.h, plus the yellow stage in between.</summary>
    public enum TrafficLightPhase
    {
        Green = 0,
        Yellow = 1,
        Red = 2
    }

    /// <summary>
    /// The two junction phases and the light-to-phase assignment from `CTrafficLights`. Phase
    /// timings are exact (16 second cycle), but they run on the simulation clock instead of the
    /// original wall-clock `CTimer`, so probe runs stay deterministic.
    /// </summary>
    public static class TrafficLights
    {
        /// <summary>`CTimer::GetTimeInMilliseconds() % 16384`.</summary>
        public const uint CycleMilliseconds = 16384;

        /// <summary>`CTrafficLights::LightForCars1`: green 5 s, yellow 1 s, red for the rest.</summary>
        public static TrafficLightPhase PhaseCars1(uint milliseconds)
        {
            uint period = milliseconds % CycleMilliseconds;
            if (period < 5000)
                return TrafficLightPhase.Green;
            if (period < 5000 + 1000)
                return TrafficLightPhase.Yellow;
            return TrafficLightPhase.Red;
        }

        /// <summary>`CTrafficLights::LightForCars2`: red 6 s, green 5 s, yellow 1 s, red.</summary>
        public static TrafficLightPhase PhaseCars2(uint milliseconds)
        {
            uint period = milliseconds % CycleMilliseconds;
            if (period < 6000)
                return TrafficLightPhase.Red;
            if (period < 12000 - 1000)
                return TrafficLightPhase.Green;
            if (period < 12000)
                return TrafficLightPhase.Yellow;
            return TrafficLightPhase.Red;
        }

        /// <summary>`PED_LIGHTS_*` from TrafficLights.h.</summary>
        public enum PedPhase
        {
            DontWalk = 0,
            Walk = 1,
            WalkBlink = 2
        }

        /// <summary>
        /// `CTrafficLights::LightForPeds`: 12 seconds of "don't walk", then walk, with the last
        /// second blinking. Only an exact walk phase lets a waiting ped cross.
        /// </summary>
        public static PedPhase PhasePeds(uint milliseconds)
        {
            uint period = milliseconds % CycleMilliseconds;
            if (period < 12000)
                return PedPhase.DontWalk;
            if (period < CycleMilliseconds - 1000)
                return PedPhase.Walk;
            return PedPhase.WalkBlink;
        }

        /// <summary>
        /// `CTrafficLights::FindTrafficLightType`: a light facing roughly east or west governs
        /// phase 1, everything else phase 2. The original measures the angle with its own
        /// `GetATanOfXY(x, y)`, which is `atan2(y, x)` normalised to [0, 2π).
        /// </summary>
        public static int SignalType(Vector3 forward)
        {
            double degrees = Math.Atan2(forward.Y, forward.X) * (180.0 / Math.PI);
            if (degrees < 0.0)
                degrees += 360.0;
            if ((degrees > 60.0 && degrees < 150.0) || (degrees > 240.0 && degrees < 330.0))
                return 1;
            return 2;
        }

        /// <summary>
        /// Stores a light on one car connection. Both games treat the more connected end of the
        /// link as "the" node; when that is not the node the link is registered under, the stop
        /// only applies to cars heading for it (`trafficLightDirection` in VC, the 0x80 flag in
        /// III). Ties go to the other end, as in both scans.
        /// </summary>
        public static void Mark(PathGraph graph, PathGraphNode node, int linkIndex, int toNode, int type)
        {
            if (graph == null)
                throw new ArgumentNullException("graph");
            if (node == null)
                throw new ArgumentNullException("node");
            int owner = node.LinkOwnerNode(linkIndex);
            int fromNode = node.GlobalIndex;
            int chosen = graph.Nodes[fromNode].LinkCount > graph.Nodes[toNode].LinkCount
                ? fromNode
                : toNode;
            node.SetCarLinkTrafficLight(linkIndex, type, owner != chosen);
        }
    }
}