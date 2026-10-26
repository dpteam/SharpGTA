using System;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    public sealed class DffDocument
    {
        public DffClump[] Clumps { get; internal set; }
    }

    public sealed class DffClump
    {
        public uint Version { get; internal set; }
        public int LightCount { get; internal set; }
        public int CameraCount { get; internal set; }
        public DffFrame[] Frames { get; internal set; }
        public DffGeometry[] Geometries { get; internal set; }
        public DffAtomic[] Atomics { get; internal set; }
        public int DeclaredLength { get; internal set; }
        public int ActualLength { get; internal set; }
        public bool HasDeclaredLengthMismatch { get; internal set; }
    }

    public sealed class DffFrame
    {
        public Matrix LocalTransform { get; internal set; }
        public int ParentIndex { get; internal set; }
        public int CreationFlags { get; internal set; }
        public string Name { get; internal set; }
    }

    public sealed class DffAtomic
    {
        public int FrameIndex { get; internal set; }
        public int GeometryIndex { get; internal set; }
        public int Flags { get; internal set; }
        public int Unused { get; internal set; }
        public DffGeometry Geometry { get; internal set; }
    }

    public sealed class DffGeometry
    {
        public uint Format { get; internal set; }
        public DffColor[] PrelitColors { get; internal set; }
        public Vector2[][] TexCoords { get; internal set; }
        public DffTriangle[] Triangles { get; internal set; }
        public DffMorphTarget[] MorphTargets { get; internal set; }
        public DffMaterial[] Materials { get; internal set; }
        public DffMesh[] Meshes { get; internal set; }
        public bool HasLegacySurfaceProperties { get; internal set; }
        public DffSurfaceProperties LegacySurfaceProperties { get; internal set; }
    }

    public sealed class DffMorphTarget
    {
        public BoundingSphere BoundingSphere { get; internal set; }
        public Vector3[] Vertices { get; internal set; }
        public Vector3[] Normals { get; internal set; }
    }

    public struct DffTriangle
    {
        public ushort A;
        public ushort B;
        public ushort C;
        public short MaterialIndex;

        public DffTriangle(ushort a, ushort b, ushort c, short materialIndex)
        {
            A = a;
            B = b;
            C = c;
            MaterialIndex = materialIndex;
        }
    }

    public struct DffColor
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public DffColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    public struct DffSurfaceProperties
    {
        public float Ambient;
        public float Specular;
        public float Diffuse;

        public DffSurfaceProperties(float ambient, float specular, float diffuse)
        {
            Ambient = ambient;
            Specular = specular;
            Diffuse = diffuse;
        }
    }

    public sealed class DffMaterial
    {
        public int Flags { get; internal set; }
        public int Unused { get; internal set; }
        public int Textured { get; internal set; }
        public DffColor Color { get; internal set; }
        public DffSurfaceProperties SurfaceProperties { get; internal set; }
        public DffTextureReference Texture { get; internal set; }
    }

    public sealed class DffTextureReference
    {
        public uint FilterAddressing { get; internal set; }
        public string Name { get; internal set; }
        public string MaskName { get; internal set; }
    }

    public sealed class DffMesh
    {
        public uint Flags { get; internal set; }
        public int MaterialIndex { get; internal set; }
        public ushort[] Indices { get; internal set; }

        public bool TriangleStrip
        {
            get { return (Flags & 1u) != 0; }
        }

        public DffTriangle[] Triangulate()
        {
            short materialIndex = checked((short)MaterialIndex);
            ushort[] indices = Indices;
            if (indices == null || indices.Length < 3)
                return new DffTriangle[0];

            if (!TriangleStrip)
            {
                DffTriangle[] listTriangles = new DffTriangle[indices.Length / 3];
                int listIndex = 0;
                for (int j = 0; j + 2 < indices.Length; j += 3)
                {
                    listTriangles[listIndex++] = new DffTriangle(
                        indices[j], indices[j + 1], indices[j + 2], materialIndex);
                }
                return listTriangles;
            }

            DffTriangle[] stripTriangles = new DffTriangle[indices.Length - 2];
            int triangleCount = 0;
            for (int j = 0; j < indices.Length - 2; j++)
            {
                ushort a = indices[j];
                ushort b = indices[j + 1 + (j & 1)];
                ushort c = indices[j + 2 - (j & 1)];
                if (a == b || a == c || b == c)
                    continue;

                stripTriangles[triangleCount++] = new DffTriangle(a, b, c, materialIndex);
            }

            if (triangleCount != stripTriangles.Length)
                Array.Resize<DffTriangle>(ref stripTriangles, triangleCount);
            return stripTriangles;
        }
    }
}
