using System;
using System.Collections.Generic;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Simulation module that integrates gameplay entities each tick. Registered after world
    /// loading; it only moves entities and updates their sector membership.
    /// </summary>
    public sealed class DynamicEntityModule : ISimulationModule, IStateSerializable
    {
        private readonly StaticWorld world;

        public DynamicEntityModule(StaticWorld world)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            this.world = world;
        }

        public string Name { get { return "DynamicEntities"; } }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            world.AdvanceDynamic(context.Time.DeltaSeconds);
        }

        /// <summary>
        /// Serializes gameplay entities so State@N is sensitive to their positions and
        /// velocities; otherwise a regression in movement would not change the state hash.
        /// </summary>
        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            IList<WorldEntity> entities = world.DynamicEntities;
            writer.WriteInt32(entities.Count);
            for (int i = 0; i < entities.Count; i++)
            {
                DynamicEntity entity = entities[i] as DynamicEntity;
                if (entity == null)
                {
                    writer.WriteByte(0);
                    continue;
                }
                writer.WriteByte(1);
                writer.WriteInt32((int)entity.Kind);
                writer.WriteInt32(entity.Handle);
                writer.WriteSingle(entity.Position.X);
                writer.WriteSingle(entity.Position.Y);
                writer.WriteSingle(entity.Position.Z);
                writer.WriteSingle(entity.Velocity.X);
                writer.WriteSingle(entity.Velocity.Y);
                writer.WriteSingle(entity.Velocity.Z);
            }
        }
    }
}