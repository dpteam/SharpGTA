using System;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// A gameplay entity placed by the simulation: object, ped or vehicle. It owns a
    /// position and velocity only; there is no physics, collision response or animation yet.
    /// </summary>
    public sealed class DynamicEntity : WorldEntity
    {
        private BoundingBox bounds;
        private Vector3 velocity;

        internal DynamicEntity(ModelDefinition model, WorldEntityKind kind, Vector3 position,
            Vector3 velocity)
            : base(model, Matrix.CreateTranslation(position), position)
        {
            if (kind != WorldEntityKind.Object && kind != WorldEntityKind.Ped &&
                kind != WorldEntityKind.Vehicle)
                throw new ArgumentOutOfRangeException("kind",
                    "Dynamic entities must be objects, peds or vehicles.");
            Kind = kind;
            this.velocity = velocity;
            RefreshBounds();
        }

        public WorldEntityKind Kind { get; private set; }

        public Vector3 Velocity
        {
            get { return velocity; }
            set
            {
                if (float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsNaN(value.Z) ||
                    float.IsInfinity(value.X) || float.IsInfinity(value.Y) || float.IsInfinity(value.Z))
                    throw new ArgumentOutOfRangeException("value");
                velocity = value;
            }
        }
        public override bool IsStatic { get { return false; } }

        public override BoundingBox WorldBounds { get { return bounds; } }

        /// <summary>Deterministic linear integration; no gravity, drag or collision yet.</summary>
        internal void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0.0f)
                return;
            SetPosition(new Vector3(Position.X + velocity.X * deltaSeconds,
                Position.Y + velocity.Y * deltaSeconds,
                Position.Z + velocity.Z * deltaSeconds));
        }

        internal void SetPosition(Vector3 position)
        {
            Transform = Matrix.CreateTranslation(position);
            Position = position;
            RefreshBounds();
        }

        private void RefreshBounds()
        {
            bounds = new BoundingBox(Position, Position);
        }
    }
}