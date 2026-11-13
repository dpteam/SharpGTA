using System;
using SharpGTA.Assets;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class AnimationPlayerTests
    {
        private const float Pi = 3.14159265358979323846f;

        [Test]
        public void AdvanceMovesTheClipAndResamplesThePose()
        {
            IfpAnimation animation = OneBoneClip(1.0f);
            AnimationPlayer player = AnimationPlayer.Create(Clump(), animation);
            Assert.Near(0.0f, player.Time);
            Assert.True(player.Looping);

            player.Advance(0.25f);
            Assert.Near(0.25f, player.Time);
            // The pose must be exactly what the sampler returns for that time.
            AnimationPose expected = AnimationSampler.SampleLooping(animation, 0.25f);
            Assert.Near(expected.Bones[0].Rotation.Y, player.Pose.Bones[0].Rotation.Y, 1e-5f);
            Assert.Near(expected.Bones[0].Translation.Z, player.Pose.Bones[0].Translation.Z, 1e-5f);

            player.Speed = 2.0f;
            player.Advance(0.25f);
            Assert.Near(0.75f, player.Time, 1e-5f);
        }

        [Test]
        public void LoopingWrapsWithoutDrifting()
        {
            IfpAnimation animation = OneBoneClip(1.0f);
            AnimationPlayer player = AnimationPlayer.Create(Clump(), animation);
            // Thirty ticks of 1/30 s is exactly one cycle; float accumulation must not leave 0.999.
            for (int i = 0; i < 30; i++)
                player.Advance(1.0f / 30.0f);
            Assert.Near(0.0f, player.Time, 1e-4f);
            Assert.False(player.Finished);

            for (int i = 0; i < 45; i++)
                player.Advance(1.0f / 30.0f);
            Assert.Near(0.5f, player.Time, 1e-3f);
        }

        [Test]
        public void NonLoopingClipStopsAtItsEnd()
        {
            IfpAnimation animation = OneBoneClip(1.0f);
            AnimationPlayer player = AnimationPlayer.Create(Clump(), animation);
            player.Looping = false;
            player.Advance(0.6f);
            Assert.False(player.Finished);
            player.Advance(0.6f);
            Assert.True(player.Finished);
            Assert.Near(1.0f, player.Time, 1e-5f);
            AnimationPose held = player.Pose;
            player.Advance(0.6f);
            Assert.Near(held.Bones[0].Rotation.Y, player.Pose.Bones[0].Rotation.Y, 1e-6f);
            Assert.Near(1.0f, player.Time, 1e-5f);
        }

        [Test]
        public void SeekWrapsOrClampsLikeAdvance()
        {
            IfpAnimation animation = OneBoneClip(1.0f);
            AnimationPlayer player = AnimationPlayer.Create(Clump(), animation);
            player.Seek(1.25f);
            Assert.Near(0.25f, player.Time, 1e-5f);
            player.Looping = false;
            player.Seek(2.0f);
            Assert.Near(1.0f, player.Time, 1e-5f);
            Assert.True(player.Finished);
            player.Seek(-1.0f);
            Assert.Near(0.0f, player.Time, 1e-5f);
        }

        [Test]
        public void RejectsInvalidDeltas()
        {
            AnimationPlayer player = AnimationPlayer.Create(Clump(), OneBoneClip(1.0f));
            Assert.Throws<ArgumentOutOfRangeException>(delegate { player.Advance(-0.1f); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { player.Advance(float.NaN); });
            Assert.Throws<ArgumentOutOfRangeException>(delegate { player.Seek(float.PositiveInfinity); });
            player.Speed = float.NaN;
            Assert.Throws<InvalidOperationException>(delegate { player.Advance(0.1f); });
        }

        [Test]
        public void RejectsMissingInputs()
        {
            Assert.Throws<ArgumentNullException>(delegate
            {
                AnimationPlayer.Create(null, OneBoneClip(1.0f));
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                AnimationPlayer.Create(Clump(), null);
            });
        }

        /// <summary>One bone that rotates 90 degrees and slides 2 units over the clip.</summary>
        private static IfpAnimation OneBoneClip(float duration)
        {
            IfpKeyFrame first = new IfpKeyFrame();
            first.Rotation = Quaternion.Identity;
            first.Translation = Vector3.Zero;
            first.DeltaTime = 0.0f;
            IfpKeyFrame last = new IfpKeyFrame();
            last.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            last.Translation = new Vector3(0.0f, 0.0f, 2.0f);
            last.DeltaTime = duration;

            IfpSequence sequence = new IfpSequence();
            sequence.Name = "root";
            sequence.BoneTag = -1;
            sequence.HasTranslation = true;
            sequence.Frames = new[] { first, last };
            sequence.Duration = duration;

            IfpAnimation animation = new IfpAnimation();
            animation.Name = "walk";
            animation.Sequences = new[] { sequence };
            animation.FrameCount = 2;
            animation.Duration = duration;
            return animation;
        }

        private static DffClump Clump()
        {
            DffFrame frame = new DffFrame();
            frame.Name = "root";
            frame.ParentIndex = -1;
            frame.LocalTransform = Matrix.Identity;
            DffClump clump = new DffClump();
            clump.Frames = new DffFrame[] { frame };
            clump.Atomics = new DffAtomic[0];
            clump.Geometries = new DffGeometry[0];
            return clump;
        }
    }
}