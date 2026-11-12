using System;
using SharpGTA.Mathematics;

namespace SharpGTA.World
{
    /// <summary>
    /// Common state of everything the world indexes as a CEntity: placement or gameplay
    /// entity, with a model, a world transform, a pool handle and a sector scan stamp.
    /// This is deliberately not CPhysical: no collision response or rotation dynamics yet.
    /// </summary>
    public abstract class WorldEntity
    {
        protected WorldEntity(ModelDefinition model, Matrix transform, Vector3 position)
        {
            if (model == null)
                throw new ArgumentNullException("model");
            Model = model;
            Transform = transform;
            Position = position;
            Handle = -1;
        }

        public ModelDefinition Model { get; private set; }
        public Matrix Transform { get; protected set; }
        public Vector3 Position { get; protected set; }

        /// <summary>
        /// Entity-pool handle in this entity's own pool (building, dummy, treadable, object,
        /// ped or vehicle), or -1 when the pool was full. Handles are per-pool, so a lookup
        /// needs the matching WorldEntityKind, as CPools::Get*Ref requires the entity type.
        /// </summary>
        public int Handle { get; internal set; }

        /// <summary>
        /// Last sector-scan stamp; mirrors CEntity::m_scanCode, 0 means unscanned. Sector
        /// queries mutate it, so they are neither reentrant nor safe to call concurrently.
        /// </summary>
        public int ScanCode { get; internal set; }

        /// <summary>World-space bounds used for sector membership and distance tests.</summary>
        public abstract BoundingBox WorldBounds { get; }

        /// <summary>True for placements that never move after loading.</summary>
        public virtual bool IsStatic { get { return true; } }
    }
}