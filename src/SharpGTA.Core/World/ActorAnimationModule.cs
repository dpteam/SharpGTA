using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.World
{
    /// <summary>
    /// Gives actors a running animation clip. The clip's own mover sets the ground speed — a ped that
    /// walks at 1.3 units per second is exactly what `walk_civi` says it is — and the clip clock is
    /// then scaled by that speed so the feet keep up with the ground instead of moonwalking.
    /// Clips without a mover (VC peds animate in place) keep the caller's speed and play at 1x.
    /// </summary>
    public sealed class ActorAnimationModule : ISimulationModule, IStateSerializable
    {
        private readonly List<Entry> entries = new List<Entry>();

        public string Name
        {
            get { return "ActorAnimations"; }
        }

        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>Name of the clip every attached actor plays.</summary>
        public string ClipName
        {
            get { return entries.Count == 0 ? null : entries[0].Player.Name; }
        }

        /// <summary>Mover speed of the clip in units per second, or zero for an in-place clip.</summary>
        public float ClipSpeed
        {
            get { return entries.Count == 0 ? 0.0f : entries[0].ClipSpeed; }
        }

        /// <summary>
        /// Ground speed this clip implies. Callers use it instead of a hand-picked constant when it
        /// is positive; an in-place clip keeps whatever speed the caller already has.
        /// </summary>
        public static float SuggestedSpeed(IfpAnimation animation)
        {
            if (animation == null)
                throw new ArgumentNullException("animation");
            return AnimationPlayer.ComputeCycleSpeed(animation);
        }

        public bool Attach(DynamicEntity entity, DffClump clump, IfpAnimation clip, float actorSpeed)
        {
            if (entity == null)
                throw new ArgumentNullException("entity");
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (clip == null)
                throw new ArgumentNullException("clip");
            if (actorSpeed < 0.0f)
                throw new ArgumentOutOfRangeException("actorSpeed");
            if (Find(entity) >= 0)
                throw new InvalidOperationException("Actor already plays a clip.");

            AnimationPlayer player = AnimationPlayer.Create(clump, clip);
            Entry entry = new Entry();
            entry.Entity = entity;
            entry.Player = player;
            entry.ClipSpeed = player.CycleSpeed;
            // A mover clip runs as fast as the character really moves; an in-place clip has no
            // ground truth to sync to, so it plays at its authored rate.
            player.Speed = entry.ClipSpeed > 0.0f ? actorSpeed / entry.ClipSpeed : 1.0f;
            entries.Add(entry);
            return true;
        }

        public bool Release(DynamicEntity entity)
        {
            int index = Find(entity);
            if (index < 0)
                return false;
            entries.RemoveAt(index);
            return true;
        }

        public bool Contains(DynamicEntity entity)
        {
            return Find(entity) >= 0;
        }

        /// <summary>Current pose of an attached actor, for renderers and diagnostics.</summary>
        public bool TryGetPose(DynamicEntity entity, out AnimationPose pose)
        {
            int index = Find(entity);
            if (index < 0)
            {
                pose = null;
                return false;
            }
            pose = entries[index].Player.Pose;
            return true;
        }

        public Matrix[] ResolveWorld(DynamicEntity entity)
        {
            int index = Find(entity);
            if (index < 0)
                return null;
            return entries[index].Player.ResolveWorld();
        }

        /// <summary>Clip time of an actor in seconds, wrapped to the clip length.</summary>
        public float ClipTime(DynamicEntity entity)
        {
            int index = Find(entity);
            if (index < 0)
                throw new InvalidOperationException("Actor does not play a clip.");
            return entries[index].Player.Time;
        }

        public void Process(SimulationContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            float delta = context.Time.DeltaSeconds;
            for (int i = 0; i < entries.Count; i++)
                entries[i].Player.Advance(delta);
        }

        /// <summary>Order-sensitive digest of every actor pose, so a run is reproducible.</summary>
        public ulong PoseDigest()
        {
            StateWriter writer = new StateWriter();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                writer.WriteInt32(entry.Entity.Handle);
                writer.WriteSingle(entry.Player.Time);
                AnimationPose pose = entry.Player.Pose;
                for (int b = 0; b < pose.Bones.Length; b++)
                {
                    writer.WriteSingle(pose.Bones[b].Rotation.X);
                    writer.WriteSingle(pose.Bones[b].Rotation.Y);
                    writer.WriteSingle(pose.Bones[b].Rotation.Z);
                    writer.WriteSingle(pose.Bones[b].Rotation.W);
                    writer.WriteSingle(pose.Bones[b].Translation.X);
                    writer.WriteSingle(pose.Bones[b].Translation.Y);
                    writer.WriteSingle(pose.Bones[b].Translation.Z);
                }
            }
            return writer.Digest;
        }

        public void WriteState(StateWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException("writer");
            writer.WriteInt32(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                writer.WriteInt32(entry.Entity.Handle);
                writer.WriteSingle(entry.Player.Time);
                writer.WriteSingle(entry.Player.Speed);
            }
        }

        private int Find(DynamicEntity entity)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i].Entity, entity))
                    return i;
            }
            return -1;
        }

        private sealed class Entry
        {
            public DynamicEntity Entity;
            public AnimationPlayer Player;
            public float ClipSpeed;
        }
    }
}