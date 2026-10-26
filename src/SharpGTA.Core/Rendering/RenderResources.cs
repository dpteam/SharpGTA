using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.Mathematics;

namespace SharpGTA.Rendering
{
    public struct RenderVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public uint ColorRgba;
        public Vector2 Uv;
    }

    public sealed class MaterialData
    {
        internal MaterialData(string name, string textureName, uint colorRgba)
        {
            Name = name ?? string.Empty;
            TextureName = textureName;
            ColorRgba = colorRgba;
        }

        public string Name { get; private set; }
        public string TextureName { get; private set; }
        public uint ColorRgba { get; private set; }
    }

    public sealed class TextureData
    {
        internal TextureData(string name, int width, int height, byte[] rgba)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (rgba == null)
                throw new ArgumentNullException("rgba");
            if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
                throw new ArgumentException("Invalid RGBA texture dimensions or data length.");
            Name = name;
            Width = width;
            Height = height;
            Rgba = rgba;
        }

        public string Name { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Rgba { get; private set; }
    }

    public sealed class MeshData
    {
        internal MeshData(string name, RenderVertex[] vertices, ushort[] indices, int materialIndex)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (vertices == null)
                throw new ArgumentNullException("vertices");
            if (indices == null)
                throw new ArgumentNullException("indices");
            if (indices.Length % 3 != 0)
                throw new ArgumentException("Triangle-list index count must be a multiple of three.", "indices");
            Name = name;
            Vertices = vertices;
            Indices = indices;
            MaterialIndex = materialIndex;
        }

        public string Name { get; private set; }
        public RenderVertex[] Vertices { get; private set; }
        public ushort[] Indices { get; private set; }
        public int MaterialIndex { get; private set; }
    }

    public sealed class GeometryRenderData
    {
        internal GeometryRenderData(string name, RenderVertex[] vertices,
            MaterialData[] materials, MeshData[] meshes)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            if (vertices == null)
                throw new ArgumentNullException("vertices");
            if (materials == null)
                throw new ArgumentNullException("materials");
            if (meshes == null)
                throw new ArgumentNullException("meshes");
            Name = name;
            Vertices = vertices;
            Materials = materials;
            Meshes = meshes;
        }

        public string Name { get; private set; }
        public RenderVertex[] Vertices { get; private set; }
        public MaterialData[] Materials { get; private set; }
        public MeshData[] Meshes { get; private set; }
    }

    public struct RenderDrawCall
    {
        public RenderDrawCall(GeometryRenderData geometry, MeshData mesh,
            MaterialData material, Matrix transform)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            if (mesh == null)
                throw new ArgumentNullException("mesh");
            Geometry = geometry;
            Mesh = mesh;
            Material = material;
            Transform = transform;
        }

        public GeometryRenderData Geometry { get; private set; }
        public MeshData Mesh { get; private set; }
        public MaterialData Material { get; private set; }
        public Matrix Transform { get; private set; }
    }

    public sealed class RenderSceneSnapshot
    {
        private readonly ReadOnlyCollection<RenderDrawCall> drawCalls;

        public RenderSceneSnapshot(IList<RenderDrawCall> drawCalls)
        {
            if (drawCalls == null)
                throw new ArgumentNullException("drawCalls");
            this.drawCalls = new List<RenderDrawCall>(drawCalls).AsReadOnly();
        }

        public ReadOnlyCollection<RenderDrawCall> DrawCalls { get { return drawCalls; } }
    }

    public struct RenderFrameStatistics
    {
        public int ModelCount;
        public int DrawCallCount;
        public int MeshCount;
        public int MaterialCount;
        public int TextureCount;
        public int TriangleCount;
        public int InvalidReferenceCount;
        public long VertexCount;
        public long TextureBytes;
    }
}
