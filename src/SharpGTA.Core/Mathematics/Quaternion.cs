using System;

namespace SharpGTA.Mathematics
{
    public struct Quaternion : IEquatable<Quaternion>
    {
        public static readonly Quaternion Identity = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float Length()
        {
            return (float)Math.Sqrt(LengthSquared());
        }

        public float LengthSquared()
        {
            return X * X + Y * Y + Z * Z + W * W;
        }

        public Quaternion Normalize()
        {
            float lengthSquared = LengthSquared();
            if (lengthSquared == 0.0f)
                return Identity;

            float inverseLength = (float)(1.0 / Math.Sqrt(lengthSquared));
            return new Quaternion(
                X * inverseLength,
                Y * inverseLength,
                Z * inverseLength,
                W * inverseLength);
        }

        public Matrix ToMatrix()
        {
            return Matrix.CreateFromQuaternion(this);
        }

        public Quaternion Multiply(Quaternion other)
        {
            return Multiply(this, other);
        }

        public static Quaternion FromAxisAngle(Vector3 axis, float angle)
        {
            // As in re3, the caller supplies a unit-length axis.
            float halfAngle = angle * 0.5f;
            float halfSin = (float)Math.Sin(halfAngle);
            float halfCos = (float)Math.Cos(halfAngle);
            return new Quaternion(
                axis.X * halfSin,
                axis.Y * halfSin,
                axis.Z * halfSin,
                halfCos);
        }

        public static Quaternion Normalize(Quaternion value)
        {
            return value.Normalize();
        }

        public static Quaternion Multiply(Quaternion left, Quaternion right)
        {
            return new Quaternion(
                right.Z * left.Y - left.Z * right.Y + left.X * right.W + right.X * left.W,
                right.X * left.Z - left.X * right.Z + left.Y * right.W + right.Y * left.W,
                right.Y * left.X - left.Y * right.X + left.Z * right.W + right.Z * left.W,
                right.W * left.W - right.X * left.X - right.Y * left.Y - right.Z * left.Z);
        }

        public static bool operator ==(Quaternion left, Quaternion right)
        {
            return left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        }

        public static bool operator !=(Quaternion left, Quaternion right)
        {
            return !(left == right);
        }

        public bool Equals(Quaternion other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Quaternion && Equals((Quaternion)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                hash = hash * 31 + W.GetHashCode();
                return hash;
            }
        }
    }
}
