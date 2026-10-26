using System;
using System.Collections.Generic;

namespace SharpGTA.Frontend
{
    public enum FrontendPage
    {
        StartMenu,
        NewGame,
        NewGameConfirm,
        LoadGame,
        DeleteGame,
        Options,
        Controls,
        Sound,
        Display,
        Language,
        Exit
    }

    public enum FrontendLanguage
    {
        English,
        French,
        German,
        Italian,
        Spanish
    }

    public enum FrontendSetting
    {
        None,
        Brightness,
        DrawDistance,
        MusicVolume,
        SfxVolume,
        FrameLimiter,
        VSync,
        Trails,
        Subtitles,
        WideScreen,
        ScreenResolution,
        MouseSensitivity,
        InvertVertical
    }

    public enum FrontendOptionAction
    {
        Navigate,
        Back,
        Adjust,
        SetLanguage,
        RestoreDefaults,
        Unsupported,
        ExitApplication
    }

    public sealed class FrontendOption
    {
        internal FrontendOption(string labelKey, FrontendOptionAction action,
            FrontendPage target, FrontendSetting setting, FrontendLanguage language, string unavailableReason)
        {
            LabelKey = labelKey;
            Action = action;
            Target = target;
            Setting = setting;
            Language = language;
            UnavailableReason = unavailableReason;
        }

        public string LabelKey { get; private set; }
        public FrontendOptionAction Action { get; private set; }
        public FrontendPage Target { get; private set; }
        public FrontendSetting Setting { get; private set; }
        public FrontendLanguage Language { get; private set; }
        public string UnavailableReason { get; private set; }
    }

    public sealed class FrontendPageDefinition
    {
        internal FrontendPageDefinition(FrontendPage page, string headerKey, string promptKey,
            string backgroundTexture, FrontendOption[] options)
        {
            Page = page;
            HeaderKey = headerKey;
            PromptKey = promptKey;
            BackgroundTexture = backgroundTexture;
            Options = options;
        }

        public FrontendPage Page { get; private set; }
        public string HeaderKey { get; private set; }
        public string PromptKey { get; private set; }
        public string BackgroundTexture { get; private set; }
        public FrontendOption[] Options { get; private set; }
    }

    public sealed class FrontendSettings
    {
        private static readonly int[] widths = { 640, 800, 1024, 1280 };
        private static readonly int[] heights = { 480, 600, 768, 960 };

        public FrontendSettings()
        {
            RestoreAllDefaults();
        }

        public FrontendLanguage Language { get; internal set; }
        public int Brightness { get; internal set; }
        public float DrawDistance { get; internal set; }
        public int MusicVolume { get; internal set; }
        public int SfxVolume { get; internal set; }
        public bool FrameLimiter { get; internal set; }
        public bool VSync { get; internal set; }
        public bool Trails { get; internal set; }
        public bool Subtitles { get; internal set; }
        public bool WideScreen { get; internal set; }
        public int ResolutionIndex { get; internal set; }
        public float MouseSensitivity { get; internal set; }
        public bool InvertVertical { get; internal set; }
        public int ResolutionWidth { get { return widths[ResolutionIndex]; } }
        public int ResolutionHeight { get { return heights[ResolutionIndex]; } }
        public string ResolutionText { get { return ResolutionWidth + "x" + ResolutionHeight; } }

        internal void CopyTo(FrontendSettings destination)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");
            destination.Language = Language;
            destination.Brightness = Brightness;
            destination.DrawDistance = DrawDistance;
            destination.MusicVolume = MusicVolume;
            destination.SfxVolume = SfxVolume;
            destination.FrameLimiter = FrameLimiter;
            destination.VSync = VSync;
            destination.Trails = Trails;
            destination.Subtitles = Subtitles;
            destination.WideScreen = WideScreen;
            destination.ResolutionIndex = ResolutionIndex;
            destination.MouseSensitivity = MouseSensitivity;
            destination.InvertVertical = InvertVertical;
        }

