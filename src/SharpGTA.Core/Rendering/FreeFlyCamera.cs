using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Rendering
{
    /// <summary>Presentation-only free camera; it is not a player/physics entity.</summary>
    public sealed class FreeFlyCamera
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public float Speed = 18.0f;

        public void Move(bool forward, bool backward, bool left, bool right,
            bool down, bool up, float seconds)
        {
            if (seconds <= 0.0f)
                return;
            Vector3 ahead = Forward;
            Vector3 side = Right;
            Vector3 delta = Vector3.Zero;
            if (forward) delta += ahead;
            if (backward) delta -= ahead;
            if (right) delta += side;
            if (left) delta -= side;
            if (up) delta += Vector3.UnitZ;
            if (down) delta -= Vector3.UnitZ;
            if (delta.LengthSquared() > 0.0f)
                Position += delta.Normalize() * (Speed * seconds);
        }

        public void Look(float yawDelta, float pitchDelta)
        {
            Yaw += yawDelta;
            Pitch += pitchDelta;
            Pitch = Math.Max(-1.45f, Math.Min(1.45f, Pitch));
        }

        public Vector3 Forward
        {
            get
            {
                float horizontal = (float)Math.Cos(Pitch);
                return new Vector3((float)Math.Sin(Yaw) * horizontal,
                    (float)Math.Cos(Yaw) * horizontal, (float)Math.Sin(Pitch));
            }
        }

        public Vector3 Right
        {
            get { return new Vector3((float)Math.Cos(Yaw), -(float)Math.Sin(Yaw), 0.0f); }
        }

        public Vector3 Target { get { return Position + Forward * 100.0f; } }
    }
}
