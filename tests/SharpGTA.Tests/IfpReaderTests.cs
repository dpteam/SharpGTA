using System;
using SharpGTA.Assets;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class IfpReaderTests
    {
        [Test]
        public void ReadsARotationOnlyHierarchy()
        {
            // One package, one animation, one sequence, three rotation-only frames.
            byte[] data = IfpTestData.Store(
                IfpTestData.Package("ped",
                    IfpTestData.Animation("walk",
                        IfpTestData.Sequence("root", -1, "KR00",
                            IfpTestData.Frame(0.1f, 0.2f, 0.3f, 0.9f, 0.0f),
                            IfpTestData.Frame(0.2f, 0.1f, 0.0f, 0.9f, 0.5f),
                            IfpTestData.Frame(0.0f, 0.0f, 0.0f, 1.0f, 1.0f)))));

            IfpDocument document = IfpReader.Read(data);
            Assert.Equal(1, document.Blocks.Length);
            Assert.Equal(1, document.AnimationCount);
            IfpBlock block = document.Blocks[0];
            Assert.Equal("ped", block.Name);
            IfpAnimation animation = block.Animations[0];
            Assert.Equal("walk", animation.Name);
            Assert.Equal(3, animation.FrameCount);
            Assert.Equal(1, animation.Sequences.Length);

            IfpSequence sequence = animation.Sequences[0];
            Assert.Equal("root", sequence.Name);
            Assert.Equal(-1, sequence.BoneTag);
            Assert.False(sequence.HasTranslation);
            Assert.False(sequence.HasScale);
            Assert.Equal(3, sequence.Frames.Length);
            // The file stores the inverse rotation, so x/y/z are flipped and w is kept.
            Assert.Near(-0.1f, sequence.Frames[0].Rotation.X);
            Assert.Near(-0.2f, sequence.Frames[0].Rotation.Y);
            Assert.Near(-0.3f, sequence.Frames[0].Rotation.Z);
            Assert.Near(0.9f, sequence.Frames[0].Rotation.W);
            // Absolute times become deltas, and the duration is their sum.
            Assert.Near(0.0f, sequence.Frames[0].DeltaTime);
            Assert.Near(0.5f, sequence.Frames[1].DeltaTime);
            Assert.Near(0.5f, sequence.Frames[2].DeltaTime);
            Assert.Near(1.0f, sequence.Duration);
            Assert.Near(1.0f, animation.Duration);
        }

        [Test]
        public void ReadsTranslationScaleAndBoneTags()
        {
            byte[] data = IfpTestData.Store(
                IfpTestData.Package("ped",
                    IfpTestData.Animation("jump",
                        IfpTestData.Sequence("root", 2, "KRT0",
                            IfpTestData.TranslationFrame(1, 2, 3, 0.1f, 0.2f, 0.3f, 0.9f, 0.0f),
                            IfpTestData.TranslationFrame(2, 3, 4, 0.0f, 0.0f, 0.0f, 1.0f, 0.25f)),
                        IfpTestData.Sequence("head", -1, "KRTS",
                            IfpTestData.ScaleFrame(0.5f, 0.5f, 0.0f, 0.9f, 0.0f, 0.0f, 0.0f, 4.0f, 4.0f, 4.0f, 0.0f)))));

            IfpDocument document = IfpReader.Read(data);
            IfpAnimation animation = document.Blocks[0].Animations[0];
            Assert.Equal(2, animation.Sequences.Length);
            Assert.Equal(2, animation.FrameCount);

            IfpSequence translated = animation.Sequences[0];
            Assert.Equal(2, translated.BoneTag);
            Assert.True(translated.HasTranslation);
            Assert.Near(1.0f, translated.Frames[0].Translation.X);
            Assert.Near(3.0f, translated.Frames[0].Translation.Z);
            Assert.Near(0.25f, translated.Frames[1].DeltaTime);
            Assert.Near(0.25f, translated.Duration);

            // Scale is stored but ignored, as in the original loader.
            IfpSequence scaled = animation.Sequences[1];
            Assert.True(scaled.HasScale);
            Assert.Equal(-1, scaled.BoneTag);
            Assert.Near(0.0f, scaled.Duration);
        }

        [Test]
        public void RemovesQuaternionFlipsBetweenFrames()
        {
            byte[] data = IfpTestData.Store(
                IfpTestData.Package("test",
                    IfpTestData.Animation("flip",
                        IfpTestData.Sequence("root", -1, "KR00",
                            IfpTestData.Frame(0.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                            // Same orientation on the other hemisphere: the loader must flip it.
                            IfpTestData.Frame(0.0f, 0.0f, 0.0f, -1.0f, 0.5f)))));

            IfpSequence sequence = IfpReader.Read(data).Blocks[0].Animations[0].Sequences[0];
            Assert.Near(1.0f, sequence.Frames[0].Rotation.W);
            Assert.Near(1.0f, sequence.Frames[1].Rotation.W);
        }

        [Test]
        public void RejectsMalformedInput()
        {
            Assert.Throws<ArgumentNullException>(delegate { IfpReader.Read(null); });
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(new byte[] { 1, 2, 3, 4 });
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(System.Text.Encoding.ASCII.GetBytes("NOPE000000000000"));
            });
            // A bogus chunk size must be rejected before position arithmetic can overflow.
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(new byte[]
                {
                    (byte)'A', (byte)'N', (byte)'P', (byte)'K',
                    255, 255, 255, 127,
                    (byte)'I', (byte)'N', (byte)'F', (byte)'O',
                    4, 0, 0, 0
                });
            });
            // A truncated keyframe body must not be read past the end.
            byte[] truncated = IfpTestData.Store(
                IfpTestData.Package("ped",
                    IfpTestData.Animation("cut",
                        IfpTestData.Sequence("root", -1, "KR00",
                            IfpTestData.Frame(0.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                            IfpTestData.Frame(0.0f, 0.0f, 0.0f, 1.0f, 1.0f)))));
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(truncated, 0, truncated.Length - 24);
            });
        }
    }
}