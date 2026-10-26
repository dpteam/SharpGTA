using System;
using System.IO;
using System.Threading;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;

namespace SharpGTA.D3D9
{
    internal sealed class ModelViewerApplication : IDisposable
    {
        private readonly Win32Window window;
        private readonly D3D9Device device;
        private readonly GameDataLoader loader;
        private readonly RenderResourceManager resources;
        private readonly D3D9ModelRenderer renderer;
        private readonly RenderSceneSnapshot snapshot;
        private readonly Vector3 center;
        private readonly float radius;
        private float yaw = 0.55f;
        private float pitch = 0.32f;
        private float distance;
        private bool exitRequested;
        private bool dragging;
        private int mouseX;
        private int mouseY;
        private bool disposed;

        internal ModelViewerApplication(Win32Window window, D3D9Device device,
            GameDataLoader loader, string modelName)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (device == null) throw new ArgumentNullException("device");
            if (loader == null) throw new ArgumentNullException("loader");
            if (string.IsNullOrEmpty(modelName))
                throw new ArgumentException("Model name is required.", "modelName");
            this.window = window;
            this.device = device;
            this.loader = loader;
            ModelName = modelName.ToLowerInvariant();

            loader.LoadInitialImage();
            resources = new RenderResourceManager();
            resources.AddDictionary(loader.LoadTxdEntry(ModelName + ".txd"));
            DffDocument document = loader.LoadDffEntry(ModelName + ".dff");
            if (document.Clumps.Length != 1)
                throw new AssetFormatException("Model preview expects exactly one DFF clump.");
            DffClump clump = document.Clumps[0];
            GeometryRenderData[] atomics = resources.GetClump(ModelName, document);
            Matrix[] transforms = DffFrameTransforms.Resolve(clump);
            snapshot = DffRenderSceneBuilder.Build(atomics, transforms, Matrix.Identity);
            BoundingBox bounds = DffRenderSceneBuilder.ComputeBounds(atomics, transforms);
            center = (bounds.Min + bounds.Max) * 0.5f;
            radius = Math.Max(0.1f, (bounds.Max - bounds.Min).Length() * 0.5f);
            distance = Math.Max(radius * 3.4f, 2.0f);
            renderer = new D3D9ModelRenderer(device, resources);
            renderer.Prepare(snapshot);
            TriangleCount = 0;
            for (int i = 0; i < snapshot.DrawCalls.Count; i++)
                TriangleCount += snapshot.DrawCalls[i].Mesh.Indices.Length / 3;
            Console.WriteLine("Model: " + ModelName + " atomics=" + atomics.Length +
                " drawCalls=" + snapshot.DrawCalls.Count + " triangles=" + TriangleCount +
                " textures=" + renderer.TextureCount + " missing=" + resources.MissingTextureCount);
        }

        internal string ModelName { get; private set; }
        internal int TriangleCount { get; private set; }

        internal int Run(int frames, string screenshot, bool exerciseReset)
        {
            if (screenshot != null && frames <= 0)
                throw new ArgumentException("A finite positive --frames value is required with --screenshot.");
            for (int frame = 0; frames == 0 || frame < frames; frame++)
            {
                if (!window.ProcessMessages())
                    break;
                ProcessInput();
                if (exitRequested)
                    break;
                if (window.ClientWidth > 0 && window.ClientHeight > 0 &&
                    (window.ClientWidth != device.Width || window.ClientHeight != device.Height))
                    device.Reset(window.Handle, window.ClientWidth, window.ClientHeight);
                if (exerciseReset && frame > 0)
                    device.Reset(window.Handle, device.Width, device.Height);

                D3dMatrix view = D3dMatrix.LookAt(GetEye(), center, Vector3.UnitZ);
                D3dMatrix projection = D3dMatrix.PerspectiveFov((float)(Math.PI / 3.0),
                    device.Width / (float)device.Height, Math.Max(0.01f, radius * 0.005f),
                    Math.Max(1000.0f, radius * 100.0f));
                renderer.Render(snapshot, view, projection);
                if (screenshot != null && frame == frames - 1)
                    SaveScreenshot(screenshot);
                device.Present();
                Thread.Sleep(15);
            }
            return 0;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            renderer.Dispose();
        }

        private void ProcessInput()
        {
            WindowInputEvent input;
            while (window.TryReadInput(out input))
            {
                if (input.Type == WindowInputType.KeyDown)
                {
                    switch (input.Key)
                    {
                        case 27: exitRequested = true; break;
                        case 36: ResetCamera(); break;
                        case 37: yaw -= 0.07f; break;
                        case 39: yaw += 0.07f; break;
                        case 38: pitch += 0.05f; break;
                        case 40: pitch -= 0.05f; break;
                        case 33: distance *= 0.9f; break;
                        case 34: distance *= 1.1f; break;
                    }
                    ClampCamera();
                }
                else if (input.Type == WindowInputType.LeftButtonDown)
                {
                    dragging = true;
                    mouseX = input.X;
                    mouseY = input.Y;
                }
                else if (input.Type == WindowInputType.LeftButtonUp)
                {
                    dragging = false;
                }
                else if (input.Type == WindowInputType.MouseMove && dragging)
                {
                    int deltaX = input.X - mouseX;
                    int deltaY = input.Y - mouseY;
                    mouseX = input.X;
                    mouseY = input.Y;
                    yaw += deltaX * 0.008f;
                    pitch += deltaY * 0.006f;
                    ClampCamera();
                }
                else if (input.Type == WindowInputType.MouseWheel)
                {
                    distance *= input.Key > 0 ? 0.9f : 1.1f;
                    ClampCamera();
                }
            }
        }

        private void ResetCamera()
        {
            yaw = 0.55f;
            pitch = 0.32f;
            distance = Math.Max(radius * 3.4f, 2.0f);
        }

        private void ClampCamera()
        {
            pitch = Math.Max(-0.35f, Math.Min(1.35f, pitch));
            float minimum = Math.Max(0.25f, radius * 0.65f);
            float maximum = Math.Max(minimum * 2.0f, radius * 20.0f);
            distance = Math.Max(minimum, Math.Min(maximum, distance));
        }

        private Vector3 GetEye()
        {
            float horizontal = (float)Math.Cos(pitch) * distance;
            return new Vector3(
                center.X + (float)Math.Sin(yaw) * horizontal,
                center.Y - (float)Math.Cos(yaw) * horizontal,
                center.Z + (float)Math.Sin(pitch) * distance);
        }

        private void SaveScreenshot(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            byte[] pixels = device.ReadBackBufferBgra(device.Width, device.Height);
            BmpWriter.WriteBgra(fullPath, device.Width, device.Height, pixels);
            Console.WriteLine("Screenshot: " + fullPath);
        }
    }
}
