using SharpGTA.Mathematics;

namespace SharpGTA.Collision
{
    public struct ColSphere
    {
        public float Radius;
        public Vector3 Center;
        public byte Surface;
        public byte Piece;

        public ColSphere(float radius, Vector3 center, byte surface, byte piece)
        {
            Radius = radius;
            Center = center;
            Surface = surface;
            Piece = piece;
        }
    }

    /// <summary>A line exactly as stored in a legacy COLL payload.</summary>
    public struct ColLine
    {
        public Vector3 P0;
        public Vector3 P1;

        public ColLine(Vector3 p0, Vector3 p1)
        {
            P0 = p0;
            P1 = p1;
        }
    }

    public struct ColBox
    {
        public Vector3 Min;
        public Vector3 Max;
        public byte Surface;
        public byte Piece;

        public ColBox(Vector3 min, Vector3 max, byte surface, byte piece)
        {
            Min = min;
            Max = max;
            Surface = surface;
            Piece = piece;
        }
    }

    public struct ColTriangle
    {
        public ushort A;
        public ushort B;
        public ushort C;
        public byte Surface;

        public ColTriangle(ushort a, ushort b, ushort c, byte surface)
        {
            A = a;
            B = b;
            C = c;
            Surface = surface;
        }
    }

    public struct ColTrianglePlane
    {
        public Vector3 Normal;
        public float Distance;
        public bool IsDegenerate;

        internal ColTrianglePlane(Vector3 normal, float distance, bool isDegenerate)
        {
            Normal = normal;
            Distance = distance;
            IsDegenerate = isDegenerate;
        }

        public float Evaluate(Vector3 point)
        {
            return Normal.X * point.X + Normal.Y * point.Y + Normal.Z * point.Z - Distance;
        }
    }

    /// <summary>
    /// A local-space ray. Direction need not be normalized; MaxDistance is measured
    /// in world/model units along its normalized direction.
    /// </summary>
    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float MaxDistance;

        public Ray(Vector3 origin, Vector3 direction)
            : this(origin, direction, float.PositiveInfinity)
        {
        }

        public Ray(Vector3 origin, Vector3 direction, float maxDistance)
        {
            Origin = origin;
            Direction = direction;
            MaxDistance = maxDistance;
        }
    }

    public enum ColShapeType
    {
        Sphere,
        Box,
        Triangle
    }

    public struct ColHit
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Distance;
        public byte Surface;
        public byte Piece;
        public ColShapeType ShapeType;
        public int ShapeIndex;

        internal ColHit(Vector3 position, Vector3 normal, float distance, byte surface,
            byte piece, ColShapeType shapeType, int shapeIndex)
        {
            Position = position;
            Normal = normal;
            Distance = distance;
            Surface = surface;
            Piece = piece;
            ShapeType = shapeType;
            ShapeIndex = shapeIndex;
        }
    }
}
