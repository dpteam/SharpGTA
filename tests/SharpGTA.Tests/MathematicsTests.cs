using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class MathematicsTests
    {
        [Test]
        public void Vector3OperationsAndZeroNormalization()
        {
            Vector3 value = new Vector3(3.0f, 4.0f, 12.0f);
            Assert.Near(169.0f, value.LengthSquared());
            Assert.Near(13.0f, value.Length());
            AssertVector(new Vector3(3.0f / 13.0f, 4.0f / 13.0f, 12.0f / 13.0f), value.Normalize(), 0.000001f);
            Assert.Equal(Vector3.UnitX, Vector3.Zero.Normalize());
            Assert.Equal(Vector3.UnitX, Vector3.Normalize(Vector3.Zero));

            Assert.Near(12.0f, Vector3.Dot(new Vector3(1.0f, 2.0f, 3.0f), new Vector3(4.0f, -5.0f, 6.0f)));
            Assert.Equal(Vector3.UnitZ, Vector3.Cross(Vector3.UnitX, Vector3.UnitY));
            Assert.Equal(new Vector3(2.0f, 4.0f, 6.0f), new Vector3(1.0f, 2.0f, 3.0f) * 2.0f);
            Assert.Equal(new Vector3(2.0f, 4.0f, 6.0f), 2.0f * new Vector3(1.0f, 2.0f, 3.0f));
            Assert.Equal(new Vector3(0.5f, 1.0f, 1.5f), new Vector3(1.0f, 2.0f, 3.0f) / 2.0f);
            Assert.Equal(new Vector3(-1.0f, -2.0f, -3.0f), -new Vector3(1.0f, 2.0f, 3.0f));
            Assert.Equal(new Vector3(5.0f, 7.0f, 9.0f), new Vector3(1.0f, 2.0f, 3.0f) + new Vector3(4.0f, 5.0f, 6.0f));
            Assert.Equal(new Vector3(3.0f, 3.0f, 3.0f), new Vector3(4.0f, 5.0f, 6.0f) - new Vector3(1.0f, 2.0f, 3.0f));
            Assert.Near(13.0f, Vector3.Distance(new Vector3(1.0f, 2.0f, 3.0f), new Vector3(4.0f, 6.0f, 15.0f)));

            float halfPi = (float)Math.PI * 0.5f;
            Assert.Near(0.0f, Vector3.UnitY.Heading());
            Assert.Near(halfPi, new Vector3(-1.0f, 0.0f, 0.0f).Heading());
            Assert.Near(-halfPi, Vector3.UnitX.Heading());

            Vector3 equalValue = new Vector3(3.0f, 4.0f, 12.0f);
            Assert.True(value == equalValue);
            Assert.False(value != equalValue);
            Assert.True(value.Equals((object)equalValue));
            Assert.Equal(value.GetHashCode(), equalValue.GetHashCode());
        }

        [Test]
        public void Vector2Operations()
        {
            Vector2 value = new Vector2(3.0f, 4.0f);
            Assert.Near(25.0f, value.LengthSquared());
            Assert.Near(5.0f, value.Length());
            Assert.Near(0.6f, value.Normalize().X);
            Assert.Near(0.8f, value.Normalize().Y);
            Assert.Equal(Vector2.UnitX, Vector2.Zero.Normalize());
            Assert.Near(-5.0f, Vector2.Dot(new Vector2(1.0f, 2.0f), new Vector2(3.0f, -4.0f)));
            Assert.Near(5.0f, Vector2.Distance(Vector2.Zero, value));
            Assert.Near((float)Math.PI * 0.5f, new Vector2(-1.0f, 0.0f).Heading());
            Assert.Equal(new Vector2(2.0f, 6.0f), new Vector2(1.0f, 3.0f) * 2.0f);
            Assert.Equal(value.GetHashCode(), new Vector2(3.0f, 4.0f).GetHashCode());
        }

        [Test]
        public void MatrixRotationsAndTransformsMatchRe3()
        {
            float halfPi = (float)Math.PI * 0.5f;
            Matrix rotationX = Matrix.CreateRotationX(halfPi);
            Matrix rotationY = Matrix.CreateRotationY(halfPi);
            Matrix rotationZ = Matrix.CreateRotationZ(halfPi);

            AssertVector(Vector3.UnitZ, rotationX.TransformDirection(Vector3.UnitY), 0.0001f);
            AssertVector(-Vector3.UnitY, rotationX.TransformDirection(Vector3.UnitZ), 0.0001f);
            AssertVector(Vector3.UnitX, rotationY.TransformDirection(Vector3.UnitZ), 0.0001f);
            AssertVector(-Vector3.UnitZ, rotationY.TransformDirection(Vector3.UnitX), 0.0001f);
            AssertVector(Vector3.UnitY, rotationZ.TransformDirection(Vector3.UnitX), 0.0001f);
            AssertVector(-Vector3.UnitX, rotationZ.TransformDirection(Vector3.UnitY), 0.0001f);

            Matrix translation = Matrix.CreateTranslation(10.0f, 20.0f, 30.0f);
            Matrix scale = Matrix.CreateScale(2.0f, 3.0f, 4.0f);
            Vector3 scaled = scale.TransformPoint(new Vector3(1.0f, 2.0f, 3.0f));
            Assert.Equal(new Vector3(2.0f, 6.0f, 12.0f), scaled);
            Assert.Equal(new Vector3(12.0f, 26.0f, 42.0f), translation.TransformPoint(scaled));
            Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), translation.TransformDirection(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.Equal(rotationZ.TransformDirection(Vector3.UnitX), rotationZ.Multiply3x3(Vector3.UnitX));
            Assert.Equal(rotationZ.TransformDirection(Vector3.UnitX), Matrix.Multiply3x3(rotationZ, Vector3.UnitX));

            Matrix transform = Matrix.CreateRotationZ(0.37f);
            transform.Position = new Vector3(8.0f, -3.0f, 2.0f);
            Vector3 localPoint = new Vector3(2.0f, 5.0f, -1.0f);
            Vector3 worldPoint = transform.TransformPoint(localPoint);
            AssertVector(localPoint, transform.InverseTransformPoint(worldPoint), 0.0001f);
            Assert.Equal(Matrix.Identity, new Matrix(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Zero));
            Assert.Equal(Matrix.Identity.GetHashCode(), new Matrix(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.Zero).GetHashCode());
        }

        [Test]
        public void QuaternionMatrixAndMultiplicationMatchRe3()
        {
            float halfPi = (float)Math.PI * 0.5f;
            float halfSqrt = (float)Math.Sqrt(0.5);
            Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, halfPi);
            Assert.Near(0.0f, rotation.X);
            Assert.Near(0.0f, rotation.Y);
            Assert.Near(halfSqrt, rotation.Z);
            Assert.Near(halfSqrt, rotation.W);

            Matrix matrix = rotation.ToMatrix();
            AssertVector(new Vector3(0.0f, 1.0f, 0.0f), matrix.Right, 0.0001f);
            AssertVector(new Vector3(-1.0f, 0.0f, 0.0f), matrix.Forward, 0.0001f);
            AssertVector(Vector3.UnitZ, matrix.Up, 0.0001f);
            Assert.Equal(Vector3.Zero, matrix.Position);
            AssertVector(Vector3.UnitY, matrix.TransformDirection(Vector3.UnitX), 0.0001f);

            Quaternion xRotation = Quaternion.FromAxisAngle(Vector3.UnitX, halfPi);
            Quaternion yRotation = Quaternion.FromAxisAngle(Vector3.UnitY, halfPi);
            Quaternion combined = Quaternion.Multiply(xRotation, yRotation);
            AssertVector(Vector3.UnitY, combined.ToMatrix().TransformDirection(Vector3.UnitX), 0.0001f);
            Assert.Equal(combined, xRotation.Multiply(yRotation));

            Assert.Equal(Quaternion.Identity, default(Quaternion).Normalize());
            Assert.Equal(Quaternion.Identity, new Quaternion(0.0f, 0.0f, 0.0f, 2.0f).Normalize());
            Quaternion normalized = new Quaternion(1.0f, 2.0f, 3.0f, 4.0f).Normalize();
            Assert.Near(1.0f, normalized.Length(), 0.000001f);
            Assert.Equal(normalized.GetHashCode(), Quaternion.Normalize(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f)).GetHashCode());
        }

        [Test]
        public void BoundingTypesContainIntersectExpandAndGrow()
        {
            BoundingBox box = new BoundingBox(new Vector3(-1.0f, -2.0f, -3.0f), new Vector3(1.0f, 2.0f, 3.0f));
            Assert.True(box.Contains(Vector3.Zero));
            Assert.True(box.Contains(box.Min));
            Assert.True(box.Contains(box.Max));
            Assert.False(box.Contains(new Vector3(1.01f, 0.0f, 0.0f)));

            BoundingBox touching = new BoundingBox(new Vector3(1.0f, -1.0f, -1.0f), new Vector3(4.0f, 1.0f, 1.0f));
            BoundingBox separated = new BoundingBox(new Vector3(1.01f, -1.0f, -1.0f), new Vector3(4.0f, 1.0f, 1.0f));
            Assert.True(box.Intersects(touching));
            Assert.True(touching.Intersects(box));
            Assert.False(box.Intersects(separated));

            BoundingBox expanded = box.Expand(new Vector3(5.0f, -4.0f, 2.0f));
            Assert.Equal(new Vector3(-1.0f, -4.0f, -3.0f), expanded.Min);
            Assert.Equal(new Vector3(5.0f, 2.0f, 3.0f), expanded.Max);
            BoundingBox grown = box.Grow(2.0f);
            Assert.Equal(new Vector3(-3.0f, -4.0f, -5.0f), grown.Min);
            Assert.Equal(new Vector3(3.0f, 4.0f, 5.0f), grown.Max);
            BoundingBox grownByVector = box.Grow(new Vector3(1.0f, 2.0f, 3.0f));
            Assert.Equal(new Vector3(-2.0f, -4.0f, -6.0f), grownByVector.Min);
            Assert.Equal(new Vector3(2.0f, 4.0f, 6.0f), grownByVector.Max);
            Assert.Equal(box.GetHashCode(), new BoundingBox(box.Min, box.Max).GetHashCode());

            BoundingSphere sphere = new BoundingSphere(Vector3.Zero, 2.0f);
            Assert.True(sphere.Intersects(new BoundingSphere(new Vector3(4.0f, 0.0f, 0.0f), 2.0f)));
            Assert.False(sphere.Intersects(new BoundingSphere(new Vector3(4.01f, 0.0f, 0.0f), 2.0f)));
            Assert.True(sphere.Intersects(sphere));
            Assert.Equal(sphere.GetHashCode(), new BoundingSphere(Vector3.Zero, 2.0f).GetHashCode());
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float epsilon)
        {
            Assert.Near(expected.X, actual.X, epsilon);
            Assert.Near(expected.Y, actual.Y, epsilon);
            Assert.Near(expected.Z, actual.Z, epsilon);
        }
    }
}
