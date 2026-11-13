using System;
using System.Collections.Generic;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    /// <summary>
    /// Reads GTA III/VC IFP animation packages. The walk follows `CAnimManager::LoadAnimFile`:
    /// `ANLF` wraps several `ANPK` packages, each package holds `INFO` (block name and count),
    /// per animation `NAME` + `DGAN` + `INFO` (sequence count), per sequence `CPAN` + `ANIM`
    /// (node name, frame count, optional bone tag) and one keyframe chunk — `KRTS` with scale,
    /// `KRT0` with translation or `KR00` with rotation only.
    /// </summary>
    public static class IfpReader
    {
        /// <summary>Frame layouts: 4 floats of rotation, then translation, scale and time.</summary>
        private const int FrameRotationOnly = 0x14;
        private const int FrameWithTranslation = 0x20;
        private const int FrameWithScale = 0x2C;

        /// <summary>Biggest accepted input, so a bogus size cannot make the reader allocate.</summary>
        public const int MaximumInputLength = 64 * 1024 * 1024;

        public static IfpDocument Read(byte[] data)
        {
            return Read(data, 0, data == null ? 0 : data.Length);
        }

        public static IfpDocument Read(byte[] data, int offset, int count)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(offset < 0 ? "offset" : "count");
            if (count > MaximumInputLength)
                throw new AssetFormatException("IFP input exceeds the configured size limit.");
            return new Parser(data, offset, count).ReadDocument();
        }

        /// <summary>`ROUNDSIZE` from the original: chunk bodies are padded to four bytes.</summary>
        internal static int RoundUp(int value)
        {
            return (value & 3) == 0 ? value : value + (4 - (value & 3));
        }

        private sealed class Parser
        {
            private readonly byte[] data;
            private readonly int end;
            private int position;

            internal Parser(byte[] data, int offset, int count)
            {
                this.data = data;
                this.position = offset;
                this.end = offset + count;
            }

            internal IfpDocument ReadDocument()
            {
                List<IfpBlock> blocks = new List<IfpBlock>();
                int packages = 1;
                string ident = ReadIdent(position);
                if (ident == "ANLF")
                {
                    int size = ReadSize(position, "ANLF");
                    Ensure(position + 8, 4, "ANLF body");
                    packages = ReadInt32(position + 8);
                    if (packages < 0 || packages > 100000)
                        throw new AssetFormatException("IFP package count is implausible.");
                    position += 8 + RoundUp(size);
                }
                else if (ident != "ANPK")
                {
                    throw new AssetFormatException("IFP does not start with ANLF or ANPK.");
                }

                for (int i = 0; i < packages; i++)
                    blocks.Add(ReadBlock());
                if (blocks.Count == 0)
                    throw new AssetFormatException("IFP contains no animation packages.");
                IfpDocument document = new IfpDocument();
                document.Blocks = blocks.ToArray();
                return document;
            }

            /// <summary>`ANPK` header, then `INFO` (count + block name), then the animations.</summary>
            private IfpBlock ReadBlock()
            {
                Expect("ANPK", "package");
                int blockSize = ReadSize(position, "ANPK");
                Ensure(position + 8, 4, "ANPK header");
                position += 8;

                Expect("INFO", "package info");
                int infoSize = ReadSize(position, "INFO");
                Ensure(position + 8, infoSize, "package info body");
                int count = ReadInt32(position + 8);
                string name = ReadName(position + 12);
                position += 8 + RoundUp(infoSize);
                if (count < 0 || count > 100000)
                    throw new AssetFormatException("IFP animation count is implausible.");
                // The ANPK body is only informational here: children are walked in order, exactly
                // like the original, so a wrong size cannot desynchronise the parse.
                if (blockSize < 0)
                    throw new AssetFormatException("IFP package size is negative.");

                List<IfpAnimation> animations = new List<IfpAnimation>(count);
                for (int i = 0; i < count; i++)
                    animations.Add(ReadAnimation());
                IfpBlock block = new IfpBlock();
                block.Name = name;
                block.Animations = animations.ToArray();
                return block;
            }

            /// <summary>`NAME`, `DGAN`, `INFO` (sequence count) and then the sequences.</summary>
            private IfpAnimation ReadAnimation()
            {
                Expect("NAME", "animation name");
                int nameSize = ReadSize(position, "NAME");
                Ensure(position + 8, nameSize, "animation name body");
                string name = ReadName(position + 8);
                position += 8 + RoundUp(nameSize);

                // DGAN only announces the sequence list; its body is the INFO chunk that follows.
                Expect("DGAN", "animation node count");
                ReadSize(position, "DGAN");
                position += 8;

                Expect("INFO", "animation info");
                int infoSize = ReadSize(position, "INFO");
                Ensure(position + 8, infoSize, "animation info body");
                int count = ReadInt32(position + 8);
                position += 8 + RoundUp(infoSize);
                if (count < 0 || count > 100000)
                    throw new AssetFormatException("IFP sequence count is implausible.");

                List<IfpSequence> sequences = new List<IfpSequence>(count);
                int frames = 0;
                float duration = 0.0f;
                for (int i = 0; i < count; i++)
                {
                    IfpSequence sequence = ReadSequence();
                    sequences.Add(sequence);
                    if (sequence.Frames.Length > frames)
                        frames = sequence.Frames.Length;
                    if (sequence.Duration > duration)
                        duration = sequence.Duration;
                }
                IfpAnimation animation = new IfpAnimation();
                animation.Name = name;
                animation.Sequences = sequences.ToArray();
                animation.FrameCount = frames;
                animation.Duration = duration;
                return animation;
            }

            /// <summary>`CPAN`, then `ANIM` (node name, frame count, optional bone tag).</summary>
            private IfpSequence ReadSequence()
            {
                Expect("CPAN", "sequence");
                ReadSize(position, "CPAN");
                position += 8;

                Expect("ANIM", "sequence header");
                int size = ReadSize(position, "ANIM");
                Ensure(position + 8, size, "sequence header body");
                string name = ReadName(position + 8);
                Ensure(position + 8 + 28, 4, "sequence frame count");
                int frames = ReadInt32(position + 8 + 28);
                int boneTag = -1;
                if (size == 44)
                {
                    Ensure(position + 8 + 40, 4, "sequence bone tag");
                    boneTag = ReadInt32(position + 8 + 40);
                }
                position += 8 + RoundUp(size);
                if (frames < 0 || frames > 1000000)
                    throw new AssetFormatException("IFP frame count is implausible.");

                IfpSequence sequence = new IfpSequence();
                sequence.Name = name;
                sequence.BoneTag = boneTag;
                sequence.Frames = new IfpKeyFrame[0];
                sequence.Duration = 0.0f;
                if (frames == 0)
                    return sequence;

                string chunk = ReadIdent(position);
                int frameSize;
                if (chunk == "KRTS")
                {
                    sequence.HasScale = true;
                    frameSize = FrameWithScale;
                }
                else if (chunk == "KRT0")
                {
                    sequence.HasTranslation = true;
                    frameSize = FrameWithTranslation;
                }
                else if (chunk == "KR00")
                {
                    frameSize = FrameRotationOnly;
                }
                else
                {
                    throw new AssetFormatException("IFP keyframe chunk '" + chunk + "' is unknown.");
                }

                int chunkSize = ReadSize(position, chunk);
                Ensure(position + 8, frames * frameSize, "keyframe data");
                int cursor = position + 8;
                IfpKeyFrame[] keyFrames = new IfpKeyFrame[frames];
                float duration = 0.0f;
                for (int i = 0; i < frames; i++)
                {
                    if (cursor + frameSize > end)
                        throw new AssetFormatException("IFP keyframe data is truncated.");
                    float x = ReadSingle(cursor);
                    float y = ReadSingle(cursor + 4);
                    float z = ReadSingle(cursor + 8);
                    float w = ReadSingle(cursor + 12);
                    // The original stores the inverse rotation in the file.
                    IfpKeyFrame frame = new IfpKeyFrame();
                    frame.Rotation = new Quaternion(-x, -y, -z, w);
                    if (frameSize == FrameWithTranslation)
                    {
                        frame.Translation = new Vector3(ReadSingle(cursor + 16),
                            ReadSingle(cursor + 20), ReadSingle(cursor + 24));
                        frame.DeltaTime = ReadSingle(cursor + 28);
                    }
                    else if (frameSize == FrameWithScale)
                    {
                        frame.Translation = new Vector3(ReadSingle(cursor + 16),
                            ReadSingle(cursor + 20), ReadSingle(cursor + 24));
                        // Scaling is ignored, as in the original.
                        frame.DeltaTime = ReadSingle(cursor + 40);
                    }
                    else
                    {
                        frame.DeltaTime = ReadSingle(cursor + 16);
                    }
                    keyFrames[i] = frame;
                    cursor += frameSize;
                }

                // Absolute times in the file become per-frame deltas; frame 0 keeps its start.
                for (int i = frames - 1; i > 0; i--)
                    keyFrames[i].DeltaTime -= keyFrames[i - 1].DeltaTime;
                for (int i = 1; i < frames; i++)
                    duration += keyFrames[i].DeltaTime;

                sequence.Frames = keyFrames;
                sequence.Duration = duration;
                RemoveQuaternionFlips(keyFrames);
                // Trust the frame count over the chunk size, like the original, but honour a
                // plausible declared size so padding cannot desynchronise the next chunk.
                int declaredEnd = position + 8 + RoundUp(chunkSize);
                position = declaredEnd >= cursor && declaredEnd <= end ? declaredEnd : cursor;
                return sequence;
            }

            /// <summary>`CAnimBlendSequence::RemoveQuaternionFlips`: keep consecutive rotations on
            /// the same hemisphere so interpolation takes the short way round.</summary>
            private static void RemoveQuaternionFlips(IfpKeyFrame[] frames)
            {
                if (frames.Length < 2)
                    return;
                Quaternion last = frames[0].Rotation;
                for (int i = 1; i < frames.Length; i++)
                {
                    Quaternion current = frames[i].Rotation;
                    float dot = last.X * current.X + last.Y * current.Y +
                        last.Z * current.Z + last.W * current.W;
                    if (dot < 0.0f)
                    {
                        current = new Quaternion(-current.X, -current.Y, -current.Z, -current.W);
                        frames[i].Rotation = current;
                    }
                    last = current;
                }
            }

            private void Expect(string ident, string what)
            {
                Ensure(position, 8, what + " header");
                string actual = ReadIdent(position);
                if (actual != ident)
                {
                    throw new AssetFormatException("IFP expected " + ident + " for the " + what +
                        ", found '" + actual + "'.");
                }
            }

            private int ReadSize(int at, string ident)
            {
                Ensure(at, 8, ident + " header");
                int size = ReadInt32(at + 4);
                if (size < 0)
                    throw new AssetFormatException("IFP " + ident + " size is negative.");
                // No chunk can be larger than the whole input, so a bogus size cannot overflow the
                // position arithmetic below or send the parser past the buffer.
                if (size > end)
                    throw new AssetFormatException("IFP " + ident + " size exceeds the input length.");
                return size;
            }

            private string ReadIdent(int at)
            {
                Ensure(at, 4, "chunk id");
                return new string(new char[]
                {
                    (char)data[at], (char)data[at + 1], (char)data[at + 2], (char)data[at + 3]
                });
            }

            /// <summary>Names are fixed 24-byte, NUL-padded ASCII.</summary>
            private string ReadName(int at)
            {
                Ensure(at, 24, "name");
                int length = 0;
                while (length < 24 && data[at + length] != 0)
                    length++;
                char[] chars = new char[length];
                for (int i = 0; i < length; i++)
                    chars[i] = (char)data[at + i];
                return new string(chars);
            }

            private int ReadInt32(int at)
            {
                Ensure(at, 4, "int32");
                return data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
            }

            private float ReadSingle(int at)
            {
                Ensure(at, 4, "float");
                int bits = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
                return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            }

            private void Ensure(int at, int length, string what)
            {
                if (at < 0 || length < 0 || at > end - length)
                    throw new AssetFormatException("IFP " + what + " is truncated.");
            }
        }
    }
}