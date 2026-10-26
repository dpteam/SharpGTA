using System;

namespace SharpGTA.Mathematics
{
    public struct BoundingBox : IEquatable<BoundingBox>
    {
        public Vector3 Min;
        public Vector3 Max;

        public BoundingBox(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public bool Contains(Vector3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                point.Y >= Min.Y && point.Y <= Max.Y &&
                point.Z >= Min.Z && point.Z <= Max.Z;
        }

        public bool Intersects(BoundingBox other)
        {
            return Min.X <= other.Max.X && Max.X >= other.Min.X &&
                Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
                Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
        }

        public BoundingBox Expand(Vector3 point)
        {
            return new BoundingBox(Vector3.Min(Min, point), Vector3.Max(Max, point));
        }

        public BoundingBox Grow(float amount)
        {
            return Grow(new Vector3(amount, amount, amount));
        }

        public BoundingBox Grow(Vector3 amount)
        {
            return new BoundingBox(Min - amount, Max + amount);
        }

        public static bool operator ==(BoundingBox left, BoundingBox right)
        {
            return left.Min == right.Min && left.Max == right.Max;
        }

        public static bool operator !=(BoundingBox left, BoundingBox right)
        {
            return !(left == right);
        }

        public bool Equals(BoundingBox other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is BoundingBox && Equals((BoundingBox)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Min.GetHashCode();
                hash = hash * 31 + Max.GetHashCode();
                return hash;
            }
        }
    }
}