        internal void Adjust(FrontendSetting setting, int direction)
        {
            if (direction == 0)
                return;
            int sign = direction < 0 ? -1 : 1;
            switch (setting)
            {
                case FrontendSetting.Brightness:
                    Brightness = Clamp(Brightness + sign * 8, 0, 256);
                    break;
                case FrontendSetting.DrawDistance:
                    DrawDistance = Clamp(DrawDistance + sign * 0.05f, 0.8f, 1.6f);
                    break;
                case FrontendSetting.MusicVolume:
                    MusicVolume = Clamp(MusicVolume + sign * 4, 0, 128);
                    break;
                case FrontendSetting.SfxVolume:
                    SfxVolume = Clamp(SfxVolume + sign * 4, 0, 128);
                    break;
                case FrontendSetting.FrameLimiter:
                    FrameLimiter = !FrameLimiter;
                    break;
                case FrontendSetting.VSync:
                    VSync = !VSync;
                    break;
                case FrontendSetting.Trails:
                    Trails = !Trails;
                    break;
                case FrontendSetting.Subtitles:
                    Subtitles = !Subtitles;
                    break;
                case FrontendSetting.WideScreen:
                    WideScreen = !WideScreen;
                    break;
                case FrontendSetting.ScreenResolution:
                    ResolutionIndex = (ResolutionIndex + sign + widths.Length) % widths.Length;
                    break;
                case FrontendSetting.MouseSensitivity:
                    MouseSensitivity = Clamp(MouseSensitivity + sign * 0.0005f, 0.001f, 0.01f);
                    break;
                case FrontendSetting.InvertVertical:
                    InvertVertical = !InvertVertical;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("setting");
            }
        }

        internal float GetSliderValue(FrontendSetting setting)
        {
            switch (setting)
            {
                case FrontendSetting.Brightness: return Brightness / 256.0f;
                case FrontendSetting.DrawDistance: return (DrawDistance - 0.8f) / 0.8f;
                case FrontendSetting.MusicVolume: return MusicVolume / 128.0f;
                case FrontendSetting.SfxVolume: return SfxVolume / 128.0f;
                case FrontendSetting.MouseSensitivity: return (MouseSensitivity - 0.001f) / 0.009f;
                default: throw new ArgumentException("Setting is not a slider.", "setting");
            }
        }

        internal void SetSliderValue(FrontendSetting setting, float value)
        {
            value = Clamp(value, 0.0f, 1.0f);
            switch (setting)
            {
                case FrontendSetting.Brightness:
                    Brightness = (int)Math.Round(value * 256.0f);
                    break;
                case FrontendSetting.DrawDistance:
                    DrawDistance = 0.8f + value * 0.8f;
                    break;
                case FrontendSetting.MusicVolume:
                    MusicVolume = (int)Math.Round(value * 128.0f);
                    break;
                case FrontendSetting.SfxVolume:
                    SfxVolume = (int)Math.Round(value * 128.0f);
                    break;
                case FrontendSetting.MouseSensitivity:
                    MouseSensitivity = 0.001f + value * 0.009f;
                    break;
                default:
                    throw new ArgumentException("Setting is not a slider.", "setting");
            }
        }

        internal bool IsToggle(FrontendSetting setting)
        {
            return setting == FrontendSetting.FrameLimiter || setting == FrontendSetting.VSync ||
                setting == FrontendSetting.Trails || setting == FrontendSetting.Subtitles ||
                setting == FrontendSetting.WideScreen || setting == FrontendSetting.InvertVertical;
        }

        internal bool GetToggleValue(FrontendSetting setting)
        {
            switch (setting)
            {
                case FrontendSetting.FrameLimiter: return FrameLimiter;
                case FrontendSetting.VSync: return VSync;
                case FrontendSetting.Trails: return Trails;
                case FrontendSetting.Subtitles: return Subtitles;
                case FrontendSetting.WideScreen: return WideScreen;
                case FrontendSetting.InvertVertical: return InvertVertical;
                default: throw new ArgumentException("Setting is not a toggle.", "setting");
            }
        }

        internal void RestoreAudioDefaults()
        {
            MusicVolume = 64;
            SfxVolume = 64;
        }

        internal void RestoreDisplayDefaults()
        {
            Brightness = 128;
            DrawDistance = 1.2f;
            FrameLimiter = true;
            VSync = true;
            Trails = true;
            Subtitles = false;
            WideScreen = false;
            ResolutionIndex = 0;
        }

        internal void RestoreControlDefaults()
        {
            MouseSensitivity = 0.005f;
            InvertVertical = false;
        }

        private void RestoreAllDefaults()
        {
            Language = FrontendLanguage.English;
            RestoreAudioDefaults();
            RestoreDisplayDefaults();
            RestoreControlDefaults();
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }

    public sealed class FrontendMenuModel
    {
        private readonly Dictionary<FrontendPage, FrontendPageDefinition> pages;
        private readonly Stack<FrontendPage> history = new Stack<FrontendPage>();

        public FrontendMenuModel()
        {
            pages = CreateIIIPages();
            Settings = new FrontendSettings();
            CurrentPage = FrontendPage.StartMenu;
        }

        public FrontendSettings Settings { get; private set; }
        public FrontendPage CurrentPage { get; private set; }
        public int SelectedIndex { get; private set; }
        public string Notice { get; private set; }
        public bool ExitRequested { get; private set; }
        public bool LanguageChanged { get; private set; }
        public bool ResolutionChanged { get; private set; }
        public bool PresentationChanged { get; private set; }
        public FrontendPageDefinition CurrentDefinition { get { return pages[CurrentPage]; } }
        public FrontendOption[] CurrentOptions { get { return CurrentDefinition.Options; } }
        public FrontendOption SelectedOption { get { return CurrentOptions[SelectedIndex]; } }

