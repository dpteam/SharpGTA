using System;

namespace SharpGTA.Mathematics
{
    public struct Matrix : IEquatable<Matrix>
    {
        public static readonly Matrix Identity = new Matrix(
            Vector3.UnitX,
            Vector3.UnitY,
            Vector3.UnitZ,
            Vector3.Zero);

        // RenderWare right/up/at/pos map to Right/Forward/Up/Position.
        public Vector3 Right;
        public Vector3 Forward;
        public Vector3 Up;
        public Vector3 Position;

        public Matrix(Vector3 right, Vector3 forward, Vector3 up, Vector3 position)
        {
            Right = right;
            Forward = forward;
            Up = up;
            Position = position;
        }

        public static Matrix CreateTranslation(Vector3 translation)
        {
            return new Matrix(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, translation);
        }

        public static Matrix CreateTranslation(float x, float y, float z)
        {
            return CreateTranslation(new Vector3(x, y, z));
        }

        public static Matrix CreateScale(float scale)
        {
            return CreateScale(new Vector3(scale, scale, scale));
        }

        public static Matrix CreateScale(Vector3 scale)
        {
            return new Matrix(
                new Vector3(scale.X, 0.0f, 0.0f),
                new Vector3(0.0f, scale.Y, 0.0f),
                new Vector3(0.0f, 0.0f, scale.Z),
                Vector3.Zero);
        }

        public static Matrix CreateScale(float x, float y, float z)
        {
            return CreateScale(new Vector3(x, y, z));
        }

        public static Matrix CreateRotationX(float angle)
        {
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return new Matrix(
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, cosine, sine),
                new Vector3(0.0f, -sine, cosine),
                Vector3.Zero);
        }

        public static Matrix CreateRotationY(float angle)
        {
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return new Matrix(
                new Vector3(cosine, 0.0f, -sine),
                new Vector3(0.0f, 1.0f, 0.0f),
                new Vector3(sine, 0.0f, cosine),
                Vector3.Zero);
        }

        public static Matrix CreateRotationZ(float angle)
        {
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return new Matrix(
                new Vector3(cosine, sine, 0.0f),
                new Vector3(-sine, cosine, 0.0f),
                new Vector3(0.0f, 0.0f, 1.0f),
                Vector3.Zero);
        }

        public static Matrix CreateFromQuaternion(Quaternion quaternion)
        {
            float x2 = quaternion.X + quaternion.X;
            float y2 = quaternion.Y + quaternion.Y;
            float z2 = quaternion.Z + quaternion.Z;

            float xx = quaternion.X * x2;
            float xy = quaternion.X * y2;
            float xz = quaternion.X * z2;
            float yy = quaternion.Y * y2;
            float yz = quaternion.Y * z2;
            float zz = quaternion.Z * z2;
            float wx = quaternion.W * x2;
            float wy = quaternion.W * y2;
            float wz = quaternion.W * z2;

            return new Matrix(
                new Vector3(1.0f - (yy + zz), xy + wz, xz - wy),
                new Vector3(xy - wz, 1.0f - (xx + zz), yz + wx),
                new Vector3(xz + wy, yz - wx, 1.0f - (xx + yy)),
                Vector3.Zero);
        }

        public Vector3 TransformPoint(Vector3 point)
        {
            return new Vector3(
                Right.X * point.X + Forward.X * point.Y + Up.X * point.Z + Position.X,
                Right.Y * point.X + Forward.Y * point.Y + Up.Y * point.Z + Position.Y,
                Right.Z * point.X + Forward.Z * point.Y + Up.Z * point.Z + Position.Z);
        }

        public Vector3 TransformDirection(Vector3 direction)
        {
            return new Vector3(
                Right.X * direction.X + Forward.X * direction.Y + Up.X * direction.Z,
                Right.Y * direction.X + Forward.Y * direction.Y + Up.Y * direction.Z,
                Right.Z * direction.X + Forward.Z * direction.Y + Up.Z * direction.Z);
        }

        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 translated = point - Position;
            return new Vector3(
                Vector3.Dot(Right, translated),
                Vector3.Dot(Forward, translated),
                Vector3.Dot(Up, translated));
        }

        public Vector3 Multiply3x3(Vector3 vector)
        {
            return TransformDirection(vector);
        }

        public static Vector3 Multiply3x3(Matrix matrix, Vector3 vector)
        {
            return matrix.TransformDirection(vector);
        }

        public static bool operator ==(Matrix left, Matrix right)
        {
            return left.Right == right.Right &&
                left.Forward == right.Forward &&
                left.Up == right.Up &&
                left.Position == right.Position;
        }

        public static bool operator !=(Matrix left, Matrix right)
        {
            return !(left == right);
        }

        public bool Equals(Matrix other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Matrix && Equals((Matrix)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Right.GetHashCode();
                hash = hash * 31 + Forward.GetHashCode();
                hash = hash * 31 + Up.GetHashCode();
                hash = hash * 31 + Position.GetHashCode();
                return hash;
            }
        }
    }
}
