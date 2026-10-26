using System;
using System.IO;
using System.Text;
using SharpGTA.Frontend;
using SharpGTA.Presentation;

namespace SharpGTA.D3D9
{
    /// <summary>Optional explicit import/export of the shared launcher/frontend settings.</summary>
    internal static class FrontendPreferences
    {
        internal static void Export(string path, LauncherState state)
        {
            if (path == null)
                throw new ArgumentNullException("path");
            if (state == null)
                throw new ArgumentNullException("state");
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            string temporary = fullPath + ".tmp";
            string content = "# " + Branding.Name + " frontend settings\n" +
                FrontendPreferencesText.CreateFile(state);
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, fullPath + ".bak");
            else
                File.Move(temporary, fullPath);
        }

        internal static bool TryImport(string path, out LauncherState state)
        {
            state = null;
            if (path == null)
                throw new ArgumentNullException("path");
            if (!File.Exists(path))
                return false;
            FrontendPreferencesState loaded;
            if (!FrontendPreferencesText.TryReadFile(File.ReadAllText(path), out loaded))
                return false;
            state = new LauncherState();
            state.ApplyState(loaded);
            return true;
        }
    }

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
    }
}
