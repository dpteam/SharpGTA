using SharpGTA;
using SharpGTA.Data;

namespace SharpGTA.Games.VC
{
    public sealed class VCDefinition : GameDefinition
    {
        private static readonly VCDefinition instance = new VCDefinition();

        private VCDefinition()
            : base(GameId.ViceCity, "Grand Theft Auto: Vice City", "DATA/GTA_VC.DAT",
                "beach", 21, 7, CollisionStorageKind.StreamedSlots, DataSchema.VC,
                new GameLimits(6500, 1385, 35, 450, 9650, 20400, 67, 16))
        {
        }

        public static VCDefinition Instance
        {
            get { return instance; }
        }
    }
}
