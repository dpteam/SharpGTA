using System;
using System.IO;
using SharpGTA.Headless;

namespace SharpGTA.Tests
{
    public sealed class HeadlessApplicationTests
    {
        [Test]
        public void VersionUsageAndErrorsHaveStableExitCodes()
        {
            string output;
            string error;
            Assert.Equal(0, Invoke(new string[] { "--version" }, out output, out error));
            Assert.True(output.IndexOf(SharpGTA.Branding.Title, StringComparison.Ordinal) >= 0);
            Assert.Equal(0, Invoke(new string[] { "--help" }, out output, out error));
            Assert.True(output.IndexOf("--query-ground", StringComparison.Ordinal) >= 0);
            Assert.Equal(2, Invoke(new string[] { "--state-at", "2", "--ticks", "1" }, out output, out error));
            Assert.True(error.IndexOf("--state-at", StringComparison.Ordinal) >= 0);
            Assert.Equal(2, Invoke(new string[] { "--dat", "DATA/A.DAT" }, out output, out error));
            Assert.True(error.IndexOf("--root", StringComparison.Ordinal) >= 0);
        }

        [Test]
        public void BuildingQueryRequiresLoadedMapData()
        {
            string output;
            string error;
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                int result = Invoke(new string[]
                {
                    "--game", "iii", "--root", directory.Path, "--query-building", "257"
                }, out output, out error);
                Assert.Equal(2, result);
                Assert.True(error.IndexOf("--standard", StringComparison.Ordinal) >= 0);
                // The misleading "stale" line must not be printed for an unloaded world.
                Assert.False(output.IndexOf("stale=", StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void HeadlessSimulationIsDeterministicAcrossProcessesEntryPoints()
        {
            string firstOutput;
            string secondOutput;
            string error;
            string[] args = { "--game", "iii", "--ticks", "90", "--state-at", "30" };
            Assert.Equal(0, Invoke(args, out firstOutput, out error));
            Assert.Equal(0, Invoke(args, out secondOutput, out error));
            Assert.Equal(firstOutput, secondOutput);
            Assert.True(firstOutput.IndexOf("State@30:", StringComparison.Ordinal) >= 0);
            Assert.True(firstOutput.IndexOf("State@90:", StringComparison.Ordinal) >= 0);
            Assert.True(firstOutput.IndexOf("time=12:03:00", StringComparison.Ordinal) >= 0);
        }

        [Test]
        public void StateAtFinalTickIsPrintedOnlyOnce()
        {
            string output;
            string error;
            Assert.Equal(0, Invoke(new string[] { "--ticks", "1", "--state-at", "1" }, out output, out error));
            Assert.Equal(1, CountOccurrences(output, "State@1:"));
            Assert.Equal(0, Invoke(new string[] { "--ticks", "0", "--state-at", "0" }, out output, out error));
            Assert.Equal(1, CountOccurrences(output, "State@0:"));
        }

        [Test]
        public void SpawnedObjectsTickDeterministically()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);
                string[] args =
                {
                    "--root", temporary.Path, "--standard", "--spawn-objects", "4", "--ticks", "1500"
                };
                string first;
                string second;
                string error;
                Assert.Equal(0, Invoke(args, out first, out error));
                Assert.Equal(0, Invoke(args, out second, out error));
                // Same command, same world, same positions: no wall clock, no randomness.
                Assert.Equal(first, second);
                Assert.True(first.IndexOf("Dynamic: requested=4 alive=4 objects=4 rejected=0",
                    StringComparison.Ordinal) >= 0);
                System.Text.RegularExpressions.Match moves =
                    System.Text.RegularExpressions.Regex.Match(first, "sectorMoves=(\\d+)");
                Assert.True(moves.Success, "Dynamic summary is missing sectorMoves.");
                Assert.True(int.Parse(moves.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) > 0,
                    "Fifty units of travel must cross at least one sector boundary.");
            }
        }

