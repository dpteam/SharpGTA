using System;
using SharpGTA;
using SharpGTA.Data;
using SharpGTA.World;

namespace SharpGTA.Games.III
{
    public sealed class IIIDefinition : GameDefinition
    {
        private static readonly IIIDefinition instance = new IIIDefinition();

        private IIIDefinition()
            : base(GameId.III, "Grand Theft Auto III", "DATA/GTA3.DAT",
                "industrial", 24, 4, CollisionStorageKind.LevelBased, DataSchema.III,
                new GameLimits(5500, 1024, 2, 250, 4930, 10260, 31, 8, 5500, 450, 140, 110, 2802, 1214))
        {
        }

        public static IIIDefinition Instance
        {
            get { return instance; }
        }

        public override void ScanTrafficLights(StaticWorld world, PathGraph graph)
        {
            if (world == null)
                throw new ArgumentNullException("world");
            if (graph == null)
                throw new ArgumentNullException("graph");
            IIITrafficLightScan.Apply(world, graph);
        }
    }
}
