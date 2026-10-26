using System;
using SharpGTA.Assets;
using SharpGTA.Mathematics;

namespace SharpGTA.Rendering
{
    /// <summary>Converts decoded DFF data to backend-neutral CPU render resources.</summary>
    public static class DffRenderConverter
    {
        public static GeometryRenderData ConvertSingleGeometry(DffGeometry geometry, string name)
        {
            if (geometry == null)
                throw new ArgumentNullException("geometry");
            if (name == null)
                throw new ArgumentNullException("name");
            if (geometry.MorphTargets.Length == 0 || geometry.MorphTargets[0].Vertices == null)
                throw new ArgumentException("Geometry has no static morph target vertices.", "geometry");

            Vector3[] positions = geometry.MorphTargets[0].Vertices;
            Vector3[] normals = geometry.MorphTargets[0].Normals;
            RenderVertex[] vertices = new RenderVertex[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                vertices[i].Position = positions[i];
                vertices[i].Normal = normals == null ? Vector3.Zero : normals[i];
                vertices[i].ColorRgba = geometry.PrelitColors == null
                    ? 0xFFFFFFFFu
                    : PackRgba(geometry.PrelitColors[i]);
                vertices[i].Uv = geometry.TexCoords.Length == 0
                    ? Vector2.Zero
                    : geometry.TexCoords[0][i];
            }

            MeshData[] meshes = new MeshData[geometry.Meshes.Length];
            for (int meshIndex = 0; meshIndex < geometry.Meshes.Length; meshIndex++)
            {
                DffMesh source = geometry.Meshes[meshIndex];
                DffTriangle[] triangles = source.Triangulate();
                ushort[] indices = new ushort[checked(triangles.Length * 3)];
                for (int i = 0; i < triangles.Length; i++)
                {
                    indices[i * 3] = triangles[i].A;
                    indices[i * 3 + 1] = triangles[i].B;
                    indices[i * 3 + 2] = triangles[i].C;
                }
                meshes[meshIndex] = new MeshData(name + "#mesh" + meshIndex, vertices, indices, source.MaterialIndex);
            }

            MaterialData[] materials = new MaterialData[geometry.Materials.Length];
            for (int i = 0; i < geometry.Materials.Length; i++)
            {
                DffMaterial material = geometry.Materials[i];
                materials[i] = new MaterialData(name + "#material" + i,
                    material.Texture == null ? null : material.Texture.Name,
                    PackRgba(material.Color));
            }
            return new GeometryRenderData(name, vertices, materials, meshes);
        }

        public static GeometryRenderData[] ConvertClump(DffClump clump, string name)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (name == null)
                throw new ArgumentNullException("name");
            GeometryRenderData[] models = new GeometryRenderData[clump.Atomics.Length];
            for (int i = 0; i < clump.Atomics.Length; i++)
            {
                DffAtomic atomic = clump.Atomics[i];
                models[i] = ConvertSingleGeometry(atomic.Geometry,
                    name + "#atomic" + i + "#geometry" + atomic.GeometryIndex);
            }
            return models;
        }

        public static GeometryRenderData[] ConvertGeometry(DffGeometry geometry, string name)
        {
            return new GeometryRenderData[] { ConvertSingleGeometry(geometry, name) };
        }

        public static uint PackRgba(DffColor color)
        {
            return (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);
        }
    }
}
