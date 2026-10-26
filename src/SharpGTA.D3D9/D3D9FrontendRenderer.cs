using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.Frontend;

namespace SharpGTA.D3D9
{
    internal sealed class D3D9FrontendRenderer : IDisposable
    {
        private readonly D3D9Device device;
        private readonly IIIFrontendAssets assets;
        private readonly Gta3Font font;
        private readonly Dictionary<string, D3D9Texture> textures =
            new Dictionary<string, D3D9Texture>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;

        internal D3D9FrontendRenderer(D3D9Device device, IIIFrontendAssets assets, Gta3Font font)
        {
            if (device == null) throw new ArgumentNullException("device");
            if (assets == null) throw new ArgumentNullException("assets");
            if (font == null) throw new ArgumentNullException("font");
            this.device = device;
            this.assets = assets;
            this.font = font;
        }

        internal void Render(FrontendLayout layout)
        {
            if (disposed)
                throw new ObjectDisposedException("D3D9FrontendRenderer");
            if (layout == null)
                throw new ArgumentNullException("layout");
            device.BeginFrame(0xFF000000);
            try
            {
                for (int i = 0; i < layout.Commands.Count; i++)
                {
                    FrontendDrawCommand command = layout.Commands[i];
                    if (command.Kind == FrontendDrawKind.Sprite)
                    {
                        device.DrawSprite(GetTexture(command.TextureName), command.X, command.Y,
                            command.Right, command.Bottom, command.U0, command.V0,
                            command.U1, command.V1, command.ColorArgb);
                    }
                    else if (command.Kind == FrontendDrawKind.Rectangle)
                    {
                        device.DrawRectangle(command.X, command.Y, command.Right, command.Bottom,
                            command.ColorArgb);
                    }
                    else
                    {
                        DrawText(command);
                    }
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
        }

        private D3D9Texture GetTexture(string name)
        {
            D3D9Texture texture;
            if (textures.TryGetValue(name, out texture))
                return texture;
            TxdImage image = assets.GetImage(name);
            texture = device.CreateTextureRgba(image.Width, image.Height, image.Rgba);
            textures.Add(name, texture);
            return texture;
        }

        private void DrawText(FrontendDrawCommand command)
        {
            int[] glyphs = font.GetGlyphs(command.Text);
            float x = command.X;
            if (command.Align != FrontendTextAlign.Left)
            {
                float width = font.Measure(command.Text, command.FontStyle, command.ScaleX);
                if (command.Align == FrontendTextAlign.Center)
                    x -= width * 0.5f;
                else
                    x -= width;
            }

            int drawableCount = 0;
            for (int i = 0; i < glyphs.Length; i++)
            {
                if (glyphs[i] != 0)
                    drawableCount++;
            }
            if (drawableCount == 0)
                return;

            D3dTexturedVertex[] vertices = new D3dTexturedVertex[drawableCount * 6];
            int output = 0;
            for (int i = 0; i < glyphs.Length; i++)
            {
                int glyph = glyphs[i];
                int characterWidth = font.GetCharacterWidth(command.FontStyle, glyph);
                float advance = characterWidth * command.ScaleX;
                if (glyph != 0)
                {
                    float xOffset = glyph % 16;
                    float yOffset = glyph / 16;
                    float width;
                    float height;
                    float u0;
                    float v0;
                    float u1;
                    float v1;
                    if (command.FontStyle == Gta3Font.Pager)
                    {
                        float atlasWidth = characterWidth / 32.0f;
                        width = advance;
                        height = 16.0f * command.ScaleY;
                        u0 = xOffset / 16.0f;
                        v0 = yOffset / 16.0f;
                        u1 = (xOffset + atlasWidth) / 16.0f - 0.0001f;
                        v1 = (yOffset + 1.0f) / 16.0f - 0.0001f;
                    }
                    else
                    {
                        width = 32.0f * command.ScaleX;
                        height = 20.0f * command.ScaleY;
                        u0 = xOffset / 16.0f;
                        v0 = yOffset / 12.8f;
                        u1 = (xOffset + 1.0f) / 16.0f - 0.001f;
                        v1 = (yOffset + 1.0f) / 12.8f - 0.002f;
                    }
                    AddQuad(vertices, ref output, x, command.Y, x + width, command.Y + height,
                        u0, v0, u1, v1, command.ColorArgb);
                }
                x += advance;
            }
            device.DrawQuads(GetTexture(command.TextureName), vertices);
        }

        private static void AddQuad(D3dTexturedVertex[] vertices, ref int index,
            float x, float y, float right, float bottom,
            float u0, float v0, float u1, float v1, uint color)
        {
            vertices[index++] = new D3dTexturedVertex(x, y, color, u0, v0);
            vertices[index++] = new D3dTexturedVertex(right, y, color, u1, v0);
            vertices[index++] = new D3dTexturedVertex(x, bottom, color, u0, v1);
            vertices[index++] = new D3dTexturedVertex(right, y, color, u1, v0);
            vertices[index++] = new D3dTexturedVertex(right, bottom, color, u1, v1);
            vertices[index++] = new D3dTexturedVertex(x, bottom, color, u0, v1);
        }
    }
}
