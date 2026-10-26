using System;
using System.IO;
using SharpGTA.IO;

namespace SharpGTA.Tests
{
    public sealed class FileSystemTests
    {
        [Test]
        public void PhysicalFilesAreRootedCaseInsensitiveAndDoNotChangeCurrentDirectory()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                temporary.WriteText("Data/Sub/Test.DAT", "abc");
                temporary.WriteBytes("Data/High.DAT", new byte[] { 65, 0xFF, 66 });
                string current = Directory.GetCurrentDirectory();
                PhysicalGameFileSystem files = new PhysicalGameFileSystem(temporary.Path);
                Assert.True(files.FileExists("data\\sub\\test.dat"));
                Assert.Equal("abc", files.ReadAllText("DATA/SUB/TEST.DAT", 100));
                string high = files.ReadAllText("data/high.dat", 100);
                Assert.Equal(3, high.Length);
                Assert.Equal(255, (int)high[1]);
                Assert.False(files.FileExists("data/missing.dat"));
                Assert.Equal(current, Directory.GetCurrentDirectory());
                using (Stream stream = files.OpenRead("data/sub/test.dat"))
                {
                    Assert.Equal(3L, stream.Length);
                }
            }
        }

        [Test]
        public void PhysicalFilesRejectJunctionsBelowRoot()
        {
            using (TemporaryDirectory root = new TemporaryDirectory())
            using (TemporaryDirectory outside = new TemporaryDirectory())
            {
                outside.WriteText("secret.dat", "outside");
                string link = System.IO.Path.Combine(root.Path, "linked");
                System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo("cmd.exe");
                start.Arguments = "/d /c mklink /J \"" + link + "\" \"" + outside.Path + "\"";
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
                {
                    process.WaitForExit(10000);
                    if (!process.HasExited)
                    {
                        process.Kill();
                        Assert.Fail("mklink timed out.");
                    }
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("SKIP PhysicalFilesRejectJunctionsBelowRoot: junction creation unavailable.");
                        return;
                    }
                }
                PhysicalGameFileSystem files = new PhysicalGameFileSystem(root.Path);
                Assert.Throws<IOException>(delegate { files.ReadAllText("linked/secret.dat", 100); });
            }
        }

        [Test]
        public void PhysicalFilesRejectTraversalVolumesAndMissingRoots()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                PhysicalGameFileSystem files = new PhysicalGameFileSystem(temporary.Path);
                Assert.Throws<ArgumentException>(delegate { files.FileExists("../outside.dat"); });
                Assert.Throws<ArgumentException>(delegate { files.FileExists("data/../../outside.dat"); });
                Assert.Throws<ArgumentException>(delegate { files.FileExists("C:/outside.dat"); });
                Assert.Throws<ArgumentException>(delegate { files.FileExists("data/file.dat:stream"); });
                Assert.Throws<DirectoryNotFoundException>(delegate
                {
                    new PhysicalGameFileSystem(System.IO.Path.Combine(temporary.Path, "missing"));
                });
            }
        }
    }
}
