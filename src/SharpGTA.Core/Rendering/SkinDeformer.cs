using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Rendering
{
    /// <summary>
    /// A skin's bone-to-frame map, resolved once from the bind pose. Skin bone `i` is hierarchy node
    /// `i`, but a DFF stores node ids only in the HAnim plugin of the hierarchy parent frame and the
    /// VC ped frame list is not in node order, so the mapping is read back from the bind matrices
    /// themselves: `inverse(inverseMatrix[b])` is bone b's bind transform, i.e. exactly one frame's
    /// bind transform. Ambiguity or a missing match throws instead of guessing.
    /// </summary>
    public sealed class SkinBinding
    {
        public DffClump Clump { get; private set; }
        public DffSkin Skin { get; private set; }
        public int[] BoneFrames { get; private set; }
        public int HierarchyParent { get; private set; }

        public static SkinBinding Bind(DffClump clump, DffSkin skin)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (skin == null)
                throw new ArgumentNullException("skin");
            if (clump.Frames == null || clump.Frames.Length == 0)
                throw new AssetFormatException("DFF clump has no frame array.");
            if (skin.NumBones <= 0)
                throw new AssetFormatException("DFF skin has no bones.");

            SkinBinding binding = new SkinBinding();
            binding.Clump = clump;
            binding.Skin = skin;
            binding.HierarchyParent = SkinDeformer.FindHierarchyParent(clump, skin);
            Matrix[] rest = SkinDeformer.ResolveRestWorld(clump);
            Matrix[] relative = SkinDeformer.RelativeTo(rest, binding.HierarchyParent);
            binding.BoneFrames = skin.IsLegacyAtomic
                ? LegacyBoneFrames(skin, relative.Length)
                : SkinDeformer.MatchBoneFrames(skin, relative);
            return binding;
        }

        /// <summary>Bone matrices in skin bone order for any resolved skeleton pose.</summary>
        public Matrix[] ComputeBoneMatrices(Matrix[] frameWorld)
        {
            if (frameWorld == null)
                throw new ArgumentNullException("frameWorld");
            if (frameWorld.Length != Clump.Frames.Length)
                throw new ArgumentException("Frame transforms do not cover the clump.", "frameWorld");
            Matrix[] skeleton = SkinDeformer.RelativeTo(frameWorld, HierarchyParent);
            Matrix[] bones = new Matrix[Skin.NumBones];
            for (int i = 0; i < bones.Length; i++)
            {
                int frame = BoneFrames[i];
                if (frame < 0 || frame >= skeleton.Length)
                    throw new AssetFormatException("Skin bone refers to a frame outside the clump.");
                bones[i] = DffFrameTransforms.Concatenate(
                    SkinDeformer.InverseBindMatrix(Skin, i), skeleton[frame]);
            }
            return bones;
        }

        private static int[] LegacyBoneFrames(DffSkin skin, int frameCount)
        {
            if (skin.LegacyBones == null || skin.LegacyBones.Length < skin.NumBones)
                throw new AssetFormatException("Legacy Atomic Skin bone table is truncated.");
            int[] frames = new int[skin.NumBones];
            for (int i = 0; i < frames.Length; i++)
            {
                if (skin.LegacyBones[i] == null)
                    throw new AssetFormatException("Legacy Atomic Skin bone table is truncated.");
                frames[i] = skin.LegacyBones[i].Index;
                if (frames[i] < 0 || frames[i] >= frameCount)
                    throw new AssetFormatException("Legacy Atomic Skin bone is outside the clump.");
            }
            return frames;
        }
    }

    public static class SkinDeformer
    {
        /// <summary>Weights per vertex, as the skin plugin stores them.</summary>
        public const int WeightsPerVertex = 4;

        /// <summary>Bone matrices in skin bone order from the resolved skeleton.</summary>
        public static Matrix[] ComputeBoneMatrices(DffClump clump, DffSkin skin, Matrix[] frameWorld)
        {
            return SkinBinding.Bind(clump, skin).ComputeBoneMatrices(frameWorld);
        }

        /// <summary>Rest-pose world matrices of every clump frame.</summary>
        public static Matrix[] ResolveRestWorld(DffClump clump)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (clump.Frames == null)
                throw new AssetFormatException("DFF clump has no frame array.");
            Matrix[] world = new Matrix[clump.Frames.Length];
            byte[] states = new byte[clump.Frames.Length];
            for (int i = 0; i < world.Length; i++)
            {
                if (clump.Frames[i] == null)
                    throw new AssetFormatException("DFF frame is null.");
                ResolveRestFrame(clump.Frames, world, states, i);
            }
            return world;
        }

        private static Matrix ResolveRestFrame(DffFrame[] frames, Matrix[] world, byte[] states, int index)
        {
            if (states[index] == 1)
                return world[index];
            if (states[index] == 2)
                throw new AssetFormatException("Cycle in DFF frame hierarchy.");
            states[index] = 2;
            Matrix result = frames[index].LocalTransform;
            int parent = frames[index].ParentIndex;
            if (parent < -1)
                throw new AssetFormatException("Frame parent must be -1 or refer to another frame.");
            if (parent >= 0)
            {
                if (parent >= frames.Length)
                    throw new AssetFormatException("Frame parent is outside the frame list.");
                result = DffFrameTransforms.Concatenate(result,
                    ResolveRestFrame(frames, world, states, parent));
            }
            world[index] = result;
            states[index] = 1;
            return result;
        }

        /// <summary>
        /// Resolves which frame each skin bone drives. Skin bone `i` is hierarchy node `i`, but the
        /// DFF only stores node ids in the HAnim plugin of the hierarchy parent frame — there is no
        /// per-frame node index — and the frame list of the VC peds is not in node order. The bind
        /// matrices resolve it unambiguously: `inverse(inverseMatrix[b])` is bone b's bind transform,
        /// so bone b is the unused frame whose bind transform equals it. Anything ambiguous or
        /// unmatched throws instead of guessing, and the headless `bindDelta` check re-verifies the
        /// result by skinning the rest pose back onto the stored vertices.
        /// </summary>
        /// <summary>
        /// The bind transform stored for a bone. Version 34003+ (`IsOldFormat == false`) stores the
        /// inverse bind matrix; older skins store the bind matrix itself, which is why librw inverts
        /// after reading a legacy skin.
        /// </summary>
        public static Matrix BindMatrix(DffSkin skin, int bone)
        {
            if (skin == null)
                throw new ArgumentNullException("skin");
            if (bone < 0 || bone >= skin.NumBones)
                throw new ArgumentOutOfRangeException("bone");
            Matrix stored = ReadMatrix(skin.InverseMatrices, bone * 16);
            return skin.IsOldFormat ? stored : InvertAffine(stored);
        }

        /// <summary>What skinning needs: model space to bone space.</summary>
        public static Matrix InverseBindMatrix(DffSkin skin, int bone)
        {
            if (skin == null)
                throw new ArgumentNullException("skin");
            if (bone < 0 || bone >= skin.NumBones)
                throw new ArgumentOutOfRangeException("bone");
            Matrix stored = ReadMatrix(skin.InverseMatrices, bone * 16);
            return skin.IsOldFormat ? InvertAffine(stored) : stored;
        }

        public static int[] MatchBoneFrames(DffSkin skin, Matrix[] skeleton)
        {
            if (skin == null)
                throw new ArgumentNullException("skin");
            if (skeleton == null)
                throw new ArgumentNullException("skeleton");
            if (skin.NumBones <= 0)
                throw new AssetFormatException("DFF skin has no bones.");
            if (skin.InverseMatrices == null || skin.InverseMatrices.Length < skin.NumBones * 16)
                throw new AssetFormatException("DFF skin inverse matrices are truncated.");

            // Old-format VC skins keep slightly different bone offsets in the frames than in the bind
            // matrices (about 2 cm on a 1.8 m ped), so the match allows that much; a wrong bone would
            // be an order of magnitude further away. `bindDelta` in the headless report states the
            // residual instead of hiding it.
            const float tolerance = 0.05f;
            int[] frames = new int[skin.NumBones];
            for (int bone = 0; bone < frames.Length; bone++)
                frames[bone] = -1;
            bool[] usedFrame = new bool[skeleton.Length];
            bool[] usedBone = new bool[skin.NumBones];

            // Assign the globally closest pairs first: several bones can be within the tolerance of the
            // same frame, so taking candidates bone by bone in index order would steal frames.
            int total = skin.NumBones * skeleton.Length;
            float[] differences = new float[total];
            for (int bone = 0; bone < skin.NumBones; bone++)
            {
                Matrix bind = BindMatrix(skin, bone);
                for (int frame = 0; frame < skeleton.Length; frame++)
                    differences[bone * skeleton.Length + frame] = MatrixDifference(bind, skeleton[frame]);
            }
            for (int assigned = 0; assigned < skin.NumBones; assigned++)
            {
                int bestIndex = -1;
                float bestDifference = float.MaxValue;
                for (int index = 0; index < total; index++)
                {
                    if (differences[index] >= bestDifference)
                        continue;
                    int bone = index / skeleton.Length;
                    int frame = index % skeleton.Length;
                    if (usedBone[bone] || usedFrame[frame])
                        continue;
                    bestDifference = differences[index];
                    bestIndex = index;
                }
                if (bestIndex < 0 || bestDifference > tolerance)
                {
                    throw new AssetFormatException(
                        "DFF skin bind matrix does not match any skeleton frame (difference " +
                        (bestIndex < 0 ? "none" : bestDifference.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture)) + ").");
                }
                int assignedBone = bestIndex / skeleton.Length;
                int assignedFrame = bestIndex % skeleton.Length;
                frames[assignedBone] = assignedFrame;
                usedBone[assignedBone] = true;
                usedFrame[assignedFrame] = true;
            }
            return frames;
        }

        internal static int FindHierarchyParent(DffClump clump, DffSkin skin)
        {
            if (clump.Frames == null || clump.Frames.Length == 0)
                throw new AssetFormatException("DFF clump has no frame array.");
            for (int i = 0; i < clump.Frames.Length; i++)
            {
                DffFrame frame = clump.Frames[i];
                if (frame == null)
                    throw new AssetFormatException("DFF frame is null.");
                if (frame.HAnim != null && frame.HAnim.Nodes != null && frame.HAnim.Nodes.Length != 0)
                    return i;
            }
            int fallback = clump.Frames.Length - skin.NumBones;
            return fallback > 0 ? fallback : 0;
        }

        private static float MatrixDifference(Matrix left, Matrix right)
        {
            return Math.Abs(left.Right.X - right.Right.X) + Math.Abs(left.Right.Y - right.Right.Y) +
                Math.Abs(left.Right.Z - right.Right.Z) + Math.Abs(left.Forward.X - right.Forward.X) +
                Math.Abs(left.Forward.Y - right.Forward.Y) +
                Math.Abs(left.Forward.Z - right.Forward.Z) + Math.Abs(left.Up.X - right.Up.X) +
                Math.Abs(left.Up.Y - right.Up.Y) + Math.Abs(left.Up.Z - right.Up.Z) +
                Math.Abs(left.Position.X - right.Position.X) +
                Math.Abs(left.Position.Y - right.Position.Y) +
                Math.Abs(left.Position.Z - right.Position.Z);
        }

        /// <summary>
        /// The bind matrices live in skeleton space, i.e. relative to the hierarchy parent frame,
        /// while the clump also carries that frame. Dividing it out makes the rest skeleton
        /// reproduce the stored vertices exactly — the headless `bindDelta` diagnostic guards this.
        /// </summary>
        internal static Matrix[] RelativeTo(Matrix[] frameWorld, int referenceFrame)
        {
            if (frameWorld.Length == 0 || referenceFrame < 0 || referenceFrame >= frameWorld.Length)
                return frameWorld;
            Matrix referenceInverse = InvertAffine(frameWorld[referenceFrame]);
            Matrix[] relative = new Matrix[frameWorld.Length];
            for (int i = 0; i < frameWorld.Length; i++)
                relative[i] = DffFrameTransforms.Concatenate(frameWorld[i], referenceInverse);
            return relative;
        }

        /// <summary>Affine inverse of a row-vector matrix (`p * M + position`).</summary>
        public static Matrix InvertAffine(Matrix matrix)
        {
            float a = matrix.Right.X, b = matrix.Right.Y, c = matrix.Right.Z;
            float d = matrix.Forward.X, e = matrix.Forward.Y, f = matrix.Forward.Z;
            float g = matrix.Up.X, h = matrix.Up.Y, i = matrix.Up.Z;
            float determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            if (determinant == 0.0f)
                throw new AssetFormatException("DFF frame transform is singular.");
            float inverseDeterminant = 1.0f / determinant;
            Matrix result = new Matrix(
                new Vector3((e * i - f * h) * inverseDeterminant,
                    (c * h - b * i) * inverseDeterminant,
                    (b * f - c * e) * inverseDeterminant),
                new Vector3((f * g - d * i) * inverseDeterminant,
                    (a * i - c * g) * inverseDeterminant,
                    (c * d - a * f) * inverseDeterminant),
                new Vector3((d * h - e * g) * inverseDeterminant,
                    (b * g - a * h) * inverseDeterminant,
                    (a * e - b * d) * inverseDeterminant),
                Vector3.Zero);
            Vector3 shifted = result.TransformDirection(matrix.Position);
            result.Position = new Vector3(-shifted.X, -shifted.Y, -shifted.Z);
            return result;
        }

        /// <summary>Skinned positions for one geometry; vertices without a skin are returned as-is.</summary>
        public static Vector3[] DeformPositions(DffGeometry geometry, Matrix[] boneMatrices)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            return DeformPositions(geometry, geometry.Skin, boneMatrices);
        }

        /// <summary>
        /// Skinned positions with an explicit skin, because legacy Atomic Skin lives on the atomic
        /// rather than on the geometry.
        /// </summary>
        public static Vector3[] DeformPositions(DffGeometry geometry, DffSkin skin,
            Matrix[] boneMatrices)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            Vector3[] source = SourceVertices(geometry);
            if (skin == null)
                return (Vector3[])source.Clone();
            ValidateSkin(skin, source.Length);
            if (boneMatrices == null || boneMatrices.Length < skin.NumBones)
                throw new ArgumentException("Bone matrices do not cover the skin bones.", "boneMatrices");

            Vector3[] result = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 position = source[i];
                float x = 0.0f;
                float y = 0.0f;
                float z = 0.0f;
                for (int k = 0; k < WeightsPerVertex; k++)
                {
                    float weight = skin.BoneWeights[i * WeightsPerVertex + k];
                    if (weight == 0.0f)
                        continue;
                    Matrix bone = boneMatrices[skin.BoneIndices[i * WeightsPerVertex + k]];
                    Vector3 transformed = bone.TransformPoint(position);
                    x += weight * transformed.X;
                    y += weight * transformed.Y;
                    z += weight * transformed.Z;
                }
                result[i] = new Vector3(x, y, z);
            }
            return result;
        }

        /// <summary>
        /// Skinned normals: only the rotation part applies, so the translation of the bone matrix is
        /// dropped the way `RwV3dTransformVectors` does. Normals of a skinless geometry are copied.
        /// </summary>
        public static Vector3[] DeformNormals(DffGeometry geometry, Matrix[] boneMatrices)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            return DeformNormals(geometry, geometry.Skin, boneMatrices);
        }

        public static Vector3[] DeformNormals(DffGeometry geometry, DffSkin skin,
            Matrix[] boneMatrices)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            Vector3[] source = SourceNormals(geometry);
            if (skin == null)
                return (Vector3[])source.Clone();
            ValidateSkin(skin, source.Length);
            if (boneMatrices == null || boneMatrices.Length < skin.NumBones)
                throw new ArgumentException("Bone matrices do not cover the skin bones.", "boneMatrices");

            Vector3[] result = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 normal = source[i];
                float x = 0.0f;
                float y = 0.0f;
                float z = 0.0f;
                for (int k = 0; k < WeightsPerVertex; k++)
                {
                    float weight = skin.BoneWeights[i * WeightsPerVertex + k];
                    if (weight == 0.0f)
                        continue;
                    Matrix bone = boneMatrices[skin.BoneIndices[i * WeightsPerVertex + k]];
                    Vector3 transformed = bone.TransformDirection(normal);
                    x += weight * transformed.X;
                    y += weight * transformed.Y;
                    z += weight * transformed.Z;
                }
                float length = (float)Math.Sqrt(x * x + y * y + z * z);
                result[i] = length > 0.0f
                    ? new Vector3(x / length, y / length, z / length)
                    : normal;
            }
            return result;
        }

        /// <summary>`numVertices * 4` bone indices and weights must cover the geometry.</summary>
        public static void ValidateSkin(DffSkin skin, int vertexCount)
        {
            if (skin == null)
                throw new ArgumentNullException("skin");
            int expected = checked(vertexCount * WeightsPerVertex);
            if (skin.BoneIndices == null || skin.BoneIndices.Length < expected)
                throw new AssetFormatException("DFF skin bone indices do not cover the vertices.");
            if (skin.BoneWeights == null || skin.BoneWeights.Length < expected)
                throw new AssetFormatException("DFF skin bone weights do not cover the vertices.");
            for (int i = 0; i < expected; i++)
            {
                if (skin.BoneIndices[i] >= skin.NumBones)
                    throw new AssetFormatException("DFF skin bone index is outside the bone array.");
            }
        }

        /// <summary>Reads one RenderWare row matrix: right, up, at, position.</summary>
        public static Matrix ReadMatrix(float[] values, int offset)
        {
            if (values == null)
                throw new ArgumentNullException("values");
            if (offset < 0 || offset > values.Length - 16)
                throw new ArgumentOutOfRangeException("offset");
            return new Matrix(
                new Vector3(values[offset], values[offset + 1], values[offset + 2]),
                // RenderWare's up row is stored where GTA keeps forward.
                new Vector3(values[offset + 4], values[offset + 5], values[offset + 6]),
                new Vector3(values[offset + 8], values[offset + 9], values[offset + 10]),
                new Vector3(values[offset + 12], values[offset + 13], values[offset + 14]));
        }

        private static Vector3[] SourceVertices(DffGeometry geometry)
        {
            if (geometry.MorphTargets == null || geometry.MorphTargets.Length == 0 ||
                geometry.MorphTargets[0] == null || geometry.MorphTargets[0].Vertices == null)
            {
                throw new AssetFormatException("DFF geometry has no vertices.");
            }
            return geometry.MorphTargets[0].Vertices;
        }

        private static Vector3[] SourceNormals(DffGeometry geometry)
        {
            if (geometry.MorphTargets == null || geometry.MorphTargets.Length == 0 ||
                geometry.MorphTargets[0] == null || geometry.MorphTargets[0].Normals == null)
            {
                throw new AssetFormatException("DFF geometry has no normals.");
            }
            return geometry.MorphTargets[0].Normals;
        }
    }
}