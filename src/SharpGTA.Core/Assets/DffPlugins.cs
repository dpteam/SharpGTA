namespace SharpGTA.Assets
{
    public sealed class DffHAnim
    {
        public int Version { get; internal set; }
        public int Id { get; internal set; }
        public int Flags { get; internal set; }
        public int MaxKeyFrameSize { get; internal set; }
        public DffHAnimNode[] Nodes { get; internal set; }
    }

    public sealed class DffHAnimNode
    {
        public int Id { get; internal set; }
        public int Index { get; internal set; }
        public int Flags { get; internal set; }
    }

    public sealed class DffSkin
    {
        public int NumBones { get; internal set; }
        public int NumUsedBones { get; internal set; }
        public int NumWeights { get; internal set; }
        public bool IsOldFormat { get; internal set; }
        public bool IsLegacyAtomic { get; internal set; }
        public byte[] UsedBones { get; internal set; }
        public byte[] BoneIndices { get; internal set; }
        public float[] BoneWeights { get; internal set; }

        // Sixteen serialized right/up/at/pos 4x4 values per bone.
        public float[] InverseMatrices { get; internal set; }
        public DffSkinBone[] LegacyBones { get; internal set; }
        public int BoneLimit { get; internal set; }
        public int SplitMeshCount { get; internal set; }
        public int RleSize { get; internal set; }
        public byte[] RemapIndices { get; internal set; }

        // Two bytes per split mesh and per RLE entry respectively.
        public byte[] RleCounts { get; internal set; }
        public byte[] RleEntries { get; internal set; }
    }

    public sealed class DffSkinBone
    {
        public int Id { get; internal set; }
        public int Index { get; internal set; }
        public int Flags { get; internal set; }
    }

    public enum DffMatFxEffectType
    {
        Nothing0 = 0,
        BumpMap1 = 1,
        EnvMap2 = 2,
        BumpEnvMap3 = 3,
        Dual4 = 4,
        UvTransform5 = 5,
        DualUvTransform6 = 6
    }

    public sealed class DffMatFx
    {
        public uint OverallEffectType { get; internal set; }
        public DffMatFxEffect[] Effects { get; internal set; }
    }

    public sealed class DffMatFxEffect
    {
        public DffMatFxEffectType Type { get; internal set; }
        public float Coefficient { get; internal set; }
        public int FrameBufferAlpha { get; internal set; }
        public int SourceBlend { get; internal set; }
        public int DestinationBlend { get; internal set; }
        public DffTextureReference Texture { get; internal set; }
        public DffTextureReference SecondaryTexture { get; internal set; }
    }
}
