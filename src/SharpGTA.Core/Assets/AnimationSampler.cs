using System;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    /// <summary>
    /// One bone at a sampled time: the rotation and (when the sequence carries one) the
    /// translation the engine writes into the frame, exactly as `CAnimBlendNode::Update` returns
    /// them before blending.
    /// </summary>
    public struct BonePose
    {
        public string Name;
        public Quaternion Rotation;
        public Vector3 Translation;
        public bool HasTranslation;
    }

    /// <summary>A whole hierarchy sampled at one time.</summary>
    public sealed class AnimationPose
    {
        public float Time { get; internal set; }

        /// <summary>True when the requested time wrapped back to the start of any sequence.</summary>
        public bool Looped { get; internal set; }

        public BonePose[] Bones { get; internal set; }
    }

    /// <summary>
    /// Samples IFP sequences the way `CAnimBlendNode::FindKeyFrame` + `Update` do: the stored
    /// deltas describe how long each frame lasts, the position inside that segment gives the
    /// blend fraction, rotations are slerped and translations lerped in the same direction as the
    /// original. There is no blending between animations, no partial weights and no skeleton yet.
    /// </summary>
    public static class AnimationSampler
    {
        private const float Pi = 3.14159265358979323846f;
        private const float PiOverTwo = 1.57079632679489661923f;

        public static AnimationPose Sample(IfpAnimation animation, float time)
        {
            return SampleCore(animation, time, false);
        }

        /// <summary>
        /// Wraps the time into each sequence's own length, the way a repeating association does.
        /// The original `FindKeyFrame` only walks forward, so an explicit modulo is the
        /// deterministic equivalent for an arbitrary time; equal-length sequences behave the same.
        /// </summary>
        public static AnimationPose SampleLooping(IfpAnimation animation, float time)
        {
            return SampleCore(animation, time, true);
        }

        private static AnimationPose SampleCore(IfpAnimation animation, float time, bool looping)
        {
            if (animation == null)
                throw new ArgumentNullException("animation");
            if (animation.Sequences == null)
                throw new AssetFormatException("IFP animation has no sequence array.");

            BonePose[] bones = new BonePose[animation.Sequences.Length];
            bool looped = false;
            for (int i = 0; i < bones.Length; i++)
            {
                IfpSequence sequence = animation.Sequences[i];
                if (sequence == null)
                    throw new AssetFormatException("IFP animation has a null sequence.");
                Quaternion rotation;
                Vector3 translation;
                bool sequenceLooped;
                SampleSequence(sequence, time, looping, out rotation, out translation, out sequenceLooped);
                looped |= sequenceLooped;
                BonePose bone = new BonePose();
                bone.Name = sequence.Name;
                bone.Rotation = rotation;
                bone.Translation = translation;
                bone.HasTranslation = sequence.HasTranslation;
                bones[i] = bone;
            }

            AnimationPose pose = new AnimationPose();
            pose.Time = time;
            pose.Looped = looped;
            pose.Bones = bones;
            return pose;
        }

        private static void SampleSequence(IfpSequence sequence, float time, bool looping,
            out Quaternion rotation, out Vector3 translation, out bool looped)
        {
            looped = false;
            if (sequence.Frames == null)
                throw new AssetFormatException("IFP sequence has no frame array.");

            IfpKeyFrame[] frames = sequence.Frames;
            if (frames.Length == 0)
            {
                rotation = Quaternion.Identity;
                translation = Vector3.Zero;
                return;
            }
            if (frames.Length == 1)
            {
                // A single frame has no segment to interpolate along.
                rotation = frames[0].Rotation;
                translation = frames[0].Translation;
                return;
            }

            float local = time;
            float duration = sequence.Duration;
            if (looping && duration > 0.0f)
            {
                looped = local >= duration || local < 0.0f;
                local = local % duration;
                if (local < 0.0f)
                    local += duration;
            }
            else if (local < 0.0f)
            {
                local = 0.0f;
            }

            // Frame 0 stores the absolute start time, so the walk begins at frame 1 and
            // `frames[i].DeltaTime` is the length of the segment that ends at frame i.
            int index = 1;
            float remaining = local;
            while (index < frames.Length)
            {
                float delta = frames[index].DeltaTime;
                if (delta <= 0.0f)
                {
                    // Zero-length segments are skipped instead of dividing by zero; the original
                    // would spin in `NextKeyFrame` for the same data.
                    index++;
                    continue;
                }
                if (remaining <= delta)
                    break;
                remaining -= delta;
                index++;
            }

            int frameA;
            int frameB;
            float fraction;
            if (index >= frames.Length)
            {
                frameA = frames.Length - 1;
                frameB = frameA - 1;
                fraction = 1.0f;
            }
            else
            {
                frameA = index;
                frameB = index - 1;
                fraction = remaining / frames[index].DeltaTime;
            }

            rotation = Slerp(frames[frameB].Rotation, frames[frameA].Rotation, fraction);
            translation = new Vector3(
                frames[frameB].Translation.X + fraction * (frames[frameA].Translation.X - frames[frameB].Translation.X),
                frames[frameB].Translation.Y + fraction * (frames[frameA].Translation.Y - frames[frameB].Translation.Y),
                frames[frameB].Translation.Z + fraction * (frames[frameA].Translation.Z - frames[frameB].Translation.Z));
        }

        /// <summary>`CQuaternion::Slerp`, including its "shorter arc" branch.</summary>
        internal static Quaternion Slerp(Quaternion from, Quaternion to, float fraction)
        {
            float cos = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
            if (cos > 1.0f)
                cos = 1.0f;
            if (cos < -1.0f)
                cos = -1.0f;
            float theta = (float)Math.Acos(cos);
            if (theta == 0.0f)
                return to;
            if (theta == Pi)
            {
                // Exactly opposite quaternions have no unique shortest arc; the original would
                // divide by sin(pi) and return NaN, so keep the first frame instead.
                return from;
            }

            float invSin = 1.0f / (float)Math.Sin(theta);
            float w1;
            float w2;
            if (theta > PiOverTwo)
            {
                theta = Pi - theta;
                w1 = (float)Math.Sin((1.0f - fraction) * theta) * invSin;
                w2 = -(float)Math.Sin(fraction * theta) * invSin;
            }
            else
            {
                w1 = (float)Math.Sin((1.0f - fraction) * theta) * invSin;
                w2 = (float)Math.Sin(fraction * theta) * invSin;
            }
            return new Quaternion(
                w1 * from.X + w2 * to.X,
                w1 * from.Y + w2 * to.Y,
                w1 * from.Z + w2 * to.Z,
                w1 * from.W + w2 * to.W);
        }
    }
}