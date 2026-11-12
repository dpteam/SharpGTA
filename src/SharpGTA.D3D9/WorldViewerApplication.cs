using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SharpGTA.Assets;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;
using SharpGTA.Runtime;
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
        private const float ViewRadius = 260.0f;
        private const float StreamRadius = ViewRadius * 1.5f;
        private const int VerifyFrameLimit = 30;
        private readonly FreeFlyCamera camera = new FreeFlyCamera();
        private readonly SimulationRuntime runtime;
        private readonly GameClock clock;
        private readonly TimeObjectController timeObjects = new TimeObjectController();
        private readonly List<StaticInstance> candidates = new List<StaticInstance>();
        private readonly List<StaticInstance> verifyBuffer = new List<StaticInstance>();
        private readonly HashSet<StaticInstance> verifySet = new HashSet<StaticInstance>();
        private readonly bool verifySectors;
        private bool verifySectorsFailed;
        private bool verifySectorsDone;
        private bool sectorsPrinted;
        private int verifyFrameCount;
        private readonly Vector3 spawn;
        private readonly bool[] keys = new bool[256];
        private bool exitRequested;
        private bool lookActive;
        private int pendingMoveKey = -1;
        private int mouseX;
        private int mouseY;
        private bool disposed;

        internal WorldViewerApplication(Win32Window window, D3D9Device device,
            GameDataLoader loader, int modelBudget, int txdBudget, int byteBudget, int startHour,
            bool verifySectors)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (device == null) throw new ArgumentNullException("device");
            if (loader == null) throw new ArgumentNullException("loader");
            if (modelBudget <= 0) throw new ArgumentOutOfRangeException("modelBudget");
            if (txdBudget <= 0) throw new ArgumentOutOfRangeException("txdBudget");
            if (byteBudget <= 0) throw new ArgumentOutOfRangeException("byteBudget");
            if (startHour < 0 || startHour > 23) throw new ArgumentOutOfRangeException("startHour");
            this.window = window;
            this.device = device;
            this.loader = loader;
            loader.LoadStandard();
            runtime = new SimulationRuntime(loader.Definition);
            clock = new GameClock();
            runtime.AddModule(clock);
            resources = new WorldResourceManager(loader, modelBudget, txdBudget, byteBudget);
            renderer = new D3D9ModelRenderer(device, resources.Textures);
            resources.AttachRenderer(renderer);
            clock.SetTime(startHour, 0);
            this.verifySectors = verifySectors;
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
                // The 30 Hz deterministic driver advances the clock one game minute per
                // 30 rendered frames, so a preview never depends on wall-clock speed.
                runtime.Step();
                timeObjects.Update(loader.World.Instances, clock);
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

                // Sector candidates cover the streaming radius, so the prefetch ring just
                // outside the view radius stays inside one sector query.
                int candidateCount = loader.World.QuerySectors(camera.Position, StreamRadius, candidates);
                if (verifySectors && verifyFrameCount < VerifyFrameLimit)
                {
                    // The candidate set changes as the camera moves, so several frames are
                    // verified. Only the first result is printed; a long menu-driven run
                    // stops verifying after the limit to keep the debug cost bounded.
                    if (!VerifySectorQuery(candidateCount, !verifySectorsDone))
                        verifySectorsFailed = true;
                    verifySectorsDone = true;
                    verifyFrameCount++;
                }
                if (!sectorsPrinted)
                {
                    sectorsPrinted = true;
                    Console.WriteLine("World-Sectors: candidates=" + candidateCount +
                        " instances=" + loader.World.Count +
                        " unindexed=" + loader.World.UnindexedCount);
                }
                RenderSceneSnapshot snapshot = resources.BuildVisible(candidates,
                    camera.Position, ViewRadius, frame == 0, timeObjects);
                renderer.Prepare(snapshot);
                if (!diagnosticsPrinted && snapshot.DrawCalls.Count > 0)
                {
                    diagnosticsPrinted = true;

                    Console.WriteLine("World-Entities: " + EntitySummary());
                    Console.WriteLine("World-Time: hour=" + clock.Hours.ToString("00", CultureInfo.InvariantCulture) +
                        ":" + clock.Minutes.ToString("00", CultureInfo.InvariantCulture) +
                        " day=" + clock.DaysPassed +
                        " timed=" + timeObjects.AllocatedCount + "/" + timeObjects.Capacity +
                        " created=" + timeObjects.CreatedCount +
                        " destroyed=" + timeObjects.DestroyedCount +
                        " rejected=" + timeObjects.RejectedCount);
                    Console.WriteLine("World-Render: models=" + resources.ModelCount + " txd=" + resources.TextureDictionaryCount +
                        " drawCalls=" + snapshot.DrawCalls.Count + " textures=" + renderer.TextureCount +
                        " failedModels=" + resources.FailedModelCount + " missingTextures=" + resources.MissingTextureCount +
                        " gpuMeshes=" + renderer.MeshCount + " gpuVertices=" + renderer.VertexCount +
                        " gpuIndices=" + renderer.IndexCount + " backend=indexed-managed");
                    Console.WriteLine("World-Streaming: models=" + resources.ModelCount + "/" + resources.ModelCapacity +
                        " txds=" + resources.TextureDictionaryCount + "/" + resources.TxdCapacity +
                        " evictedModels=" + resources.ModelEvictionCount +
                        " evictedTxds=" + resources.TxdEvictionCount +
                        " remaining=" + resources.LastLoadReport.RemainingRequestedCount +
                        " deferred=" + resources.LastLoadReport.DeferredCount);
                    Console.WriteLine("World-Bytes: cached=" + resources.ByteCacheCount +
                        " bytes=" + resources.ByteCacheBytes +
                        "/" + resources.ByteCacheCapacity +
                        " pending=" + resources.ByteCachePending +
                        " evicted=" + resources.ByteCacheEvictions +
                        " failed=" + resources.ByteCacheFailures +
                        " oversize=" + resources.ByteCacheOversized);
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
            if (diagnosticsPrinted)
            {
                Console.WriteLine("World-Time-Final: hour=" + clock.Hours.ToString("00", CultureInfo.InvariantCulture) +
                    ":" + clock.Minutes.ToString("00", CultureInfo.InvariantCulture) +
                    " day=" + clock.DaysPassed +
                    " timed=" + timeObjects.AllocatedCount + "/" + timeObjects.Capacity +
                    " created=" + timeObjects.CreatedCount +
                    " destroyed=" + timeObjects.DestroyedCount +
                    " rejected=" + timeObjects.RejectedCount);
                Console.WriteLine("World-Streaming-Final: models=" + resources.ModelCount +
                    "/" + resources.ModelCapacity +
                    " txds=" + resources.TextureDictionaryCount + "/" + resources.TxdCapacity +
                    " evictedModels=" + resources.ModelEvictionCount +
                    " evictedTxds=" + resources.TxdEvictionCount +
                    " remaining=" + resources.LastLoadReport.RemainingRequestedCount +
                    " deferred=" + resources.LastLoadReport.DeferredCount);
                Console.WriteLine("World-Bytes-Final: cached=" + resources.ByteCacheCount +
                    " bytes=" + resources.ByteCacheBytes + "/" + resources.ByteCacheCapacity +
                    " pending=" + resources.ByteCachePending +
                    " evicted=" + resources.ByteCacheEvictions +
                    " failed=" + resources.ByteCacheFailures +
                    " oversize=" + resources.ByteCacheOversized);
            }
            return verifySectorsFailed ? 3 : 0;
        }

        private string EntitySummary()
        {
            WorldEntities entities = loader.World.Entities;
            return "buildings=" + entities.Count(WorldEntityKind.Building) +
                "/" + entities.Capacity(WorldEntityKind.Building) +
                " dummies=" + entities.Count(WorldEntityKind.Dummy) +
                "/" + entities.Capacity(WorldEntityKind.Dummy) +
                " treadables=" + entities.Count(WorldEntityKind.Treadable) +
                "/" + entities.Capacity(WorldEntityKind.Treadable) +
                " objects=" + entities.Count(WorldEntityKind.Object) +
                "/" + entities.Capacity(WorldEntityKind.Object) +
                " rejected=" + loader.World.InstancesWithoutHandle;
        }

        /// <summary>
        /// Cross-checks the sector index against a linear scan with the same candidate
        /// predicate, so a broken grid can never silently drop world geometry.
        /// </summary>
        private bool VerifySectorQuery(int candidateCount, bool print)
        {
            int expected = loader.World.QuerySectorsLinear(camera.Position, StreamRadius, verifyBuffer);
            verifySet.Clear();
            for (int i = 0; i < candidates.Count; i++)
                verifySet.Add(candidates[i]);
            int extra = 0;
            for (int i = 0; i < verifyBuffer.Count; i++)
            {
                if (!verifySet.Contains(verifyBuffer[i]))
                    extra++;
            }
            int missing = candidates.Count - (expected - extra);
            bool matched = missing == 0 && extra == 0;
            if (print || !matched)
            {
                Console.WriteLine("World-Sectors-Verify: sectors=" + candidateCount +
                    " linear=" + expected + " missing=" + missing + " extra=" + extra);
            }
            return matched;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            try
            {
                resources.Dispose();
            }
            finally
            {
                renderer.Dispose();
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
