using System;
using System.Globalization;
using System.IO;

namespace SharpGTA.D3D9
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool hidden = false;
            bool exerciseReset = false;
            string screenshot = null;
            int frames = 1;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--hidden")
                    hidden = true;
                else if (args[i] == "--exercise-reset")
                    exerciseReset = true;
                else if (args[i] == "--version")
                {
                    Console.WriteLine(Branding.Title);
                    return 0;
                }
                else if (args[i] == "--screenshot" && i + 1 < args.Length)
                    screenshot = args[++i];
                else if (args[i] == "--frames" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) || frames <= 0)
                    {
                        Console.Error.WriteLine("Invalid --frames value.");
                        return 2;
                    }
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    PrintHelp();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine("Unknown option: " + args[i]);
                    return 2;
                }
            }

            Console.WriteLine(Branding.Name + " D3D9 proof-of-concept");
            Win32Window window = new Win32Window();
            D3D9Device device = new D3D9Device();
            try
            {
                window.Create(Branding.Name + " D3D9", 640, 480, hidden);
                device.Create(window.Handle, 640, 480);
                Console.WriteLine("Device: " + device.Description);
                for (int frame = 0; frame < frames; frame++)
                {
                    if (!window.ProcessMessages())
                        break;
                    if (exerciseReset && frame > 0)
                        device.Reset(window.Handle, window.ClientWidth, window.ClientHeight);
                    device.DrawTestTriangle();
                    // Read before Present: discard semantics do not guarantee the
                    // old back-buffer contents after presentation. Reset mode also
                    // acquires the surface in the first frame to test its release.
                    if (screenshot != null && (frame == frames - 1 || exerciseReset))
                    {
                        byte[] pixels = device.ReadBackBufferBgra(640, 480);
                        if (frame == frames - 1)
                        {
                            string fullPath = Path.GetFullPath(screenshot);
                            string directory = Path.GetDirectoryName(fullPath);
                            if (!string.IsNullOrEmpty(directory))
                                Directory.CreateDirectory(directory);
                            BmpWriter.WriteBgra(fullPath, 640, 480, pixels);
                            Console.WriteLine("Screenshot: " + fullPath);
                        }
                    }
                    device.Present();
                }
                Console.WriteLine("D3D9: create/clear/draw/present succeeded.");
                return 0;
            }
            catch (DllNotFoundException exception)
            {
                Console.Error.WriteLine("D3D9 unavailable: " + exception.Message);
                return 3;
            }
            catch (EntryPointNotFoundException exception)
            {
                Console.Error.WriteLine("Required Win32/D3D9 entry point unavailable: " + exception.Message);
                return 3;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("D3D9 failure: " + exception.Message);
                return 1;
            }
            finally
            {
                try { device.Dispose(); }
                finally { window.Dispose(); }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(Branding.Name + ".D3D9.exe [--hidden] [--frames N] [--screenshot PATH] [--exercise-reset] [--version]");
            Console.WriteLine("  Creates a real D3D9 device and draws a fixed-function triangle.");
            Console.WriteLine("  --exercise-reset releases cached surfaces and resets between frames.");
            Console.WriteLine("  No D3DX, external DLL, model asset or game data is required.");
        }
    }
}
