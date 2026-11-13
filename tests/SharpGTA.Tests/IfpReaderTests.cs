using System;
using System.Collections.Generic;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Tests
{
    public sealed class IfpReaderTests
    {
        [Test]
        public void ReadsARotationOnlyHierarchy()
        {
            // One package, one animation, one sequence, three rotation-only frames.
            byte[] data = Store(
                Package("ped",
                    Animation("walk",
                        Sequence("root", -1, "KR00",
                            Frame(0.1f, 0.2f, 0.3f, 0.9f, 0.0f),
                            Frame(0.2f, 0.1f, 0.0f, 0.9f, 0.5f),
                            Frame(0.0f, 0.0f, 0.0f, 1.0f, 1.0f)))));

            IfpDocument document = IfpReader.Read(data);
            Assert.Equal(1, document.Blocks.Length);
            Assert.Equal(1, document.AnimationCount);
            IfpBlock block = document.Blocks[0];
            Assert.Equal("ped", block.Name);
            IfpAnimation animation = block.Animations[0];
            Assert.Equal("walk", animation.Name);
            Assert.Equal(3, animation.FrameCount);
            Assert.Equal(1, animation.Sequences.Length);

            IfpSequence sequence = animation.Sequences[0];
            Assert.Equal("root", sequence.Name);
            Assert.Equal(-1, sequence.BoneTag);
            Assert.False(sequence.HasTranslation);
            Assert.False(sequence.HasScale);
            Assert.Equal(3, sequence.Frames.Length);
            // The file stores the inverse rotation, so x/y/z are flipped and w is kept.
            Assert.Near(-0.1f, sequence.Frames[0].Rotation.X);
            Assert.Near(-0.2f, sequence.Frames[0].Rotation.Y);
            Assert.Near(-0.3f, sequence.Frames[0].Rotation.Z);
            Assert.Near(0.9f, sequence.Frames[0].Rotation.W);
            // Absolute times become deltas, and the duration is their sum.
            Assert.Near(0.0f, sequence.Frames[0].DeltaTime);
            Assert.Near(0.5f, sequence.Frames[1].DeltaTime);
            Assert.Near(0.5f, sequence.Frames[2].DeltaTime);
            Assert.Near(1.0f, sequence.Duration);
            Assert.Near(1.0f, animation.Duration);
        }

        [Test]
        public void ReadsTranslationScaleAndBoneTags()
        {
            byte[] data = Store(
                Package("ped",
                    Animation("jump",
                        Sequence("root", 2, "KRT0",
                            TranslationFrame(1, 2, 3, 0.1f, 0.2f, 0.3f, 0.9f, 0.0f),
                            TranslationFrame(2, 3, 4, 0.0f, 0.0f, 0.0f, 1.0f, 0.25f)),
                        Sequence("head", -1, "KRTS",
                            ScaleFrame(0.5f, 0.5f, 0.0f, 0.9f, 0.0f, 0.0f, 0.0f, 4.0f, 4.0f, 4.0f, 0.0f)))));

            IfpDocument document = IfpReader.Read(data);
            IfpAnimation animation = document.Blocks[0].Animations[0];
            Assert.Equal(2, animation.Sequences.Length);
            Assert.Equal(2, animation.FrameCount);

            IfpSequence translated = animation.Sequences[0];
            Assert.Equal(2, translated.BoneTag);
            Assert.True(translated.HasTranslation);
            Assert.Near(1.0f, translated.Frames[0].Translation.X);
            Assert.Near(3.0f, translated.Frames[0].Translation.Z);
            Assert.Near(0.25f, translated.Frames[1].DeltaTime);
            Assert.Near(0.25f, translated.Duration);

            // Scale is stored but ignored, as in the original loader.
            IfpSequence scaled = animation.Sequences[1];
            Assert.True(scaled.HasScale);
            Assert.Equal(-1, scaled.BoneTag);
            Assert.Near(0.0f, scaled.Duration);
        }

        [Test]
        public void RemovesQuaternionFlipsBetweenFrames()
        {
            byte[] data = Store(
                Package("test",
                    Animation("flip",
                        Sequence("root", -1, "KR00",
                            Frame(0.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                            // Same orientation on the other hemisphere: the loader must flip it.
                            Frame(0.0f, 0.0f, 0.0f, -1.0f, 0.5f)))));

            IfpSequence sequence = IfpReader.Read(data).Blocks[0].Animations[0].Sequences[0];
            Assert.Near(1.0f, sequence.Frames[0].Rotation.W);
            Assert.Near(1.0f, sequence.Frames[1].Rotation.W);
        }

        [Test]
        public void RejectsMalformedInput()
        {
            Assert.Throws<ArgumentNullException>(delegate { IfpReader.Read(null); });
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(new byte[] { 1, 2, 3, 4 });
            });
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(System.Text.Encoding.ASCII.GetBytes("NOPE000000000000"));
            });
            // A bogus chunk size must be rejected before position arithmetic can overflow.
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(new byte[]
                {
                    (byte)'A', (byte)'N', (byte)'P', (byte)'K',
                    255, 255, 255, 127,
                    (byte)'I', (byte)'N', (byte)'F', (byte)'O',
                    4, 0, 0, 0
                });
            });
            // A truncated keyframe body must not be read past the end.
            byte[] truncated = Store(
                Package("ped",
                    Animation("cut",
                        Sequence("root", -1, "KR00",
                            Frame(0.0f, 0.0f, 0.0f, 1.0f, 0.0f),
                            Frame(0.0f, 0.0f, 0.0f, 1.0f, 1.0f)))));
            Assert.Throws<AssetFormatException>(delegate
            {
                IfpReader.Read(truncated, 0, truncated.Length - 24);
            });
        }

        /// <summary>Wraps packages in an `ANLF` container. The original reads the package count as the
        /// whole `ANLF` body and then walks the `ANPK` chunks as siblings.</summary>
        private static byte[] Store(params byte[][] packages)
        {
            List<byte> output = new List<byte>();
            output.AddRange(System.Text.Encoding.ASCII.GetBytes("ANLF"));
            WriteInt32(output, 4);
            WriteInt32(output, packages.Length);
            for (int i = 0; i < packages.Length; i++)
                output.AddRange(packages[i]);
            return output.ToArray();
        }

        private static byte[] Package(string name, params byte[][] animations)
        {
            List<byte> info = new List<byte>();
            WriteInt32(info, animations.Length);
            info.AddRange(Name24(name));
            List<byte> body = new List<byte>();
            body.AddRange(Chunk("INFO", info.ToArray()));
            for (int i = 0; i < animations.Length; i++)
                body.AddRange(animations[i]);
            // ANPK keeps no payload of its own: the block name and count live in the INFO child.
            return Chunk("ANPK", body.ToArray());
        }

        private static byte[] Animation(string name, params byte[][] sequences)
        {
            List<byte> body = new List<byte>();
            body.AddRange(Chunk("NAME", Name24(name)));
            body.AddRange(Chunk("DGAN", new byte[0]));
            List<byte> info = new List<byte>();
            WriteInt32(info, sequences.Length);
            body.AddRange(Chunk("INFO", info.ToArray()));
            for (int i = 0; i < sequences.Length; i++)
                body.AddRange(sequences[i]);
            // Animations are children of the package body, so they are not wrapped again.
            return body.ToArray();
        }

        /// <summary>`CPAN` + `ANIM` header, optionally with a bone tag, then the keyframe chunk.</summary>
        private static byte[] Sequence(string name, int boneTag, string keyframeChunk,
            params byte[][] frames)
        {
            byte[] anim = new byte[boneTag >= 0 ? 44 : 32];
            byte[] nameBytes = Name24(name);
            Array.Copy(nameBytes, anim, 24);
            // Offset 24..27 stays zero; the frame count sits at 28 and the bone tag at 40.
            WriteInto(anim, 28, frames.Length);
            if (boneTag >= 0)
                WriteInto(anim, 40, boneTag);

            List<byte> body = new List<byte>();
            body.AddRange(Chunk("CPAN", new byte[0]));
            body.AddRange(Chunk("ANIM", anim));
            List<byte> keyData = new List<byte>();
            for (int i = 0; i < frames.Length; i++)
                keyData.AddRange(frames[i]);
            body.AddRange(Chunk(keyframeChunk, keyData.ToArray()));
            return body.ToArray();
        }

        private static byte[] Frame(float x, float y, float z, float w, float time)
        {
            List<byte> frame = new List<byte>();
            WriteSingle(frame, x);
            WriteSingle(frame, y);
            WriteSingle(frame, z);
            WriteSingle(frame, w);
            WriteSingle(frame, time);
            return frame.ToArray();
        }

        private static byte[] TranslationFrame(float tx, float ty, float tz,
            float x, float y, float z, float w, float time)
        {
            List<byte> frame = new List<byte>();
            WriteSingle(frame, x);
            WriteSingle(frame, y);
            WriteSingle(frame, z);
            WriteSingle(frame, w);
            WriteSingle(frame, tx);
            WriteSingle(frame, ty);
            WriteSingle(frame, tz);
            WriteSingle(frame, time);
            return frame.ToArray();
        }

        private static byte[] ScaleFrame(float x, float y, float z, float w,
            float tx, float ty, float tz, float sx, float sy, float sz, float time)
        {
            List<byte> frame = new List<byte>();
            WriteSingle(frame, x);
            WriteSingle(frame, y);
            WriteSingle(frame, z);
            WriteSingle(frame, w);
            WriteSingle(frame, tx);
            WriteSingle(frame, ty);
            WriteSingle(frame, tz);
            WriteSingle(frame, sx);
            WriteSingle(frame, sy);
            WriteSingle(frame, sz);
            WriteSingle(frame, time);
            return frame.ToArray();
        }

        private static byte[] Chunk(string ident, byte[] payload)
        {
            List<byte> output = new List<byte>();
            output.AddRange(System.Text.Encoding.ASCII.GetBytes(ident));
            WriteInt32(output, payload.Length);
            output.AddRange(payload);
            while ((output.Count & 3) != 0)
                output.Add(0);
            return output.ToArray();
        }

        private static byte[] Name24(string name)
        {
            byte[] bytes = new byte[24];
            for (int i = 0; i < name.Length && i < 24; i++)
                bytes[i] = (byte)name[i];
            return bytes;
        }

        private static void WriteInto(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt32(List<byte> output, int value)
        {
            output.Add((byte)value);
            output.Add((byte)(value >> 8));
            output.Add((byte)(value >> 16));
            output.Add((byte)(value >> 24));
        }

        private static void WriteSingle(List<byte> output, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            output.AddRange(bytes);
        }
    }
}