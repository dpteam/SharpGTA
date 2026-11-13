using System;
using SharpGTA.Assets;
using SharpGTA.Collision;
using SharpGTA.Data;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;
using SharpGTA.World;

namespace SharpGTA.Tests
{
    /// <summary>
    /// Character movement with walls: the first player-shaped motion in the engine. A chest-high
    /// sweep must not clip COLL buildings, and the feet must follow the ground query.
    /// </summary>
    public sealed class CharacterMoveTests
    {
        [Test]
        public void WalksForwardOnFlatGround()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry, false);
            DynamicEntity ped = SpawnPed(world, registry, Vector3.Zero);
            CharacterMoveModule module = new CharacterMoveModule(world, ped);
            module.Speed = 2.0f;

            SimulationRuntime runtime = Runtime(Forward(1.0f));
            runtime.AddModule(module);
            runtime.RunTicks(60);
            // Two seconds at two units per second.
            Assert.Near(4.0f, ped.Position.X, 0.05f);
            Assert.Near(0.0f, ped.Position.Z, 0.01f);
            Assert.Equal(0, module.CollisionHitCount);
            Assert.Near(4.0f, module.TravelledDistance, 0.05f);
        }

        [Test]
        public void StopsAtAWallInsteadOfWalkingThroughIt()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry, true);
            DynamicEntity ped = SpawnPed(world, registry, Vector3.Zero);
            CharacterMoveModule module = new CharacterMoveModule(world, ped);
            module.Speed = 3.0f;

            SimulationRuntime runtime = Runtime(Forward(1.0f));
            runtime.AddModule(module);
            runtime.RunTicks(300000 / 10000); // four seconds: ten units of desired travel
            runtime.RunTicks(120);

            // The wall box spans x = 5..7, so the body stops one radius short of it.
            Assert.True(ped.Position.X < 5.0f, "The character walked into the wall.");
            Assert.Near(5.0f - module.Radius, ped.Position.X, 0.2f);
            Assert.True(module.CollisionHitCount > 0);
            Assert.Near(0.0f, ped.Position.Z, 0.01f);
        }

        [Test]
        public void SlidesAlongAWallDiagonalToTheMovement()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry, true);
            DynamicEntity ped = SpawnPed(world, registry, Vector3.Zero);
            CharacterMoveModule module = new CharacterMoveModule(world, ped);
            module.Speed = 3.0f;
            // Diagonal input: the wall must stop X but let the character slide along Y.
            SimulationRuntime runtime = Runtime(new DiagonalInput());
            runtime.AddModule(module);
            runtime.RunTicks(180);

            Assert.True(ped.Position.X < 5.0f, "The character walked into the wall.");
            Assert.True(ped.Position.Y > 1.0f, "The character did not slide along the wall.");
            Assert.True(module.CollisionHitCount > 0);
        }

        [Test]
        public void ZeroInputKeepsTheCharacterInPlace()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry, false);
            DynamicEntity ped = SpawnPed(world, registry, new Vector3(2.0f, 3.0f, 0.0f));
            CharacterMoveModule module = new CharacterMoveModule(world, ped);

            SimulationRuntime runtime = Runtime(new ForwardInput(0.0f, 0.0f));
            runtime.AddModule(module);
            runtime.RunTicks(60);
            Assert.Near(2.0f, ped.Position.X, 0.001f);
            Assert.Near(3.0f, ped.Position.Y, 0.001f);
            Assert.Near(0.0f, ped.Position.Z, 0.001f);
            Assert.Equal(0, module.CollisionHitCount);
        }

        [Test]
        public void MovementIsDeterministic()
        {
            string first = RunDiagonal();
            string second = RunDiagonal();
            Assert.Equal(first, second);
        }

        private static string RunDiagonal()
        {
            ModelRegistry registry;
            StaticWorld world = CreateWorld(out registry, true);
            DynamicEntity ped = SpawnPed(world, registry, Vector3.Zero);
            CharacterMoveModule module = new CharacterMoveModule(world, ped);
            module.Speed = 3.0f;
            SimulationRuntime runtime = Runtime(new DiagonalInput());
            runtime.AddModule(module);
            runtime.RunTicks(200);
            return "hits=" + module.CollisionHitCount + " x=" +
                ped.Position.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " y=" + ped.Position.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " hash=" + runtime.ComputeStateHash();
        }

        private static SimulationRuntime Runtime(IInputSource input)
        {
            return new SimulationRuntime(new WorldTestDefinition(GameId.III, DataSchema.III, 32),
                input, null, null);
        }

        private static IInputSource Forward(float amount)
        {
            return new ForwardInput(0.0f, amount);
        }

        private static StaticWorld CreateWorld(out ModelRegistry registry, bool withWall)
        {
            registry = new ModelRegistry(new WorldTestDefinition(GameId.III, DataSchema.III, 32));
            CollisionRepository collisions = new CollisionRepository(registry);
            collisions.Add(WorldTestAssets.CreateBoxCollision("ground", 20.0f, -1.0f, 0.0f), 0);
            if (withWall)
            {
                // Chest-high wall at x = 6, two units thick, forty long: long enough that a
                // diagonal walker cannot round its end and escape the test.
                collisions.Add(CreateWallCollision("wall", 1.0f, 20.0f, -1.0f, 4.0f), 0);
            }
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "objs\n1, ground, generic, 1, 100, 0\nend\n" +
                "objs\n10, wall, generic, 1, 100, 0\nend\n"));
            StaticWorld world = new StaticWorld();
            world.Bind(new IplReader(DataSchema.III).Read(
                "inst\n1, ground, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1\n" +
                "10, wall, 6, 0, 0, 1, 1, 1, 0, 0, 1, 1\nend\n"),
                registry, new LoadReport());
            return world;
        }

        /// <summary>One COLL file with a single box model; X and Y extents differ.</summary>
        private static byte[] CreateWallCollision(string modelName, float halfX, float halfY,
            float minimumZ, float maximumZ)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream);
                writer.Write(0x4C4C4F43u);
                writer.Write(0u);
                byte[] name = new byte[24];
                for (int i = 0; i < modelName.Length && i < name.Length; i++)
                    name[i] = (byte)modelName[i];
                writer.Write(name);
                Vector3 maximum = new Vector3(halfX, halfY, maximumZ);
                Vector3 minimum = new Vector3(-halfX, -halfY, minimumZ);
                float radius = maximum.Length();
                writer.Write(radius);
                WriteVector(writer, Vector3.Zero);
                WriteVector(writer, minimum);
                WriteVector(writer, maximum);
                writer.Write(0);
                writer.Write(0);
                writer.Write(1);
                WriteVector(writer, minimum);
                WriteVector(writer, maximum);
                writer.Write((byte)17);
                writer.Write((byte)3);
                writer.Write((ushort)0);
                writer.Write(0);
                writer.Write(0);
                long end = stream.Position;
                stream.Position = 4;
                writer.Write((uint)(end - 8));
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteVector(System.IO.BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private static DynamicEntity SpawnPed(StaticWorld world, ModelRegistry registry,
            Vector3 position)
        {
            registry.Bind(new IdeReader(DataSchema.III).Read(
                "peds\n7, civmale, civmale, CIVMALE, STAT_PLAYER, ped, 0\nend\n"));
            DynamicEntity entity;
            Assert.True(world.TrySpawnEntity(WorldEntityKind.Ped, registry.Find("civmale"),
                position, Vector3.Zero, out entity));
            return entity;
        }

        /// <summary>Scripted input: `MoveX` strafes, `MoveY` walks forward.</summary>
        private sealed class ForwardInput : IInputSource
        {
            private readonly float strafe;
            private readonly float forward;

            internal ForwardInput(float strafe, float forward)
            {
                this.strafe = strafe;
                this.forward = forward;
            }

            public InputFrame ReadInput(ulong tick)
            {
                InputFrame frame = new InputFrame();
                frame.MoveX = strafe;
                frame.MoveY = forward;
                return frame;
            }
        }

        private sealed class DiagonalInput : IInputSource
        {
            public InputFrame ReadInput(ulong tick)
            {
                InputFrame frame = new InputFrame();
                frame.MoveX = 1.0f;
                frame.MoveY = 1.0f;
                return frame;
            }
        }
    }
}