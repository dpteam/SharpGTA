using System;
using System.Globalization;
using SharpGTA.Frontend;

namespace SharpGTA.Presentation
{
    /// <summary>Shared versioned state used by the launcher and the III frontend.</summary>
    public class FrontendPreferencesState
    {
        public string Game = "iii";
        public string Root = "";
        public string Theme = "dark";
        public FrontendSettings Settings = new FrontendSettings();
        public FrontendLanguage Language { get { return Settings.Language; } set { Settings.Language = value; } }
        public int ResolutionIndex { get { return Settings.ResolutionIndex; } set { Settings.ResolutionIndex = value; } }
        public bool FrameLimiter { get { return Settings.FrameLimiter; } set { Settings.FrameLimiter = value; } }
        public bool VSync { get { return Settings.VSync; } set { Settings.VSync = value; } }
        public int ResolutionWidth { get { return Settings.ResolutionWidth; } }
        public int ResolutionHeight { get { return Settings.ResolutionHeight; } }

        public string Encode()
        {
            return string.Join("\n", new[] {
                Game, Root, Theme,
                ((int)Settings.Language).ToString(CultureInfo.InvariantCulture),
                Settings.Brightness.ToString(CultureInfo.InvariantCulture),
                Settings.DrawDistance.ToString("R", CultureInfo.InvariantCulture),
                Settings.MusicVolume.ToString(CultureInfo.InvariantCulture),
                Settings.SfxVolume.ToString(CultureInfo.InvariantCulture),
                Settings.FrameLimiter ? "1" : "0",
                Settings.VSync ? "1" : "0",
                Settings.Trails ? "1" : "0",
                Settings.Subtitles ? "1" : "0",
                Settings.WideScreen ? "1" : "0",
                Settings.ResolutionIndex.ToString(CultureInfo.InvariantCulture),
                Settings.MouseSensitivity.ToString("R", CultureInfo.InvariantCulture),
                Settings.InvertVertical ? "1" : "0"
            });
        }

        public static FrontendPreferencesState Decode(string payload)
        {
            FrontendPreferencesState result = new FrontendPreferencesState();
            if (string.IsNullOrEmpty(payload))
                return result;
            string[] fields = payload.Split('\n');
            result.Game = Field(fields, 0).ToLowerInvariant() == "vc" ? "vc" : "iii";
            result.Root = Field(fields, 1);
            result.Theme = Field(fields, 2) == "light" ? "light" : "dark";
            result.Settings.Language = (FrontendLanguage)ParseInt(Field(fields, 3), 0, 4, 0);
            result.Settings.Brightness = ParseInt(Field(fields, 4), 0, 256, 128);
            result.Settings.DrawDistance = ParseFloat(Field(fields, 5), 0.8f, 1.6f, 1.2f);
            result.Settings.MusicVolume = ParseInt(Field(fields, 6), 0, 128, 64);
            result.Settings.SfxVolume = ParseInt(Field(fields, 7), 0, 128, 64);
            result.Settings.FrameLimiter = Field(fields, 8) != "0";
            result.Settings.VSync = Field(fields, 9) != "0";
            result.Settings.Trails = Field(fields, 10) != "0";
            result.Settings.Subtitles = Field(fields, 11) == "1";
            result.Settings.WideScreen = Field(fields, 12) == "1";
            result.Settings.ResolutionIndex = ParseInt(Field(fields, 13), 0, 3, 0);
            result.Settings.MouseSensitivity = ParseFloat(Field(fields, 14), 0.001f, 0.01f, 0.005f);
            result.Settings.InvertVertical = Field(fields, 15) == "1";
            return result;
        }

        internal void ApplyFrontend(FrontendSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            Settings = settings;
        }

        private static string Field(string[] fields, int index)
        {
            return index < fields.Length ? fields[index] : "";
        }

        private static int ParseInt(string text, int minimum, int maximum, int fallback)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return fallback;
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static float ParseFloat(string text, float minimum, float maximum, float fallback)
        {
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }

    public static class FrontendPreferencesText
    {
        public static string Escape(string value)
        {
            if (value == null)
                throw new ArgumentNullException("value");
            return value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
        }

        public static string Unescape(string value)
        {
            if (value == null)
                throw new ArgumentNullException("value");
            return value.Replace("%0A", "\n").Replace("%0D", "\r").Replace("%25", "%");
        }

        public static string CreateFile(FrontendPreferencesState state)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            return "state=" + Escape(state.Encode()) + "\n";
        }

        public static bool TryReadFile(string text, out FrontendPreferencesState state)
        {
            state = null;
            if (text == null)
                throw new ArgumentNullException("text");
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("state=", StringComparison.Ordinal))
                    continue;
                state = FrontendPreferencesState.Decode(Unescape(line.Substring(6)));
                return true;
            }
            return false;
        }
    }
}
