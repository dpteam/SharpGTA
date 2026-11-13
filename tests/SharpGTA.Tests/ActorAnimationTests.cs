using System;
using SharpGTA.Assets;
using SharpGTA.Data;
using SharpGTA.Games;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    public sealed class ActorAnimationTests
    {
        private const float Pi = 3.14159265358979323846f;

        [Test]
        public void ClipSpeedDrivesTheActorAndTheClipClock()
        {
            ActorAnimationModule module = new ActorAnimationModule();
            DynamicEntity ped = SpawnPed();
            Assert.True(module.Attach(ped, Clump(), MovingClip(1.0f, 2.0f), 1.0f));
            Assert.Equal(1, module.Count);
            Assert.True(module.Contains(ped));
            // The clip covers 2 units per cycle while the actor walks 1 unit per second, so the clip
            // must play at half rate to keep the feet on the ground.
            Assert.Near(2.0f, module.ClipSpeed, 1e-4f);
            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(15);
            Assert.Near(0.25f, module.ClipTime(ped), 1e-3f);
        }

        [Test]
        public void InPlaceClipKeepsTheConfiguredSpeed()
        {
            ActorAnimationModule module = new ActorAnimationModule();
            DynamicEntity ped = SpawnPed();
            Assert.True(module.Attach(ped, Clump(), MovingClip(1.0f, 0.0f), 5.0f));
            Assert.Near(0.0f, module.ClipSpeed, 1e-6f);
            AnimationPose pose;
            Assert.True(module.TryGetPose(ped, out pose));
            Assert.Equal(1, pose.Bones.Length);
        }

        [Test]
        public void TicksAdvanceThePoseAndTheDigest()
        {
            ActorAnimationModule module = new ActorAnimationModule();
            DynamicEntity ped = SpawnPed();
            Assert.True(module.Attach(ped, Clump(), MovingClip(1.0f, 2.0f), 2.0f));
            ulong start = module.PoseDigest();

            SimulationRuntime runtime = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            runtime.AddModule(module);
            runtime.RunTicks(10);
            ulong afterTen = module.PoseDigest();
            Assert.True(afterTen != start);
            runtime.RunTicks(20);
            Assert.True(module.PoseDigest() != afterTen);

            // The pose digest is part of the runtime state, so replays must match exactly.
            SimulationRuntime replay = new SimulationRuntime(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            ActorAnimationModule second = new ActorAnimationModule();
            Assert.True(second.Attach(SpawnPed(), Clump(), MovingClip(1.0f, 2.0f), 2.0f));
            replay.AddModule(second);
            replay.RunTicks(10);
            Assert.Equal(afterTen, second.PoseDigest());
        }

        [Test]
        public void SuggestedSpeedComesFromTheMover()
        {
            Assert.Near(2.0f, ActorAnimationModule.SuggestedSpeed(MovingClip(1.0f, 2.0f)), 1e-4f);
            Assert.Near(0.0f, ActorAnimationModule.SuggestedSpeed(MovingClip(1.0f, 0.0f)), 1e-6f);
            Assert.Throws<ArgumentNullException>(delegate
            {
                ActorAnimationModule.SuggestedSpeed(null);
            });
        }

        [Test]
        public void RejectsDuplicateAttachAndBadInputs()
        {
            ActorAnimationModule module = new ActorAnimationModule();
            DynamicEntity ped = SpawnPed();
            Assert.True(module.Attach(ped, Clump(), MovingClip(1.0f, 2.0f), 1.0f));
            Assert.Throws<InvalidOperationException>(delegate
            {
                module.Attach(ped, Clump(), MovingClip(1.0f, 2.0f), 1.0f);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                module.Attach(null, Clump(), MovingClip(1.0f, 2.0f), 1.0f);
            });
            Assert.Throws<ArgumentNullException>(delegate
            {
                module.Attach(SpawnPed(), null, MovingClip(1.0f, 2.0f), 1.0f);
            });
            Assert.False(module.Release(SpawnPed()));
            Assert.True(module.Release(ped));
            Assert.Equal(0, module.Count);
        }

        private static DynamicEntity SpawnPed()
        {
            ModelRegistry registry = new ModelRegistry(
                new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, plain, generic, 1, 300, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, plain, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1\nend\n"),
                registry, new LoadReport());
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("plain"),
                Vector3.Zero, Vector3.Zero, out entity));
            return entity;
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

        /// <summary>One bone that rotates 90 degrees and slides <paramref name="meters"/> over 1 s.</summary>
        private static IfpAnimation MovingClip(float duration, float meters)
        {
            IfpKeyFrame first = new IfpKeyFrame();
            first.Rotation = Quaternion.Identity;
            first.Translation = Vector3.Zero;
            first.DeltaTime = 0.0f;
            IfpKeyFrame last = new IfpKeyFrame();
            last.Rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            last.Translation = new Vector3(0.0f, meters, 0.0f);
            last.DeltaTime = duration;

            IfpSequence sequence = new IfpSequence();
            sequence.Name = "root";
            sequence.BoneTag = -1;
            sequence.HasTranslation = meters != 0.0f;
            sequence.Frames = new[] { first, last };
            sequence.Duration = duration;

            IfpAnimation animation = new IfpAnimation();
            animation.Name = "walk";
            animation.Sequences = new[] { sequence };
            animation.FrameCount = 2;
            animation.Duration = duration;
            return animation;
        }
    }
}