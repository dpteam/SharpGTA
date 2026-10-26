using SharpGTA.Assets;

namespace SharpGTA.Tests
{
    public sealed class DffPluginReaderTests
    {
        [Test]
        public void ReadsHAnimSkinMatFxAndAtomicFlag()
        {
            DffClump clump = DffReader.ReadClump(DffPluginTestData.CreateWithPlugins(0x34003u, false));
            DffHAnim hierarchy = clump.Frames[0].HAnim;
            Assert.NotNull(hierarchy);
            Assert.Equal(0x100, hierarchy.Version);
            Assert.Equal(7, hierarchy.Id);
            Assert.Equal(0x10, hierarchy.Flags);
            Assert.Equal(36, hierarchy.MaxKeyFrameSize);
            Assert.Equal(2, hierarchy.Nodes.Length);
            Assert.Equal(10, hierarchy.Nodes[0].Id);
            Assert.Equal(1, hierarchy.Nodes[0].Flags);
            Assert.Equal(11, hierarchy.Nodes[1].Id);
            Assert.Equal(2, hierarchy.Nodes[1].Flags);

            DffSkin skin = clump.Geometries[0].Skin;
            Assert.NotNull(skin);
            Assert.False(skin.IsOldFormat);
            Assert.Equal(2, skin.NumBones);
            Assert.Equal(2, skin.NumUsedBones);
            Assert.Equal(4, skin.NumWeights);
            Assert.Equal(12, skin.BoneIndices.Length);
            Assert.Equal(12, skin.BoneWeights.Length);
            Assert.Equal(32, skin.InverseMatrices.Length);
            Assert.Near(0.5f, skin.BoneWeights[0]);
            Assert.Near(10.0f, skin.InverseMatrices[12]);
            Assert.Near(40.0f, skin.InverseMatrices[28]);
            Assert.Equal(4, skin.BoneLimit);
            Assert.Equal(1, skin.SplitMeshCount);
            Assert.Equal(1, skin.RleSize);
            Assert.Equal(2, skin.RemapIndices.Length);
            Assert.Equal((byte)1, skin.RemapIndices[0]);
            Assert.Equal(2, skin.RleCounts.Length);
            Assert.Equal(2, skin.RleEntries.Length);

            DffMatFx effects = clump.Geometries[0].Materials[0].MatFx;
            Assert.NotNull(effects);
            Assert.Equal(2u, effects.OverallEffectType);
            Assert.Equal(2, effects.Effects.Length);
            Assert.Equal(DffMatFxEffectType.EnvMap2, effects.Effects[0].Type);
            Assert.Near(0.5f, effects.Effects[0].Coefficient);
            Assert.Equal(1, effects.Effects[0].FrameBufferAlpha);
            Assert.Equal("envtex", effects.Effects[0].Texture.Name);
            Assert.Equal("mask", effects.Effects[0].Texture.MaskName);
            Assert.Equal(DffMatFxEffectType.Nothing0, effects.Effects[1].Type);
            Assert.Equal(null, effects.Effects[1].Texture);

            Assert.True(clump.Atomics[0].HasMatFx);
            Assert.True(clump.Atomics[0].MatFxEnabled);
            Assert.Equal(null, clump.Atomics[0].LegacySkin);
        }

        [Test]
        public void ReadsOldSkinMarkerAndDerivedWeightCount()
        {
            DffSkin skin = DffReader.ReadClump(
                DffPluginTestData.CreateWithPlugins(0x33002u, true)).Geometries[0].Skin;
            Assert.NotNull(skin);
            Assert.True(skin.IsOldFormat);
            Assert.Equal(null, skin.UsedBones);
            Assert.Equal(2, skin.NumUsedBones);
            Assert.Equal(2, skin.NumWeights);
            Assert.Equal(32, skin.InverseMatrices.Length);
            Assert.Near(10.0f, skin.InverseMatrices[12]);
            Assert.Near(40.0f, skin.InverseMatrices[28]);
            Assert.Equal(null, skin.RemapIndices);
            Assert.Equal(null, skin.RleCounts);
        }

        [Test]
        public void RejectsTruncatedPluginAndBadSkinIndices()
        {
            byte[] valid = DffPluginTestData.CreateWithPlugins(0x34003u, false);
            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate
            {
                DffReader.Read(valid, 0, valid.Length - 1);
            });

            byte[] indices = { 0, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0 };
            int found = FindSequence(valid, indices);
            Assert.True(found >= 0, "Synthetic skin index sequence not found.");
            valid[found + 4] = 99;
            Assert.Throws<SharpGTA.IO.AssetFormatException>(delegate { DffReader.Read(valid); });
        }

        private static int FindSequence(byte[] data, byte[] sequence)
        {
            for (int i = 0; i <= data.Length - sequence.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (data[i + j] != sequence[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }
    }
}
