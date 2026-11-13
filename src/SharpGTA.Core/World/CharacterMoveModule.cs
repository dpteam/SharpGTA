using System;
using System.Collections.Generic;
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

        /// <summary>Body probe heights: just above a climbable ledge and at chest level.</summary>
        private static readonly float[] ProbeHeights = { 0.45f, ChestHeight };

        /// <summary>Vertical band in which two bodies count as touching.</summary>
        private const float BodyHeight = 1.5f;

        /// <summary>Contact radii for the other actor kinds; vehicles are wide.</summary>
        private const float SmallActorRadius = 0.4f;
        private const float LargeActorRadius = 1.2f;
        private const float VehicleRadius = 1.2f;

        /// <summary>`GRAVITY` from Physical.h, in units per 50 Hz step squared.</summary>
        private const float Gravity = 0.008f;

        /// <summary>How far below the feet the ground probe looks before the body is airborne.</summary>
        private const float GroundProbeBelow = 60.0f;

        /// <summary>Safety net for falling out of the world (there is no water plane yet).</summary>
        private const float MinimumZ = -100.0f;

        /// <summary>Slides attempted after the first contact; one is enough for a wall corner.</summary>
        private const int MaximumSlideAttempts = 2;

        private readonly StaticWorld world;
        private readonly DynamicEntity entity;
        private int collisionHits;
        private float travelled;
        private readonly List<WorldEntity> actorBuffer = new List<WorldEntity>();
        private float verticalSpeed;
        private bool grounded;

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
            StepHeight = 0.4f;
        }

        public string Name { get { return "CharacterMove"; } }

        public DynamicEntity Entity { get { return entity; } }

        /// <summary>Horizontal speed in world units per second.</summary>
        public float Speed { get; set; }

        /// <summary>Body radius used to keep the chest off walls.</summary>
        public float Radius { get; set; }

        /// <summary>
        /// How far above the feet the ground probe starts. Anything below chest height is climbed
        /// by that probe rather than by an explicit step rule, so this is a look-up window, not a
        /// step limit: there is no `CPhysical` step response yet.
        /// </summary>
        public float StepHeight { get; set; }

        /// <summary>True while the ground is under the feet; a fall clears it.</summary>
        public bool IsGrounded { get { return grounded; } }

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
            ApplyVerticalMotion(context.Time.TimeStep);
        }

        /// <summary>
        /// Moves by `step`, stopping at walls and sliding along them. The body is a stack of probe
        /// rings (six rays: three lateral offsets at the chest and three just above the step
        /// height), so geometry above <see cref="StepHeight"/> blocks and anything lower is walked
        /// over with the ground query lifting the feet. It is still not a swept volume: a corner
        /// can be clipped at the probe heights, and a slope steeper than about 45 degrees reads as
        /// a wall because its normal leaves the "floor" band.
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
                for (int height = 0; height < ProbeHeights.Length; height++)
                {
                    for (int probe = -1; probe <= 1; probe++)
                    {
                        Vector3 origin = entity.Position +
                            new Vector3(0.0f, 0.0f, ProbeHeights[height]) +
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
                }
                // Other actors are cylinders, not rays: a body must not walk through a ped or a car.
                float actorLimit;
                Vector3 actorNormal;
                if (ActorLimit(direction, distance, out actorLimit, out actorNormal) &&
                    actorLimit < allowed)
                {
                    allowed = actorLimit;
                    normal = actorNormal;
                    blocked = true;
                }
                if (!blocked)
                {
                    MoveEntity(remaining);
                    travelled += distance;
                    return;
                }

                collisionHits++;

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

        /// <summary>
        /// Nearest other actor on this movement line, treated as a vertical cylinder. One-sided by
        /// design: the walker stops, the other body is never pushed, because there is no shared
        /// `CPhysical` solver yet and traffic keeps its own lane gap (`VehicleGap`).
        /// </summary>
        private bool ActorLimit(Vector3 direction, float distance, out float limit,
            out Vector3 normal)
        {
            limit = float.MaxValue;
            normal = Vector3.Zero;
            Vector3 position = entity.Position;
            float reach = distance + Radius + LargeActorRadius;
            world.QueryEntities(position, reach, actorBuffer);
            bool found = false;
            for (int i = 0; i < actorBuffer.Count; i++)
            {
                DynamicEntity other = actorBuffer[i] as DynamicEntity;
                if (other == null || ReferenceEquals(other, entity))
                    continue;
                Vector3 delta = other.Position - position;
                // Bodies only block when they share the vertical band; a car on a bridge above is
                // not something a walker on the street bumps into.
                if (Math.Abs(delta.Z) > BodyHeight)
                    continue;
                float actorRadius = other.Kind == WorldEntityKind.Vehicle
                    ? VehicleRadius
                    : SmallActorRadius;
                float combined = Radius + actorRadius;
                float along = Vector3.Dot(delta, direction);
                if (along <= 0.0f)
                    continue;
                float perpendicularSquared = delta.X * delta.X + delta.Y * delta.Y - along * along;
                if (perpendicularSquared >= combined * combined)
                    continue;
                float entry = along - (float)Math.Sqrt(combined * combined - perpendicularSquared);
                if (entry < 0.0f)
                    entry = 0.0f;
                if (entry >= limit)
                    continue;
                limit = entry;
                Vector3 sideways = new Vector3(delta.X, delta.Y, 0.0f) - direction * along;
                normal = sideways.LengthSquared() > 0.0001f ? -sideways.Normalize() : -direction;
                found = true;
            }
            return found;
        }


        /// <summary>Keeps the feet on the static ground under the current position.</summary>
        /// <summary>
        /// Gravity and ground contact, the `CPhysical::ApplyGravity` part: vertical speed is in GTA
        /// 50 Hz units, so it loses `GRAVITY * timeStep` per step and the body moves by
        /// `speed * timeStep`. A body is grounded only when the ground is at or below the next
        /// position; walking off a ledge therefore starts a real fall instead of teleporting down.
        /// </summary>
        private void ApplyVerticalMotion(float timeStep)
        {
            if (!(timeStep > 0.0f))
                return;
            Vector3 position = entity.Position;
            StaticWorldHit ground;
            bool hasGround = world.QueryVertical(position.X, position.Y,
                position.Z + StepHeight, position.Z - GroundProbeBelow, out ground);
            verticalSpeed -= Gravity * timeStep;
            float nextZ = position.Z + verticalSpeed * timeStep;
            if (hasGround && nextZ <= ground.Position.Z)
            {
                verticalSpeed = 0.0f;
                nextZ = ground.Position.Z;
                grounded = true;
            }
            else
            {
                grounded = false;
            }
            if (nextZ < MinimumZ)
            {
                verticalSpeed = 0.0f;
                nextZ = MinimumZ;
            }
            if (Math.Abs(nextZ - position.Z) > 0.0001f)
                world.MoveEntity(entity, new Vector3(position.X, position.Y, nextZ));
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
            writer.WriteSingle(verticalSpeed);
            writer.WriteByte(grounded ? (byte)1 : (byte)0);
        }
    }
}