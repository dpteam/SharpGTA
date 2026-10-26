using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>Binary-only DFF plugin readers. No animation or rendering behavior.</summary>
    internal static class DffPluginReader
    {
        internal static DffHAnim ReadHAnim(RenderWareStreamChunk plugin)
        {
            ByteReader reader = plugin.Payload;
            int version = reader.ReadInt32();
            if (version != 0x100)
                throw Error("Unsupported HAnim payload version " + version + ".", plugin);
            int id = reader.ReadInt32();
            int nodeCount = ReadCount(reader, 0, 4096, "HAnim nodes", plugin);
            int flags = 0;
            int maxKeyFrameSize = 0;
            DffHAnimNode[] nodes = new DffHAnimNode[nodeCount];
            if (nodeCount != 0)
            {
                flags = reader.ReadInt32();
                maxKeyFrameSize = reader.ReadInt32();
                for (int i = 0; i < nodeCount; i++)
                {
                    nodes[i] = new DffHAnimNode
                    {
                        Id = reader.ReadInt32(),
                        Index = reader.ReadInt32(),
                        Flags = reader.ReadInt32()
                    };
                }
            }
            plugin.FinishPayload("HAnim");
            return new DffHAnim
            {
                Version = version,
                Id = id,
                Flags = flags,
                MaxKeyFrameSize = maxKeyFrameSize,
                Nodes = nodes
            };
        }

        internal static DffSkin ReadSkin(RenderWareStreamChunk plugin, int vertexCount)
        {
            ByteReader reader = plugin.Payload;
            int boneCount = reader.ReadByte();
            int usedBoneCount = reader.ReadByte();
            int weightCount = reader.ReadByte();
            reader.ReadByte();
            bool oldFormat = usedBoneCount == 0;
            if (boneCount == 0)
                throw Error("Skin has no bones.", plugin);
            if (usedBoneCount > boneCount)
                throw Error("Skin used-bone count exceeds bone count.", plugin);
            if (oldFormat)
                usedBoneCount = boneCount;
            if (weightCount > 4)
                throw Error("Skin has more than four weights per vertex.", plugin);

            DffSkin skin = new DffSkin
            {
                NumBones = boneCount,
                NumUsedBones = usedBoneCount,
                NumWeights = weightCount,
                IsOldFormat = oldFormat,
                UsedBones = oldFormat ? null : reader.ReadBytes(usedBoneCount),
                BoneIndices = reader.ReadBytes(checked(vertexCount * 4)),
                BoneWeights = new float[checked(vertexCount * 4)],
                InverseMatrices = new float[checked(boneCount * 16)]
            };
            if (skin.UsedBones != null)
            {
                for (int i = 0; i < skin.UsedBones.Length; i++)
                {
                    if (skin.UsedBones[i] >= boneCount)
                        throw Error("Skin used-bone index is outside the bone array.", plugin);
                }
            }
            for (int i = 0; i < skin.BoneIndices.Length; i++)
            {
                if (skin.BoneIndices[i] >= boneCount)
                    throw Error("Skin vertex bone index is outside the bone array.", plugin);
            }
            for (int i = 0; i < skin.BoneWeights.Length; i++)
                skin.BoneWeights[i] = reader.ReadSingle();
            for (int bone = 0; bone < boneCount; bone++)
            {
                if (oldFormat)
                {
                    uint marker = reader.ReadUInt32();
                    if (marker != 0xDEADDEADu)
                        throw Error("Old Skin inverse matrix marker is not 0xDEADDEAD.", plugin);
                }
                for (int i = 0; i < 16; i++)
                    skin.InverseMatrices[bone * 16 + i] = reader.ReadSingle();
            }

            if (oldFormat)
            {
                // librw derives these for old data; preserving them makes the DTO useful
                // without changing the serialized values.
                for (int i = 0; i < skin.BoneWeights.Length; i += 4)
                {
                    int weights = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        if (skin.BoneWeights[i + j] != 0.0f)
                            weights++;
                    }
                    if (weights > skin.NumWeights)
                        skin.NumWeights = weights;
                }
            }
            else
            {
                skin.BoneLimit = reader.ReadInt32();
                skin.SplitMeshCount = ReadCount(reader, 0, vertexCount == 0 ? 0 : vertexCount * 3, "Skin split meshes", plugin);
                skin.RleSize = ReadCount(reader, 0, 1024 * 1024, "Skin RLE entries", plugin);
                if (skin.SplitMeshCount != 0)
                {
                    skin.RemapIndices = reader.ReadBytes(boneCount);
                    skin.RleCounts = reader.ReadBytes(checked(skin.SplitMeshCount * 2));
                    skin.RleEntries = reader.ReadBytes(checked(skin.RleSize * 2));
                }
            }
            plugin.FinishPayload(oldFormat ? "old Skin" : "split Skin");
            return skin;
        }

        internal static DffSkin ReadLegacyAtomicSkin(RenderWareStreamChunk plugin,
            DffGeometry geometry)
        {
            if (geometry == null || geometry.MorphTargets.Length == 0 || geometry.MorphTargets[0].Vertices == null)
                throw Error("Legacy Atomic Skin has no geometry vertices.", plugin);
            ByteReader reader = plugin.Payload;
            int boneCount = ReadCount(reader, 1, 255, "legacy Skin bones", plugin);
            int vertexCount = ReadCount(reader, 0, 1000000, "legacy Skin vertices", plugin);
            if (vertexCount != geometry.MorphTargets[0].Vertices.Length)
                throw Error("Legacy Atomic Skin vertex count differs from geometry.", plugin);
            DffSkin skin = new DffSkin
            {
                NumBones = boneCount,
                NumUsedBones = boneCount,
                NumWeights = 4,
                IsOldFormat = true,
                IsLegacyAtomic = true,
                BoneIndices = reader.ReadBytes(checked(vertexCount * 4)),
                BoneWeights = new float[checked(vertexCount * 4)],
                InverseMatrices = new float[checked(boneCount * 16)],
                LegacyBones = new DffSkinBone[boneCount]
            };
            for (int i = 0; i < skin.BoneIndices.Length; i++)
            {
                if (skin.BoneIndices[i] >= boneCount)
                    throw Error("Legacy Skin vertex bone index is outside the bone array.", plugin);
            }
            for (int i = 0; i < skin.BoneWeights.Length; i++)
                skin.BoneWeights[i] = reader.ReadSingle();
            for (int bone = 0; bone < boneCount; bone++)
            {
                skin.LegacyBones[bone] = new DffSkinBone
                {
                    Id = reader.ReadInt32(),
                    Index = reader.ReadInt32(),
                    Flags = reader.ReadInt32() & 3
                };
                for (int i = 0; i < 16; i++)
                    skin.InverseMatrices[bone * 16 + i] = reader.ReadSingle();
            }
            plugin.FinishPayload("legacy Atomic Skin");
            geometry.Skin = skin;
            return skin;
        }

        internal static DffMatFx ReadMatFx(RenderWareStreamChunk plugin, DffReader.Parser parser)
        {
            ByteReader reader = plugin.Payload;
            uint overall = reader.ReadUInt32();
            if (overall > (uint)DffMatFxEffectType.DualUvTransform6)
                throw Error("Unknown overall MatFX effect type " + overall + ".", plugin);
            DffMatFxEffect[] effects = new DffMatFxEffect[2];
            for (int i = 0; i < effects.Length; i++)
            {
                uint type = reader.ReadUInt32();
                if (type > (uint)DffMatFxEffectType.DualUvTransform6)
                    throw Error("Unknown MatFX slot effect type " + type + ".", plugin);
                DffMatFxEffect effect = new DffMatFxEffect { Type = (DffMatFxEffectType)type };
                switch (effect.Type)
                {
                    case DffMatFxEffectType.BumpMap1:
                        effect.Coefficient = reader.ReadSingle();
                        if (reader.ReadInt32() != 0)
                            effect.SecondaryTexture = ReadTexture(plugin, parser);
                        if (reader.ReadInt32() != 0)
                            effect.Texture = ReadTexture(plugin, parser);
                        break;
                    case DffMatFxEffectType.EnvMap2:
                        effect.Coefficient = reader.ReadSingle();
                        effect.FrameBufferAlpha = reader.ReadInt32();
                        if (reader.ReadInt32() != 0)
                            effect.Texture = ReadTexture(plugin, parser);
                        break;
                    case DffMatFxEffectType.Dual4:
                        effect.SourceBlend = reader.ReadInt32();
                        effect.DestinationBlend = reader.ReadInt32();
                        if (reader.ReadInt32() != 0)
                            effect.Texture = ReadTexture(plugin, parser);
                        break;
                    case DffMatFxEffectType.Nothing0:
                    case DffMatFxEffectType.UvTransform5:
                    case DffMatFxEffectType.DualUvTransform6:
                        break;
                    case DffMatFxEffectType.BumpEnvMap3:
                        throw Error("MatFX BUMPENVMAP has no independent serialized payload in the reference layout.", plugin);
                }
                effects[i] = effect;
            }
            plugin.FinishPayload("material MatFX");
            return new DffMatFx { OverallEffectType = overall, Effects = effects };
        }

        private static DffTextureReference ReadTexture(RenderWareStreamChunk plugin,
            DffReader.Parser parser)
        {
            // MatFX embeds complete Texture chunks after small scalar records. They are
            // siblings in the plugin byte stream, not children of the MatFX payload.
            RenderWareChunkReader content = new RenderWareChunkReader(plugin.Buffer,
                plugin.PayloadOffset + plugin.Payload.Position,
                plugin.PayloadOffset + plugin.PayloadLength - (plugin.PayloadOffset + plugin.Payload.Position));
            DffTextureReference texture = parser.ReadTextureForPlugin(content);
            plugin.Payload.Seek(content.Position - plugin.PayloadOffset);
            return texture;
        }

        private static int ReadCount(ByteReader reader, int minimum, int maximum,
            string description, RenderWareStreamChunk plugin)
        {
            int count = reader.ReadInt32();
            if (count < minimum || count > maximum)
                throw Error("Invalid " + description + " count " + count + ".", plugin);
            return count;
        }

        private static AssetFormatException Error(string message, RenderWareStreamChunk plugin)
        {
            return new AssetFormatException(message, plugin.ChunkOffset);
        }
    }
}
