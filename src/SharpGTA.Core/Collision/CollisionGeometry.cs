using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Collision
{
    /// <summary>Local-space COLL geometry queries; no entity transforms or CWorld broad phase.</summary>
    public static class CollisionGeometry
    {
        private const double BarycentricTolerance = 1.0e-12;

        /// <summary>
        /// Builds the PC ColTrianglePlane: normal = normalize(cross(vc-va, vb-va)),
        /// distance = dot(normal, va). Degenerate planes have zero normal/distance.
        /// </summary>
        public static ColTrianglePlane CalculateTrianglePlane(Vector3 va, Vector3 vb, Vector3 vc)
        {
            if (!IsFinite(va) || !IsFinite(vb) || !IsFinite(vc))
                return DegeneratePlane();

            double abX = (double)vb.X - va.X;
            double abY = (double)vb.Y - va.Y;
            double abZ = (double)vb.Z - va.Z;
            double acX = (double)vc.X - va.X;
            double acY = (double)vc.Y - va.Y;
            double acZ = (double)vc.Z - va.Z;

            double normalX = acY * abZ - acZ * abY;
            double normalY = acZ * abX - acX * abZ;
            double normalZ = acX * abY - acY * abX;
            double lengthSquared = normalX * normalX + normalY * normalY + normalZ * normalZ;
            if (!(lengthSquared > 0.0) || double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared))
                return DegeneratePlane();

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            normalX *= inverseLength;
            normalY *= inverseLength;
            normalZ *= inverseLength;
            double distance = normalX * va.X + normalY * va.Y + normalZ * va.Z;
            if (!IsFinite(normalX) || !IsFinite(normalY) || !IsFinite(normalZ) || !IsFinite(distance))
                return DegeneratePlane();

            return new ColTrianglePlane(
                new Vector3((float)normalX, (float)normalY, (float)normalZ),
                (float)distance, false);
        }

        /// <summary>Returns the closest non-negative hit on spheres, boxes, and triangles.</summary>
        public static bool RayCast(ColModel model, Ray ray, out ColHit hit)
        {
            if (model == null)
                throw new ArgumentNullException("model");

            hit = new ColHit();
            RayState state;
            if (!TryCreateRay(ray, out state))
                return false;

            bool found = false;
            double closest = state.MaxDistance;
            for (int i = 0; i < model.Spheres.Length; i++)
            {
                ColSphere sphere = model.Spheres[i];
                double distance;
                Vector3 normal;
                if (TryIntersectSphere(state, sphere, out distance, out normal) &&
                    IsCloser(distance, state.MaxDistance, closest, found))
                {
                    closest = distance;
                    found = true;
                    hit = CreateHit(state, distance, normal, sphere.Surface, sphere.Piece,
                        ColShapeType.Sphere, i);
                }
            }

            for (int i = 0; i < model.Boxes.Length; i++)
            {
                ColBox box = model.Boxes[i];
                double distance;
                Vector3 normal;
                if (TryIntersectBox(state, box, out distance, out normal) &&
                    IsCloser(distance, state.MaxDistance, closest, found))
                {
                    closest = distance;
                    found = true;
                    hit = CreateHit(state, distance, normal, box.Surface, box.Piece,
                        ColShapeType.Box, i);
                }
            }

            for (int i = 0; i < model.Triangles.Length; i++)
            {
                ColTriangle triangle = model.Triangles[i];
                double distance;
                Vector3 normal;
                if (TryIntersectTriangle(state, model.Vertices, triangle, out distance, out normal) &&
                    IsCloser(distance, state.MaxDistance, closest, found))
                {
                    closest = distance;
                    found = true;
                    hit = CreateHit(state, distance, normal, triangle.Surface, 0,
                        ColShapeType.Triangle, i);
                }
            }
            return found;
        }

        /// <summary>Casts downward through the supplied local z range.</summary>
        public static bool QueryVertical(ColModel model, float x, float y,
            float minZ, float maxZ, out ColHit hit)
        {
            if (minZ > maxZ)
            {
                float temporary = minZ;
                minZ = maxZ;
                maxZ = temporary;
            }
            Ray ray = new Ray(new Vector3(x, y, maxZ), new Vector3(0.0f, 0.0f, -1.0f), maxZ - minZ);
            return RayCast(model, ray, out hit);
        }

        private static bool TryIntersectSphere(RayState ray, ColSphere sphere,
            out double distance, out Vector3 normal)
        {
            distance = 0.0;
            normal = Vector3.Zero;
            double radius = sphere.Radius;
            if (!IsFinite(sphere.Center) || !IsFinite(radius) || radius < 0.0)
                return false;

            double offsetX = ray.OriginX - sphere.Center.X;
            double offsetY = ray.OriginY - sphere.Center.Y;
            double offsetZ = ray.OriginZ - sphere.Center.Z;
            double projection = offsetX * ray.DirectionX + offsetY * ray.DirectionY + offsetZ * ray.DirectionZ;
            double constant = offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ - radius * radius;
            double discriminant = projection * projection - constant;
            if (!(discriminant >= 0.0) || double.IsInfinity(discriminant))
                return false;

            double root = Math.Sqrt(discriminant);
            double first = -projection - root;
            double second = -projection + root;
            distance = first >= 0.0 ? first : second;
            if (!(distance >= 0.0) || double.IsInfinity(distance))
                return false;

            if (radius > 0.0)
            {
                double inverseRadius = 1.0 / radius;
                normal = new Vector3(
                    (float)((ray.OriginX + ray.DirectionX * distance - sphere.Center.X) * inverseRadius),
                    (float)((ray.OriginY + ray.DirectionY * distance - sphere.Center.Y) * inverseRadius),
                    (float)((ray.OriginZ + ray.DirectionZ * distance - sphere.Center.Z) * inverseRadius));
            }
            return true;
        }

        private static bool TryIntersectBox(RayState ray, ColBox box,
            out double distance, out Vector3 normal)
        {
            distance = 0.0;
            normal = Vector3.Zero;
            if (!IsFinite(box.Min) || !IsFinite(box.Max) ||
                box.Min.X > box.Max.X || box.Min.Y > box.Max.Y || box.Min.Z > box.Max.Z)
                return false;

            double enter = double.NegativeInfinity;
            double exit = double.PositiveInfinity;
            int enterAxis = -1;
            int exitAxis = -1;
            double enterSign = 0.0;
            double exitSign = 0.0;
            if (!UpdateSlab(ray.OriginX, ray.DirectionX, box.Min.X, box.Max.X, 0,
                    ref enter, ref exit, ref enterAxis, ref exitAxis, ref enterSign, ref exitSign) ||
                !UpdateSlab(ray.OriginY, ray.DirectionY, box.Min.Y, box.Max.Y, 1,
                    ref enter, ref exit, ref enterAxis, ref exitAxis, ref enterSign, ref exitSign) ||
                !UpdateSlab(ray.OriginZ, ray.DirectionZ, box.Min.Z, box.Max.Z, 2,
                    ref enter, ref exit, ref enterAxis, ref exitAxis, ref enterSign, ref exitSign))
                return false;

            int axis;
            double sign;
            if (enter >= 0.0)
            {
                distance = enter;
                axis = enterAxis;
                sign = enterSign;
            }
            else
            {
                // A ray starting inside reports its exit boundary, like the sphere query.
                distance = exit;
                axis = exitAxis;
                sign = exitSign;
            }
            if (!(distance >= 0.0) || double.IsInfinity(distance) || axis < 0)
                return false;

            normal = AxisNormal(axis, sign);
            return true;
        }

        private static bool UpdateSlab(double origin, double direction, double min, double max, int axis,
            ref double enter, ref double exit, ref int enterAxis, ref int exitAxis,
            ref double enterSign, ref double exitSign)
        {
            if (direction == 0.0)
                return origin >= min && origin <= max;

            double near = (min - origin) / direction;
            double far = (max - origin) / direction;
            if (near > far)
            {
                double temporary = near;
                near = far;
                far = temporary;
            }

            double nearSign = direction > 0.0 ? -1.0 : 1.0;
            if (near > enter)
            {
                enter = near;
                enterAxis = axis;
                enterSign = nearSign;
            }
            if (far < exit)
            {
                exit = far;
                exitAxis = axis;
                exitSign = -nearSign;
            }
            return enter <= exit;
        }

        private static bool TryIntersectTriangle(RayState ray, Vector3[] vertices, ColTriangle triangle,
            out double distance, out Vector3 normal)
        {
            distance = 0.0;
            normal = Vector3.Zero;
            if (triangle.A >= vertices.Length || triangle.B >= vertices.Length || triangle.C >= vertices.Length)
                return false;

            Vector3 va = vertices[triangle.A];
            Vector3 vb = vertices[triangle.B];
            Vector3 vc = vertices[triangle.C];
            ColTrianglePlane plane = CalculateTrianglePlane(va, vb, vc);
            if (plane.IsDegenerate)
                return false;

            // Double-precision Möller-Trumbore intersection, with both sides accepted.
            double e1X = (double)vb.X - va.X;
            double e1Y = (double)vb.Y - va.Y;
            double e1Z = (double)vb.Z - va.Z;
            double e2X = (double)vc.X - va.X;
            double e2Y = (double)vc.Y - va.Y;
            double e2Z = (double)vc.Z - va.Z;
            double pX = ray.DirectionY * e2Z - ray.DirectionZ * e2Y;
            double pY = ray.DirectionZ * e2X - ray.DirectionX * e2Z;
            double pZ = ray.DirectionX * e2Y - ray.DirectionY * e2X;
            double determinant = e1X * pX + e1Y * pY + e1Z * pZ;
            if (determinant == 0.0 || !IsFinite(determinant))
                return false;

            double inverseDeterminant = 1.0 / determinant;
            double tX = ray.OriginX - va.X;
            double tY = ray.OriginY - va.Y;
            double tZ = ray.OriginZ - va.Z;
            double u = (tX * pX + tY * pY + tZ * pZ) * inverseDeterminant;
            if (u < -BarycentricTolerance || u > 1.0 + BarycentricTolerance || !IsFinite(u))
                return false;

            double qX = tY * e1Z - tZ * e1Y;
            double qY = tZ * e1X - tX * e1Z;
            double qZ = tX * e1Y - tY * e1X;
            double v = (ray.DirectionX * qX + ray.DirectionY * qY + ray.DirectionZ * qZ) * inverseDeterminant;
            if (v < -BarycentricTolerance || u + v > 1.0 + BarycentricTolerance || !IsFinite(v))
                return false;

            distance = (e2X * qX + e2Y * qY + e2Z * qZ) * inverseDeterminant;
            if (!(distance >= 0.0) || !IsFinite(distance))
                return false;

            normal = plane.Normal;
            return true;
        }

        private static bool TryCreateRay(Ray source, out RayState ray)
        {
            ray = new RayState();
            if (!IsFinite(source.Origin) || !IsFinite(source.Direction) ||
                float.IsNaN(source.MaxDistance) || source.MaxDistance < 0.0f)
                return false;

            double lengthSquared = (double)source.Direction.X * source.Direction.X +
                (double)source.Direction.Y * source.Direction.Y +
                (double)source.Direction.Z * source.Direction.Z;
            if (!(lengthSquared > 0.0) || !IsFinite(lengthSquared))
                return false;

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            ray.OriginX = source.Origin.X;
            ray.OriginY = source.Origin.Y;
            ray.OriginZ = source.Origin.Z;
            ray.DirectionX = source.Direction.X * inverseLength;
            ray.DirectionY = source.Direction.Y * inverseLength;
            ray.DirectionZ = source.Direction.Z * inverseLength;
            ray.MaxDistance = source.MaxDistance;
            return IsFinite(ray.DirectionX) && IsFinite(ray.DirectionY) && IsFinite(ray.DirectionZ);
        }

        private static bool IsCloser(double distance, double maxDistance, double closest, bool found)
        {
            return distance >= 0.0 && distance <= maxDistance && IsFinite(distance) &&
                (!found || distance < closest);
        }

        private static ColHit CreateHit(RayState ray, double distance, Vector3 normal,
            byte surface, byte piece, ColShapeType shapeType, int shapeIndex)
        {
            Vector3 position = new Vector3(
                (float)(ray.OriginX + ray.DirectionX * distance),
                (float)(ray.OriginY + ray.DirectionY * distance),
                (float)(ray.OriginZ + ray.DirectionZ * distance));
            return new ColHit(position, normal, (float)distance, surface, piece, shapeType, shapeIndex);
        }

        private static Vector3 AxisNormal(int axis, double sign)
        {
            float value = sign < 0.0 ? -1.0f : 1.0f;
            if (axis == 0)
                return new Vector3(value, 0.0f, 0.0f);
            if (axis == 1)
                return new Vector3(0.0f, value, 0.0f);
            return new Vector3(0.0f, 0.0f, value);
        }

        private static ColTrianglePlane DegeneratePlane()
        {
            return new ColTrianglePlane(Vector3.Zero, 0.0f, true);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private struct RayState
        {
            public double OriginX;
            public double OriginY;
            public double OriginZ;
            public double DirectionX;
            public double DirectionY;
            public double DirectionZ;
            public double MaxDistance;
        }
    }
}
