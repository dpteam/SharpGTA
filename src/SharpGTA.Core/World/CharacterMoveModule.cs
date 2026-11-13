using System;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Walks one entity around the static world from the simulation input: forward/strafe at a
    /// constant speed, wall collision from COLL rays and ground snapping. This is the first
    /// player-shaped motion — a humanoid with a body radius that cannot pass through buildings —
    /// not a physics body: no gravity, no step-up, no slope response, no collision against other
    /// entities. Those belong to `CPhysical`, which is still missing.
    /// </summary>
    public sealed class CharacterMoveModule : ISimulationModule, IStateSerializable
    {
        /// <summary>Ray height above the feet: a chest-high sweep, like a ped's collision volume.</summary>
        private const float ChestHeight = 1.0f;

        /// <summary>Ground probe window, in world units.</summary>
        private const float GroundProbeAbove = 1.0f;
        private const float GroundProbeBelow = 2.0f;

        /// <summary>Slides attempted after the first contact; one is enough for a wall corner.</summary>
        private const int MaximumSlideAttempts = 2;

        private readonly StaticWorld world;
        private readonly DynamicEntity entity;
        private int collisionHits;
        private float travelled;

        public CharacterMoveModule(StaticWorld world, DynamicEntity entity)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            if (entity == null)
                throw new ArgumentNullException("entity");
            this.world = world;
            this.entity = entity;
            Speed = 2.0f;
            Radius = 0.35f;
        }

        public string Name { get { return "CharacterMove"; } }

        public DynamicEntity Entity { get { return entity; } }

        /// <summary>Horizontal speed in world units per second.</summary>
        public float Speed { get; set; }

        /// <summary>Body radius used to keep the chest off walls.</summary>
        public float Radius { get; set; }

        /// <summary>Yaw in radians; 0 walks along +X and rotates the input axes.</summary>
        public float Heading { get; set; }

        /// <summary>Contacts that stopped or deflected the character, for probes.</summary>
        public int CollisionHitCount { get { return collisionHits; } }

        public float TravelledDistance { get { return travelled; } }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            float delta = context.Time.DeltaSeconds;
            if (!(delta > 0.0f))
                return;

            float forward = context.Input.MoveY;
            float strafe = context.Input.MoveX;
            if (float.IsNaN(forward) || float.IsNaN(strafe))
                return;
            float cos = (float)Math.Cos(Heading);
            float sin = (float)Math.Sin(Heading);
            Vector3 direction = new Vector3(cos * forward - sin * strafe,
                sin * forward + cos * strafe, 0.0f);
            float magnitude = direction.Length();
            if (magnitude > 1.0f)
                direction = direction * (1.0f / magnitude);
            if (magnitude > 0.0001f)
                MoveByStep(direction * (Speed * delta));
            StayOnGround();
        }

        /// <summary>
        /// Moves by `step`, stopping at walls and sliding along them. Each of the three probe rays
        /// is offset by the body radius across the direction of travel, so the chest cannot clip a
        /// corner even though the body is not a swept volume.
        /// </summary>
        private void MoveByStep(Vector3 step)
        {
            Vector3 remaining = step;
            for (int attempt = 0; attempt <= MaximumSlideAttempts; attempt++)
            {
                float distance = remaining.Length();
                if (distance <= 0.0001f)
                    return;
                Vector3 direction = remaining * (1.0f / distance);
                Vector3 side = new Vector3(-direction.Y, direction.X, 0.0f);
                float allowed = distance;
                Vector3 normal = Vector3.Zero;
                bool blocked = false;
                for (int probe = -1; probe <= 1; probe++)
                {
                    Vector3 origin = entity.Position + new Vector3(0.0f, 0.0f, ChestHeight) +
                        side * (Radius * probe);
                    StaticWorldHit wall;
                    if (!world.QueryRay(origin, direction, distance + Radius, out wall))
                        continue;
                    // Floors and ceilings are not walls: only near-vertical surfaces block.
                    if (Math.Abs(wall.Normal.Z) > 0.7f)
                        continue;
                    float limit = wall.Distance - Radius;
                    if (limit < 0.0f)
                        limit = 0.0f;
                    if (limit < allowed)
                    {
                        allowed = limit;
                        normal = wall.Normal;
                        blocked = true;
                    }
                }
                if (!blocked)
                {
                    MoveEntity(remaining);
                    travelled += distance;
                    return;
                }
                collisionHits++;
                if (allowed > 0.0f)
                {
                    Vector3 moved = direction * allowed;
                    MoveEntity(moved);
                    travelled += allowed;
                    remaining -= moved;
                }
                float intoWall = Vector3.Dot(remaining, normal);
                if (intoWall < 0.0f)
                    remaining -= normal * intoWall;
                else
                    return;
            }
        }

        private void MoveEntity(Vector3 delta)
        {
            world.MoveEntity(entity, entity.Position + delta);
        }

        /// <summary>Keeps the feet on the static ground under the current position.</summary>
        private void StayOnGround()
        {
            Vector3 position = entity.Position;
            StaticWorldHit ground;
            if (!world.QueryVertical(position.X, position.Y, position.Z + GroundProbeAbove,
                position.Z - GroundProbeBelow, out ground))
            {
                return;
            }
            if (Math.Abs(ground.Position.Z - position.Z) > 0.0001f)
                world.MoveEntity(entity, new Vector3(position.X, position.Y, ground.Position.Z));
        }

        /// <summary>Position is hashed through the entity; the counters describe the movement.</summary>
        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteInt32(collisionHits);
            writer.WriteSingle(travelled);
            writer.WriteSingle(Speed);
            writer.WriteSingle(Heading);
        }
    }
}