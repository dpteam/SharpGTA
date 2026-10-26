using SharpGTA.Frontend;

namespace SharpGTA.Tests
{
    public sealed class FrontendModelTests
    {
        [Test]
        public void NavigationHistoryAndAudioAdjustmentsWorkWithoutGpu()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            Assert.Equal(FrontendPage.StartMenu, menu.CurrentPage);
            Assert.Equal(3, menu.CurrentOptions.Length);
            menu.MoveSelection(1);
            Assert.Equal("FET_OPT", menu.SelectedOption.LabelKey);
            menu.Activate();
            Assert.Equal(FrontendPage.Options, menu.CurrentPage);
            menu.MoveSelection(1);
            menu.Activate();
            Assert.Equal(FrontendPage.Sound, menu.CurrentPage);
            Assert.Equal(64, menu.Settings.MusicVolume);
            menu.Adjust(FrontendSetting.MusicVolume, -1);
            Assert.Equal(60, menu.Settings.MusicVolume);
            menu.Back();
            Assert.Equal(FrontendPage.Options, menu.CurrentPage);
            menu.Back();
            Assert.Equal(FrontendPage.StartMenu, menu.CurrentPage);
            Assert.False(menu.ExitRequested);
        }

        [Test]
        public void ResolutionVsyncRestoreAndLanguageRaiseRealHostChanges()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            menu.Select(1);
            menu.Activate();
            menu.Select(2);
            menu.Activate();
            Assert.Equal(FrontendPage.Display, menu.CurrentPage);
            menu.Adjust(FrontendSetting.VSync, 1);
            Assert.False(menu.Settings.VSync);
            Assert.True(menu.PresentationChanged);
            menu.ClearPresentationChanged();

            menu.Adjust(FrontendSetting.ScreenResolution, 1);
            Assert.Equal(800, menu.Settings.ResolutionWidth);
            Assert.Equal(600, menu.Settings.ResolutionHeight);
            Assert.True(menu.ResolutionChanged);
            menu.ClearResolutionChanged();
            menu.Select(8);
            menu.Activate();
            Assert.Equal(640, menu.Settings.ResolutionWidth);
            Assert.Equal(480, menu.Settings.ResolutionHeight);
            Assert.True(menu.Settings.VSync);
            Assert.True(menu.ResolutionChanged);
            Assert.True(menu.PresentationChanged);

            menu.Back();
            menu.Select(3);
            menu.Activate();
            Assert.Equal(FrontendPage.Language, menu.CurrentPage);
            menu.Select(1);
            menu.Activate();
            Assert.Equal(FrontendLanguage.French, menu.Settings.Language);
            Assert.True(menu.LanguageChanged);
        }

        [Test]
        public void ModelPreviewIsExplicitAndSavesAndExitStayHonest()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            menu.Select(0);
            menu.Activate();
            menu.Select(0);
            menu.Activate();
            Assert.Equal(FrontendPage.NewGameConfirm, menu.CurrentPage);
            menu.Select(1);
            menu.Activate();
            Assert.Equal(FrontendPage.NewGameConfirm, menu.CurrentPage);
            Assert.True(menu.ModelPreviewRequested);
            Assert.Equal(null, menu.Notice);
            Assert.False(menu.ExitRequested);

            menu.Back();
            menu.Select(1);
            menu.Activate();
            Assert.Equal(FrontendPage.LoadGame, menu.CurrentPage);
            menu.Select(1);
            menu.Activate();
            Assert.Equal("Save profiles and loading are not implemented yet.", menu.Notice);

            menu.Back();
            menu.Back();
            menu.Select(2);
            menu.Activate();
            Assert.Equal(FrontendPage.Exit, menu.CurrentPage);
            menu.Select(0);
            menu.Activate();
            Assert.False(menu.ExitRequested);
            menu.Select(2);
            menu.Activate();
            menu.Select(1);
            menu.Activate();
            Assert.True(menu.ExitRequested);
        }

        [Test]
        public void SlidersClampAndSettingsRestoreTheirOwnDefaults()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            menu.SetSlider(FrontendSetting.Brightness, -10.0f);
            Assert.Equal(0, menu.Settings.Brightness);
            menu.SetSlider(FrontendSetting.Brightness, 10.0f);
            Assert.Equal(256, menu.Settings.Brightness);
            menu.SetSlider(FrontendSetting.MusicVolume, 0.5f);
            Assert.Equal(64, menu.Settings.MusicVolume);
            menu.SetSlider(FrontendSetting.DrawDistance, 0.5f);
            Assert.Near(1.2f, menu.Settings.DrawDistance);
            for (int i = 0; i < 100; i++)
                menu.Adjust(FrontendSetting.MusicVolume, 1);
            Assert.Equal(128, menu.Settings.MusicVolume);
            for (int i = 0; i < 100; i++)
                menu.Adjust(FrontendSetting.MusicVolume, -1);
            Assert.Equal(0, menu.Settings.MusicVolume);
            menu.Adjust(FrontendSetting.InvertVertical, 1);
            Assert.True(menu.Settings.InvertVertical);
        }
    }
}
