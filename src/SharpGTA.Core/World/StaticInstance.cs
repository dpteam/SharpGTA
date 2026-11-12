using System;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>A static IPL placement with metadata and no entity/physics behavior.</summary>
    public sealed class StaticInstance : WorldEntity
    {
        private BoundingBox worldBounds;

        internal StaticInstance(ObjectInstanceRecord record, ModelDefinition model)
            : base(model, Matrix.Identity, record.Position)
        {
            Scale = record.Scale;
            Area = record.Area;
            HasArea = record.HasArea;

            // The original normalises the axis before rotating: re3 calls
            // RwMatrixRotate(xform, &axis, -2*acos(w)), and RenderWare normalises the axis
            // internally. IPL stores the vector part of a unit quaternion, which is generally
            // not unit length itself (e.g. 0.707107 for a 90 degree turn), so using it raw
            // would rotate by the wrong angle and scale the result.
            Vector3 axis = record.Axis;
            float axisLength = axis.Length();
            if (axisLength > 0.0f)
                axis = axis / axisLength;
            float quaternionW = record.QuaternionW;
            if (quaternionW > 1.0f)
                quaternionW = 1.0f;
            else if (quaternionW < -1.0f)
                quaternionW = -1.0f;
            float angle = -2.0f * (float)Math.Acos(quaternionW);
            Rotation = axisLength > 0.0f
                ? Quaternion.FromAxisAngle(axis, angle)
                : Quaternion.Identity;
            Matrix transform = Matrix.CreateFromQuaternion(Rotation);
            transform.Position = record.Position;
            Transform = transform;
            RefreshWorldBounds();
        }

        public Quaternion Rotation { get; private set; }

        /// <summary>Parsed IPL scale, retained as metadata but deliberately not applied.</summary>
        public Vector3 Scale { get; private set; }
        public float Area { get; private set; }
        public bool HasArea { get; private set; }

        public override BoundingBox WorldBounds { get { return worldBounds; } }

        internal void RefreshWorldBounds()
        {
            worldBounds = CalculateWorldBounds(this);
        }

        private static BoundingBox CalculateWorldBounds(StaticInstance instance)
        {
            if (instance.Model.Collision == null)
                return new BoundingBox(instance.Position, instance.Position);

            BoundingBox local = instance.Model.Collision.BoundingBox;
            Vector3 first = instance.Transform.TransformPoint(local.Min);
            Vector3 minimum = first;
            Vector3 maximum = first;
            for (int i = 1; i < 8; i++)
            {
                Vector3 point = new Vector3(
                    (i & 1) == 0 ? local.Min.X : local.Max.X,
                    (i & 2) == 0 ? local.Min.Y : local.Max.Y,
                    (i & 4) == 0 ? local.Min.Z : local.Max.Z);
                Vector3 transformed = instance.Transform.TransformPoint(point);
                minimum = Vector3.Min(minimum, transformed);
                maximum = Vector3.Max(maximum, transformed);
            }
            return new BoundingBox(minimum, maximum);
        }
    }
}