        /// <summary>
        /// Same map as <see cref="CreateSyntheticTree"/> plus one detached car path group with
        /// four real slots (0 -> 1 -> 2 -> 3) and eight type-0 padding slots.
        /// </summary>
        private static void CreatePathTree(TemporaryDirectory temporary)
        {
            temporary.WriteBytes("MODELS/GTA3.DIR", new byte[0]);
            temporary.WriteBytes("MODELS/GTA3.IMG", new byte[0]);
            temporary.WriteText("DATA/A.IDE",
                "objs\n1, ground, generic, 1, 100, 0\nend\n" +
                "cars\n130, landstal, landstal, car, LANDSTAL, LANDSTAL, normal, 10, 0, 1a, 0, 1.0\nend\n" +
                "path\n" +
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                "2, 3, 0, 320, 0, 0, 160, 1, 1\n" +
                "2, -1, 0, 480, 0, 0, 160, 1, 1\n" +
                RepeatLine("0, -1, 0, 0, 0, 0, 0, 0, 0", 8) +
                "end\n");
            temporary.WriteBytes("DATA/A.COL", WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f));
            temporary.WriteText("DATA/A.IPL", "inst\n1, ground, 10, 20, 3, 5, 5, 5, 0, 0, 1, 1\nend\n");
            temporary.WriteText("DATA/DEFAULT.DAT", "IDE DATA/A.IDE\nCOLFILE 0 DATA/A.COL\nIPL DATA/A.IPL\n");
            temporary.WriteText("DATA/GTA3.DAT", "# empty main\n");
        }