        public void MoveSelection(int direction)
        {
            if (direction == 0)
                return;
            int count = CurrentOptions.Length;
            SelectedIndex = (SelectedIndex + (direction < 0 ? -1 : 1) + count) % count;
            Notice = null;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= CurrentOptions.Length)
                throw new ArgumentOutOfRangeException("index");
            SelectedIndex = index;
        }

        public void Activate()
        {
            FrontendOption option = SelectedOption;
            switch (option.Action)
            {
                case FrontendOptionAction.Navigate:
                    Navigate(option.Target);
                    break;
                case FrontendOptionAction.Back:
                    Back();
                    break;
                case FrontendOptionAction.Adjust:
                    if (Settings.IsToggle(option.Setting) || option.Setting == FrontendSetting.ScreenResolution)
                        Adjust(option.Setting, 1);
                    break;
                case FrontendOptionAction.SetLanguage:
                    if (Settings.Language != option.Language)
                    {
                        Settings.Language = option.Language;
                        LanguageChanged = true;
                    }
                    Notice = null;
                    break;
                case FrontendOptionAction.RestoreDefaults:
                    RestoreDefaults();
                    break;
                case FrontendOptionAction.Unsupported:
                    Notice = option.UnavailableReason;
                    break;
                case FrontendOptionAction.ExitApplication:
                    ExitRequested = true;
                    break;
                default:
                    throw new InvalidOperationException("Unknown frontend action.");
            }
        }

        public void Adjust(FrontendSetting setting, int direction)
        {
            int oldResolution = Settings.ResolutionIndex;
            Settings.Adjust(setting, direction);
            if (setting == FrontendSetting.ScreenResolution && oldResolution != Settings.ResolutionIndex)
                ResolutionChanged = true;
            if (setting == FrontendSetting.VSync)
                PresentationChanged = true;
            Notice = null;
        }

        public void SetSlider(FrontendSetting setting, float value)
        {
            Settings.SetSliderValue(setting, value);
            Notice = null;
        }

        public void Back()
        {
            Notice = null;
            if (history.Count == 0)
            {
                CurrentPage = FrontendPage.StartMenu;
                SelectedIndex = 0;
                return;
            }
            CurrentPage = history.Pop();
            SelectedIndex = 0;
        }

        public void ClearLanguageChanged() { LanguageChanged = false; }
        public void ClearResolutionChanged() { ResolutionChanged = false; }
        public void ClearPresentationChanged() { PresentationChanged = false; }

        private void Navigate(FrontendPage target)
        {
            if (target == CurrentPage)
                return;
            history.Push(CurrentPage);
            CurrentPage = target;
            SelectedIndex = 0;
            Notice = null;
        }

        private void RestoreDefaults()
        {
            if (CurrentPage == FrontendPage.Sound)
                Settings.RestoreAudioDefaults();
            else if (CurrentPage == FrontendPage.Controls)
                Settings.RestoreControlDefaults();
            else if (CurrentPage == FrontendPage.Display)
            {
                int oldResolution = Settings.ResolutionIndex;
                Settings.RestoreDisplayDefaults();
                if (oldResolution != Settings.ResolutionIndex)
                    ResolutionChanged = true;
                PresentationChanged = true;
            }
            Notice = null;
        }

