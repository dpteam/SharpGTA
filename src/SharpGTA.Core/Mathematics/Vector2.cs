using System;

namespace SharpGTA.Mathematics
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public static readonly Vector2 Zero = new Vector2(0.0f, 0.0f);
        public static readonly Vector2 UnitX = new Vector2(1.0f, 0.0f);
        public static readonly Vector2 UnitY = new Vector2(0.0f, 1.0f);

        public float X;
        public float Y;

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length()
        {
            return (float)Math.Sqrt(LengthSquared());
        }

        public float LengthSquared()
        {
            return X * X + Y * Y;
        }

        public Vector2 Normalize()
        {
            float lengthSquared = LengthSquared();
            if (lengthSquared > 0.0f)
            {
                float inverseLength = (float)(1.0 / Math.Sqrt(lengthSquared));
                return new Vector2(X * inverseLength, Y * inverseLength);
            }
            return new Vector2(1.0f, Y);
        }

        public float Heading()
        {
            return (float)Math.Atan2(-X, Y);
        }

        public static Vector2 Normalize(Vector2 value)
        {
            return value.Normalize();
        }

        public static float Dot(Vector2 left, Vector2 right)
        {
            return left.X * right.X + left.Y * right.Y;
        }

        public static float Distance(Vector2 left, Vector2 right)
        {
            return (right - left).Length();
        }

        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X + right.X, left.Y + right.Y);
        }

        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            return new Vector2(left.X - right.X, left.Y - right.Y);
        }

        public static Vector2 operator -(Vector2 value)
        {
            return new Vector2(-value.X, -value.Y);
        }

        public static Vector2 operator *(Vector2 value, float scalar)
        {
            return new Vector2(value.X * scalar, value.Y * scalar);
        }

        public static Vector2 operator *(float scalar, Vector2 value)
        {
            return new Vector2(scalar * value.X, scalar * value.Y);
        }

        public static Vector2 operator /(Vector2 value, float scalar)
        {
            return new Vector2(value.X / scalar, value.Y / scalar);
        }

        public static bool operator ==(Vector2 left, Vector2 right)
        {
            return left.X == right.X && left.Y == right.Y;
        }

        public static bool operator !=(Vector2 left, Vector2 right)
        {
            return !(left == right);
        }

        public bool Equals(Vector2 other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector2 && Equals((Vector2)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                return hash;
            }
        }
    }
}
