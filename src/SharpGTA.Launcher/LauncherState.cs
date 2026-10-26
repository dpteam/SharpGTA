using System;
using System.IO;
using System.Text;
using SharpGTA.Frontend;
using SharpGTA.IO;
using SharpGTA.Presentation;

namespace SharpGTA.Launcher
{
    internal sealed class LauncherState : FrontendPreferencesState
    {
        internal static new LauncherState Decode(string payload)
        {
            LauncherState result = new LauncherState();
            result.ApplyState(FrontendPreferencesState.Decode(payload));
            return result;
        }

        internal void ApplyState(FrontendPreferencesState source)
        {
            Game = source.Game;
            Root = source.Root;
            Theme = source.Theme;
            Settings = source.Settings;
        }

        internal void Validate()
        {
            if (Game != "iii" && Game != "vc")
                throw new InvalidOperationException("Unknown game profile: " + Game);
            if (string.IsNullOrEmpty(Root))
                throw new InvalidOperationException("Select the original game data folder first.");
            if (Game != "iii")
                throw new InvalidOperationException("Vice City assets are detected, but its interactive frontend is not implemented yet. Select GTA III.");
            PhysicalGameFileSystem files = new PhysicalGameFileSystem(Root);
            string[] required = { "MODELS/MENU.TXD", "MODELS/FONTS.TXD", "TEXT/AMERICAN.GXT" };
            for (int i = 0; i < required.Length; i++)
            {
                if (!files.FileExists(required[i]))
                    throw new FileNotFoundException("Required GTA III frontend resource is missing: " + required[i]);
            }
        }

    }

    internal sealed class LauncherSettings
    {
        internal LauncherState State = new LauncherState();

        internal static string DataDirectory
        {
            get
            {
                string testDirectory = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_TEST_DATA_DIR");
                if (!string.IsNullOrEmpty(testDirectory))
                    return testDirectory;
                string overrideDirectory = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_DATA_DIR");
                if (!string.IsNullOrEmpty(overrideDirectory))
                    return overrideDirectory;
                string baseDirectory = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_BIN_DIR");
                if (string.IsNullOrEmpty(baseDirectory))
                    baseDirectory = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                if (File.Exists(Path.Combine(baseDirectory, "portable.txt")))
                    return Path.Combine(baseDirectory, "user");
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root))
                    root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(root))
                    root = baseDirectory;
                return Path.Combine(root, Branding.Name);
            }
        }

        internal static string SettingsPath { get { return Path.Combine(DataDirectory, "launcher.ini"); } }
        internal static bool IsPortable
        {
            get
            {
                string directory = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_BIN_DIR");
                if (string.IsNullOrEmpty(directory))
                    directory = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                return File.Exists(Path.Combine(directory, "portable.txt"));
            }
        }

        internal static LauncherSettings Load()
        {
            LauncherSettings settings = new LauncherSettings();
            if (!File.Exists(SettingsPath))
                return settings;
            foreach (string raw in File.ReadAllLines(SettingsPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;
                string key = line.Substring(0, separator).Trim().ToLowerInvariant();
                string value = line.Substring(separator + 1);
                if (key == "state")
                    settings.State = LauncherState.Decode(FrontendPreferencesText.Unescape(value));
            }
            return settings;
        }

        internal void Save()
        {
            Directory.CreateDirectory(DataDirectory);
            string path = SettingsPath;
            string temporary = path + ".tmp";
            string content = "# " + Branding.Name + " launcher settings\n" +
                FrontendPreferencesText.CreateFile(State);
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
        }

    }
}
