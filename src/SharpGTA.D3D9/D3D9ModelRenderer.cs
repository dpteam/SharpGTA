using System;
using System.Collections.Generic;
using SharpGTA.Rendering;

namespace SharpGTA.D3D9
{
    /// <summary>Static non-skinned DFF meshes with original normals, colors and materials.</summary>
    internal sealed class D3D9ModelRenderer : IDisposable
    {
        private readonly D3D9Device device;
        private readonly RenderResourceManager resources;
        private readonly Dictionary<MeshData, D3dModelVertex[]> meshes =
            new Dictionary<MeshData, D3dModelVertex[]>();
        private readonly Dictionary<string, D3D9Texture> textures =
            new Dictionary<string, D3D9Texture>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;

        internal D3D9ModelRenderer(D3D9Device device, RenderResourceManager resources)
        {
            if (device == null) throw new ArgumentNullException("device");
            if (resources == null) throw new ArgumentNullException("resources");
            this.device = device;
            this.resources = resources;
        }

        public int TextureCount
        {
            get
            {
                int count = 0;
                foreach (D3D9Texture texture in textures.Values)
                {
                    if (texture != null)
                        count++;
                }
                return count;
            }
        }
        public int MeshCount { get { return meshes.Count; } }

        internal void Prepare(RenderSceneSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            for (int i = 0; i < snapshot.DrawCalls.Count; i++)
            {
                RenderDrawCall call = snapshot.DrawCalls[i];
                MeshData mesh = call.Mesh;
                if (!meshes.ContainsKey(mesh))
                    meshes.Add(mesh, Expand(mesh));
                if (call.Material != null)
                    GetTexture(call.Material.TextureName);
            }
        }

        internal void Render(RenderSceneSnapshot snapshot, D3dMatrix view, D3dMatrix projection)
        {
            if (disposed)
                throw new ObjectDisposedException("D3D9ModelRenderer");
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            Prepare(snapshot);
            device.BeginFrame(0xFF18222E);
            try
            {
                device.SetTransform(D3dConstants.StateView, ref view);
                device.SetTransform(D3dConstants.StateProjection, ref projection);
                device.SetDirectionalLight(new D3dVector(-0.35f, -0.45f, -1.0f));
                for (int i = 0; i < snapshot.DrawCalls.Count; i++)
                {
                    RenderDrawCall call = snapshot.DrawCalls[i];
                    D3dMatrix world = D3dMatrix.FromGtaMatrix(call.Transform);
                    device.SetTransform(D3dConstants.StateWorld, ref world);
                    MaterialData material = call.Material;
                    uint color = material == null ? 0xFFFFFFFFu : ToD3dColor(material.ColorRgba);
                    D3D9Texture texture = material == null ? null : GetTexture(material.TextureName);
                    device.DrawModelMesh(texture, color, meshes[call.Mesh]);
                }
                device.EndFrame();
            }
            finally
            {
                if (device.SceneOpen)
                {
                    try { device.EndFrame(); }
                    catch { /* Preserve the original rendering failure. */ }
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (D3D9Texture texture in textures.Values)
                texture.Dispose();
            textures.Clear();
            meshes.Clear();
        }

        private D3D9Texture GetTexture(string name)
        {
            if (name == null)
                return null;
            D3D9Texture texture;
            if (textures.TryGetValue(name, out texture))
                return texture;
            TextureData data = resources.GetTexture(name);
            if (data != null)
                texture = device.CreateTextureRgba(data.Width, data.Height, data.Rgba);
            textures.Add(name, texture);
            return texture;
        }

        private static D3dModelVertex[] Expand(MeshData mesh)
        {
            D3dModelVertex[] result = new D3dModelVertex[mesh.Indices.Length];
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                RenderVertex source = mesh.Vertices[mesh.Indices[i]];
                result[i].X = source.Position.X;
                result[i].Y = source.Position.Y;
                result[i].Z = source.Position.Z;
                result[i].NormalX = source.Normal.X;
                result[i].NormalY = source.Normal.Y;
                result[i].NormalZ = source.Normal.Z;
                result[i].Color = ToD3dColor(source.ColorRgba);
                result[i].U = source.Uv.X;
                result[i].V = source.Uv.Y;
            }
            return result;
        }

        internal static uint ToD3dColor(uint rgba)
        {
            return (rgba & 0xFF000000u) |
                ((rgba & 0x000000FFu) << 16) |
                (rgba & 0x0000FF00u) |
                ((rgba & 0x00FF0000u) >> 16);
        }
    }
}
