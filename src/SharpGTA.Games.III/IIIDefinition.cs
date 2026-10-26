using SharpGTA;
using SharpGTA.Data;

namespace SharpGTA.Games.III
{
    public sealed class IIIDefinition : GameDefinition
    {
        private static readonly IIIDefinition instance = new IIIDefinition();

        private IIIDefinition()
            : base(GameId.III, "Grand Theft Auto III", "DATA/GTA3.DAT",
                "industrial", 24, 4, CollisionStorageKind.LevelBased, DataSchema.III,
                new GameLimits(5500, 1024, 2, 250, 4930, 10260, 31, 8))
        {
        }

        public static IIIDefinition Instance
        {
            get { return instance; }
        }
    }
}
