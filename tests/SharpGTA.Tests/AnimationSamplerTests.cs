using System;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class AnimationSamplerTests
    {
        private const float Pi = 3.14159265358979323846f;

        [Test]
        public void InterpolatesRotationAndTranslationInsideTheSegment()
        {
            Quaternion from = Quaternion.Identity;
            Quaternion to = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            IfpAnimation animation = Animation("walk",
                Sequence("root", true,
                    Frame(from, Vector3.Zero, 0.0f),
                    Frame(to, new Vector3(2.0f, 0.0f, 0.0f), 1.0f)));

            // Halfway through the only segment: 45 degrees about Y and half the translation.
            AnimationPose pose = AnimationSampler.Sample(animation, 0.5f);
            Assert.Equal(1, pose.Bones.Length);
            Assert.False(pose.Looped);
            BonePose bone = pose.Bones[0];
            Assert.Equal("root", bone.Name);
            Assert.True(bone.HasTranslation);
            Quaternion expected = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 4.0f);
            AssertNearQuaternion(expected, bone.Rotation);
            Assert.Near(1.0f, bone.Translation.X);
            Assert.Near(0.0f, bone.Translation.Y);
            Assert.Near(0.0f, bone.Translation.Z);
            Assert.True(bone.Rotation.W > 0.9f);
        }

        [Test]
        public void WalksTheStoredDeltas()
        {
            // Frame deltas are 0, 0.25 and 0.5, so t=0.3 sits 0.05 into the last segment.
            IfpAnimation animation = Animation("walk",
                Sequence("root", true,
                    Frame(Quaternion.Identity, Vector3.Zero, 0.0f),
                    Frame(RotationDegrees(10), new Vector3(1.0f, 0.0f, 0.0f), 0.25f),
                    Frame(RotationDegrees(20), new Vector3(2.0f, 0.0f, 0.0f), 0.5f)));

            AnimationPose pose = AnimationSampler.Sample(animation, 0.3f);
            BonePose bone = pose.Bones[0];
            // 10% of the way from frame 1 to frame 2.
            AssertNearQuaternion(Quaternion.FromAxisAngle(Vector3.UnitY, (10.0f + 1.0f) * Pi / 180.0f),
                bone.Rotation);
            Assert.Near(1.1f, bone.Translation.X);

            // The sequence ends at 0.75; after that the last frame holds.
            AnimationPose end = AnimationSampler.Sample(animation, 0.75f);
            AssertNearQuaternion(Quaternion.FromAxisAngle(Vector3.UnitY, 20.0f * Pi / 180.0f),
                end.Bones[0].Rotation);
            Assert.Near(2.0f, end.Bones[0].Translation.X);

            AnimationPose past = AnimationSampler.Sample(animation, 4.0f);
            Assert.False(past.Looped);
            AssertNearQuaternion(end.Bones[0].Rotation, past.Bones[0].Rotation);
            Assert.Near(2.0f, past.Bones[0].Translation.X);
        }

        [Test]
        public void WrapsTimeOnlyWhenLooping()
        {
            IfpAnimation animation = Animation("walk",
                Sequence("root", true,
                    Frame(Quaternion.Identity, Vector3.Zero, 0.0f),
                    Frame(RotationDegrees(90), new Vector3(4.0f, 0.0f, 0.0f), 2.0f)));

            AnimationPose wrapped = AnimationSampler.SampleLooping(animation, 2.5f);
            Assert.True(wrapped.Looped);
            AnimationPose inside = AnimationSampler.SampleLooping(animation, 0.5f);
            Assert.False(inside.Looped);
            AssertNearQuaternion(inside.Bones[0].Rotation, wrapped.Bones[0].Rotation);
            Assert.Near(1.0f, wrapped.Bones[0].Translation.X);

            // Negative times wrap forward instead of clamping.
            AnimationPose negative = AnimationSampler.SampleLooping(animation, -1.5f);
            Assert.True(negative.Looped);
            AssertNearQuaternion(inside.Bones[0].Rotation, negative.Bones[0].Rotation);
            Assert.Near(1.0f, negative.Bones[0].Translation.X);

            // Repeating a full cycle is a no-op.
            AnimationPose full = AnimationSampler.SampleLooping(animation, 4.5f);
            AssertNearQuaternion(inside.Bones[0].Rotation, full.Bones[0].Rotation);
        }

        [Test]
        public void SkipsZeroLengthSegmentsInsteadOfDividingByZero()
        {
            IfpAnimation animation = Animation("walk",
                Sequence("root", false,
                    Frame(Quaternion.Identity, 0.0f),
                    Frame(RotationDegrees(0), 0.0f),
                    Frame(RotationDegrees(90), 1.0f)));

            AnimationPose pose = AnimationSampler.Sample(animation, 0.5f);
            AssertNearQuaternion(Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 4.0f),
                pose.Bones[0].Rotation);
        }

        [Test]
        public void HandlesEmptyAndSingleFrameSequences()
        {
            IfpAnimation animation = Animation("walk",
                Sequence("empty", false),
                Sequence("single", false, Frame(RotationDegrees(30), 0.0f)));

            AnimationPose pose = AnimationSampler.Sample(animation, 1.0f);
            Assert.Equal(2, pose.Bones.Length);
            Assert.Near(1.0f, pose.Bones[0].Rotation.W);
            AssertNearQuaternion(Quaternion.FromAxisAngle(Vector3.UnitY, 30.0f * Pi / 180.0f),
                pose.Bones[1].Rotation);
        }

        [Test]
        public void RejectsInvalidAnimations()
        {
            Assert.Throws<ArgumentNullException>(delegate { AnimationSampler.Sample(null, 0.0f); });
            IfpAnimation broken = new IfpAnimation();
            broken.Name = "broken";
            broken.Sequences = null;
            Assert.Throws<AssetFormatException>(delegate { AnimationSampler.Sample(broken, 0.0f); });
            IfpAnimation nullSequence = Animation("broken", Sequence("root", false));
            nullSequence.Sequences[0] = null;
            Assert.Throws<AssetFormatException>(delegate
            {
                AnimationSampler.Sample(nullSequence, 0.0f);
            });
        }

        [Test]
        public void BindsSequencesToFramesByNameIgnoringCase()
        {
            DffClump clump = Clump(
                Frame("Root", -1, Vector3.Zero),
                Frame("Pelvis", 0, new Vector3(1.0f, 0.0f, 0.0f)));
            IfpAnimation animation = Animation("walk",
                Sequence("root", false, Frame(Quaternion.Identity, 0.0f)),
                Sequence("PELVIS", false, Frame(Quaternion.Identity, 0.0f)),
                Sequence("missing", false, Frame(Quaternion.Identity, 0.0f)));

            AnimationBinding binding = AnimationBinding.Bind(clump, animation);
            Assert.Equal(2, binding.BoundCount);
            Assert.Equal(0, binding.SequenceFrames[0]);
            Assert.Equal(1, binding.SequenceFrames[1]);
            Assert.Equal(-1, binding.SequenceFrames[2]);
            Assert.Equal(1, binding.UnboundSequences.Length);
            Assert.Equal("missing", binding.UnboundSequences[0]);
            Assert.False(binding.IsSkinned);
        }

        [Test]
        public void SkinnedClumpsMatchPedBoneTags()
        {
            DffFrame frame = Frame("", -1, Vector3.Zero);
            DffHAnimNode node = new DffHAnimNode();
            node.Id = 0; // BONE_waist
            DffHAnim hanim = new DffHAnim();
            hanim.Nodes = new DffHAnimNode[] { node };
            frame.HAnim = hanim;
            DffClump clump = new DffClump();
            clump.Frames = new DffFrame[] { frame };
            clump.Atomics = new DffAtomic[] { SkinnedAtomic(0) };
            clump.Geometries = new DffGeometry[] { new DffGeometry() };
            IfpAnimation animation = Animation("walk",
                Sequence("Swaist", false, Frame(Quaternion.Identity, 0.0f)));

            AnimationBinding binding = AnimationBinding.Bind(clump, animation);
            Assert.True(binding.IsSkinned);
            Assert.Equal(1, binding.BoundCount);
            Assert.Equal("Swaist", AnimationBinding.PedBoneName(0));
            Assert.Equal("Shead", AnimationBinding.PedBoneName(9));
            Assert.True(AnimationBinding.PedBoneName(99) == null);
        }

        [Test]
        public void PoseReplacesRotationAndAddsToTheRestPosition()
        {
            DffClump clump = Clump(Frame("root", -1, new Vector3(1.0f, 2.0f, 3.0f)));
            Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            IfpAnimation animation = Animation("walk",
                Sequence("root", true,
                    Frame(rotation, new Vector3(0.0f, 1.0f, 0.0f), 0.0f),
                    Frame(rotation, new Vector3(0.0f, 1.0f, 0.0f), 1.0f)));

            AnimationBinding binding = AnimationBinding.Bind(clump, animation);
            AnimationPose pose = AnimationSampler.Sample(animation, 0.5f);
            Matrix[] local = binding.ResolveLocal(pose);
            Assert.Equal(1, local.Length);
            // The animation translation is added to the rest offset, as FrameUpdateCallBack does.
            Assert.Near(1.0f, local[0].Position.X);
            Assert.Near(3.0f, local[0].Position.Y);
            Assert.Near(3.0f, local[0].Position.Z);
            Matrix expected = rotation.ToMatrix();
            AssertNearVector(expected.Right, local[0].Right);
            AssertNearVector(expected.Forward, local[0].Forward);
            AssertNearVector(expected.Up, local[0].Up);
        }

        [Test]
        public void WorldTransformsPropagateThroughTheHierarchy()
        {
            DffClump clump = Clump(
                Frame("root", -1, Vector3.Zero),
                Frame("child", 0, new Vector3(0.0f, 0.0f, 5.0f)));
            Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            IfpAnimation animation = Animation("walk",
                Sequence("root", false,
                    Frame(rotation, 0.0f),
                    Frame(rotation, 1.0f)));

            AnimationBinding binding = AnimationBinding.Bind(clump, animation);
            Matrix[] world = binding.ResolveWorld(AnimationSampler.Sample(animation, 0.5f));
            Assert.Equal(2, world.Length);
            Vector3 expectedChild = rotation.ToMatrix().TransformPoint(new Vector3(0.0f, 0.0f, 5.0f));
            AssertNearVector(expectedChild, world[1].Position);
            // The child keeps its own rest rotation, so its world rotation matches the parent's.
            Matrix expectedRotation = rotation.ToMatrix();
            AssertNearVector(expectedRotation.Right, world[1].Right);

            // The rest skeleton is the unanimated one.
            Matrix[] rest = binding.RestWorld();
            AssertNearVector(new Vector3(0.0f, 0.0f, 5.0f), rest[1].Position);
        }

        [Test]
        public void RejectsMismatchedPosesAndBrokenHierarchies()
        {
            DffClump clump = Clump(Frame("root", -1, Vector3.Zero));
            IfpAnimation animation = Animation("walk",
                Sequence("root", false, Frame(Quaternion.Identity, 0.0f)));
            AnimationBinding binding = AnimationBinding.Bind(clump, animation);
            IfpAnimation other = Animation("walk",
                Sequence("root", false, Frame(Quaternion.Identity, 0.0f)),
                Sequence("extra", false, Frame(Quaternion.Identity, 0.0f)));
            Assert.Throws<ArgumentException>(delegate
            {
                binding.ResolveLocal(AnimationSampler.Sample(other, 0.0f));
            });

            DffClump cyclic = Clump(Frame("a", 0, Vector3.Zero));
            AnimationBinding cyclicBinding = AnimationBinding.Bind(cyclic, animation);
            Assert.Throws<AssetFormatException>(delegate
            {
                cyclicBinding.RestWorld();
            });

            DffClump dangling = Clump(Frame("a", 7, Vector3.Zero));
            AnimationBinding danglingBinding = AnimationBinding.Bind(dangling, animation);
            Assert.Throws<AssetFormatException>(delegate { danglingBinding.RestWorld(); });
        }

        private static IfpAnimation Animation(string name, params IfpSequence[] sequences)
        {
            IfpAnimation animation = new IfpAnimation();
            animation.Name = name;
            animation.Sequences = sequences;
            int frames = 0;
            float longest = 0.0f;
            for (int i = 0; i < sequences.Length; i++)
            {
                if (sequences[i].Frames.Length > frames)
                    frames = sequences[i].Frames.Length;
                if (sequences[i].Duration > longest)
                    longest = sequences[i].Duration;
            }
            animation.FrameCount = frames;
            animation.Duration = longest;
            return animation;
        }

        private static IfpSequence Sequence(string name, bool hasTranslation,
            params IfpKeyFrame[] frames)
        {
            IfpSequence sequence = new IfpSequence();
            sequence.Name = name;
            sequence.BoneTag = -1;
            sequence.HasTranslation = hasTranslation;
            sequence.Frames = frames;
            float duration = 0.0f;
            for (int i = 1; i < frames.Length; i++)
                duration += frames[i].DeltaTime;
            sequence.Duration = duration;
            return sequence;
        }

        private static IfpKeyFrame Frame(Quaternion rotation, float deltaTime)
        {
            IfpKeyFrame frame = new IfpKeyFrame();
            frame.Rotation = rotation;
            frame.Translation = Vector3.Zero;
            frame.DeltaTime = deltaTime;
            return frame;
        }

        private static IfpKeyFrame Frame(Quaternion rotation, Vector3 translation, float deltaTime)
        {
            IfpKeyFrame frame = new IfpKeyFrame();
            frame.Rotation = rotation;
            frame.Translation = translation;
            frame.DeltaTime = deltaTime;
            return frame;
        }

        private static Quaternion RotationDegrees(float degrees)
        {
            return Quaternion.FromAxisAngle(Vector3.UnitY, degrees * Pi / 180.0f);
        }

        private static DffClump Clump(params DffFrame[] frames)
        {
            DffClump clump = new DffClump();
            clump.Frames = frames;
            clump.Atomics = new DffAtomic[0];
            clump.Geometries = new DffGeometry[0];
            return clump;
        }

        private static DffFrame Frame(string name, int parent, Vector3 position)
        {
            DffFrame frame = new DffFrame();
            frame.Name = name;
            frame.ParentIndex = parent;
            frame.LocalTransform = Matrix.CreateTranslation(position);
            return frame;
        }

        private static DffAtomic SkinnedAtomic(int geometryIndex)
        {
            DffGeometry geometry = new DffGeometry();
            geometry.Skin = new DffSkin();
            DffAtomic atomic = new DffAtomic();
            atomic.Geometry = geometry;
            atomic.GeometryIndex = geometryIndex;
            return atomic;
        }

        private static void AssertNearQuaternion(Quaternion expected, Quaternion actual)
        {
            // Quaternions that describe the same rotation may differ by sign.
            float dot = expected.X * actual.X + expected.Y * actual.Y + expected.Z * actual.Z +
                expected.W * actual.W;
            Assert.Near(1.0f, Math.Abs(dot), 1e-4f,
                "expected " + expected.X + "," + expected.Y + "," + expected.Z + "," + expected.W +
                " got " + actual.X + "," + actual.Y + "," + actual.Z + "," + actual.W);
        }

        private static void AssertNearVector(Vector3 expected, Vector3 actual)
        {
            Assert.Near(expected.X, actual.X, 1e-4f);
            Assert.Near(expected.Y, actual.Y, 1e-4f);
            Assert.Near(expected.Z, actual.Z, 1e-4f);
        }
    }
}