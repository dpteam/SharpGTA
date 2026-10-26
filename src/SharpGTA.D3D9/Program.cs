using System;
using System.Globalization;
using System.IO;
using SharpGTA.Assets;
using SharpGTA.Frontend;
using SharpGTA.Games.III;
using SharpGTA.IO;

namespace SharpGTA.D3D9
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool hidden = false;
            bool exerciseReset = false;
            bool menuMode = false;
            bool framesSpecified = false;
            string screenshot = null;
            string root = null;
            string game = "iii";
            string modelName = null;
            bool worldMode = false;
            string importSettings = null;
            string exportSettings = null;
            string languageText = null;
            string resolutionText = null;
            bool noVSync = false;
            bool noFrameLimit = false;
            int frames = 1;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--hidden")
                    hidden = true;
                else if (args[i] == "--exercise-reset")
                    exerciseReset = true;
                else if (args[i] == "--menu")
                    menuMode = true;
                else if (args[i] == "--model" && i + 1 < args.Length)
                    modelName = args[++i];
                else if (args[i] == "--world")
                    worldMode = true;
                else if (args[i] == "--root" && i + 1 < args.Length)
                    root = args[++i];
                else if (args[i] == "--game" && i + 1 < args.Length)
                    game = args[++i].ToLowerInvariant();
                else if (args[i] == "--language" && i + 1 < args.Length)
                    languageText = args[++i];
                else if (args[i] == "--resolution" && i + 1 < args.Length)
                    resolutionText = args[++i];
                else if (args[i] == "--no-vsync")
                    noVSync = true;
                else if (args[i] == "--no-frame-limit")
                    noFrameLimit = true;
                else if (args[i] == "--import-settings" && i + 1 < args.Length)
                    importSettings = args[++i];
                else if (args[i] == "--export-settings" && i + 1 < args.Length)
                    exportSettings = args[++i];
                else if (args[i] == "--version")
                {
                    Console.WriteLine(Branding.Title);
                    return 0;
                }
                else if (args[i] == "--screenshot" && i + 1 < args.Length)
                    screenshot = args[++i];
                else if (args[i] == "--frames" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) || frames < 0)
                    {
                        Console.Error.WriteLine("Invalid --frames value.");
                        return 2;
                    }
                    framesSpecified = true;
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

            LauncherState imported = null;
            if (importSettings != null)
            {
                if (!FrontendPreferences.TryImport(importSettings, out imported))
                {
                    Console.Error.WriteLine("Settings file does not contain a valid state: " + importSettings);
                    return 2;
                }
                if (root == null)
                    root = imported.Root;
                if (game == "iii")
                    game = imported.Game;
            }
            bool modelMode = modelName != null;
            if (modelMode && menuMode || worldMode && (menuMode || modelMode))
            {
                Console.Error.WriteLine("--world, --model and --menu are mutually exclusive.");
                return 2;
            }
            if ((menuMode || modelMode || worldMode) && root == null)
            {
                Console.Error.WriteLine("--menu/--model/--world requires --root PATH to an original GTA III data tree.");
                return 2;
            }
            if ((menuMode || modelMode || worldMode) && game != "iii")
            {
                Console.Error.WriteLine("The interactive frontend/model/world preview currently supports only --game iii.");
                return 2;
            }
            if (!menuMode && !modelMode && !worldMode && frames == 0)
            {
                Console.Error.WriteLine("Triangle mode requires a positive --frames value.");
                return 2;
            }
            if (!framesSpecified)
                frames = (menuMode || modelMode || worldMode) && screenshot == null ? 0 : 1;
            if ((menuMode || modelMode || worldMode) && screenshot != null && frames == 0)
            {
                Console.Error.WriteLine("--screenshot requires a finite positive --frames value.");
                return 2;
            }

            Console.WriteLine(Branding.Name + (worldMode ? " III world preview" : modelMode ? " III model preview" : menuMode ? " III frontend" : " D3D9 proof-of-concept"));
            Win32Window window = new Win32Window();
            D3D9Device device = new D3D9Device();
            try
            {
                FrontendSettings initialSettings = imported == null ? new FrontendSettings() :
                    imported.Settings;
                if (languageText != null)
                {
                    int language;
                    if (!int.TryParse(languageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out language) ||
                        language < 0 || language > 4)
                    {
                        Console.Error.WriteLine("Invalid --language value; expected 0..4.");
                        return 2;
                    }
                    initialSettings.Language = (SharpGTA.Frontend.FrontendLanguage)language;
                }
                if (resolutionText != null)
                {
                    int resolution = ResolutionIndex(resolutionText);
                    if (resolution < 0)
                    {
                        Console.Error.WriteLine("Invalid --resolution; expected 640x480, 800x600, 1024x768 or 1280x960.");
                        return 2;
                    }
                    initialSettings.ResolutionIndex = resolution;
                }
                if (noVSync)
                    initialSettings.VSync = false;
                if (noFrameLimit)
                    initialSettings.FrameLimiter = false;
                window.Create(Branding.Name + (worldMode ? " world" : modelMode ? " " + modelName : menuMode ? " III" : " D3D9"),
                    (menuMode || modelMode || worldMode) ? initialSettings.ResolutionWidth : 640,
                    (menuMode || modelMode || worldMode) ? initialSettings.ResolutionHeight : 480, hidden);
                device.VerticalSync = (menuMode || modelMode || worldMode) && initialSettings.VSync;
                device.Create(window.Handle, window.ClientWidth, window.ClientHeight);
                Console.WriteLine("Device: " + device.Description);
                if (worldMode)
                    return RunWorldViewer(window, device, new PhysicalGameFileSystem(root),
                        frames, screenshot, exerciseReset);
                if (modelMode)
                    return RunModelViewer(window, device, new PhysicalGameFileSystem(root),
                        modelName, frames, screenshot, exerciseReset);
                if (menuMode)
                {
                    PhysicalGameFileSystem files = new PhysicalGameFileSystem(root);
                    using (FrontendApplication frontend = new FrontendApplication(window, device, files, initialSettings))
                    {
                        Console.WriteLine("Controls: mouse, arrows, Enter, Esc; Left/Right changes settings.");
                        int result = frontend.Run(frames, screenshot, exerciseReset);
                        if (exportSettings != null)
                        {
                            LauncherState exported = imported ?? new LauncherState();
                            exported.Game = game;
                            exported.Root = root;
                            exported.ApplyFrontend(frontend.Menu.Settings);
                            FrontendPreferences.Export(exportSettings, exported);
                            Console.WriteLine("Settings exported: " + Path.GetFullPath(exportSettings));
                        }
                        Console.WriteLine("Frontend: shutdown completed.");
                        if (frontend.Menu.WorldPreviewRequested)
                            return RunWorldViewer(window, device, files, frames, screenshot, exerciseReset);
                        return result;
                    }
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    if (!window.ProcessMessages())
                        break;
                    if (window.ClientWidth > 0 && window.ClientHeight > 0 &&
                        (window.ClientWidth != device.Width || window.ClientHeight != device.Height))
                        device.Reset(window.Handle, window.ClientWidth, window.ClientHeight);
                    if (exerciseReset && frame > 0)
                        device.Reset(window.Handle, device.Width, device.Height);
                    device.DrawTestTriangle();
                    // Read before Present: discard semantics do not guarantee the
                    // old back-buffer contents after presentation. Reset mode also
                    // acquires the surface in the first frame to test its release.
                    if (screenshot != null && (frame == frames - 1 || exerciseReset))
                    {
                        byte[] pixels = device.ReadBackBufferBgra(device.Width, device.Height);
                        if (frame == frames - 1)
                            SaveScreenshot(screenshot, device, pixels);
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
                Console.Error.WriteLine((worldMode ? "World preview" : modelMode ? "Model preview" : menuMode ? "Frontend" : "D3D9") + " failure: " + exception.ToString());
                return 1;
            }
            finally
            {
                try { device.Dispose(); }
                finally { window.Dispose(); }
            }
        }

        private static int RunWorldViewer(Win32Window window, D3D9Device device,
            IGameFileSystem files, int frames, string screenshot, bool exerciseReset)
        {
            using (GameDataLoader loader = new GameDataLoader(IIIDefinition.Instance, files))
            {
                loader.LoadInitialImage();
                using (WorldViewerApplication viewer = new WorldViewerApplication(window, device, loader))
                    return viewer.Run(frames, screenshot, exerciseReset);
            }
        }

        private static int RunModelViewer(Win32Window window, D3D9Device device,
            IGameFileSystem files, string modelName, int frames, string screenshot, bool exerciseReset)
        {
            using (GameDataLoader loader = new GameDataLoader(IIIDefinition.Instance, files))
            using (ModelViewerApplication viewer = new ModelViewerApplication(window, device, loader, modelName))
            {
                Console.WriteLine("Model preview, not gameplay. Drag to orbit, wheel zooms; arrows, PgUp/PgDn, Home, Esc.");
                return viewer.Run(frames, screenshot, exerciseReset);
            }
        }

        private static void SaveScreenshot(string path, D3D9Device device, byte[] pixels)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            BmpWriter.WriteBgra(fullPath, device.Width, device.Height, pixels);
            Console.WriteLine("Screenshot: " + fullPath);
        }

        private static int ResolutionIndex(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (normalized == "640x480") return 0;
            if (normalized == "800x600") return 1;
            if (normalized == "1024x768") return 2;
            if (normalized == "1280x960") return 3;
            return -1;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(Branding.Name + ".D3D9.exe [--menu --root PATH [--game iii]] [--model NAME --root PATH] [--world --root PATH] [--hidden]");
            Console.WriteLine("  [--frames N] [--screenshot PATH] [--exercise-reset] [--version]");
            Console.WriteLine("  Default: creates a real D3D9 device and draws a fixed-function triangle.");
            Console.WriteLine("  --menu: opens the interactive GTA III frontend using original TXD/GXT data.");
            Console.WriteLine("  --model NAME: renders NAME.DFF/TXD from GTA3.IMG with an orbit camera.");
            Console.WriteLine("  --world: loads real static IPL placements with free-fly camera; New Game opens it.");
            Console.WriteLine("  Menu controls: mouse, arrows, Enter, Esc; Left/Right changes settings.");
            Console.WriteLine("  --frames 0 keeps the menu open; screenshots require a finite positive count.");
            Console.WriteLine("  --language 0..4, --resolution WxH, --no-vsync, --no-frame-limit.");
            Console.WriteLine("  --import-settings FILE and --export-settings FILE share launcher settings.");
            Console.WriteLine("  --exercise-reset releases cached surfaces and resets between frames.");
            Console.WriteLine("  No D3DX, external DLL or game data is required for triangle mode.");
        }
    }
}
