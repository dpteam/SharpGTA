using System;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;

namespace SharpGTA.Tests
{
    public sealed class SkinDeformerTests
    {
        private const float Pi = 3.14159265358979323846f;

        [Test]
        public void BindPoseReproducesStoredVerticesForBothSkinFormats()
        {
            // A two-bone skeleton and one geometry; the bind pose must deform back onto the vertices
            // whether the file stores the bind matrices (old) or their inverses (34003+).
            Vector3[] vertices = { new Vector3(0.0f, 0.0f, 0.5f), new Vector3(0.5f, 0.0f, 1.0f) };
            AssertBindPoseMatches(vertices, false);
            AssertBindPoseMatches(vertices, true);
        }

        [Test]
        public void DeformationUsesBindMatricesAroundTheRightPivot()
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin;
            DffGeometry geometry = TwoBoneGeometry(false, out skin);
            SkinBinding binding = SkinBinding.Bind(clump, skin);
            Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);

            Matrix[] posed = SkinDeformer.ResolveRestWorld(clump);
            Matrix rotated = rotation.ToMatrix();
            posed[1] = new Matrix(rotated.Right, rotated.Forward, rotated.Up, posed[1].Position);
            Vector3[] positions = SkinDeformer.DeformPositions(geometry, skin,
                binding.ComputeBoneMatrices(posed));

