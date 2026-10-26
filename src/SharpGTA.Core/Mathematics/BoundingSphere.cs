using System;

namespace SharpGTA.Mathematics
{
    public struct BoundingSphere : IEquatable<BoundingSphere>
    {
        public Vector3 Center;
        public float Radius;

        public BoundingSphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool Intersects(BoundingSphere other)
        {
            float radiusSum = Radius + other.Radius;
            return (other.Center - Center).LengthSquared() <= radiusSum * radiusSum;
        }

        public static bool operator ==(BoundingSphere left, BoundingSphere right)
        {
            return left.Center == right.Center && left.Radius == right.Radius;
        }

        public static bool operator !=(BoundingSphere left, BoundingSphere right)
        {
            return !(left == right);
        }

        public bool Equals(BoundingSphere other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is BoundingSphere && Equals((BoundingSphere)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Center.GetHashCode();
                hash = hash * 31 + Radius.GetHashCode();
                return hash;
            }
        }
    }
}
