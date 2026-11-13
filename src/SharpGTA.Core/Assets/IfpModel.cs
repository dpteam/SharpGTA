using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    /// <summary>
    /// One IFP file: a list of animation packages (`ANPK`), each with a block name and the
    /// animations it holds. Times are stored the way the engine keeps them after loading —
    /// per-frame deltas, with the first frame keeping its absolute start time.
    /// </summary>
    public sealed class IfpDocument
    {
        public IfpBlock[] Blocks { get; internal set; }

        public int AnimationCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Blocks.Length; i++)
                    total += Blocks[i].Animations.Length;
                return total;
            }
        }
    }

    public sealed class IfpBlock
    {
        public string Name { get; internal set; }
        public IfpAnimation[] Animations { get; internal set; }
    }

    public sealed class IfpAnimation
    {
        public string Name { get; internal set; }
        public IfpSequence[] Sequences { get; internal set; }

        /// <summary>Frames in the longest sequence, i.e. what a blended hierarchy is sampled on.</summary>
        public int FrameCount { get; internal set; }

        /// <summary>`CAnimBlendHierarchy::totalLength`, the sum of the sequence deltas.</summary>
        public float Duration { get; internal set; }
    }

    public sealed class IfpSequence
    {
        public string Name { get; internal set; }

        /// <summary>`boneTag` for ped skin hierarchies (ANIM chunk size 44), else -1.</summary>
        public int BoneTag { get; internal set; }

        public bool HasTranslation { get; internal set; }
        public bool HasScale { get; internal set; }

        /// <summary>Sum of the frame deltas.</summary>
        public float Duration { get; internal set; }

        public IfpKeyFrame[] Frames { get; internal set; }
    }

    /// <summary>One keyframe: the conjugate rotation, optional translation and the frame delta.</summary>
    public struct IfpKeyFrame
    {
        public Quaternion Rotation;
        public Vector3 Translation;

        /// <summary>
        /// Delta to the next frame, as `KeyFrame::deltaTime` holds it after loading. The first
        /// frame keeps the absolute start time the file stores (normally zero).
        /// </summary>
        public float DeltaTime;
    }
}