        /// <summary>
        /// A ped-only network next to the car one: same four-node chain, offset on Y so the two
        /// networks never overlap. Used to prove peds spawn, walk and hash deterministically.
        /// </summary>
        private static void CreatePedPathTree(TemporaryDirectory temporary)
        {
            temporary.WriteBytes("MODELS/GTA3.DIR", new byte[0]);
            temporary.WriteBytes("MODELS/GTA3.IMG", new byte[0]);
            temporary.WriteText("DATA/A.IDE",
                "objs\n1, ground, generic, 1, 100, 0\nend\n" +
                "peds\n0, null, null, CIVMALE, STAT_PLAYER, ped, 0\n" +
                "7, civmale, civmale, CIVMALE, STAT_CIVILE, ped, 0\nend\n" +
                "path\n" +
                "ped, -1, unused\n" +
                "2, 1, 0, 0, 320, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 320, 0, 160, 1, 1\n" +
                "2, 3, 0, 320, 320, 0, 160, 1, 1\n" +
                "2, -1, 0, 480, 320, 0, 160, 1, 1\n" +
                RepeatLine("0, -1, 0, 0, 0, 0, 0, 0, 0", 8) +
                "end\n");
            temporary.WriteBytes("DATA/A.COL", WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f));
            temporary.WriteText("DATA/A.IPL", "inst\n1, ground, 10, 20, 3, 5, 5, 5, 0, 0, 1, 1\nend\n");
            temporary.WriteText("DATA/DEFAULT.DAT", "IDE DATA/A.IDE\nCOLFILE 0 DATA/A.COL\nIPL DATA/A.IPL\n");
            temporary.WriteText("DATA/GTA3.DAT", "# empty main\n");
        }

        /// <summary>Both networks and both model kinds, for mixing cars, peds and objects.</summary>
        private static void CreateMixedPathTree(TemporaryDirectory temporary)
        {
            temporary.WriteBytes("MODELS/GTA3.DIR", new byte[0]);
            temporary.WriteBytes("MODELS/GTA3.IMG", new byte[0]);
            temporary.WriteText("DATA/A.IDE",
                "objs\n1, ground, generic, 1, 100, 0\nend\n" +
                "cars\n130, landstal, landstal, car, LANDSTAL, LANDSTAL, normal, 10, 0, 1a, 0, 1.0\nend\n" +
                "peds\n7, civmale, civmale, CIVMALE, STAT_PLAYER, ped, 0\nend\n" +
                "path\n" +
                "car, -1, unused\n" +
                "2, 1, 0, 0, 0, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 0, 0, 160, 1, 1\n" +
                "2, 3, 0, 320, 0, 0, 160, 1, 1\n" +
                "2, -1, 0, 480, 0, 0, 160, 1, 1\n" +
                RepeatLine("0, -1, 0, 0, 0, 0, 0, 0, 0", 8) +
                "ped, -1, unused\n" +
                "2, 1, 0, 0, 320, 0, 160, 1, 1\n" +
                "2, 2, 0, 160, 320, 0, 160, 1, 1\n" +
                "2, 3, 0, 320, 320, 0, 160, 1, 1\n" +
                "2, -1, 0, 480, 320, 0, 160, 1, 1\n" +
                RepeatLine("0, -1, 0, 0, 0, 0, 0, 0, 0", 8) +
                "end\n");
            temporary.WriteBytes("DATA/A.COL", WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f));
            temporary.WriteText("DATA/A.IPL", "inst\n1, ground, 10, 20, 3, 5, 5, 5, 0, 0, 1, 1\nend\n");
            temporary.WriteText("DATA/DEFAULT.DAT", "IDE DATA/A.IDE\nCOLFILE 0 DATA/A.COL\nIPL DATA/A.IPL\n");
            temporary.WriteText("DATA/GTA3.DAT", "# empty main\n");
        }

        private static string RepeatLine(string line, int count)
        {
            string result = "";
            for (int i = 0; i < count; i++)
                result += line + "\n";
            return result;
        }

        private static void CreateSyntheticTree(TemporaryDirectory temporary)
        {
            temporary.WriteBytes("MODELS/GTA3.DIR", new byte[0]);
            temporary.WriteBytes("MODELS/GTA3.IMG", new byte[0]);
            temporary.WriteText("DATA/A.IDE", "objs\n1, ground, generic, 1, 100, 0\nend\n");
            temporary.WriteBytes("DATA/A.COL", WorldTestAssets.CreateBoxCollision("ground", 2.0f, -1.0f, 0.0f));
            temporary.WriteText("DATA/A.IPL", "inst\n1, ground, 10, 20, 3, 5, 5, 5, 0, 0, 1, 1\nend\n");
            temporary.WriteText("DATA/DEFAULT.DAT", "IDE DATA/A.IDE\nCOLFILE 0 DATA/A.COL\nIPL DATA/A.IPL\n");
            temporary.WriteText("DATA/GTA3.DAT", "# empty main\n");
        }

        [Test]
        public void SpawnObjectsRequiresARootAndLoadsStandardImplicitly()
        {
            string output;
            string error;
            Assert.Equal(2, Invoke(new string[] { "--spawn-objects", "3", "--ticks", "5" },
                out output, out error));
            Assert.True(error.IndexOf("--root", StringComparison.Ordinal) >= 0);

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);
                // No --standard: spawning needs a loaded world, so plain --root must load it.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-objects", "3", "--ticks", "5" },
                    out output, out error));
                Assert.True(output.IndexOf("instances=1", StringComparison.Ordinal) >= 0);
                Assert.True(output.IndexOf("Dynamic: requested=3 alive=3 objects=3 rejected=0",
                    StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void PathGraphProbeReportsNodesLinksAndNearestNode()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePathTree(temporary);

                string output;
                string error;
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-nearest", "0", "0", "0", "--path-radius", "10", "--verify-paths" },
                    out output, out error), error);
                Assert.True(output.IndexOf(
                    "Path-Graph: mergeDistance=1 tiles=1 nodes=4 car=4 ped=0 water=0 links=6 crossTile=0 crossings=0 " + "carLinks=3 deadEnds=4 laneFallbacks=6 unpairedExtern=0 " +
                    "unresolvedExtern=0 externalNext=0 danglingNext=0 selfNext=0 externalSlots=0 " +
                    "emptySlots=8 unknownSlots=0 unresolvedGroups=0",
                    StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf(
                    "Path-Groups: components=1 car=1 ped=0 water=0 singles=0 largest=4",
                    StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf(
                    "Path-Verify: nodes=4 links=6 selfLinks=0 asymmetricLinks=0 overLengthLinks=0 ",
                    StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf("consistent=true", StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf(
                    "Path-Nearest: kind=car position=(0,0,0) radius=10 found=true node=0 group=0 local=0 " +
                    "nodePosition=(0,0,0) distance=0 links=1 next=1 component=1", StringComparison.Ordinal) >= 0, output);

                // The same coordinates five hundred units away have no node inside the limit.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-nearest", "500", "0", "0", "--path-radius", "50" }, out output, out error), error);
                Assert.True(output.IndexOf("found=false", StringComparison.Ordinal) >= 0, output);

                // The merge threshold is a diagnostic knob; --root alone must still load the map.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path,
                    "--path-merge-distance", "5" }, out output, out error), error);
                Assert.True(output.IndexOf(
                    "Path-Graph: mergeDistance=5 tiles=1 nodes=4", StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf(
                    "Path-Nearest:", StringComparison.Ordinal) < 0, output);

                // The four synthetic nodes sit ten units apart on one chain.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-route", "0", "0", "0", "30", "0", "0", "--path-radius", "5" },
                    out output, out error), error);
                Assert.True(output.IndexOf(
                    "Path-Route: kind=car radius=5 startNode=0 endNode=3 found=true hops=3 length=30 component=1",
                    StringComparison.Ordinal) >= 0, output);

                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-route", "500", "0", "0", "30", "0", "0", "--path-radius", "5" },
                    out output, out error), error);
                Assert.True(output.IndexOf("found=false reason=start-not-found",
                    StringComparison.Ordinal) >= 0, output);
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-route", "0", "0", "0", "500", "0", "0", "--path-radius", "5" },
                    out output, out error), error);
                Assert.True(output.IndexOf("endNode=-1 found=false reason=end-not-found",
                    StringComparison.Ordinal) >= 0, output);

                // The synthetic tree only has car paths, so a ped query must come back empty.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--standard",
                    "--query-path-nearest", "0", "0", "0", "--path-kind", "ped", "--path-radius", "100" },
                    out output, out error), error);
                Assert.True(output.IndexOf("Path-Nearest: kind=ped", StringComparison.Ordinal) >= 0);
                Assert.True(output.IndexOf("found=false", StringComparison.Ordinal) >= 0, output);
            }
        }

        [Test]
        public void PathNearestOptionsAreValidatedAndNeedLoadedPaths()
        {
            string output;
            string error;
            Assert.Equal(2, Invoke(new string[] { "--path-kind", "ped" }, out output, out error));
            Assert.True(error.IndexOf("--query-path-nearest", StringComparison.Ordinal) >= 0);

            Assert.Equal(2, Invoke(new string[] { "--query-path-nearest", "0", "0", "0",
                "--path-radius", "0" }, out output, out error));
            Assert.True(error.IndexOf("--path-radius", StringComparison.Ordinal) >= 0);

            Assert.Equal(2, Invoke(new string[] { "--query-path-nearest", "0", "0", "0",
                "--path-kind", "boat" }, out output, out error));
            Assert.True(error.IndexOf("--path-kind", StringComparison.Ordinal) >= 0);

            Assert.Equal(2, Invoke(new string[] { "--query-path-nearest", "0", "0" },
                out output, out error));
            Assert.True(error.IndexOf("--query-path-nearest", StringComparison.Ordinal) >= 0);

            Assert.Equal(2, Invoke(new string[] { "--path-merge-distance", "0" }, out output, out error));
            Assert.True(error.IndexOf("--path-merge-distance", StringComparison.Ordinal) >= 0);
            Assert.Equal(2, Invoke(new string[] { "--path-merge-distance", "5" }, out output, out error));
            Assert.True(error.IndexOf("--root", StringComparison.Ordinal) >= 0);
            Assert.Equal(2, Invoke(new string[] { "--query-path-route", "1", "2", "3" },
                out output, out error));
            Assert.True(error.IndexOf("--query-path-route", StringComparison.Ordinal) >= 0);

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);
                Assert.Equal(2, Invoke(new string[] { "--root", temporary.Path, "--query-path-nearest",
                    "0", "0", "0" }, out output, out error));
                Assert.True(error.IndexOf("--standard", StringComparison.Ordinal) >= 0);
                Assert.False(output.IndexOf("Path-Nearest:", StringComparison.Ordinal) >= 0);
                // A world without path data must not pretend to have an empty graph.
                Assert.False(output.IndexOf("Path-Graph:", StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void SpawnedCarsFollowTheCarNetwork()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePathTree(temporary);
                string first;
                string second;
                string error;
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-cars", "1",
                    "--ticks", "120", "--state-at", "120" }, out first, out error), error);
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-cars", "1",
                    "--ticks", "120", "--state-at", "120" }, out second, out error), error);
                Assert.Equal(first, second);
                // The synthetic chain is three ten-unit segments, driven at 12 units per second.
                Assert.True(first.IndexOf(
                    "Path-Follow: vehicles=1 peds=0 actors=1 completed=1 replans=0 stalled=0 holds=0 travelled=30",
                    StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("State@120:", StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void RecyclingTrafficDespawnsAndReplacesActors()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePathTree(temporary);
                string first;
                string second;
                string error;
                // Twenty units per second over a thirty-unit chain: each car finishes every forty
                // five ticks, so six hundred ticks exercise the whole despawn/replace cycle.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-cars", "2",
                    "--recycle", "--spawn-car-speed", "20", "--ticks", "600" }, out first, out error), error);
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-cars", "2",
                    "--recycle", "--spawn-car-speed", "20", "--ticks", "600" }, out second, out error), error);
                Assert.Equal(first, second);
                Assert.True(first.IndexOf("Path-Follow: vehicles=2 peds=0 actors=2",
                    StringComparison.Ordinal) >= 0, first);
                // The population must not grow, but it must actually recycle.
                Assert.True(first.IndexOf("Path-Traffic: cars=2/2 spawned=",
                    StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("spawned=0 despawned=0 recycled=0",
                    StringComparison.Ordinal) < 0, first);
                int spawned = ReadCounter(first, "Path-Traffic:", "spawned=");
                int despawned = ReadCounter(first, "Path-Traffic:", "despawned=");
                int recycled = ReadCounter(first, "Path-Traffic:", "recycled=");
                Assert.Equal(spawned, despawned);
                Assert.Equal(spawned, recycled);
                Assert.True(spawned > 10, first);
            }
        }

        [Test]
        public void RecycleNeedsActorsToRecycle()
        {
            string output;
            string error;
            Assert.Equal(2, Invoke(new string[] { "--root", ".", "--recycle" }, out output, out error));
            Assert.True(error.IndexOf("--recycle", StringComparison.Ordinal) >= 0);
        }

        private static int ReadCounter(string text, string marker, string name)
        {
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, text);
            int value = text.IndexOf(name, start, StringComparison.Ordinal);
            Assert.True(value >= 0, text);
            value += name.Length;
            int end = value;
            while (end < text.Length && char.IsDigit(text[end]))
                end++;
            return int.Parse(text.Substring(value, end - value),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void WalkProbeMovesACharacterWithCollision()
        {
            string output;
            string error;
            Assert.Equal(2, Invoke(new string[] { "--walk", "0", "0", "0", "1", "0", "3" },
                out output, out error));
            Assert.True(error.IndexOf("--root", StringComparison.Ordinal) >= 0);
            Assert.Equal(2, Invoke(new string[] { "--root", ".", "--walk", "0", "0", "0", "0", "0", "3" },
                out output, out error));
            Assert.True(error.IndexOf("--walk direction", StringComparison.Ordinal) >= 0);

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePedPathTree(temporary);
                string first;
                string second;
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--walk",
                    "0", "0", "0", "1", "0", "3", "--ticks", "60" }, out first, out error), error);
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--walk",
                    "0", "0", "0", "1", "0", "3", "--ticks", "60" }, out second, out error), error);
                Assert.Equal(first, second);
                // Two seconds at three units per second, on a map with no wall in the way.
                Assert.True(first.IndexOf("Walk: hits=", StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("travelled=6", StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("State@60:", StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void SpawnedPedsFollowThePedNetwork()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePedPathTree(temporary);
                string first;
                string second;
                string error;
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-peds", "2",
                    "--ticks", "640", "--state-at", "640" }, out first, out error), error);
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-peds", "2",
                    "--ticks", "640", "--state-at", "640" }, out second, out error), error);
                Assert.Equal(first, second);
                // Two ped nodes start routes, so both walk: the counters prove movement, not a fixed
                // distance, because RouteSource keeps them walking after the chain ends.
                Assert.True(first.IndexOf(
                    "Path-Follow: vehicles=0 peds=2 actors=2", StringComparison.Ordinal) >= 0, first);
                // Model 0 is "null" in the IDE and has no DFF: the spawner must skip it.
                Assert.True(first.IndexOf("pedModel=civmale", StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("pedModel=null", StringComparison.Ordinal) < 0, first);
                Assert.True(first.IndexOf("completed=", StringComparison.Ordinal) >= 0, first);
                Assert.True(first.IndexOf("Path-Graph: mergeDistance=1 tiles=1 nodes=4 car=0 ped=4",
                    StringComparison.Ordinal) >= 0, first);
            }
        }

        [Test]
        public void PedSpawningRejectsBadInput()
        {
            string output;
            string error;
            Assert.Equal(2, Invoke(new string[] { "--spawn-peds", "1" }, out output, out error));
            Assert.True(error.IndexOf("--root", StringComparison.Ordinal) >= 0);

            Assert.Equal(2, Invoke(new string[] { "--root", ".", "--spawn-ped-speed", "0" },
                out output, out error));
            Assert.True(error.IndexOf("--spawn-ped-speed", StringComparison.Ordinal) >= 0);

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);
                // Path data is a prerequisite: no path groups means no network to walk.
                Assert.Equal(2, Invoke(new string[] { "--root", temporary.Path, "--spawn-peds", "1" },
                    out output, out error));
                Assert.True(error.IndexOf("--spawn-cars/--spawn-peds", StringComparison.Ordinal) >= 0);
            }

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePathTree(temporary);
                // A car model cannot stand in for a ped: the model kind is checked, not guessed.
                Assert.Equal(2, Invoke(new string[] { "--root", temporary.Path, "--spawn-peds", "1",
                    "--spawn-model", "landstal" }, out output, out error));
                Assert.True(error.IndexOf("is not a ped model", StringComparison.Ordinal) >= 0);
            }

            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreatePedPathTree(temporary);
                // The engine's null model is a valid record with no mesh; naming it is an error
                // instead of spawning an actor that would never draw.
                Assert.Equal(2, Invoke(new string[] { "--root", temporary.Path, "--spawn-peds", "1",
                    "--spawn-model", "null" }, out output, out error));
                Assert.True(error.IndexOf("null model", StringComparison.Ordinal) >= 0);
            }
        }

        [Test]
        public void VehiclesAndPedsShareOneFollowerAndOneIntegrator()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateMixedPathTree(temporary);
                string output;
                string error;
                // Mixing spawned objects with actors must not register DynamicEntityModule twice,
                // otherwise every entity integrates twice per tick.
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-objects", "1",
                    "--spawn-cars", "1", "--spawn-peds", "1", "--ticks", "30" },
                    out output, out error), error);
                Assert.True(output.IndexOf("Dynamic: requested=1 alive=3 objects=1",
                    StringComparison.Ordinal) >= 0, output);
                Assert.True(output.IndexOf("Path-Follow: vehicles=1 peds=1 actors=2",
                    StringComparison.Ordinal) >= 0, output);
            }
        }

        [Test]
        public void DynamicStateIsPartOfTheStateHash()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);
                string withThree;
                string withFour;
                string repeated;
                string error;
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-objects", "3",
                    "--ticks", "20", "--state-at", "20" }, out withThree, out error));
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-objects", "4",
                    "--ticks", "20", "--state-at", "20" }, out withFour, out error));
                Assert.Equal(0, Invoke(new string[] { "--root", temporary.Path, "--spawn-objects", "3",
                    "--ticks", "20", "--state-at", "20" }, out repeated, out error));
                Assert.Equal(withThree, repeated);
                Assert.True(withThree.IndexOf("State@20:", StringComparison.Ordinal) >= 0);
                Assert.False(HashOf(withThree) == HashOf(withFour),
                    "Dynamic entities must contribute to the deterministic state hash.");
            }
        }

        private static string HashOf(string output)
        {
            int index = output.IndexOf("State@20: ", StringComparison.Ordinal);
            return index < 0 ? null : output.Substring(index + 10, 16);
        }

        [Test]
        public void StandardSyntheticAssetsLoadAndAnswerGroundQueryWithoutGpu()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                CreateSyntheticTree(temporary);

                string output;
                string error;
                int exit = Invoke(new string[] { "--root", temporary.Path, "--standard", "--query-ground", "10", "20", "-5", "10", "--ticks", "1" }, out output, out error);
                Assert.Equal(0, exit, error);
                Assert.True(output.IndexOf("models=1", StringComparison.Ordinal) >= 0);
                Assert.True(output.IndexOf("instances=1", StringComparison.Ordinal) >= 0);
                Assert.True(output.IndexOf("Ground: hit=true model=ground", StringComparison.Ordinal) >= 0);
            }
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static int Invoke(string[] args, out string output, out string error)
        {
            StringWriter outputWriter = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            StringWriter errorWriter = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            int result = HeadlessApplication.Run(args, outputWriter, errorWriter);
            output = outputWriter.ToString();
            error = errorWriter.ToString();
            return result;
        }
    }
}
