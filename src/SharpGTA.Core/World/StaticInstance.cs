using System;
using SharpGTA.Data;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>A static IPL placement with metadata and no entity/physics behavior.</summary>
    public sealed class StaticInstance
    {
        internal StaticInstance(ObjectInstanceRecord record, ModelDefinition model)
        {
            Model = model;
            Scale = record.Scale;
            Area = record.Area;
            HasArea = record.HasArea;

            // Original IPL loading reconstructs a quaternion from the stored axis and w.
            // Do not use ObjectInstanceRecord.RawRotation as a normalized quaternion.
            float angle = -2.0f * (float)Math.Acos(record.QuaternionW);
            Rotation = Quaternion.FromAxisAngle(record.Axis, angle);
            Matrix transform = Matrix.CreateFromQuaternion(Rotation);
            transform.Position = record.Position;
            Transform = transform;
            Position = record.Position;
            RefreshWorldBounds();
        }

        public ModelDefinition Model { get; private set; }
        public Matrix Transform { get; private set; }
        public Quaternion Rotation { get; private set; }
        public Vector3 Position { get; private set; }

        /// <summary>Parsed IPL scale, retained as metadata but deliberately not applied.</summary>
        public Vector3 Scale { get; private set; }
        public float Area { get; private set; }
        public bool HasArea { get; private set; }
        public BoundingBox WorldBounds { get; private set; }

        internal void RefreshWorldBounds()
        {
            WorldBounds = CalculateWorldBounds(this);
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
