namespace SharpGTA.Assets
{
    /// <summary>RenderWare stream chunk identifiers used by GTA DFF and TXD files.</summary>
    public static class RenderWareChunkTypes
    {
        public const uint Struct = 0x01;
        public const uint String = 0x02;
        public const uint Extension = 0x03;
        public const uint Camera = 0x05;
        public const uint Texture = 0x06;
        public const uint Material = 0x07;
        public const uint MaterialList = 0x08;
        public const uint FrameList = 0x0E;
        public const uint Geometry = 0x0F;
        public const uint Clump = 0x10;
        public const uint Light = 0x12;
        public const uint Atomic = 0x14;
        public const uint TextureNative = 0x15;
        public const uint TextureDictionary = 0x16;
        public const uint GeometryList = 0x1A;
        public const uint NodeName = 0x0253F2FE;
        public const uint BinMesh = 0x050E;

        internal static bool IsKnownContainer(uint id)
        {
            return id == Extension || id == Texture || id == Material || id == MaterialList ||
                id == FrameList || id == Geometry || id == Clump || id == Atomic ||
                id == TextureNative || id == TextureDictionary || id == GeometryList ||
                id == Light || id == Camera;
        }
    }
}
