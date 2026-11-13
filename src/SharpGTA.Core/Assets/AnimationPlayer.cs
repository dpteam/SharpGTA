using System;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>
    /// Drives one animation clip over time. The player owns the clock, not the sampler: advancing it
    /// is the only thing that moves the pose, so the same tick sequence always produces the same
    /// frames. Time is accumulated in double precision because the simulation steps 1/30 s and a
    /// float accumulator drifts visibly over a few minutes.
    /// </summary>
    public sealed class AnimationPlayer
    {
        private double time;
        private AnimationBinding binding;

        public AnimationBinding Binding
        {
            get { return binding; }
        }

        /// <summary>Clip name, kept for diagnostics.</summary>
        public string Name
        {
            get { return binding.Animation.Name; }
        }

        public float Speed { get; set; }

        public bool Looping { get; set; }

        /// <summary>True once a non-looping clip reached its end.</summary>
        public bool Finished { get; private set; }

        public float Duration
        {
            get { return binding.Animation.Duration; }
        }

        public float Time
        {
            get { return (float)time; }
        }

        public AnimationPose Pose { get; private set; }

        private AnimationPlayer(AnimationBinding binding)
        {
            this.binding = binding;
            Speed = 1.0f;
            Looping = true;
            Pose = AnimationSampler.SampleLooping(binding.Animation, 0.0f);
        }

        public static AnimationPlayer Create(DffClump clump, IfpAnimation animation)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (animation == null)
                throw new ArgumentNullException("animation");
            return new AnimationPlayer(AnimationBinding.Bind(clump, animation));
        }

        /// <summary>Advances the clip clock and resamples the pose. Negative deltas are rejected.</summary>
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds < 0.0f)
                throw new ArgumentOutOfRangeException("deltaSeconds");
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException("deltaSeconds");
            if (float.IsNaN(Speed) || float.IsInfinity(Speed))
                throw new InvalidOperationException("Animation speed must be finite.");

            float duration = Duration;
            double next = time + deltaSeconds * Speed;
            if (duration <= 0.0f)
            {
                time = 0.0;
                Pose = AnimationSampler.SampleLooping(binding.Animation, 0.0f);
                Finished = true;
                return;
            }
            if (next < 0.0)
                next = 0.0;

            if (Looping)
            {
                double wrapped = next % duration;
                time = wrapped;
                Finished = false;
                Pose = AnimationSampler.SampleLooping(binding.Animation, (float)wrapped);
            }
            else if (next >= duration)
            {
                time = duration;
                Finished = true;
                Pose = AnimationSampler.Sample(binding.Animation, duration);
            }
            else
            {
                time = next;
                Finished = false;
                Pose = AnimationSampler.Sample(binding.Animation, (float)next);
            }
        }

        /// <summary>Jump to a pose time; the clip is resampled immediately.</summary>
        public void Seek(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException("seconds");
            float duration = Duration;
            double target = seconds;
            if (target < 0.0)
                target = 0.0;
            if (duration > 0.0f)
            {
                if (Looping)
                    target = target % duration;
                else if (target > duration)
                    target = duration;
            }
            time = target;
            Finished = !Looping && duration > 0.0f && target >= duration;
            Pose = Looping
                ? AnimationSampler.SampleLooping(binding.Animation, (float)target)
                : AnimationSampler.Sample(binding.Animation, (float)target);
        }

        /// <summary>Frame world transforms for the current pose.</summary>
        public Mathematics.Matrix[] ResolveWorld()
        {
            return binding.ResolveWorld(Pose);
        }
    }
}