using SharpGTA.Collision;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>A world-space collision hit and the static instance that produced it.</summary>
    public struct StaticWorldHit
    {
        public StaticInstance Instance;
        public Vector3 Position;
        public Vector3 Normal;
        public float Distance;
        public byte Surface;
        public byte Piece;
        public ColShapeType ShapeType;
        public int ShapeIndex;

        internal StaticWorldHit(StaticInstance instance, Vector3 position, Vector3 normal,
            float distance, ColHit localHit)
        {
            Instance = instance;
            Position = position;
            Normal = normal;
            Distance = distance;
            Surface = localHit.Surface;
            Piece = localHit.Piece;
            ShapeType = localHit.ShapeType;
            ShapeIndex = localHit.ShapeIndex;
        }
    }
}
