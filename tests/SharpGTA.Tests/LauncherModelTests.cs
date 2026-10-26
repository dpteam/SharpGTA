using SharpGTA.Frontend;
using SharpGTA.Launcher;

namespace SharpGTA.Tests
{
    public sealed class LauncherModelTests
    {
        [Test]
        public void SharedStatePreservesEverySettingAndBuildsQuotedArguments()
        {
            LauncherState state = new LauncherState();
            state.Game = "iii";
            state.Root = "C:\\Games\\GTA III";
            state.Theme = "light";
            state.Settings.Language = FrontendLanguage.French;
            state.Settings.Brightness = 96;
            state.Settings.DrawDistance = 1.35f;
            state.Settings.MusicVolume = 77;
            state.Settings.SfxVolume = 88;
            state.Settings.FrameLimiter = false;
            state.Settings.VSync = false;
            state.Settings.Trails = false;
            state.Settings.Subtitles = true;
            state.Settings.WideScreen = true;
            state.Settings.ResolutionIndex = 1;
            state.Settings.MouseSensitivity = 0.0075f;
            state.Settings.InvertVertical = true;

            LauncherState decoded = LauncherState.Decode(state.Encode());
            Assert.Equal("iii", decoded.Game);
            Assert.Equal(state.Root, decoded.Root);
            Assert.Equal("light", decoded.Theme);
            Assert.Equal(FrontendLanguage.French, decoded.Settings.Language);
            Assert.Equal(96, decoded.Settings.Brightness);
            Assert.Near(1.35f, decoded.Settings.DrawDistance);
            Assert.Equal(77, decoded.Settings.MusicVolume);
            Assert.Equal(88, decoded.Settings.SfxVolume);
            Assert.False(decoded.Settings.FrameLimiter);
            Assert.False(decoded.Settings.VSync);
            Assert.False(decoded.Settings.Trails);
            Assert.True(decoded.Settings.Subtitles);
            Assert.True(decoded.Settings.WideScreen);
            Assert.Equal(800, decoded.Settings.ResolutionWidth);
            Assert.Equal(600, decoded.Settings.ResolutionHeight);
            Assert.Near(0.0075f, decoded.Settings.MouseSensitivity);
            Assert.True(decoded.Settings.InvertVertical);

            string arguments = GameRunner.BuildArguments(decoded, false);
            Assert.Equal("--menu --game iii --root \"C:\\Games\\GTA III\" --language 1 --resolution 800x600 --no-vsync --no-frame-limit", arguments);
            Assert.Throws<System.ArgumentException>(delegate { GameRunner.BuildArguments(null, false); });
        }

        [Test]
        public void EmbeddedLauncherPageUsesMshtmlSafeAssetsAndCentralBranding()
        {
            string page = LauncherForm.AssemblePage();
            Assert.False(page.Contains("__BRAND_"));
            Assert.True(page.Contains("SharpGTA Launcher"));
            Assert.True(page.Contains("sharpgta://"));
            Assert.True(page.Contains("sharpGtaReceive"));
            Assert.False(page.Contains("dptdoom://"));
            Assert.False(page.Contains("display: grid"));
            Assert.False(page.Contains("display: flex"));
            Assert.False(page.Contains("var(--"));
            Assert.False(page.Contains("http://"));
            Assert.False(page.Contains("https://"));
            Assert.False(page.Contains("=>"));
            Assert.False(page.Contains("let "));
            Assert.False(page.Contains("const "));
            Assert.Equal("sharpgta://", LauncherForm.BridgeScheme);
        }
    }
}