        private static Dictionary<FrontendPage, FrontendPageDefinition> CreateIIIPages()
        {
            Dictionary<FrontendPage, FrontendPageDefinition> result =
                new Dictionary<FrontendPage, FrontendPageDefinition>();
            Add(result, FrontendPage.StartMenu, "FEM_MM", null, "mainmenu24",
                Navigate("FEN_STA", FrontendPage.NewGame),
                Navigate("FET_OPT", FrontendPage.Options),
                Navigate("FEM_QT", FrontendPage.Exit));
            Add(result, FrontendPage.NewGame, "FET_SGA", null, "singleplayer24",
                Navigate("FES_SNG", FrontendPage.NewGameConfirm),
                Navigate("GMLOAD", FrontendPage.LoadGame),
                Navigate("FES_DGA", FrontendPage.DeleteGame),
                BackOption());
            Add(result, FrontendPage.NewGameConfirm, "FET_NG", "FESZ_QR", "singleplayer24",
                BackOption("FEM_NO"),
                Unsupported("FEM_YES", "Gameplay bootstrap is not connected yet."));
            Add(result, FrontendPage.LoadGame, "FET_LG", null, "singleplayer24",
                Slots(false));
            Add(result, FrontendPage.DeleteGame, "FET_DG", null, "singleplayer24",
                Slots(true));
            Add(result, FrontendPage.Options, "FET_OPT", null, "Playersetup24",
                Navigate("FET_CTL", FrontendPage.Controls),
                Navigate("FET_AUD", FrontendPage.Sound),
                Navigate("FET_DIS", FrontendPage.Display),
                Navigate("FET_LAN", FrontendPage.Language),
                BackOption());
            Add(result, FrontendPage.Controls, "FET_CTL", null, "Playersetup24",
                Slider("FEC_MSH", FrontendSetting.MouseSensitivity),
                Toggle("FEC_IVV", FrontendSetting.InvertVertical),
                Defaults(),
                BackOption());
            Add(result, FrontendPage.Sound, "FET_AUD", null, "Playersetup24",
                Slider("FEA_MUS", FrontendSetting.MusicVolume),
                Slider("FEA_SFX", FrontendSetting.SfxVolume),
                Defaults(),
                BackOption());
            Add(result, FrontendPage.Display, "FET_DIS", null, "Playersetup24",
                Slider("FED_BRI", FrontendSetting.Brightness),
                Slider("FEM_LOD", FrontendSetting.DrawDistance),
                Toggle("FEM_VSC", FrontendSetting.VSync),
                Toggle("FEM_FRM", FrontendSetting.FrameLimiter),
                Toggle("FED_TRA", FrontendSetting.Trails),
                Toggle("FED_SUB", FrontendSetting.Subtitles),
                Toggle("FED_WIS", FrontendSetting.WideScreen),
                Adjust("FED_RES", FrontendSetting.ScreenResolution),
                Defaults(),
                BackOption());
            Add(result, FrontendPage.Language, "FET_LAN", null, "Playersetup24",
                Language("FEL_ENG", FrontendLanguage.English),
                Language("FEL_FRE", FrontendLanguage.French),
                Language("FEL_GER", FrontendLanguage.German),
                Language("FEL_ITA", FrontendLanguage.Italian),
                Language("FEL_SPA", FrontendLanguage.Spanish),
                BackOption());
            Add(result, FrontendPage.Exit, "FET_QG", "FEQ_SRW", "singleplayer24",
                BackOption("FEM_NO"),
                new FrontendOption("FEM_YES", FrontendOptionAction.ExitApplication,
                    FrontendPage.StartMenu, FrontendSetting.None, FrontendLanguage.English, null));
            return result;
        }

        private static FrontendOption[] Slots(bool delete)
        {
            List<FrontendOption> options = new List<FrontendOption>();
            options.Add(BackOption("FESZ_CA"));
            string reason = delete ? "Save profiles and deletion are not implemented yet." :
                "Save profiles and loading are not implemented yet.";
            for (int i = 0; i < 8; i++)
                options.Add(Unsupported("FEM_SL" + i.ToString(), reason));
            return options.ToArray();
        }

        private static void Add(Dictionary<FrontendPage, FrontendPageDefinition> pages,
            FrontendPage page, string header, string prompt, string background, params FrontendOption[] options)
        {
            pages.Add(page, new FrontendPageDefinition(page, header, prompt, background, options));
        }

        private static FrontendOption Navigate(string key, FrontendPage target)
        {
            return new FrontendOption(key, FrontendOptionAction.Navigate, target,
                FrontendSetting.None, FrontendLanguage.English, null);
        }

        private static FrontendOption BackOption() { return BackOption("FEDS_TB"); }

        private static FrontendOption BackOption(string key)
        {
            return new FrontendOption(key, FrontendOptionAction.Back, FrontendPage.StartMenu,
                FrontendSetting.None, FrontendLanguage.English, null);
        }

        private static FrontendOption Unsupported(string key, string reason)
        {
            return new FrontendOption(key, FrontendOptionAction.Unsupported, FrontendPage.StartMenu,
                FrontendSetting.None, FrontendLanguage.English, reason);
        }

        private static FrontendOption Slider(string key, FrontendSetting setting)
        {
            return new FrontendOption(key, FrontendOptionAction.Adjust, FrontendPage.Sound,
                setting, FrontendLanguage.English, null);
        }

        private static FrontendOption Toggle(string key, FrontendSetting setting)
        {
            return Slider(key, setting);
        }

        private static FrontendOption Adjust(string key, FrontendSetting setting)
        {
            return Slider(key, setting);
        }

        private static FrontendOption Defaults()
        {
            return new FrontendOption("FET_DEF", FrontendOptionAction.RestoreDefaults,
                FrontendPage.StartMenu, FrontendSetting.None, FrontendLanguage.English, null);
        }

        private static FrontendOption Language(string key, FrontendLanguage language)
        {
            return new FrontendOption(key, FrontendOptionAction.SetLanguage, FrontendPage.Language,
                FrontendSetting.None, language, null);
        }
    }
}
