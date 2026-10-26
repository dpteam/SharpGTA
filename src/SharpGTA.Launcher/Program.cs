using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SharpGTA.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--check")
                return 0;
            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine(Branding.Title);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--dump-html")
            {
                string target = args.Length > 1 ? args[1] : "launcher-preview.html";
                string fullPath = Path.GetFullPath(target);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(fullPath, LauncherForm.AssemblePage(), new System.Text.UTF8Encoding(false));
                Console.WriteLine("Wrote " + fullPath);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--reset-smoke")
            {
                string directory = LauncherSettings.DataDirectory;
                string[] generated = { "launcher.ini", "launcher.ini.bak", "launcher.ini.tmp", "launcher-smoke.bmp" };
                for (int i = 0; i < generated.Length; i++)
                {
                    string path = Path.Combine(directory, generated[i]);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                string page = Path.Combine(directory, "webview", "launcher.html");
                if (File.Exists(page))
                    File.Delete(page);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--smoke-launch")
            {
                string gameRoot = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_GAME_ROOT");
                if (string.IsNullOrEmpty(gameRoot))
                {
                    Console.Error.WriteLine("--smoke-launch requires SHARPGTA_LAUNCHER_GAME_ROOT.");
                    return 2;
                }
                LauncherSettings settings = new LauncherSettings();
                settings.State.Root = gameRoot;
                settings.State.Language = SharpGTA.Frontend.FrontendLanguage.French;
                settings.State.ResolutionIndex = 1;
                Environment.SetEnvironmentVariable("SHARPGTA_LAUNCHER_SMOKE", "1");
                try
                {
                    using (System.Diagnostics.Process process = GameRunner.Start(settings.State))
                    {
                        Console.WriteLine("Launcher smoke: D3D9 exited with code " + process.ExitCode + ".");
                        return process.ExitCode;
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("Launcher smoke failed: " + exception.Message);
                    return 1;
                }
            }
            if (args.Length != 0)
            {
                Console.Error.WriteLine("Usage: SharpGTA.Launcher.exe [--check | --version | --dump-html PATH]");
                return 2;
            }

            EnableModernDocumentMode();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new LauncherForm());
                return 0;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.ToString(), Branding.Name + " Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void EnableModernDocumentMode()
        {
            const string keyPath =
                @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (key != null)
                        key.SetValue(Path.GetFileName(Application.ExecutablePath), 11001, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // IE=edge in the page and older system document modes remain a fallback.
            }
        }
    }
}
