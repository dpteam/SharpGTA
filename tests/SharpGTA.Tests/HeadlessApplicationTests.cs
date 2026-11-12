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
