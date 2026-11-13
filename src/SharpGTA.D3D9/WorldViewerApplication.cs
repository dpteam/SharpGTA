using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using SharpGTA.Assets;
using SharpGTA.Data;
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

        /// <summary>
        /// Route length used by the preview when recycling; short enough that actors finish inside
        /// a normal preview run, unlike the forty-hop routes the headless probes use.
        /// </summary>
        private const int PreviewRecycleHops = 6;
        private readonly FreeFlyCamera camera = new FreeFlyCamera();
        private readonly SimulationRuntime runtime;
        private readonly GameClock clock;
        private readonly TimeObjectController timeObjects = new TimeObjectController();
        private readonly List<StaticInstance> candidates = new List<StaticInstance>();
        private readonly List<StaticInstance> verifyBuffer = new List<StaticInstance>();
        private readonly HashSet<StaticInstance> verifySet = new HashSet<StaticInstance>();
        private readonly bool verifySectors;
        private readonly bool pathOverlay;
        private readonly bool pathRoute;
        private readonly float[] routePoints;
        private readonly PathKind routeKind;
        private readonly float routeRadius;
        private bool routeReported;
        private const int MaxPathOverlaySegments = 20000;
        private bool pathOverlayReported;
        private RenderLineBatch pathOverlayBatch;
        private RenderLineBatch routeBatch;
        private int dynamicSpawned;
        private int dynamicCars;
        private int dynamicPeds;
        private PathFollowerModule dynamicFollowers;
        private TrafficPopulationModule dynamicPopulation;
        private bool dynamicModuleAdded;
        private string dynamicCarModel = "";
        private string dynamicPedModel = "";
        private float dynamicCarNearest;
        private float dynamicPedNearest;
        private int dynamicDrawn;
        private int dynamicRejected;
        private readonly List<RenderDrawCall> mergedCalls = new List<RenderDrawCall>();
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
            bool verifySectors, int spawnCount, string spawnModel, bool pathOverlay,
            bool pathRoute, float[] routePoints, string pathKind, float pathRouteRadius,
            int spawnCarCount, float spawnCarSpeed, int spawnPedCount, float spawnPedSpeed,
            bool recycle)
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
            this.pathOverlay = pathOverlay || pathRoute;
            this.pathRoute = pathRoute;
            this.routePoints = routePoints;
            this.routeKind = ParsePathKind(pathKind);
            this.routeRadius = pathRouteRadius;
            spawn = new Vector3(812.0f, -936.0f, 36.0f);
            ResetCamera();
            if (spawnCount > 0)
                SpawnDynamicEntities(spawnCount, spawnModel);
            if (spawnCarCount > 0 || spawnPedCount > 0)
                SpawnActors(spawnCarCount, spawnPedCount, spawnModel, spawnCarSpeed, spawnPedSpeed, recycle);
        }

        /// <summary>
        /// Spawns objects in front of the spawn point so the preview actually shows gameplay
        /// entities. They drift along X through the shared sector grid every frame.
        /// </summary>
        /// <summary>
        /// Spawns actors that follow one path network: cars on the car network, peds on the ped
        /// network. The model comes from --spawn-model when its kind matches, else the first
        /// record of that kind. Both share a single follower module and a single integrator, so
        /// --spawn-cars + --spawn-peds never integrates an entity twice.
        /// </summary>
        private void SpawnActors(int carCount, int pedCount, string modelName,
            float carSpeed, float pedSpeed, bool recycle)
        {
            if (loader.Paths.GroupCount == 0)
                throw new ArgumentException(
                    "--spawn-cars/--spawn-peds needs loaded path data; add --standard.");
            PathGraph graph = loader.PathGraph;
            // A forty-hop route in III is roughly 2800 units long, which no short preview run can
            // complete, so recycling there uses short routes and the lifecycle stays observable.
            int hops = recycle ? PreviewRecycleHops : 40;
            PathFollowerModule followers = new PathFollowerModule(graph, loader.World);
            if (!recycle)
            {
                // Recycling needs actors that actually finish; a provider would keep every actor
                // moving forever and nothing would ever be replaced.
                followers.RouteSource = delegate(int node)
                {
                    return PathRouteBuilder.BuildGreedy(graph, node, 40, true);
                };
            }
            ModelDefinition carModel = null;
            ModelDefinition pedModel = null;
            if (carCount > 0)
            {
                carModel = ResolveActorModel(modelName, ModelKind.Vehicle,
                    "--spawn-cars needs a registered vehicle model.");
                dynamicCars = SpawnFollowers(followers, graph, PathKind.Car,
                    WorldEntityKind.Vehicle, carModel, carSpeed, carCount, hops);
                dynamicCarModel = carModel.Name;
                dynamicCarNearest = NearestActor(WorldEntityKind.Vehicle);
            }
            if (pedCount > 0)
            {
                pedModel = ResolveActorModel(modelName, ModelKind.Ped,
                    "--spawn-peds needs a registered ped model.");
                dynamicPeds = SpawnFollowers(followers, graph, PathKind.Ped,
                    WorldEntityKind.Ped, pedModel, pedSpeed, pedCount, hops);
                dynamicPedModel = pedModel.Name;
                dynamicPedNearest = NearestActor(WorldEntityKind.Ped);
            }
            if (recycle)
            {
                // Replacements stay near the preview spawn instead of appearing city-wide: this
                // is the first place where the population anchor is a camera, not the origin.
                dynamicPopulation = new TrafficPopulationModule(loader.World, graph, followers);
                dynamicPopulation.RouteHops = hops;
                if (dynamicCars > 0)
                {
                    dynamicPopulation.AddCohort(PathKind.Car, WorldEntityKind.Vehicle, carModel,
                        carSpeed, dynamicCars, spawn, ViewRadius);
                }
                if (dynamicPeds > 0)
                {
                    dynamicPopulation.AddCohort(PathKind.Ped, WorldEntityKind.Ped, pedModel,
                        pedSpeed, dynamicPeds, spawn, ViewRadius);
                }
            }
            dynamicFollowers = followers;
            dynamicRejected = loader.World.DynamicRejectedCount;
            EnsureDynamicModule();
            // Followers are registered after the integrator so their positions win.
            runtime.AddModule(followers);
            if (dynamicPopulation != null)
                runtime.AddModule(dynamicPopulation);
        }

        private ModelDefinition ResolveActorModel(string modelName, ModelKind kind, string message)
        {
            if (modelName != null)
            {
                ModelDefinition named = loader.Models.Find(modelName);
                if (named != null && named.Kind == kind && !ModelRegistry.IsPlaceholder(named))
                    return named;
                if (named != null)
                {
                    Console.Error.WriteLine("--spawn-model '" + modelName + "' is not a spawnable " +
                        kind + " model; using the first " + kind + " instead.");
                }
            }
            ModelDefinition model;
            if (loader.Models.TryFindFirst(kind, true, out model))
                return model;
            throw new ArgumentException(message);
        }

        /// <summary>
        /// Starts <paramref name="count"/> actors on the nearest nodes of one kind, so they are
        /// close enough to survive the model draw distance. Nodes in front of the camera are
        /// preferred, because a ped network can start behind the camera and 150 issued draw
        /// calls that are all clipped look exactly like a broken spawner. Returns how many
        /// actors actually spawned.
        /// </summary>
        private int SpawnFollowers(PathFollowerModule followers, PathGraph graph, PathKind pathKind,
            WorldEntityKind entityKind, ModelDefinition model, float speed, int count, int hops)
        {
            const int MaxCandidates = 64;
            List<int> candidates = new List<int>();
            List<float> distances = new List<float>();
            List<bool> inFront = new List<bool>();
            Vector3 forward = camera.Forward;
            float radiusSquared = ViewRadius * ViewRadius;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                PathGraphNode node = graph.Nodes[i];
                if (node.Kind != pathKind || node.LinkCount == 0)
                    continue;
                float distanceSquared = (node.Position - spawn).LengthSquared();
                if (distanceSquared > radiusSquared)
                    continue;
                int insert = candidates.Count;
                while (insert > 0 && distances[insert - 1] > distanceSquared)
                    insert--;
                candidates.Insert(insert, i);
                distances.Insert(insert, distanceSquared);
                inFront.Insert(insert, Vector3.Dot(node.Position - camera.Position, forward) > 0.0f);
                if (candidates.Count > MaxCandidates)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                    distances.RemoveAt(distances.Count - 1);
                    inFront.RemoveAt(inFront.Count - 1);
                }
            }

            int spawned = 0;
            // Pass 0 takes candidates the camera can actually see, pass 1 fills the rest in case
            // the whole network of that kind sits behind the spawn point.
            for (int pass = 0; pass < 2 && spawned < count; pass++)
            {
                for (int c = 0; c < candidates.Count && spawned < count; c++)
                {
                    if (inFront[c] != (pass == 0))
                        continue;
                    int i = candidates[c];
                    PathGraphNode node = graph.Nodes[i];
                    int[] route = PathRouteBuilder.BuildGreedy(graph, i, hops, true);
                    if (route.Length < 2)
                        route = PathRouteBuilder.BuildGreedy(graph, i, hops);
                    if (route.Length < 2)
                        continue;
                    DynamicEntity entity;
                    if (!loader.World.TrySpawnEntity(entityKind, model, node.Position,
                        Vector3.Zero, out entity))
                        return spawned;
                    if (!followers.Assign(entity, route, speed))
                        continue;
                    spawned++;
                }
            }
            return spawned;
        }

        private float NearestActor(WorldEntityKind kind)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < loader.World.DynamicEntities.Count; i++)
            {
                DynamicEntity candidate = loader.World.DynamicEntities[i] as DynamicEntity;
                if (candidate == null || candidate.Kind != kind)
                    continue;
                float distance = (candidate.Position - camera.Position).Length();
                if (distance < nearest)
                    nearest = distance;
            }
            // No actor of that kind survived, so report a neutral distance instead of "inf".
            return nearest == float.MaxValue ? 0.0f : nearest;
        }

        private void SpawnDynamicEntities(int count, string modelName)
        {
            if (loader.World.Count == 0)
                throw new ArgumentException("--spawn requires loaded map data.");
            ModelDefinition model = modelName == null
                ? loader.World.Instances[0].Model
                : loader.Models.Find(modelName);
            if (model == null)
            {
                Console.Error.WriteLine("--spawn-model '" + modelName +
                    "' is not registered; spawning the first placement's model instead.");
                model = loader.World.Instances[0].Model;
            }
            // Place the row in front of the camera, spread sideways, standing on the ground.
            Vector3 forward = camera.Forward;
            Vector3 right = camera.Right;
            Vector3 origin = camera.Position + forward * 20.0f;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                DynamicEntity entity;
                Vector3 position = origin + right * ((i % 8) * 4.0f - 14.0f) +
                    forward * ((i / 8) * 8.0f);
                StaticWorldHit hit;
                if (loader.World.QueryVertical(position.X, position.Y,
                    position.Z + 20.0f, position.Z - 40.0f, out hit))
                    position.Z = hit.Position.Z;
                if (loader.World.TrySpawnEntity(WorldEntityKind.Object, model, position,
                    new Vector3(0.0f, 2.0f, 0.0f), out entity))
                    spawned++;
                else
                    break;
            }
            dynamicSpawned = spawned;
            dynamicRejected = loader.World.DynamicRejectedCount;
            EnsureDynamicModule();
        }

        /// <summary>
        /// The linear integrator must exist exactly once: adding it from several spawn paths
        /// (--spawn, --spawn-cars, --spawn-peds) threw "Duplicate simulation module name".
        /// </summary>
        private void EnsureDynamicModule()
        {
            if (dynamicModuleAdded)
                return;
            dynamicModuleAdded = true;
            runtime.AddModule(new DynamicEntityModule(loader.World));
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
                resources.BeginFrame();
                // Gameplay entities get the frame's first pick of streaming capacity: they are
                // the objects the player is looking at, and a starved budget must not hide them
                // behind map models. Draw calls stay static-first to keep the previous order.
                RenderSceneSnapshot dynamicSnapshot = null;
                if (dynamicSpawned > 0 || dynamicCars > 0 || dynamicPeds > 0)
                    dynamicSnapshot = resources.BuildDynamic(loader.World.DynamicEntities,
                        camera.Position, ViewRadius, frame == 0);
                RenderSceneSnapshot staticSnapshot = resources.BuildVisible(candidates,
                    camera.Position, ViewRadius, frame == 0, timeObjects);
                RenderSceneSnapshot snapshot = staticSnapshot;
                if (dynamicSnapshot != null)
                {
                    mergedCalls.Clear();
                    mergedCalls.AddRange(staticSnapshot.DrawCalls);
                    mergedCalls.AddRange(dynamicSnapshot.DrawCalls);
                    dynamicDrawn = dynamicSnapshot.DrawCalls.Count;
                    snapshot = new RenderSceneSnapshot(mergedCalls);
                }
                renderer.Prepare(snapshot);
                if (!diagnosticsPrinted && snapshot.DrawCalls.Count > 0)
                {
                    diagnosticsPrinted = true;

                    Console.WriteLine("World-Entities: " + EntitySummary());
                    if (dynamicSpawned > 0 || dynamicCars > 0 || dynamicPeds > 0)
                    {
                        Console.WriteLine("World-Dynamic: spawned=" + (dynamicSpawned + dynamicCars + dynamicPeds) +
                            " alive=" + loader.World.DynamicEntities.Count +
                            " drawn=" + dynamicDrawn +
                            " rejected=" + dynamicRejected +
                            " sectorMoves=" + loader.World.SectorMoves +
                            " deferred=" + resources.LastDynamicLoadReport.DeferredCount);
                    }
                    if (dynamicCars > 0)
                    {
                        Console.WriteLine("World-Cars: spawned=" + dynamicCars +
                            " model=" + dynamicCarModel +
                            " nearest=" + dynamicCarNearest.ToString("0.#", CultureInfo.InvariantCulture) +
                            " alive=" + loader.World.DynamicEntities.Count +
                            " completed=" + dynamicFollowers.CompletedCount +
                            " replans=" + dynamicFollowers.ReplanCount +
                            " travelled=" + dynamicFollowers.TravelledDistance.ToString("0.##",
                                CultureInfo.InvariantCulture) +
                            " rejected=" + dynamicRejected);
                        Console.WriteLine(VehicleGroundProbe.Format(
                            VehicleGroundProbe.Measure(loader.World)));
                    }
                    if (dynamicPeds > 0)
                    {
                        Console.WriteLine("World-Peds: spawned=" + dynamicPeds +
                            " model=" + dynamicPedModel +
                            " nearest=" + dynamicPedNearest.ToString("0.#", CultureInfo.InvariantCulture) +
                            " alive=" + loader.World.DynamicEntities.Count +
                            " completed=" + dynamicFollowers.CompletedCount +
                            " replans=" + dynamicFollowers.ReplanCount +
                            " travelled=" + dynamicFollowers.TravelledDistance.ToString("0.##",
                                CultureInfo.InvariantCulture) +
                            " rejected=" + dynamicRejected);
                    }
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
                if (pathOverlay)
                    pathOverlayBatch = BuildPathOverlay();
                renderer.Render(snapshot,
                    D3dMatrix.LookAt(camera.Position, camera.Target, Vector3.UnitZ),
                    D3dMatrix.PerspectiveFov((float)(Math.PI / 3.0), device.Width / (float)device.Height,
                        0.05f, 1800.0f), 0xFFE0E0E0, pathOverlayBatch);
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
                // The per-frame lines are printed on the first diagnostic frame, so only this
                // final line proves the actors kept moving for the whole run.
                if (dynamicFollowers != null)
                {
                    string actorsFinal = "World-Actors-Final: cars=" + dynamicCars +
                        " peds=" + dynamicPeds +
                        " completed=" + dynamicFollowers.CompletedCount +
                        " replans=" + dynamicFollowers.ReplanCount +
                        " travelled=" + dynamicFollowers.TravelledDistance.ToString("0.##",
                            CultureInfo.InvariantCulture);
                    if (dynamicPopulation != null)
                    {
                        actorsFinal += " recycled=" + dynamicPopulation.RecycledCount +
                            " respawned=" + dynamicPopulation.SpawnedCount;
                    }
                    Console.WriteLine(actorsFinal);
                }
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

        private RenderLineBatch BuildPathOverlay()
        {
            PathGraph graph = loader.PathGraph;
            pathOverlayBatch = PathOverlayBuilder.Build(graph, camera.Position, ViewRadius * 4.0f,
                MaxPathOverlaySegments);
            if (pathOverlayBatch.SegmentCount > 0 && !pathOverlayReported)
            {
                pathOverlayReported = true;
                Console.WriteLine("Path-Overlay: segments=" + pathOverlayBatch.SegmentCount +
                    " nodes=" + graph.NodeCount +
                    " radius=" + (ViewRadius * 4.0f).ToString("0.#", CultureInfo.InvariantCulture));
            }
            if (pathRoute)
            {
                RenderLineBatch routeBatch = BuildRouteOverlay(graph);
                pathOverlayBatch = PathOverlayBuilder.Concat(pathOverlayBatch, routeBatch);
            }
            return pathOverlayBatch;
        }

        /// <summary>
        /// Resolves both route endpoints once and draws the node chain in a single colour. The
        /// endpoints live in the command line, so the search result cannot change between frames.
        /// </summary>
        private RenderLineBatch BuildRouteOverlay(PathGraph graph)
        {
            if (routeBatch != null)
                return routeBatch;
            Vector3 start = new Vector3(routePoints[0], routePoints[1], routePoints[2]);
            Vector3 end = new Vector3(routePoints[3], routePoints[4], routePoints[5]);
            PathGraphNode startNode;
            float startDistance;
            if (!graph.TryFindNearestNode(start, routeKind, routeRadius, out startNode,
                out startDistance))
            {
                Console.WriteLine("Path-Overlay-Route: found=false reason=start-not-found");
                routeBatch = new RenderLineBatch(new Vector3[0], new uint[0]);
                return routeBatch;
            }
            PathGraphNode endNode;
            float endDistance;
            if (!graph.TryFindNearestNode(end, routeKind, routeRadius, out endNode, out endDistance))
            {
                Console.WriteLine("Path-Overlay-Route: found=false reason=end-not-found");
                routeBatch = new RenderLineBatch(new Vector3[0], new uint[0]);
                return routeBatch;
            }
            int[] route;
            float length;
            if (!graph.TryFindRoute(startNode.GlobalIndex, endNode.GlobalIndex, out route, out length))
            {
                Console.WriteLine("Path-Overlay-Route: found=false reason=disconnected" +
                    " startComponent=" + startNode.GroupId +
                    " endComponent=" + endNode.GroupId);
                routeBatch = new RenderLineBatch(new Vector3[0], new uint[0]);
                return routeBatch;
            }
            if (!routeReported)
            {
                routeReported = true;
                Console.WriteLine("Path-Overlay-Route: kind=" + routeKind +
                    " startNode=" + startNode.GlobalIndex +
                    " endNode=" + endNode.GlobalIndex +
                    " hops=" + (route.Length - 1) +
                    " length=" + length.ToString("0.##", CultureInfo.InvariantCulture) +
                    " component=" + startNode.GroupId);
            }
            routeBatch = PathOverlayBuilder.BuildRoute(graph, route);
            return routeBatch;
        }

        private static PathKind ParsePathKind(string value)
        {
            if (value == "ped")
                return PathKind.Ped;
            if (value == "water")
                return PathKind.Water;
            return PathKind.Car;
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
