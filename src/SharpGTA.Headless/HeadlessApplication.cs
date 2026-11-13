using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.World;
using SharpGTA.Data;
using SharpGTA.Games.III;
using SharpGTA.Games.VC;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Runtime;

namespace SharpGTA.Headless
{
    public static class HeadlessApplication
    {
        private const int MaximumLooseAssetLength = 256 * 1024 * 1024;

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            if (error == null)
                throw new ArgumentNullException("error");
            try
            {
                HeadlessOptions options = HeadlessOptions.Parse(args);
                if (options.Help)
                {
                    WriteBanner(output);
                    WriteUsage(output);
                    return 0;
                }
                if (options.Version)
                {
                    output.WriteLine(Branding.Title);
                    return 0;
                }

                GameDefinition definition = SelectGame(options.Game);
                WriteBanner(output);
                output.WriteLine("Game: " + options.Game.ToLowerInvariant() + " (" + definition.DisplayName + ")");
                GameClock gameClock = null;
                bool inconsistentPathGraph = false;
                int carsSpawned = 0;
                int pedsSpawned = 0;
                string carModelName = null;
                string pedModelName = null;
                TrafficPopulationModule population = null;
                CharacterMoveModule walkerModule = null;
                bool dynamicModuleAdded = false;
                using (GameDataLoader loader = CreateLoader(definition, options))
                {
                    if (loader != null)
                    {
                        LoadAssets(loader, options, output);
                        if (options.HasGroundQuery)
                            WriteGroundQuery(loader.World, options, output);
                        if (options.HasZoneQuery)
                            WriteZoneQuery(loader.Zones, options, output);
                        if (options.HasPathQuery)
                            WritePathQuery(loader.Paths, options, output);
                        if (options.HasPathNearestQuery)
                            WritePathNearestQuery(loader, options, output);
                        if (options.HasPathRouteQuery)
                            WritePathRouteQuery(loader, options, output);
                        if (options.VerifyPaths && !WritePathVerification(loader, options, output))
                            inconsistentPathGraph = true;
                        if (options.HasBuildingQuery)
                            WriteBuildingQuery(loader.World, options, output);
                        if (options.Verbose)
                        {
                            WriteIssues(loader.Report, output);
                            WriteUnresolvedPaths(loader.Paths, output);
                        }
                    }

                    SimulationRuntime runtime = new SimulationRuntime(definition,
                        options.HasWalk ? WalkInputSource.Instance : null, null, null);
                    gameClock = new GameClock();
                    runtime.AddModule(gameClock);
                    PathFollowerModule followers = null;
                    if (options.HasWalk)
                    {
                        // A player-shaped probe: one ped, scripted forward input, COLL walls.
                        if (loader.World.Count == 0)
                        {
                            throw new CommandLineException(
                                "--walk requires loaded map data; add --standard.");
                        }
                        ModelDefinition walkModel = ResolvePedModel(loader, options);
                        DynamicEntity walker;
                        if (!loader.World.TrySpawnEntity(WorldEntityKind.Ped, walkModel,
                            new Vector3(options.WalkX, options.WalkY, options.WalkZ),
                            Vector3.Zero, out walker))
                        {
                            throw new CommandLineException("--walk could not spawn a ped.");
                        }
                        walkerModule = new CharacterMoveModule(loader.World, walker);
                        walkerModule.Speed = options.WalkSpeed;
                        walkerModule.Heading = (float)Math.Atan2(options.WalkDirectionY,
                            options.WalkDirectionX);
                        runtime.AddModule(walkerModule);
                    }
                    if (options.HasSpawnObjects)
                    {
                        SpawnObjects(loader, options);
                        runtime.AddModule(new DynamicEntityModule(loader.World));
                        dynamicModuleAdded = true;
                    }
                    if (options.HasSpawnCars || options.SpawnPedCount > 0)
                    {
                        if (loader.Paths.GroupCount == 0)
                        {
                            throw new CommandLineException(
                                "--spawn-cars/--spawn-peds require loaded path data; add --standard.");
                        }
                        // One graph for spawning, following and diagnostics, so
                        // --path-merge-distance cannot mean two different worlds in one run.
                        PathGraph followGraph = CreatePathGraph(loader, options);
                        followers = new PathFollowerModule(followGraph, loader.World);
                        // The original always obeys junction lights; the follower defaults to off
                        // so a plain speed probe does not stop without being asked.
                        followers.ObeyTrafficLights = true;
                        // Six units centre to centre keeps a visible gap without collision
                        // volumes; a queue forms behind a red light instead of one shared spot.
                        followers.VehicleGap = 6.0f;
                        if (!options.Recycle)
                        {
                            // Without recycling an actor keeps driving by asking for a new route;
                            // with it the actor must actually finish so the population can replace
                            // it, otherwise nothing would ever be recycled.
                            followers.RouteSource = delegate(int node)
                            {
                                return PathRouteBuilder.BuildGreedy(followGraph, node, 40, true);
                            };
                        }
                        ModelDefinition carModel = null;
                        ModelDefinition pedModel = null;
                        if (options.HasSpawnCars)
                        {
                            carModel = ResolveCarModel(loader, options);
                            carModelName = carModel.Name;
                            carsSpawned = SpawnFollowed(loader, followGraph, followers,
                                PathKind.Car, WorldEntityKind.Vehicle, carModel,
                                options.SpawnCarSpeed, options.SpawnCarCount);
                        }
                        if (options.SpawnPedCount > 0)
                        {
                            pedModel = ResolvePedModel(loader, options);
                            pedModelName = pedModel.Name;
                            pedsSpawned = SpawnFollowed(loader, followGraph, followers,
                                PathKind.Ped, WorldEntityKind.Ped, pedModel,
                                options.SpawnPedSpeed, options.SpawnPedCount);
                        }
                        if (options.Recycle)
                        {
                            // Target equals what actually spawned, so a pool that rejected a few
                            // actors does not make the population spawn extras on the first tick.
                            population = new TrafficPopulationModule(loader.World, followGraph, followers);
                            if (carsSpawned > 0)
                            {
                                population.AddCohort(PathKind.Car, WorldEntityKind.Vehicle,
                                    carModel, options.SpawnCarSpeed, carsSpawned);
                            }
                            if (pedsSpawned > 0)
                            {
                                population.AddCohort(PathKind.Ped, WorldEntityKind.Ped,
                                    pedModel, options.SpawnPedSpeed, pedsSpawned);
                            }
                        }
                        // Registered last so path positions win over plain integration.
                        if (!dynamicModuleAdded)
                            runtime.AddModule(new DynamicEntityModule(loader.World));
                        runtime.AddModule(followers);
                        if (population != null)
                            runtime.AddModule(population);
                    }
                    if (options.RenderPreview)
                        RenderPreviewRunner.Run(loader, options, output);
                    if (options.HasStateAt && options.StateAt == 0 && options.Ticks != 0)
                        output.WriteLine("State@0: " + runtime.ComputeStateHash());
                    while (runtime.Clock.Tick < options.Ticks)
                    {
                        runtime.Step();
                        if (options.HasStateAt && options.StateAt == runtime.Clock.Tick && options.StateAt != options.Ticks)
                        {
                            output.WriteLine("State@" + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture) +
                                ": " + runtime.ComputeStateHash());
                        }
                    }
                    output.WriteLine("Ticks: " + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture));
                    if (options.HasSpawnObjects)
                        WriteDynamicSummary(loader.World, options, output);
                    if (followers != null)
                        WritePathFollowSummary(loader.World, followers, carsSpawned, pedsSpawned,
                            carModelName, pedModelName, output);
                    if (population != null)
                        WriteTrafficSummary(population, output);
                    if (walkerModule != null)
                    {
                        Vector3 walkerPosition = walkerModule.Entity.Position;
                        output.WriteLine("Walk: hits=" +
                            walkerModule.CollisionHitCount.ToString(CultureInfo.InvariantCulture) +
                            " travelled=" +
                            walkerModule.TravelledDistance.ToString("0.##", CultureInfo.InvariantCulture) +
                            " position=" + Vector(walkerPosition));
                    }
                    output.WriteLine("Clock: day=" + gameClock.DaysPassed.ToString(CultureInfo.InvariantCulture) +
                        " time=" + gameClock.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                        gameClock.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                        gameClock.Seconds.ToString("00", CultureInfo.InvariantCulture));
                    output.WriteLine("State@" + runtime.Clock.Tick.ToString(CultureInfo.InvariantCulture) + ": " + runtime.ComputeStateHash());
                    if (options.Strict && loader != null && loader.Report.TotalIssueCount != 0)
                    {
                        error.WriteLine("STRICT: " + loader.Report.TotalIssueCount.ToString(CultureInfo.InvariantCulture) + " asset issue(s) were reported.");
                        return 3;
                    }
                }
                if (inconsistentPathGraph)
                {
                    error.WriteLine("VERIFY: the path graph is internally inconsistent.");
                    return 4;
                }
                return 0;
            }
            catch (CommandLineException exception)
            {
                error.WriteLine("USAGE: " + exception.Message);
                error.WriteLine("Use --help for the command line reference.");
                return 2;
            }
            catch (Exception exception)
            {
                error.WriteLine("ERROR: " + exception.Message);
                return 1;
            }
        }

        private static GameDefinition SelectGame(string value)
        {
            if (string.Equals(value, "iii", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "gta3", StringComparison.OrdinalIgnoreCase) || value == "3")
                return IIIDefinition.Instance;
            if (string.Equals(value, "vc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "vicecity", StringComparison.OrdinalIgnoreCase) || value == "6")
                return VCDefinition.Instance;
            throw new CommandLineException("Unknown game '" + value + "'; expected iii or vc.");
        }

        private static GameDataLoader CreateLoader(GameDefinition definition, HeadlessOptions options)
        {
            if (options.Root == null)
                return null;
            PhysicalGameFileSystem files = new PhysicalGameFileSystem(options.Root);
            return new GameDataLoader(definition, files);
        }

        private static void LoadAssets(GameDataLoader loader, HeadlessOptions options, TextWriter output)
        {
            if (options.Standard)
                loader.LoadStandard();
            for (int i = 0; i < options.ImagePaths.Count; i++)
                loader.LoadImage(options.ImagePaths[i]);
            for (int i = 0; i < options.DatPaths.Count; i++)
                loader.LoadDat(options.DatPaths[i]);
            if (options.GxtPath != null)
            {
                GxtArchive gxt = loader.LoadGxt(options.GxtPath);
                output.WriteLine("GXT: source=" + options.GxtPath + " main=" + gxt.Main.Count.ToString(CultureInfo.InvariantCulture) +
                    " missions=" + gxt.MissionTables.Count.ToString(CultureInfo.InvariantCulture));
                if (options.GxtKey != null)
                {
                    string value;
                    output.WriteLine("GXT-Key: " + options.GxtKey + "=" +
                        (gxt.Main.TryGet(options.GxtKey, out value) ? value : "<missing>"));
                }
            }
            for (int i = 0; i < options.RenderWarePaths.Count; i++)
            {
                byte[] bytes = loader.FileSystem.ReadAllBytes(options.RenderWarePaths[i], MaximumLooseAssetLength);
                WriteRenderWare(options.RenderWarePaths[i], bytes, output);
            }
            for (int i = 0; i < options.RenderWareEntries.Count; i++)
            {
                string name = options.RenderWareEntries[i];
                WriteRenderWare("IMG:" + name, loader.Images.Read(name), output);
            }

            if (options.DffScan)
            {
                if (loader.Images.Count == 0)
                    loader.LoadInitialImage();
                DffScanReport scan = loader.ScanDff();
                output.WriteLine("DFF-Scan: files=" + scan.FileCount.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + scan.FailedFileCount.ToString(CultureInfo.InvariantCulture) +
                    " empty=" + scan.EmptyFileCount.ToString(CultureInfo.InvariantCulture) +
                    " clumps=" + scan.ClumpCount.ToString(CultureInfo.InvariantCulture) +
                    " geometries=" + scan.GeometryCount.ToString(CultureInfo.InvariantCulture) +
                    " frames=" + scan.FrameCount.ToString(CultureInfo.InvariantCulture) +
                    " atomics=" + scan.AtomicCount.ToString(CultureInfo.InvariantCulture) +
                    " triangles=" + scan.TriangleCount.ToString(CultureInfo.InvariantCulture) +
                    " materials=" + scan.MaterialCount.ToString(CultureInfo.InvariantCulture) +
                    " lengthMismatches=" + scan.LengthMismatchCount.ToString(CultureInfo.InvariantCulture) +
                    " skins=" + scan.SkinCount.ToString(CultureInfo.InvariantCulture) +
                    " oldSkins=" + scan.OldSkinCount.ToString(CultureInfo.InvariantCulture) +
                    " splitSkins=" + scan.SplitSkinCount.ToString(CultureInfo.InvariantCulture) +
                    " legacySkins=" + scan.LegacyAtomicSkinCount.ToString(CultureInfo.InvariantCulture) +
                    " hanim=" + scan.HAnimCount.ToString(CultureInfo.InvariantCulture) +
                    " matfx=" + scan.MatFxMaterialCount.ToString(CultureInfo.InvariantCulture) +
                    " atomicMatfx=" + scan.AtomicMatFxCount.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < scan.Failures.Count; i++)
                    output.WriteLine("DFF-Error: " + scan.Failures[i].Name + " - " + scan.Failures[i].Message);
            }
            if (options.TxdScan)
            {
                if (loader.Images.Count == 0)
                    loader.LoadInitialImage();
                TxdScanReport scan = loader.ScanTxd(true);
                output.WriteLine("TXD-Scan: files=" + scan.FileCount.ToString(CultureInfo.InvariantCulture) +
                    " failed=" + scan.FailedFileCount.ToString(CultureInfo.InvariantCulture) +
                    " empty=" + scan.EmptyFileCount.ToString(CultureInfo.InvariantCulture) +
                    " textures=" + scan.TextureCount.ToString(CultureInfo.InvariantCulture) +
                    " skipped=" + scan.SkippedTextureCount.ToString(CultureInfo.InvariantCulture) +
                    " levels=" + scan.LevelCount.ToString(CultureInfo.InvariantCulture) +
                    " decodedLevels=" + scan.DecodedLevelCount.ToString(CultureInfo.InvariantCulture) +
                    " pixels=" + scan.DecodedPixelCount.ToString(CultureInfo.InvariantCulture) +
                    " raw=" + scan.UncompressedCount.ToString(CultureInfo.InvariantCulture) +
                    " dxt1=" + scan.Dxt1Count.ToString(CultureInfo.InvariantCulture) +
                    " dxt3=" + scan.Dxt3Count.ToString(CultureInfo.InvariantCulture) +
                    " other=" + scan.OtherCompressionCount.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < scan.Failures.Count; i++)
                    output.WriteLine("TXD-Error: " + scan.Failures[i].Name + " - " + scan.Failures[i].Message);
            }
            for (int i = 0; i < options.TxdPaths.Count; i++)
                WriteTxd(options.TxdPaths[i], loader.LoadTxd(options.TxdPaths[i]), output);
            for (int i = 0; i < options.TxdEntries.Count; i++)
                WriteTxd("IMG:" + options.TxdEntries[i], loader.LoadTxdEntry(options.TxdEntries[i]), output);
            for (int i = 0; i < options.DffPaths.Count; i++)
                WriteDff(options.DffPaths[i], loader.LoadDff(options.DffPaths[i]), output);
            for (int i = 0; i < options.DffEntries.Count; i++)
                WriteDff("IMG:" + options.DffEntries[i], loader.LoadDffEntry(options.DffEntries[i]), output);
            for (int i = 0; i < options.IfpPaths.Count; i++)
                WriteIfp(options.IfpPaths[i], loader.LoadIfp(options.IfpPaths[i]), options.IfpAnimation, output);

            GameDataLoadStats stats = loader.Stats;
            output.WriteLine("Assets: dat=" + stats.DatFileCount.ToString(CultureInfo.InvariantCulture) +
                " ide=" + stats.IdeFileCount.ToString(CultureInfo.InvariantCulture) +
                " ipl=" + stats.IplFileCount.ToString(CultureInfo.InvariantCulture) +
                " col=" + stats.ColFileCount.ToString(CultureInfo.InvariantCulture) +
                " img=" + stats.ImageCount.ToString(CultureInfo.InvariantCulture) +
                " models=" + loader.Models.Count.ToString(CultureInfo.InvariantCulture) +
                " collisions=" + loader.Collisions.Count.ToString(CultureInfo.InvariantCulture) +
                " instances=" + loader.World.Count.ToString(CultureInfo.InvariantCulture));
            if (loader.World.Entities != null)
            {
                WorldEntities entities = loader.World.Entities;
                output.WriteLine("Entities: buildings=" + Context(entities, WorldEntityKind.Building) +
                    " dummies=" + Context(entities, WorldEntityKind.Dummy) +
                    " treadables=" + Context(entities, WorldEntityKind.Treadable) +
                    " objects=" + Context(entities, WorldEntityKind.Object) +
                    " rejected=" + loader.World.InstancesWithoutHandle.ToString(CultureInfo.InvariantCulture) +
                    " objectData=" + loader.Objects.Count.ToString(CultureInfo.InvariantCulture));
            }
            output.WriteLine("World-Data: navigation=" + (loader.Zones.Navigation.Count - 1).ToString(CultureInfo.InvariantCulture) +
                " information=" + (loader.Zones.Information.Count - 1).ToString(CultureInfo.InvariantCulture) +
                " mapZones=" + (loader.Zones.MapZones.Count - 1).ToString(CultureInfo.InvariantCulture) +
                " cull=" + loader.Zones.CullZones.Count.ToString(CultureInfo.InvariantCulture) +
                " occl=" + loader.Zones.OcclusionVolumes.Count.ToString(CultureInfo.InvariantCulture) +
                " pathGroups=" + loader.Paths.GroupCount.ToString(CultureInfo.InvariantCulture) +
                " pathNodes=" + loader.Paths.NodeCount.ToString(CultureInfo.InvariantCulture) +
                " detached=" + loader.Paths.DetachedCount.ToString(CultureInfo.InvariantCulture) +
                " water=" + loader.Paths.WaterCount.ToString(CultureInfo.InvariantCulture) +
                " unresolved=" + loader.Paths.UnresolvedCount.ToString(CultureInfo.InvariantCulture));
            if (loader.Paths.GroupCount != 0)
            {
                PathGraph graph = CreatePathGraph(loader, options);
                output.WriteLine("Path-Graph: mergeDistance=" + graph.ExternMergeDistance.ToString("R", CultureInfo.InvariantCulture) +
                    " tiles=" + graph.TileCount.ToString(CultureInfo.InvariantCulture) +
                    " nodes=" + graph.NodeCount.ToString(CultureInfo.InvariantCulture) +
                    " car=" + graph.CarNodeCount.ToString(CultureInfo.InvariantCulture) +
                    " ped=" + graph.PedNodeCount.ToString(CultureInfo.InvariantCulture) +
                    " water=" + graph.WaterNodeCount.ToString(CultureInfo.InvariantCulture) +
                    " links=" + graph.LinkCount.ToString(CultureInfo.InvariantCulture) +
                    " crossTile=" + graph.CrossTileLinkCount.ToString(CultureInfo.InvariantCulture) +
                    " crossings=" + graph.CrossingLinkCount.ToString(CultureInfo.InvariantCulture) +
                    " carLinks=" + graph.CarLinkCount.ToString(CultureInfo.InvariantCulture) +
                    " deadEnds=" + graph.CarDeadEndCount.ToString(CultureInfo.InvariantCulture) +
                    " laneFallbacks=" + graph.CarLinkLaneFallbackCount.ToString(CultureInfo.InvariantCulture) +
                    " unpairedExtern=" + graph.UnpairedExternCount.ToString(CultureInfo.InvariantCulture) +
                    " unresolvedExtern=" + graph.UnresolvedExternCount.ToString(CultureInfo.InvariantCulture) +
                    " externalNext=" + graph.ExternalNextCount.ToString(CultureInfo.InvariantCulture) +
                    " danglingNext=" + graph.DanglingNextCount.ToString(CultureInfo.InvariantCulture) +
                    " selfNext=" + graph.SelfNextCount.ToString(CultureInfo.InvariantCulture) +
                    " externalSlots=" + graph.ExternalSlotCount.ToString(CultureInfo.InvariantCulture) +
                    " emptySlots=" + graph.EmptySlotCount.ToString(CultureInfo.InvariantCulture) +
                    " unknownSlots=" + graph.UnknownSlotCount.ToString(CultureInfo.InvariantCulture) +
                    " unresolvedGroups=" + graph.UnresolvedGroupCount.ToString(CultureInfo.InvariantCulture));
                output.WriteLine("Path-Groups: components=" + graph.ComponentCount.ToString(CultureInfo.InvariantCulture) +
                    " car=" + graph.CarComponentCount.ToString(CultureInfo.InvariantCulture) +
                    " ped=" + graph.PedComponentCount.ToString(CultureInfo.InvariantCulture) +
                    " water=" + graph.WaterComponentCount.ToString(CultureInfo.InvariantCulture) +
                    " singles=" + graph.SingleNodeComponentCount.ToString(CultureInfo.InvariantCulture) +
                    " largest=" + graph.LargestComponentSize.ToString(CultureInfo.InvariantCulture));
                // Lanes decide where a car drives relative to the path line, so their real values
                // belong next to the graph that produces them.
                output.WriteLine("Path-Lanes: laneWidth=" + CarLanes.LaneWidth.ToString("R", CultureInfo.InvariantCulture) +
                    " maxLeft=" + graph.CarLinkMaxLeftLanes.ToString(CultureInfo.InvariantCulture) +
                    " maxRight=" + graph.CarLinkMaxRightLanes.ToString(CultureInfo.InvariantCulture) +
                    " oneWay=" + graph.CarLinkOneWayCount.ToString(CultureInfo.InvariantCulture) +
                    " multiLane=" + graph.CarLinkMultiLaneCount.ToString(CultureInfo.InvariantCulture));
                output.WriteLine("Path-Lights: lights=" + graph.TrafficLightObjectCount.ToString(CultureInfo.InvariantCulture) +
                    " links=" + graph.CarLinkTrafficLightCount.ToString(CultureInfo.InvariantCulture) +
                    " type1=" + graph.CarLinkTrafficLightType1Count.ToString(CultureInfo.InvariantCulture) +
                    " type2=" + graph.CarLinkTrafficLightType2Count.ToString(CultureInfo.InvariantCulture) +
                    " direction=" + graph.CarLinkTrafficLightDirectionCount.ToString(CultureInfo.InvariantCulture) +
                    " pedLinks=" + graph.PedTrafficLightLinkCount.ToString(CultureInfo.InvariantCulture));
            }
            output.WriteLine("Asset-Issues: total=" + loader.Report.TotalIssueCount.ToString(CultureInfo.InvariantCulture) +
                " retained=" + loader.Report.Count.ToString(CultureInfo.InvariantCulture) +
                " dropped=" + loader.Report.DroppedIssueCount.ToString(CultureInfo.InvariantCulture));
        }

        private static string Context(WorldEntities entities, WorldEntityKind kind)
        {
            return entities.Count(kind).ToString(CultureInfo.InvariantCulture) + "/" +
                entities.Capacity(kind).ToString(CultureInfo.InvariantCulture);
        }

        private static void WriteZoneQuery(ZoneWorld zones, HeadlessOptions options, TextWriter output)
        {
            Vector3 position = new Vector3(options.ZoneX, options.ZoneY, options.ZoneZ);
            ZoneRecord navigation = zones.FindSmallestZone(position);
            ZoneRecord information = zones.FindInformationZone(position);
            int wanted;
            int attributes = zones.FindCullAttributes(position, out wanted);
            output.WriteLine("Zone: navigation=" + navigation.Name + ":" + navigation.Kind +
                " info=" + information.Name + ":" + information.Kind +
                " mapLevel=" + zones.GetRawLevelFromPosition(position).ToString(CultureInfo.InvariantCulture) +
                " cullFlags=" + attributes.ToString(CultureInfo.InvariantCulture) +
                " wantedDrop=" + wanted.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Spawns deterministic gameplay objects four units apart along X, drifting with
        /// (1, 0.5, 0) u/s, so two runs of the same command move identically.
        /// </summary>
        private static void SpawnObjects(GameDataLoader loader, HeadlessOptions options)
        {
            ModelDefinition model = ResolveSpawnModel(loader, options);
            for (int i = 0; i < options.SpawnObjectCount; i++)
            {
                DynamicEntity entity;
                // Four units apart keeps even a full object pool inside the world grid.
                loader.World.TrySpawnEntity(WorldEntityKind.Object, model,
                    new Vector3(-900.0f + (i % 450) * 4.0f, 0.0f, 0.0f),
                    new Vector3(1.0f, 0.5f, 0.0f), out entity);
            }
        }

        private static ModelDefinition ResolveSpawnModel(GameDataLoader loader, HeadlessOptions options)
        {
            if (loader.World.Count == 0)
                throw new CommandLineException("--spawn-objects requires loaded map data; add --standard.");
            if (options.SpawnModel != null)
            {
                ModelDefinition named = loader.Models.Find(options.SpawnModel);
                if (named == null)
                    throw new CommandLineException("--spawn-objects: model '" + options.SpawnModel +
                        "' is not registered.");
                return named;
            }
            // Without --spawn-model borrow the first placement's model, so the probe works on
            // any real map instead of depending on a synthetic model name.
            return loader.World.Instances[0].Model;
        }

        private static void WriteDynamicSummary(StaticWorld world, HeadlessOptions options, TextWriter output)
        {
            string summary = "Dynamic: requested=" + options.SpawnObjectCount.ToString(CultureInfo.InvariantCulture) +
                " alive=" + world.DynamicEntities.Count.ToString(CultureInfo.InvariantCulture) +
                " objects=" + world.Entities.Count(WorldEntityKind.Object).ToString(CultureInfo.InvariantCulture) +
                " rejected=" + world.DynamicRejectedCount.ToString(CultureInfo.InvariantCulture) +
                " sectorMoves=" + world.SectorMoves.ToString(CultureInfo.InvariantCulture);
            if (world.DynamicEntities.Count > 0)
            {
                Vector3 first = world.DynamicEntities[0].Position;
                Vector3 last = world.DynamicEntities[world.DynamicEntities.Count - 1].Position;
                summary += " first=" + Vector(first) + " last=" + Vector(last);
            }
            output.WriteLine(summary);
        }

        private static void WriteBuildingQuery(StaticWorld world, HeadlessOptions options, TextWriter output)
        {
            string handle = options.BuildingHandle.ToString(CultureInfo.InvariantCulture);
            if (world.Entities == null || world.Count == 0)
            {
                // Without map data every lookup would look like a stale handle, which hides
                // the real mistake: the game tree was never loaded.
                throw new CommandLineException(
                    "--query-building requires loaded map data; add --standard.");
            }
            StaticInstance instance;
            if (!world.TryGetBuilding(options.BuildingHandle, out instance))
            {
                output.WriteLine("Building: handle=" + handle + " stale=true");
                return;
            }
            output.WriteLine("Building: handle=" + handle + " stale=false model=" + instance.Model.Name +
                " position=" + Vector(instance.Position));
        }

        private static void WritePathQuery(PathWorld paths, HeadlessOptions options, TextWriter output)
        {
            if (options.PathGroupIndex >= paths.GroupCount)
                throw new CommandLineException("Path group index is outside [0, " + paths.GroupCount + ").");
            PathGroup group = paths.Groups[options.PathGroupIndex];
            if (options.PathNodeIndex >= group.Nodes.Length)
                throw new CommandLineException("Path node index is outside [0, " + group.Nodes.Length + ").");
            PathNode node = group.Nodes[options.PathNodeIndex];
            if (!group.IsDetached && !group.HasTransform)
            {
                output.WriteLine("Path: group=" + options.PathGroupIndex.ToString(CultureInfo.InvariantCulture) +
                    " node=" + options.PathNodeIndex.ToString(CultureInfo.InvariantCulture) +
                    " resolved=false model=" + group.ModelName);
                return;
            }
            Vector3 position = paths.GetWorldPosition(group, node);
            output.WriteLine("Path: group=" + options.PathGroupIndex.ToString(CultureInfo.InvariantCulture) +
                " node=" + options.PathNodeIndex.ToString(CultureInfo.InvariantCulture) +
                " resolved=true kind=" + group.Kind + " model=" + group.ModelName +
                " placements=" + (group.InstanceTransforms == null ? 0 : group.InstanceTransforms.Length).ToString(CultureInfo.InvariantCulture) +
                " position=" + Vector(position) + " next=" + node.NextNodeIndex.ToString(CultureInfo.InvariantCulture));
        }

        private static void WritePathNearestQuery(GameDataLoader loader, HeadlessOptions options,
            TextWriter output)
        {
            if (loader.Paths.GroupCount == 0)
            {
                throw new CommandLineException(
                    "--query-path-nearest requires loaded path data; add --standard.");
            }
            Vector3 position = new Vector3(options.PathNearestX, options.PathNearestY,
                options.PathNearestZ);
            string prefix = "Path-Nearest: kind=" + PathKindName(options.PathNearestKind) +
                " position=" + Vector(position) +
                " radius=" + options.PathNearestRadius.ToString("R", CultureInfo.InvariantCulture);
            PathGraphNode node;
            float distance;
            if (!CreatePathGraph(loader, options).TryFindNearestNode(position, options.PathNearestKind,
                options.PathNearestRadius, out node, out distance))
            {
                output.WriteLine(prefix + " found=false");
                return;
            }
            output.WriteLine(prefix + " found=true node=" + node.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                " group=" + node.GroupIndex.ToString(CultureInfo.InvariantCulture) +
                " local=" + node.LocalIndex.ToString(CultureInfo.InvariantCulture) +
                " nodePosition=" + Vector(node.Position) +
                " distance=" + distance.ToString("R", CultureInfo.InvariantCulture) +
                " links=" + node.LinkCount.ToString(CultureInfo.InvariantCulture) +
                " next=" + node.NextNodeIndex.ToString(CultureInfo.InvariantCulture) +
                " component=" + node.GroupId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Graph with the merge distance requested on the command line; the default keeps the
        /// original one-unit threshold and the loader's cached instance.
        /// </summary>
        private static PathGraph CreatePathGraph(GameDataLoader loader, HeadlessOptions options)
        {
            return options.HasPathMergeDistance
                ? new PathGraph(loader.Paths, options.PathMergeDistance)
                : loader.PathGraph;
        }

        /// <summary>
        /// Resolves both endpoints with the same weighted nearest-node search and then walks the
        /// shortest route over the link distances. Disconnected endpoints are reported instead
        /// of inventing a path.
        /// </summary>
        private static void WritePathRouteQuery(GameDataLoader loader, HeadlessOptions options,
            TextWriter output)
        {
            if (loader.Paths.GroupCount == 0)
            {
                throw new CommandLineException(
                    "--query-path-route requires loaded path data; add --standard.");
            }
            PathGraph graph = CreatePathGraph(loader, options);
            Vector3 start = new Vector3(options.PathRouteStartX, options.PathRouteStartY,
                options.PathRouteStartZ);
            Vector3 end = new Vector3(options.PathRouteEndX, options.PathRouteEndY,
                options.PathRouteEndZ);
            string prefix = "Path-Route: kind=" + PathKindName(options.PathNearestKind) +
                " radius=" + options.PathNearestRadius.ToString("R", CultureInfo.InvariantCulture);
            PathGraphNode startNode;
            float startDistance;
            if (!graph.TryFindNearestNode(start, options.PathNearestKind, options.PathNearestRadius,
                out startNode, out startDistance))
            {
                output.WriteLine(prefix + " startNode=-1 endNode=-1 found=false reason=start-not-found");
                return;
            }
            PathGraphNode endNode;
            float endDistance;
            if (!graph.TryFindNearestNode(end, options.PathNearestKind, options.PathNearestRadius,
                out endNode, out endDistance))
            {
                output.WriteLine(prefix + " startNode=" +
                    startNode.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                    " endNode=-1 found=false reason=end-not-found");
                return;
            }
            int[] route;
            float length;
            if (!graph.TryFindRoute(startNode.GlobalIndex, endNode.GlobalIndex, out route, out length))
            {
                output.WriteLine(prefix + " startNode=" +
                    startNode.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                    " endNode=" + endNode.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                    " found=false reason=disconnected" +
                    " startComponent=" + startNode.GroupId.ToString(CultureInfo.InvariantCulture) +
                    " endComponent=" + endNode.GroupId.ToString(CultureInfo.InvariantCulture));
                return;
            }
            output.WriteLine(prefix + " startNode=" +
                startNode.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                " endNode=" + endNode.GlobalIndex.ToString(CultureInfo.InvariantCulture) +
                " found=true hops=" + (route.Length - 1).ToString(CultureInfo.InvariantCulture) +
                " length=" + length.ToString("R", CultureInfo.InvariantCulture) +
                " component=" + startNode.GroupId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Independent audit of the built graph: symmetry, self links, absurd link lengths,
        /// flood-fill labels and the nearest-node round trip.
        /// </summary>
        private static bool WritePathVerification(GameDataLoader loader, HeadlessOptions options,
            TextWriter output)
        {
            if (loader.Paths.GroupCount == 0)
            {
                throw new CommandLineException(
                    "--verify-paths requires loaded path data; add --standard.");
            }
            PathGraphReport report = PathGraphVerifier.Verify(CreatePathGraph(loader, options),
                options.PathVerifyLength);
            output.WriteLine("Path-Verify: nodes=" + report.NodeCount.ToString(CultureInfo.InvariantCulture) +
                " links=" + report.LinkCount.ToString(CultureInfo.InvariantCulture) +
                " selfLinks=" + report.SelfLinkCount.ToString(CultureInfo.InvariantCulture) +
                " asymmetricLinks=" + report.AsymmetricLinkCount.ToString(CultureInfo.InvariantCulture) +
                " overLengthLinks=" + report.OverLengthLinkCount.ToString(CultureInfo.InvariantCulture) +
                " distanceMismatch=" + report.DistanceMismatchCount.ToString(CultureInfo.InvariantCulture) +
                " overLengthCar=" + report.OverLengthCarCount.ToString(CultureInfo.InvariantCulture) +
                " overLengthPed=" + report.OverLengthPedCount.ToString(CultureInfo.InvariantCulture) +
                " overLengthWater=" + report.OverLengthWaterCount.ToString(CultureInfo.InvariantCulture) +
                " longest=" + report.LongestLink.ToString("0.##", CultureInfo.InvariantCulture) +
                " longestPair=" + report.LongestLinkFrom.ToString(CultureInfo.InvariantCulture) +
                "->" + report.LongestLinkTo.ToString(CultureInfo.InvariantCulture) +
                " unlabelled=" + report.UnlabelledNodeCount.ToString(CultureInfo.InvariantCulture) +
                " misgrouped=" + report.MisgroupedNodeCount.ToString(CultureInfo.InvariantCulture) +
                " nearestMismatch=" + report.NearestMismatchCount.ToString(CultureInfo.InvariantCulture) +
                "/" + report.NearestSampleCount.ToString(CultureInfo.InvariantCulture) +
                " consistent=" + (report.IsConsistent ? "true" : "false"));
            bool consistent = report.IsConsistent;
            output.WriteLine("Path-Verify-Length: limit=" +
                options.PathVerifyLength.ToString("0.##", CultureInfo.InvariantCulture));
            IList<PathGraphReport.LongLink> longLinks = report.OverLengthSamples;
            for (int i = 0; i < longLinks.Count; i++)
            {
                PathGraphReport.LongLink link = longLinks[i];
                output.WriteLine("Path-Verify-Long: " + link.From.ToString(CultureInfo.InvariantCulture) +
                    "->" + link.To.ToString(CultureInfo.InvariantCulture) +
                    " kind=" + PathKindName(link.Kind) +
                    " length=" + link.Distance.ToString("0.##", CultureInfo.InvariantCulture) +
                    " tiles=" + link.FromTile.ToString(CultureInfo.InvariantCulture) +
                    "->" + link.ToTile.ToString(CultureInfo.InvariantCulture) +
                    " from=" + Vector(link.FromPosition) + " to=" + Vector(link.ToPosition));
            }
            return consistent;
        }

        /// <summary>
        /// Spawns deterministic actors on one path network: each takes the next node of that kind
        /// with links, walks a short greedy route through its neighbours and follows it at a
        /// fixed speed. No traffic rules, lane offsets or model variety yet.
        /// </summary>
        private static int SpawnFollowed(GameDataLoader loader, PathGraph graph,
            PathFollowerModule followers, PathKind pathKind, WorldEntityKind entityKind,
            ModelDefinition model, float speed, int count)
        {
            int spawned = 0;
            for (int i = 0; i < graph.NodeCount && spawned < count; i++)
            {
                PathGraphNode node = graph.Nodes[i];
                if (node.Kind != pathKind || node.LinkCount == 0)
                    continue;
                int[] route = PathRouteBuilder.BuildGreedy(graph, i, 40, true);
                if (route.Length < 2)
                {
                    // A start on a spur has no dead-end-free way out; walking to the end of the
                    // spur is still a better probe than refusing to spawn the actor at all.
                    route = PathRouteBuilder.BuildGreedy(graph, i, 40);
                }
                if (route.Length < 2)
                    continue;
                DynamicEntity entity;
                if (!loader.World.TrySpawnEntity(entityKind, model, node.Position,
                    Vector3.Zero, out entity))
                    break;
                if (!followers.Assign(entity, route, speed))
                    continue;
                spawned++;
            }
            return spawned;
        }

        private static ModelDefinition ResolvePedModel(GameDataLoader loader, HeadlessOptions options)
        {
            if (options.SpawnModel != null)
            {
                ModelDefinition named = loader.Models.Find(options.SpawnModel);
                if (named == null)
                {
                    throw new CommandLineException("--spawn-model '" + options.SpawnModel +
                        "' is not registered.");
                }
                if (named.Kind != ModelKind.Ped)
                {
                    throw new CommandLineException("--spawn-model '" + options.SpawnModel +
                        "' is not a ped model; --spawn-peds needs a ped.");
                }
                if (ModelRegistry.IsPlaceholder(named))
                {
                    throw new CommandLineException("--spawn-model '" + options.SpawnModel +
                        "' is the engine's null model and has no mesh; pick a real ped.");
                }
                return named;
            }
            ModelDefinition ped;
            if (loader.Models.TryFindFirst(ModelKind.Ped, true, out ped))
                return ped;
            throw new CommandLineException(
                "--spawn-peds needs a ped model; add --spawn-model NAME.");
        }

        private static ModelDefinition ResolveCarModel(GameDataLoader loader, HeadlessOptions options)
        {
            if (options.SpawnModel != null)
            {
                ModelDefinition named = loader.Models.Find(options.SpawnModel);
                if (named == null)
                {
                    throw new CommandLineException("--spawn-model '" + options.SpawnModel +
                        "' is not registered.");
                }
                if (ModelRegistry.IsPlaceholder(named))
                {
                    throw new CommandLineException("--spawn-model '" + options.SpawnModel +
                        "' is the engine's null model and has no mesh; pick a real vehicle.");
                }
                return named;
            }
            ModelDefinition vehicle;
            if (loader.Models.TryFindFirst(ModelKind.Vehicle, true, out vehicle))
                return vehicle;
            throw new CommandLineException(
                "--spawn-cars needs a vehicle model; add --spawn-model NAME.");
        }

        /// <summary>
        /// Recycling keeps the actor count constant, so the interesting numbers are the lifecycle
        /// ones: something finished, was removed from the world and was replaced.
        /// </summary>
        private static void WriteTrafficSummary(TrafficPopulationModule population, TextWriter output)
        {
            string line = "Path-Traffic:";
            IList<TrafficCohortStatus> cohorts = population.Cohorts;
            for (int i = 0; i < cohorts.Count; i++)
            {
                TrafficCohortStatus cohort = cohorts[i];
                line += " " + (cohort.EntityKind == WorldEntityKind.Vehicle ? "cars" : "peds") +
                    "=" + cohort.LiveCount.ToString(CultureInfo.InvariantCulture) +
                    "/" + cohort.TargetCount.ToString(CultureInfo.InvariantCulture);
            }
            line += " spawned=" + population.SpawnedCount.ToString(CultureInfo.InvariantCulture) +
                " despawned=" + population.DespawnedCount.ToString(CultureInfo.InvariantCulture) +
                " recycled=" + population.RecycledCount.ToString(CultureInfo.InvariantCulture);
            output.WriteLine(line);
        }

        private static void WritePathFollowSummary(StaticWorld world, PathFollowerModule followers,
            int cars, int peds, string carModel, string pedModel, TextWriter output)
        {
            // The model names matter: an actor on the engine's "null" model would walk and count
            // exactly like a real one while never drawing, so the summary states what spawned.
            string summary = "Path-Follow: vehicles=" + cars.ToString(CultureInfo.InvariantCulture) +
                " peds=" + peds.ToString(CultureInfo.InvariantCulture) +
                " actors=" + followers.Count.ToString(CultureInfo.InvariantCulture) +
                " completed=" + followers.CompletedCount.ToString(CultureInfo.InvariantCulture) +
                " replans=" + followers.ReplanCount.ToString(CultureInfo.InvariantCulture) +
                " stalled=" + followers.StalledStepCount.ToString(CultureInfo.InvariantCulture) +
                " holds=" + followers.TrafficHoldCount.ToString(CultureInfo.InvariantCulture) +
                " travelled=" + followers.TravelledDistance.ToString("0.##", CultureInfo.InvariantCulture);
            if (carModel != null)
                summary += " vehicleModel=" + carModel;
            if (pedModel != null)
                summary += " pedModel=" + pedModel;
            output.WriteLine(summary);
            output.WriteLine(VehicleGroundProbe.Format(VehicleGroundProbe.Measure(world)));
        }

        private static string PathKindName(PathKind kind)
        {
            switch (kind)
            {
                case PathKind.Car:
                    return "car";
                case PathKind.Ped:
                    return "ped";
                case PathKind.Water:
                    return "water";
                default:
                    return "unknown";
            }
        }

        private static void WriteGroundQuery(StaticWorld world, HeadlessOptions options, TextWriter output)
        {
            StaticWorldHit hit;
            if (!world.QueryVertical(options.GroundX, options.GroundY, options.GroundMinZ, options.GroundMaxZ, out hit))
            {
                output.WriteLine("Ground: hit=false");
                return;
            }
            output.WriteLine("Ground: hit=true model=" + hit.Instance.Model.Name +
                " position=" + Vector(hit.Position) + " normal=" + Vector(hit.Normal) +
                " surface=" + hit.Surface.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteRenderWare(string source, byte[] bytes, TextWriter output)
        {
            IList<RenderWareChunk> roots = RenderWareInventory.ReadInventory(bytes);
            int count = 0;
            int depth = 0;
            for (int i = 0; i < roots.Count; i++)
                CountChunks(roots[i], 1, ref count, ref depth);
            output.WriteLine("RW: source=" + source + " roots=" + roots.Count.ToString(CultureInfo.InvariantCulture) +
                " chunks=" + count.ToString(CultureInfo.InvariantCulture) + " depth=" + depth.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteTxd(string source, TxdArchive archive, TextWriter output)
        {
            int levels = 0;
            long pixels = 0;
            int raw = 0;
            int dxt1 = 0;
            int dxt3 = 0;
            for (int i = 0; i < archive.Textures.Length; i++)
            {
                TxdTexture texture = archive.Textures[i];
                levels += texture.LevelCount;
                if (texture.Compression == TxdCompression.Uncompressed)
                    raw++;
                else if (texture.Compression == TxdCompression.Dxt1)
                    dxt1++;
                else if (texture.Compression == TxdCompression.Dxt3)
                    dxt3++;
                for (int level = 0; level < texture.LevelCount; level++)
                {
                    TxdImage image = texture.DecodeLevel(level);
                    pixels += (long)image.Width * image.Height;
                }
            }
            output.WriteLine("TXD: source=" + source + " declared=" + archive.DeclaredTextureCount.ToString(CultureInfo.InvariantCulture) +
                " textures=" + archive.Textures.Length.ToString(CultureInfo.InvariantCulture) +
                " skipped=" + archive.SkippedTextureCount.ToString(CultureInfo.InvariantCulture) +
                " levels=" + levels.ToString(CultureInfo.InvariantCulture) +
                " pixels=" + pixels.ToString(CultureInfo.InvariantCulture) +
                " raw=" + raw.ToString(CultureInfo.InvariantCulture) +
                " dxt1=" + dxt1.ToString(CultureInfo.InvariantCulture) +
                " dxt3=" + dxt3.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteIfp(string source, IfpDocument document, string wanted, TextWriter output)
        {
            int animations = 0;
            int sequences = 0;
            int frames = 0;
            int translated = 0;
            int scaled = 0;
            float longest = 0.0f;
            for (int i = 0; i < document.Blocks.Length; i++)
            {
                IfpBlock block = document.Blocks[i];
                for (int j = 0; j < block.Animations.Length; j++)
                {
                    IfpAnimation animation = block.Animations[j];
                    animations++;
                    if (animation.Duration > longest)
                        longest = animation.Duration;
                    for (int k = 0; k < animation.Sequences.Length; k++)
                    {
                        IfpSequence sequence = animation.Sequences[k];
                        sequences++;
                        frames += sequence.Frames.Length;
                        if (sequence.HasTranslation)
                            translated++;
                        if (sequence.HasScale)
                            scaled++;
                    }
                }
            }
            output.WriteLine("IFP: source=" + source +
                " blocks=" + document.Blocks.Length.ToString(CultureInfo.InvariantCulture) +
                " animations=" + animations.ToString(CultureInfo.InvariantCulture) +
                " sequences=" + sequences.ToString(CultureInfo.InvariantCulture) +
                " frames=" + frames.ToString(CultureInfo.InvariantCulture) +
                " translated=" + translated.ToString(CultureInfo.InvariantCulture) +
                " scaled=" + scaled.ToString(CultureInfo.InvariantCulture) +
                " longest=" + longest.ToString("0.###", CultureInfo.InvariantCulture));
            if (wanted == null)
                return;
            // A named lookup proves the names and hierarchy walk, not just the aggregate.
            for (int i = 0; i < document.Blocks.Length; i++)
            {
                IfpBlock block = document.Blocks[i];
                for (int j = 0; j < block.Animations.Length; j++)
                {
                    IfpAnimation animation = block.Animations[j];
                    if (!string.Equals(animation.Name, wanted, StringComparison.OrdinalIgnoreCase))
                        continue;
                    output.WriteLine("IFP-Animation: block=" + block.Name + " name=" + animation.Name +
                        " sequences=" + animation.Sequences.Length.ToString(CultureInfo.InvariantCulture) +
                        " frames=" + animation.FrameCount.ToString(CultureInfo.InvariantCulture) +
                        " duration=" + animation.Duration.ToString("0.###", CultureInfo.InvariantCulture));
                    return;
                }
            }
            output.WriteLine("IFP-Animation: name=" + wanted + " found=false");
        }

        private static void WriteDff(string source, DffDocument document, TextWriter output)
        {
            int geometries = 0;
            int frames = 0;
            int atomics = 0;
            int triangles = 0;
            int materials = 0;
            int meshes = 0;
            int vertices = 0;
            int mismatches = 0;
            int skins = 0;
            int hierarchies = 0;
            int matFx = 0;
            int atomicMatFx = 0;
            for (int i = 0; i < document.Clumps.Length; i++)
            {
                DffClump clump = document.Clumps[i];
                frames += clump.Frames.Length;
                atomics += clump.Atomics.Length;
                geometries += clump.Geometries.Length;
                for (int frame = 0; frame < clump.Frames.Length; frame++)
                {
                    if (clump.Frames[frame].HAnim != null)
                        hierarchies++;
                }
                for (int atomic = 0; atomic < clump.Atomics.Length; atomic++)
                {
                    if (clump.Atomics[atomic].HasMatFx)
                        atomicMatFx++;
                }
                if (clump.HasDeclaredLengthMismatch)
                    mismatches++;
                for (int j = 0; j < clump.Geometries.Length; j++)
                {
                    DffGeometry geometry = clump.Geometries[j];
                    triangles += geometry.Triangles.Length;
                    materials += geometry.Materials.Length;
                    meshes += geometry.Meshes.Length;
                    if (geometry.Skin != null)
                        skins++;
                    for (int material = 0; material < geometry.Materials.Length; material++)
                    {
                        if (geometry.Materials[material].MatFx != null)
                            matFx++;
                    }
                    if (geometry.MorphTargets.Length != 0 && geometry.MorphTargets[0].Vertices != null)
                        vertices += geometry.MorphTargets[0].Vertices.Length;
                }
            }
            output.WriteLine("DFF: source=" + source + " clumps=" + document.Clumps.Length.ToString(CultureInfo.InvariantCulture) +
                " geometries=" + geometries.ToString(CultureInfo.InvariantCulture) +
                " frames=" + frames.ToString(CultureInfo.InvariantCulture) +
                " atomics=" + atomics.ToString(CultureInfo.InvariantCulture) +
                " vertices=" + vertices.ToString(CultureInfo.InvariantCulture) +
                " triangles=" + triangles.ToString(CultureInfo.InvariantCulture) +
                " materials=" + materials.ToString(CultureInfo.InvariantCulture) +
                " meshes=" + meshes.ToString(CultureInfo.InvariantCulture) +
                " lengthMismatches=" + mismatches.ToString(CultureInfo.InvariantCulture) +
                " skins=" + skins.ToString(CultureInfo.InvariantCulture) +
                " hanim=" + hierarchies.ToString(CultureInfo.InvariantCulture) +
                " matfx=" + matFx.ToString(CultureInfo.InvariantCulture) +
                " atomicMatfx=" + atomicMatFx.ToString(CultureInfo.InvariantCulture));
        }

        private static void CountChunks(RenderWareChunk chunk, int level, ref int count, ref int depth)
        {
            count++;
            if (level > depth)
                depth = level;
            for (int i = 0; i < chunk.Children.Count; i++)
                CountChunks(chunk.Children[i], level + 1, ref count, ref depth);
        }

        private static void WriteIssues(LoadReport report, TextWriter output)
        {
            for (int i = 0; i < report.Items.Count; i++)
            {
                LoadIssue issue = report.Items[i];
                output.WriteLine("Issue: " + issue.Severity.ToString().ToUpperInvariant() + " " + issue.Category +
                    " " + issue.Path + (issue.Line == 0 ? "" : ":" + issue.Line.ToString(CultureInfo.InvariantCulture)) +
                    " - " + issue.Message);
            }
        }

        private static void WriteUnresolvedPaths(PathWorld paths, TextWriter output)
        {
            for (int i = 0; i < paths.GroupCount; i++)
            {
                PathGroup group = paths.Groups[i];
                if (group.IsDetached || group.HasTransform)
                    continue;
                output.WriteLine("Path-Unresolved: group=" + i.ToString(CultureInfo.InvariantCulture) +
                    " model=" + group.ModelName + " id=" + group.ModelId.ToString(CultureInfo.InvariantCulture) +
                    " source=" + group.SourceName);
            }
        }

        private static string Vector(SharpGTA.Mathematics.Vector3 value)
        {
            return "(" + value.X.ToString("R", CultureInfo.InvariantCulture) + "," +
                value.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
                value.Z.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        private static void WriteBanner(TextWriter output)
        {
            output.WriteLine(Branding.Title);
            output.WriteLine(Branding.Tagline);
        }

        private static void WriteUsage(TextWriter output)
        {
            output.WriteLine("Usage: " + Branding.Name + ".Headless.exe [options]");
            output.WriteLine("  --game iii|vc             Select the independent game data profile (default iii).");
            output.WriteLine("  --root PATH               Original game root; alone it implies --standard.");
            output.WriteLine("  --standard                Load initial IMG, DEFAULT.DAT, then the main DAT.");
            output.WriteLine("  --dat PATH                Execute an additional DAT in source order; repeatable.");
            output.WriteLine("  --img PATH                Register an IMG and sibling DIR; repeatable.");
            output.WriteLine("  --gxt PATH                Load a GXT and print table counts.");
            output.WriteLine("  --gxt-key KEY             Print one case-sensitive main GXT value.");
            output.WriteLine("  --rw PATH                 Inventory a loose DFF/TXD stream; repeatable.");
            output.WriteLine("  --rw-entry NAME           Inventory a named entry from loaded IMG files.");
            output.WriteLine("  --dff PATH                Decode static non-native DFF data; repeatable.");
            output.WriteLine("  --dff-entry NAME          Decode a named DFF entry from loaded IMG files.");
            output.WriteLine("  --dff-scan                Decode every DFF entry and print aggregate diagnostics.");
            output.WriteLine("  --ifp PATH               Read a loose IFP animation package; repeatable.");
            output.WriteLine("  --ifp-animation NAME     Print one animation from the read IFP packages.");
            output.WriteLine("  --txd PATH                Read D3D8 TXD and decode all its mip levels; repeatable.");
            output.WriteLine("  --txd-entry NAME          Read a named D3D8 TXD entry from loaded IMG files.");
            output.WriteLine("  --txd-scan                Decode every D3D8 TXD texture and mip level in IMG.");
            output.WriteLine("  --render-preview          Validate DFF/TXD render resources through NullRenderer.");
            output.WriteLine("  --query-ground X Y Z0 Z1  Query static COLL ground between two Z values.");
            output.WriteLine("  --query-zone X Y Z        Query navigation/info/map zones and cull attributes.");
            output.WriteLine("  --query-path GROUP NODE   Print one resolved path node position and next link.");
            output.WriteLine("  --query-path-nearest X Y Z  Find the closest path node; --path-kind car|ped|water, --path-radius R.");
            output.WriteLine("  --query-path-route X Y Z X2 Y2 Z2  Shortest route between the nearest nodes of both points.");
            output.WriteLine("  --verify-paths            Audit the built graph (symmetry, lengths, groups, nearest round trip).");
            output.WriteLine("  --path-verify-length R    Over-length threshold for --verify-paths (default 200 units).");
            output.WriteLine("  --path-merge-distance R   Merge external path slots closer than R units (default 1, the original).");
            output.WriteLine("  --spawn-cars N [--spawn-car-speed S]  Spawn N cars that follow the car network.");
            output.WriteLine("  --spawn-peds N [--spawn-ped-speed S]  Spawn N peds that follow the ped network.");
            output.WriteLine("  --walk X Y Z DX DY SPEED  Spawn one ped and walk it with COLL collision.");
            output.WriteLine("  --recycle                 Despawn finished actors and replace them, keeping the count.");
            output.WriteLine("  --query-building HANDLE   Resolve a building-pool handle to its IPL placement (needs --standard).");
            output.WriteLine("  --spawn-objects N [--spawn-model NAME]  Spawn N drifting gameplay objects and tick them.");
            output.WriteLine("  --ticks N                 Run N deterministic 30 Hz simulation ticks.");
            output.WriteLine("  --state-at N              Also print the diagnostic state hash at tick N.");
            output.WriteLine("  --verbose                 Print retained asset issues.");
            output.WriteLine("  --strict                  Return 3 when any asset issue was reported.");
            output.WriteLine("  --help                    Show this text.");
        }
    }
}

