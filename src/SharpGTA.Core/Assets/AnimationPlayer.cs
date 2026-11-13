using System;
using SharpGTA.IO;
using SharpGTA.Mathematics;

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

        /// <summary>
        /// Root translation this clip moves the character by over one cycle, i.e. the locomotion
        /// `mover`. III walk clips carry it; an in-place clip reports zero and takes its speed from
        /// the caller, which is what `ASSOC_MOVEMENT` + `relSpeed` do in the original.
        /// </summary>
        public Vector3 CycleDisplacement
        {
            get { return ComputeCycleDisplacement(binding.Animation); }
        }

        public static Vector3 ComputeCycleDisplacement(IfpAnimation animation)
        {
            if (animation == null)
                throw new ArgumentNullException("animation");
            AnimationPose start = AnimationSampler.SampleLooping(animation, 0.0f);
            AnimationPose end = AnimationSampler.Sample(animation, animation.Duration);
            Vector3 startTranslation = SumTranslation(start);
            Vector3 endTranslation = SumTranslation(end);
            return new Vector3(
                endTranslation.X - startTranslation.X,
                endTranslation.Y - startTranslation.Y,
                endTranslation.Z - startTranslation.Z);
        }

        /// <summary>Mover speed of a clip in units per second; zero for an in-place clip.</summary>
        public static float ComputeCycleSpeed(IfpAnimation animation)
        {
            if (animation == null)
                throw new ArgumentNullException("animation");
            if (animation.Duration <= 0.0f)
                return 0.0f;
            Vector3 displacement = ComputeCycleDisplacement(animation);
            return (float)Math.Sqrt(displacement.X * displacement.X +
                displacement.Y * displacement.Y + displacement.Z * displacement.Z) /
                animation.Duration;
        }

        /// <summary>Locomotion speed in units per second taken from the clip's mover.</summary>
        public float CycleSpeed
        {
            get { return ComputeCycleSpeed(binding.Animation); }
        }

        /// <summary>
        /// Translation difference between the previous and the current tick, divided by the delta:
        /// the per-tick velocity `FrameUpdateCallBackWithVelocityExtraction` writes into
        /// `velocity2d`, with the wrap-around segment added when the clip loops inside the tick.
        /// </summary>
        public Vector3 Velocity { get; private set; }

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
            Vector3 current = SumTranslation(Pose);
            if (duration <= 0.0f)
            {
                time = 0.0;
                Pose = AnimationSampler.SampleLooping(binding.Animation, 0.0f);
                Velocity = Vector3.Zero;
                Finished = true;
                return;
            }
            if (next < 0.0)
                next = 0.0;

            if (Looping)
            {
                bool wrapped = next >= duration;
                double wrappedTime = next % duration;
                time = wrappedTime;
                Finished = false;
                Pose = AnimationSampler.SampleLooping(binding.Animation, (float)wrappedTime);
                Vector3 moved = Difference(SumTranslation(Pose), current);
                if (wrapped)
                {
                    // The tick crossed the loop point: add the rest of the clip, exactly like
                    // `velocity2d += end` in the original extraction callback.
                    moved = Add(moved, SumTranslation(AnimationSampler.Sample(
                        binding.Animation, duration)));
                }
                Velocity = Scale(moved, deltaSeconds);
            }
            else if (next >= duration)
            {
                time = duration;
                Finished = true;
                Pose = AnimationSampler.Sample(binding.Animation, duration);
                Velocity = Scale(Difference(SumTranslation(Pose), current), deltaSeconds);
            }
            else
            {
                time = next;
                Finished = false;
                Pose = AnimationSampler.Sample(binding.Animation, (float)next);
                Velocity = Scale(Difference(SumTranslation(Pose), current), deltaSeconds);
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

        /// <summary>Sum of the translation channels, i.e. the mover offset in pose space.</summary>
        public static Vector3 SumTranslation(AnimationPose pose)
        {
            if (pose == null)
                throw new ArgumentNullException("pose");
            float x = 0.0f;
            float y = 0.0f;
            float z = 0.0f;
            for (int i = 0; i < pose.Bones.Length; i++)
            {
                if (!pose.Bones[i].HasTranslation)
                    continue;
                x += pose.Bones[i].Translation.X;
                y += pose.Bones[i].Translation.Y;
                z += pose.Bones[i].Translation.Z;
            }
            return new Vector3(x, y, z);
        }

        private static Vector3 Difference(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        private static Vector3 Add(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        private static Vector3 Scale(Vector3 value, float factor)
        {
            if (factor <= 0.0f)
                return Vector3.Zero;
            return new Vector3(value.X / factor, value.Y / factor, value.Z / factor);
        }
    }
}