            // Vertex 0 hangs off the root bone, so it stays put.
            Assert.Near(0.0f, positions[0].X, 1e-4f);
            Assert.Near(0.5f, positions[0].Z, 1e-4f);
            // Vertex 1 is bound to the child bone and rotates about that bone's origin (0,0,1).
            Vector3 local = new Vector3(0.5f, 0.0f, 1.0f - 1.0f);
            Vector3 expected = rotation.ToMatrix().TransformPoint(local);
            Assert.Near(expected.X, positions[1].X, 1e-4f);
            Assert.Near(expected.Y, positions[1].Y, 1e-4f);
            Assert.Near(1.0f + expected.Z, positions[1].Z, 1e-4f);
        }

        [Test]
        public void NormalsRotateWithoutBeingTranslated()
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin;
            DffGeometry geometry = TwoBoneGeometry(false, out skin);
            geometry.MorphTargets[0].Normals = new[]
            {
                new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, 0.0f, 1.0f)
            };
            SkinBinding binding = SkinBinding.Bind(clump, skin);
            Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, Pi / 2.0f);
            Matrix[] posed = SkinDeformer.ResolveRestWorld(clump);
            Matrix rotated = rotation.ToMatrix();
            posed[1] = new Matrix(rotated.Right, rotated.Forward, rotated.Up, posed[1].Position);

            Vector3[] normals = SkinDeformer.DeformNormals(geometry, skin,
                binding.ComputeBoneMatrices(posed));
            Assert.Near(1.0f, normals[0].Z, 1e-4f);
            Assert.Near(1.0f, Math.Abs(normals[1].X), 1e-4f);
            Assert.Near(0.0f, normals[1].Z, 1e-4f);
        }

        [Test]
        public void RejectsSkinsThatDoNotCoverTheVertices()
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin;
            DffGeometry geometry = TwoBoneGeometry(false, out skin);
            Matrix[] bones = SkinBinding.Bind(clump, skin)
                .ComputeBoneMatrices(SkinDeformer.ResolveRestWorld(clump));
            // Two vertices need eight indices; four are not enough.
            skin.BoneIndices = new byte[4];
            Assert.Throws<AssetFormatException>(delegate
            {
                SkinDeformer.DeformPositions(geometry, skin, bones);
            });
            skin.BoneIndices = new byte[8];
            skin.BoneWeights = new float[4];
            Assert.Throws<AssetFormatException>(delegate
            {
                SkinDeformer.DeformPositions(geometry, skin, bones);
            });
        }

        [Test]
        public void RejectsBindMatricesThatDoNotMatchTheSkeleton()
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin = GetValidSkin();
            skin.InverseMatrices[0] = 1.0f;
            // Push the first bone far away so no frame can match it.
            skin.InverseMatrices[12] = 40.0f;
            Assert.Throws<AssetFormatException>(delegate { SkinBinding.Bind(clump, skin); });
        }

        [Test]
        public void LegacyAtomicSkinsUseTheirOwnBoneIndexTable()
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin = GetValidSkin();
            skin.IsLegacyAtomic = true;
            skin.LegacyBones = new[]
            {
                LegacyBone(1), LegacyBone(0)
            };
            SkinBinding binding = SkinBinding.Bind(clump, skin);
            Assert.Equal(1, binding.BoneFrames[0]);
            Assert.Equal(0, binding.BoneFrames[1]);
        }

        private static void AssertBindPoseMatches(Vector3[] vertices, bool oldFormat)
        {
            DffClump clump = TwoBoneClump();
            DffSkin skin;
            DffGeometry geometry = TwoBoneGeometry(oldFormat, out skin);
            geometry.MorphTargets[0].Vertices = vertices;
            SkinBinding binding = SkinBinding.Bind(clump, skin);
            Vector3[] deformed = SkinDeformer.DeformPositions(geometry, skin,
                binding.ComputeBoneMatrices(SkinDeformer.ResolveRestWorld(clump)));
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.Near(vertices[i].X, deformed[i].X, 1e-4f);
                Assert.Near(vertices[i].Y, deformed[i].Y, 1e-4f);
                Assert.Near(vertices[i].Z, deformed[i].Z, 1e-4f);
            }
        }

        private static DffClump TwoBoneClump()
        {
            DffFrame root = new DffFrame();
            root.Name = "Root";
            root.ParentIndex = -1;
            root.LocalTransform = Matrix.Identity;
            DffFrame child = new DffFrame();
            child.Name = "Child";
            child.ParentIndex = 0;
            child.LocalTransform = Matrix.CreateTranslation(0.0f, 0.0f, 1.0f);
            DffClump clump = new DffClump();
            clump.Frames = new DffFrame[] { root, child };
            clump.Atomics = new DffAtomic[0];
            clump.Geometries = new DffGeometry[0];
            return clump;
        }

        /// <summary>Vertex 0 is bound to the root bone, vertex 1 to the child at (0,0,1).</summary>
        private static DffGeometry TwoBoneGeometry(bool oldFormat, out DffSkin skin)
        {
            DffMorphTarget target = new DffMorphTarget();
            target.Vertices = new[] { new Vector3(0.0f, 0.0f, 0.5f), new Vector3(0.5f, 0.0f, 1.0f) };
            DffGeometry geometry = new DffGeometry();
            geometry.MorphTargets = new DffMorphTarget[] { target };

            skin = GetValidSkin();
            skin.IsOldFormat = oldFormat;
            if (oldFormat)
            {
                // Old skins store the bind matrix itself; the child bone sits at (0,0,1).
                skin.InverseMatrices = new float[32];
                Identity(skin.InverseMatrices, 0);
                Matrix child = Matrix.CreateTranslation(0.0f, 0.0f, 1.0f);
                Store(child, skin.InverseMatrices, 16);
            }
            geometry.Skin = skin;
            return geometry;
        }

        /// <summary>New format: the stored matrices are already inverted.</summary>
        private static DffSkin GetValidSkin()
        {
            DffSkin skin = new DffSkin();
            skin.NumBones = 2;
            skin.NumUsedBones = 2;
            skin.NumWeights = 1;
            skin.InverseMatrices = new float[32];
            Identity(skin.InverseMatrices, 0);
            Matrix inverse = SkinDeformer.InvertAffine(Matrix.CreateTranslation(0.0f, 0.0f, 1.0f));
            Store(inverse, skin.InverseMatrices, 16);
            skin.BoneIndices = new byte[] { 0, 0, 0, 0, 1, 0, 0, 0 };
            skin.BoneWeights = new float[] { 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f };
            return skin;
        }

        private static DffSkinBone LegacyBone(int index)
        {
            DffSkinBone bone = new DffSkinBone();
            bone.Index = index;
            bone.Id = index;
            return bone;
        }

        private static void Identity(float[] target, int offset)
        {
            for (int i = 0; i < 16; i++)
                target[offset + i] = 0.0f;
            target[offset + 0] = 1.0f;
            target[offset + 5] = 1.0f;
            target[offset + 10] = 1.0f;
            target[offset + 15] = 1.0f;
        }

        private static void Store(Matrix matrix, float[] target, int offset)
        {
            target[offset + 0] = matrix.Right.X;
            target[offset + 1] = matrix.Right.Y;
            target[offset + 2] = matrix.Right.Z;
            target[offset + 3] = 0.0f;
            target[offset + 4] = matrix.Forward.X;
            target[offset + 5] = matrix.Forward.Y;
            target[offset + 6] = matrix.Forward.Z;
            target[offset + 7] = 0.0f;
            target[offset + 8] = matrix.Up.X;
            target[offset + 9] = matrix.Up.Y;
            target[offset + 10] = matrix.Up.Z;
            target[offset + 11] = 0.0f;
            target[offset + 12] = matrix.Position.X;
            target[offset + 13] = matrix.Position.Y;
            target[offset + 14] = matrix.Position.Z;
            target[offset + 15] = 1.0f;
        }
    }
}