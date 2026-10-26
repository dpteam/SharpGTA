using System;
using SharpGTA.Frontend;

namespace SharpGTA.Tests
{
    public sealed class FrontendLayoutTests
    {
        [Test]
        public void OriginalMainMenuSpritesTextAndHitRegionsShareCoordinates()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            FrontendLayout layout = IIIFrontendLayout.Build(menu, new KeyText(), new Gta3Font(),
                640, 480, true, 100, 120);
            Assert.Equal("mainmenu24", layout.Commands[0].TextureName);
            Assert.Equal("gta3logo256", layout.Commands[1].TextureName);
            Assert.Equal("mouse", layout.Commands[layout.Commands.Count - 1].TextureName);
            Assert.True(HasText(layout, "FEM_MM"));
            Assert.True(HasText(layout, "FEN_STA"));
            Assert.Equal(3, layout.HitRegions.Count);
            Assert.Near(212.0f, layout.HitRegions[0].Top);
            Assert.Near(235.6f, layout.HitRegions[0].Bottom);
            Assert.Equal(0, layout.HitTest(320, 220).OptionIndex);
            Assert.Equal(1, layout.HitTest(320, 238).OptionIndex);
            Assert.Equal(null, layout.HitTest(320, 100));
            Assert.True(HasRectangle(layout, 0x3264C832u));
        }

        [Test]
        public void DisplaySlidersValuesAndResolutionScaleWithTheBackBuffer()
        {
            FrontendMenuModel menu = new FrontendMenuModel();
            menu.Select(1);
            menu.Activate();
            menu.Select(2);
            menu.Activate();
            FrontendLayout layout = IIIFrontendLayout.Build(menu, new KeyText(), new Gta3Font(),
                640, 480, false, 0, 0);
            Assert.Equal(10, layout.HitRegions.Count);
            Assert.True(layout.HitRegions[0].IsSlider);
            Assert.Near(334.0f, layout.HitRegions[0].SliderLeft);
            Assert.Near(590.0f, layout.HitRegions[0].SliderRight);
            Assert.Near(0.5f, layout.HitRegions[0].GetSliderValue(462.0f));
            Assert.True(HasText(layout, "640x480"));
            Assert.True(HasText(layout, "FEM_ON"));
            Assert.Equal(33, CountRectangles(layout));

            menu.Adjust(FrontendSetting.ScreenResolution, 1);
            layout = IIIFrontendLayout.Build(menu, new KeyText(), new Gta3Font(), 800, 600, false, 0, 0);
            Assert.True(HasText(layout, "800x600"));
            Assert.Near(90.0f, layout.HitRegions[0].Top);
        }

        [Test]
        public void Gta3FontUsesOriginalMetricsAtlasAndSkipsFormattingTokens()
        {
            Gta3Font font = new Gta3Font();
            Assert.Equal("font2", font.GetTextureName(Gta3Font.Bank));
            Assert.Equal("pager", font.GetTextureName(Gta3Font.Pager));
            Assert.Equal("font1", font.GetTextureName(Gta3Font.Heading));
            Assert.Equal(26, font.GetCharacterWidth(Gta3Font.Bank, 'A' - ' '));
            Assert.Equal(19, font.GetCharacterWidth(Gta3Font.Heading, 'A' - ' '));
            Assert.Near(28.5f, font.Measure("AA", Gta3Font.Heading, 0.75f));
            int[] glyphs = font.GetGlyphs("A~1~ B");
            Assert.Equal(3, glyphs.Length);
            Assert.Equal('A' - ' ', glyphs[0]);
            Assert.Equal(0, glyphs[1]);
            Assert.Equal('B' - ' ', glyphs[2]);
        }

        private static bool HasText(FrontendLayout layout, string value)
        {
            for (int i = 0; i < layout.Commands.Count; i++)
            {
                if (layout.Commands[i].Kind == FrontendDrawKind.Text && layout.Commands[i].Text == value)
                    return true;
            }
            return false;
        }

        private static bool HasRectangle(FrontendLayout layout, uint color)
        {
            for (int i = 0; i < layout.Commands.Count; i++)
            {
                if (layout.Commands[i].Kind == FrontendDrawKind.Rectangle && layout.Commands[i].ColorArgb == color)
                    return true;
            }
            return false;
        }

        private static int CountRectangles(FrontendLayout layout)
        {
            int count = 0;
            for (int i = 0; i < layout.Commands.Count; i++)
            {
                if (layout.Commands[i].Kind == FrontendDrawKind.Rectangle)
                    count++;
            }
            return count;
        }

        private sealed class KeyText : IFrontendText
        {
            public string Get(string key)
            {
                if (key == null)
                    throw new ArgumentNullException("key");
                return key;
            }
        }
    }
}
