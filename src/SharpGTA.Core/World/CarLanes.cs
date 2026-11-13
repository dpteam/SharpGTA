using System;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// Lane geometry taken from `CCarPathLink`. Cars do not drive on the path centre line: they
    /// drive in a lane offset sideways from the link, and the offset side follows the travel
    /// direction, so opposing traffic never shares one line. Constants and formulas are
    /// `LANE_WIDTH` (CarCtrl.h) and `CCarPathLink::OneWayLaneOffset` (PathFind.h).
    /// </summary>
    public static class CarLanes
    {
        /// <summary>`LANE_WIDTH` from CarCtrl.h: one lane is five world units wide.</summary>
        public const float LaneWidth = 5.0f;

        /// <summary>
        /// `CCarPathLink::OneWayLaneOffset`: on a one-way link the lanes are centred on the link
        /// line, on a two-way link they all sit on the travel side of it.
        /// </summary>
        public static float OneWayLaneOffset(int leftLanes, int rightLanes)
        {
            if (leftLanes == 0)
                return 0.5f - 0.5f * rightLanes;
            if (rightLanes == 0)
                return 0.5f - 0.5f * leftLanes;
            return 0.5f;
        }

        /// <summary>
        /// Lanes available when travelling along `direction`: `+1` means toward the lower node
        /// index, which is the direction `CCarPathLink::GetDirX/Y` points at.
        /// </summary>
        public static int LanesInDirection(int leftLanes, int rightLanes, int direction)
        {
            return direction > 0 ? leftLanes : rightLanes;
        }

        /// <summary>
        /// Sideways offset for `laneUnits` lanes to the right of `forward`, as the original
        /// computes it: `(forwardY, -forwardX) * laneUnits * LANE_WIDTH`.
        /// </summary>
        public static Vector3 Offset(Vector3 forward, float laneUnits)
        {
            return new Vector3(forward.Y * laneUnits * LaneWidth,
                -forward.X * laneUnits * LaneWidth, 0.0f);
        }
    }
}