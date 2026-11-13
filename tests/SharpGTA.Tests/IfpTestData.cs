using System;
using System.Collections.Generic;

namespace SharpGTA.Tests
{
    /// <summary>
    /// Builds synthetic IFP bytes for the reader tests. The layout mirrors what the original loader
    /// expects: `ANLF` keeps only the package count in its body and then sibling `ANPK` chunks
    /// follow, so `ANLF`'s size is four bytes, not the whole container.
    /// </summary>
    internal static class IfpTestData
    {
        internal static byte[] Store(params byte[][] packages)
        {
            List<byte> output = new List<byte>();
            output.AddRange(System.Text.Encoding.ASCII.GetBytes("ANLF"));
            WriteInt32(output, 4);
            WriteInt32(output, packages.Length);
            for (int i = 0; i < packages.Length; i++)
                output.AddRange(packages[i]);
            return output.ToArray();
        }

        internal static byte[] Package(string name, params byte[][] animations)
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

        internal static byte[] Animation(string name, params byte[][] sequences)
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
        internal static byte[] Sequence(string name, int boneTag, string keyframeChunk,
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

        internal static byte[] Frame(float x, float y, float z, float w, float time)
        {
            List<byte> frame = new List<byte>();
            WriteSingle(frame, x);
            WriteSingle(frame, y);
            WriteSingle(frame, z);
            WriteSingle(frame, w);
            WriteSingle(frame, time);
            return frame.ToArray();
        }

        internal static byte[] TranslationFrame(float tx, float ty, float tz,
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

        internal static byte[] ScaleFrame(float x, float y, float z, float w,
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

        internal static byte[] Chunk(string ident, byte[] payload)
        {
            List<byte> output = new List<byte>();
            output.AddRange(System.Text.Encoding.ASCII.GetBytes(ident));
            WriteInt32(output, payload.Length);
            output.AddRange(payload);
            while ((output.Count & 3) != 0)
                output.Add(0);
            return output.ToArray();
        }

        internal static byte[] Name24(string name)
        {
            byte[] bytes = new byte[24];
            for (int i = 0; i < name.Length && i < 24; i++)
                bytes[i] = (byte)name[i];
            return bytes;
        }

        internal static void WriteInto(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        internal static void WriteInt32(List<byte> output, int value)
        {
            output.Add((byte)value);
            output.Add((byte)(value >> 8));
            output.Add((byte)(value >> 16));
            output.Add((byte)(value >> 24));
        }

        internal static void WriteSingle(List<byte> output, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            output.AddRange(bytes);
        }
    }
}