using System;
using System.Collections.Generic;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    /// <summary>
    /// Binds one IFP animation to a DFF clump skeleton. The original binds a sequence to a frame
    /// by node name — `RpAnimBlendClumpFindFrame` compares the frame name for non-skinned clumps
    /// and the ped bone name of the hierarchy node id for skinned ones — so this does the same:
    /// names are ASCII case-insensitive and unmatched sequences are reported instead of guessed.
    /// </summary>
    public sealed class AnimationBinding
    {
        /// <summary>`ConvertBoneTag2BoneName` from re3; anim names of skinned peds use these.</summary>
        private static readonly string[] BoneNames =
        {
            "Swaist", "Supperlegr", "Slowerlegr", "Sfootr", "Supperlegl", "Slowerlegl", "Sfootl",
            "Smid", "Storso", "Shead", "Supperarmr", "Slowerarmr", "SRhand", "Supperarml",
            "Slowerarml", "SLhand"
        };

        public IfpAnimation Animation { get; internal set; }
        public DffClump Clump { get; internal set; }

        /// <summary>Frame index per animation sequence, -1 when the clump has no such node.</summary>
        public int[] SequenceFrames { get; internal set; }

        public string[] UnboundSequences { get; internal set; }

        /// <summary>True when the clump carries a skin, i.e. bones are matched by bone tag.</summary>
        public bool IsSkinned { get; internal set; }

        public int BoundCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SequenceFrames.Length; i++)
                {
                    if (SequenceFrames[i] >= 0)
                        count++;
                }
                return count;
            }
        }

        /// <summary>`ConvertBoneTag2BoneName`; null for tags outside the ped bone table.</summary>
        public static string PedBoneName(int boneTag)
        {
            if (boneTag < 0 || boneTag >= BoneNames.Length)
                return null;
            return BoneNames[boneTag];
        }

        public static AnimationBinding Bind(DffClump clump, IfpAnimation animation)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (animation == null)
                throw new ArgumentNullException("animation");
            if (clump.Frames == null)
                throw new AssetFormatException("DFF clump has no frame array.");
            if (animation.Sequences == null)
                throw new AssetFormatException("IFP animation has no sequence array.");

            AnimationBinding binding = new AnimationBinding();
            binding.Clump = clump;
            binding.Animation = animation;
            binding.IsSkinned = DetectSkinned(clump);

            // A frame can be reachable by both its own name and its ped bone name, so keep the
            // first frame registered for a name and let the name map win over a duplicate.
            Dictionary<string, int> frames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < clump.Frames.Length; i++)
            {
                DffFrame frame = clump.Frames[i];
                if (frame == null)
                    throw new AssetFormatException("DFF frame is null.");
                AddName(frames, frame.Name, i);
                if (binding.IsSkinned)
                    AddName(frames, FrameBoneName(frame), i);
            }

            int[] sequenceFrames = new int[animation.Sequences.Length];
            List<string> unbound = new List<string>();
            for (int i = 0; i < sequenceFrames.Length; i++)
            {
                IfpSequence sequence = animation.Sequences[i];
                if (sequence == null)
                    throw new AssetFormatException("IFP animation has a null sequence.");
                int frame;
                if (sequence.Name != null && frames.TryGetValue(sequence.Name, out frame))
                    sequenceFrames[i] = frame;
                else
                {
                    sequenceFrames[i] = -1;
                    unbound.Add(sequence.Name == null ? "" : sequence.Name);
                }
            }

            binding.SequenceFrames = sequenceFrames;
            binding.UnboundSequences = unbound.ToArray();
            return binding;
        }

        /// <summary>
        /// Local transforms with the pose applied: the sampled rotation replaces the frame's
        /// rotation and the sampled translation is added to the rest position, as
        /// `FrameUpdateCallBack` writes into the HAnim interpolation frame.
        /// </summary>
        public Matrix[] ResolveLocal(AnimationPose pose)
        {
            if (pose == null)
                throw new ArgumentNullException("pose");
            if (pose.Bones == null || pose.Bones.Length != SequenceFrames.Length)
                throw new ArgumentException("Pose does not belong to the bound animation.", "pose");
            if (Clump.Frames.Length == 0)
                return new Matrix[0];

            Matrix[] locals = new Matrix[Clump.Frames.Length];
            for (int i = 0; i < locals.Length; i++)
            {
                DffFrame frame = Clump.Frames[i];
                if (frame == null)
                    throw new AssetFormatException("DFF frame is null.");
                locals[i] = frame.LocalTransform;
            }

            for (int i = 0; i < SequenceFrames.Length; i++)
            {
                int frameIndex = SequenceFrames[i];
                if (frameIndex < 0)
                    continue;
                BonePose bone = pose.Bones[i];
                Matrix rest = locals[frameIndex];
                Vector3 position = rest.Position;
                if (bone.HasTranslation)
                {
                    position = new Vector3(
                        rest.Position.X + bone.Translation.X,
                        rest.Position.Y + bone.Translation.Y,
                        rest.Position.Z + bone.Translation.Z);
                }
                Matrix rotation = SafeNormalize(bone.Rotation).ToMatrix();
                locals[frameIndex] = new Matrix(rotation.Right, rotation.Forward, rotation.Up, position);
            }
            return locals;
        }

        /// <summary>Resolved `local * parent` world matrices for every frame.</summary>
        public Matrix[] ResolveWorld(AnimationPose pose)
        {
            return ResolveWorld(ResolveLocal(pose));
        }

        /// <summary>The unanimated skeleton in the same form, so callers can diff poses.</summary>
        public Matrix[] RestWorld()
        {
            Matrix[] locals = new Matrix[Clump.Frames.Length];
            for (int i = 0; i < locals.Length; i++)
            {
                DffFrame frame = Clump.Frames[i];
                if (frame == null)
                    throw new AssetFormatException("DFF frame is null.");
                locals[i] = frame.LocalTransform;
            }
            return ResolveWorld(locals);
        }

        private Matrix[] ResolveWorld(Matrix[] locals)
        {
            Matrix[] world = new Matrix[locals.Length];
            byte[] states = new byte[locals.Length];
            for (int i = 0; i < locals.Length; i++)
                ResolveFrame(locals, world, states, i);
            return world;
        }

        private Matrix ResolveFrame(Matrix[] locals, Matrix[] world, byte[] states, int index)
        {
            if (states[index] == 1)
                return world[index];
            if (states[index] == 2)
                throw new AssetFormatException("Cycle in DFF frame hierarchy.");
            states[index] = 2;
            Matrix result = locals[index];
            int parent = Clump.Frames[index].ParentIndex;
            if (parent < -1)
                throw new AssetFormatException("Frame parent must be -1 or refer to another frame.");
            if (parent >= 0)
            {
                if (parent >= locals.Length)
                    throw new AssetFormatException("Frame parent is outside the frame list.");
                result = Rendering.DffFrameTransforms.Concatenate(result,
                    ResolveFrame(locals, world, states, parent));
            }
            world[index] = result;
            states[index] = 1;
            return result;
        }

        private static bool DetectSkinned(DffClump clump)
        {
            if (clump.Atomics == null)
                return false;
            for (int i = 0; i < clump.Atomics.Length; i++)
            {
                DffAtomic atomic = clump.Atomics[i];
                if (atomic == null)
                    continue;
                if (atomic.LegacySkin != null)
                    return true;
                if (atomic.Geometry != null && atomic.Geometry.Skin != null)
                    return true;
            }
            return false;
        }

        private static string FrameBoneName(DffFrame frame)
        {
            if (frame.HAnim == null || frame.HAnim.Nodes == null || frame.HAnim.Nodes.Length == 0)
                return null;
            DffHAnimNode node = frame.HAnim.Nodes[0];
            if (node == null)
                return null;
            return PedBoneName(node.Id);
        }

        private static void AddName(Dictionary<string, int> frames, string name, int index)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (!frames.ContainsKey(name))
                frames.Add(name, index);
        }

        private static Quaternion SafeNormalize(Quaternion value)
        {
            float lengthSquared = value.X * value.X + value.Y * value.Y + value.Z * value.Z +
                value.W * value.W;
            if (lengthSquared <= 0.0f)
                return Quaternion.Identity;
            float inverse = 1.0f / (float)Math.Sqrt(lengthSquared);
            return new Quaternion(value.X * inverse, value.Y * inverse, value.Z * inverse,
                value.W * inverse);
        }
    }
}