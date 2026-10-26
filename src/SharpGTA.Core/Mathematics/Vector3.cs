using System;

namespace SharpGTA.Mathematics
{
    public struct Vector3 : IEquatable<Vector3>
    {
        public static readonly Vector3 Zero = new Vector3(0.0f, 0.0f, 0.0f);
        public static readonly Vector3 UnitX = new Vector3(1.0f, 0.0f, 0.0f);
        public static readonly Vector3 UnitY = new Vector3(0.0f, 1.0f, 0.0f);
        public static readonly Vector3 UnitZ = new Vector3(0.0f, 0.0f, 1.0f);

        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length()
        {
            return (float)Math.Sqrt(LengthSquared());
        }

        public float LengthSquared()
        {
            return X * X + Y * Y + Z * Z;
        }

        public Vector3 Normalize()
        {
            float lengthSquared = LengthSquared();
            if (lengthSquared > 0.0f)
            {
                float inverseLength = (float)(1.0 / Math.Sqrt(lengthSquared));
                return new Vector3(X * inverseLength, Y * inverseLength, Z * inverseLength);
            }

            // re3's CVector::Normalise uses UnitX as the safe fallback.
            return new Vector3(1.0f, Y, Z);
        }

        public float Heading()
        {
            return (float)Math.Atan2(-X, Y);
        }

        public static Vector3 Normalize(Vector3 value)
        {
            return value.Normalize();
        }

        public static float Dot(Vector3 left, Vector3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        public static Vector3 Cross(Vector3 left, Vector3 right)
        {
            return new Vector3(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
        }

        public static float Distance(Vector3 left, Vector3 right)
        {
            return (right - left).Length();
        }

        public static Vector3 Min(Vector3 left, Vector3 right)
        {
            return new Vector3(
                Math.Min(left.X, right.X),
                Math.Min(left.Y, right.Y),
                Math.Min(left.Z, right.Z));
        }

        public static Vector3 Max(Vector3 left, Vector3 right)
        {
            return new Vector3(
                Math.Max(left.X, right.X),
                Math.Max(left.Y, right.Y),
                Math.Max(left.Z, right.Z));
        }

        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static Vector3 operator -(Vector3 value)
        {
            return new Vector3(-value.X, -value.Y, -value.Z);
        }

        public static Vector3 operator *(Vector3 value, float scalar)
        {
            return new Vector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static Vector3 operator *(float scalar, Vector3 value)
        {
            return new Vector3(scalar * value.X, scalar * value.Y, scalar * value.Z);
        }

        public static Vector3 operator /(Vector3 value, float scalar)
        {
            return new Vector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        }

        public static bool operator ==(Vector3 left, Vector3 right)
        {
            return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        }

        public static bool operator !=(Vector3 left, Vector3 right)
        {
            return !(left == right);
        }

        public bool Equals(Vector3 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector3 && Equals((Vector3)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }
    }
}
