using System;
using System.Globalization;
using System.IO;
using System.Threading;
using SharpGTA.Assets;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.World;

namespace SharpGTA.D3D9
{
    internal sealed class WorldViewerApplication : IDisposable
    {
        private readonly Win32Window window;
        private readonly D3D9Device device;
        private readonly GameDataLoader loader;
        private readonly WorldResourceManager resources;
        private readonly D3D9ModelRenderer renderer;
        private readonly FreeFlyCamera camera = new FreeFlyCamera();
        private readonly Vector3 spawn;
        private readonly bool[] keys = new bool[256];
        private bool exitRequested;
        private bool lookActive;
        private int pendingMoveKey = -1;
        private int mouseX;
        private int mouseY;
        private bool disposed;

        internal WorldViewerApplication(Win32Window window, D3D9Device device,
            GameDataLoader loader)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (device == null) throw new ArgumentNullException("device");
            if (loader == null) throw new ArgumentNullException("loader");
            this.window = window;
            this.device = device;
            this.loader = loader;
            loader.LoadStandard();
            resources = new WorldResourceManager(loader);
            renderer = new D3D9ModelRenderer(device, resources.Textures);
            spawn = new Vector3(812.0f, -936.0f, 36.0f);
            ResetCamera();
        }

        internal int Run(int frames, string screenshot, bool exerciseReset)
        {
            if (screenshot != null && frames <= 0)
                throw new ArgumentException("A finite positive --frames value is required with --screenshot.");
            Console.WriteLine("WORLD PREVIEW / FREE FLY (not player gameplay): " + loader.World.Count + " static IPL instances.");
            Console.WriteLine("Controls: WASD move, Q/E height, RMB look, F focus, G ground query, Home reset, Esc exit.");
            bool diagnosticsPrinted = false;
            for (int frame = 0; frames == 0 || frame < frames; frame++)
            {
                if (!window.ProcessMessages())
                    break;
                ProcessInput();
                if (exitRequested)
                    break;
                Vector3 previousPosition = camera.Position;
                camera.Move(keys[87], keys[83], keys[65], keys[68], keys[81], keys[69], 1.0f / 30.0f);
                if (pendingMoveKey >= 0 && (camera.Position - previousPosition).LengthSquared() > 0.0f)
                {
                    PrintCamera("Move:" + (char)pendingMoveKey);
                    pendingMoveKey = -1;
                }
                if (window.ClientWidth > 0 && window.ClientHeight > 0 &&
                    (window.ClientWidth != device.Width || window.ClientHeight != device.Height))
                    device.Reset(window.Handle, window.ClientWidth, window.ClientHeight);
                if (exerciseReset && frame > 0)
                    device.Reset(window.Handle, device.Width, device.Height);

                RenderSceneSnapshot snapshot = resources.BuildVisible(loader.World.Instances, camera.Position, 260.0f);
                renderer.Prepare(snapshot);
                if (!diagnosticsPrinted && snapshot.DrawCalls.Count > 0)
                {
                    diagnosticsPrinted = true;
                    Console.WriteLine("World-Render: models=" + resources.ModelCount + " txd=" + resources.TextureDictionaryCount +
                        " drawCalls=" + snapshot.DrawCalls.Count + " textures=" + renderer.TextureCount +
                        " failedModels=" + resources.FailedModelCount + " missingTextures=" + resources.MissingTextureCount);
                    if (resources.FirstFailure != null)
                        Console.WriteLine("World-Render-First-Failure: " + resources.FirstFailure);
                    string[] missingTextures = resources.MissingTextureDetails;
                    for (int i = 0; i < missingTextures.Length && i < 4; i++)
                        Console.WriteLine("World-Render-Missing-Texture: " + missingTextures[i]);
                    if (missingTextures.Length > 4)
                        Console.WriteLine("World-Render-Missing-Texture: " + (missingTextures.Length - 4) + " more.");
                }
                renderer.Render(snapshot,
                    D3dMatrix.LookAt(camera.Position, camera.Target, Vector3.UnitZ),
                    D3dMatrix.PerspectiveFov((float)(Math.PI / 3.0), device.Width / (float)device.Height,
                        0.05f, 1800.0f), 0xFFE0E0E0);
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
            try
            {
                renderer.Dispose();
            }
            finally
            {
                resources.Dispose();
            }
        }

        private void ProcessInput()
        {
            WindowInputEvent input;
            while (window.TryReadInput(out input))
            {
                if (input.Type == WindowInputType.InputReset)
                {
                    Array.Clear(keys, 0, keys.Length);
                    pendingMoveKey = -1;
                    lookActive = false;
                    PrintCamera("InputReset");
                }
                else if (input.Type == WindowInputType.KeyDown)
                {
                    if (input.Key >= 0 && input.Key < keys.Length)
                        keys[input.Key] = true;
                    if (IsMovementKey(input.Key))
                        pendingMoveKey = input.Key;
                    if (input.Key == 27)
                        exitRequested = true;
                    else if (input.Key == 36)
                    {
                        ResetCamera();
                        PrintCamera("Home");
                    }
                    else if (input.Key == 70)
                    {
                        ResetCamera();
                        PrintCamera("F");
                    }
                    else if (input.Key == 71)
                        QueryGround();
                }
                else if (input.Type == WindowInputType.KeyUp)
                {
                    if (input.Key >= 0 && input.Key < keys.Length)
                        keys[input.Key] = false;
                    if (IsMovementKey(input.Key))
                    {
                        if (pendingMoveKey == input.Key)
                            pendingMoveKey = -1;
                        PrintCamera("KeyUp:" + (char)input.Key);
                    }
                }
                else if (input.Type == WindowInputType.RightButtonDown)
                {
                    lookActive = true;
                    mouseX = input.X;
                    mouseY = input.Y;
                }
                else if (input.Type == WindowInputType.RightButtonUp)
                {
                    lookActive = false;
                    PrintCamera("RMB");
                }
                else if (input.Type == WindowInputType.MouseMove && lookActive)
                {
                    int dx = input.X - mouseX;
                    int dy = input.Y - mouseY;
                    mouseX = input.X;
                    mouseY = input.Y;
                    camera.Look(dx * 0.006f, -dy * 0.006f);
                }
            }
        }

        private static bool IsMovementKey(int key)
        {
            return key == 87 || key == 65 || key == 83 || key == 68 || key == 81 || key == 69;
        }

        private void PrintCamera(string action)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Camera: action={0} position=({1:R},{2:R},{3:R}) yaw={4:R} pitch={5:R}",
                action, camera.Position.X, camera.Position.Y, camera.Position.Z,
                camera.Yaw, camera.Pitch));
        }

        private void ResetCamera()
        {
            StaticWorldHit hit;
            float ground = spawn.Z;
            if (loader.World.QueryVertical(spawn.X, spawn.Y, spawn.Z - 100.0f, spawn.Z + 100.0f, out hit))
                ground = hit.Position.Z;
            camera.Position = new Vector3(spawn.X + 12.0f, spawn.Y - 24.0f, ground + 10.0f);
            camera.Yaw = 0.0f;
            camera.Pitch = -0.22f;
            camera.Look(0.0f, 0.0f);
        }

        private void QueryGround()
        {
            StaticWorldHit hit;
            if (loader.World.QueryVertical(camera.Position.X, camera.Position.Y,
                camera.Position.Z - 300.0f, camera.Position.Z + 300.0f, out hit))
            {
                Console.WriteLine("Ground: model=" + hit.Instance.Model.Name +
                    " position=(" + hit.Position.X + "," + hit.Position.Y + "," + hit.Position.Z + ") surface=" + hit.Surface);
            }
            else
                Console.WriteLine("Ground: no static COLL hit below the free-fly camera.");
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
