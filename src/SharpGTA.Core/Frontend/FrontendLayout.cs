using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Frontend
{
    public enum FrontendDrawKind
    {
        Sprite,
        Rectangle,
        Text
    }

    public enum FrontendTextAlign
    {
        Left,
        Center,
        Right
    }

    public sealed class FrontendDrawCommand
    {
        internal FrontendDrawCommand(FrontendDrawKind kind, string textureName, string text,
            int fontStyle, float x, float y, float right, float bottom,
            float u0, float v0, float u1, float v1, float scaleX, float scaleY,
            uint colorArgb, FrontendTextAlign align)
        {
            Kind = kind;
            TextureName = textureName;
            Text = text;
            FontStyle = fontStyle;
            X = x;
            Y = y;
            Right = right;
            Bottom = bottom;
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ColorArgb = colorArgb;
            Align = align;
        }

        public FrontendDrawKind Kind { get; private set; }
        public string TextureName { get; private set; }
        public string Text { get; private set; }
        public int FontStyle { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Right { get; private set; }
        public float Bottom { get; private set; }
        public float U0 { get; private set; }
        public float V0 { get; private set; }
        public float U1 { get; private set; }
        public float V1 { get; private set; }
        public float ScaleX { get; private set; }
        public float ScaleY { get; private set; }
        public uint ColorArgb { get; private set; }
        public FrontendTextAlign Align { get; private set; }
    }

    public sealed class FrontendHitRegion
    {
        internal FrontendHitRegion(int optionIndex, FrontendSetting setting,
            float left, float top, float right, float bottom, float sliderLeft, float sliderRight)
        {
            OptionIndex = optionIndex;
            Setting = setting;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            SliderLeft = sliderLeft;
            SliderRight = sliderRight;
        }

        public int OptionIndex { get; private set; }
        public FrontendSetting Setting { get; private set; }
        public float Left { get; private set; }
        public float Top { get; private set; }
        public float Right { get; private set; }
        public float Bottom { get; private set; }
        public float SliderLeft { get; private set; }
        public float SliderRight { get; private set; }
        public bool IsSlider { get { return SliderRight > SliderLeft; } }

        public bool Contains(float x, float y)
        {
            return x > Left && x < Right && y > Top && y < Bottom;
        }

        public float GetSliderValue(float x)
        {
            if (!IsSlider)
                throw new InvalidOperationException("Hit region is not a slider.");
            return (x - SliderLeft) / (SliderRight - SliderLeft);
        }
    }

    public sealed class FrontendLayout
    {
        internal FrontendLayout(List<FrontendDrawCommand> commands, List<FrontendHitRegion> hitRegions)
        {
            Commands = commands.AsReadOnly();
            HitRegions = hitRegions.AsReadOnly();
        }

        public ReadOnlyCollection<FrontendDrawCommand> Commands { get; private set; }
        public ReadOnlyCollection<FrontendHitRegion> HitRegions { get; private set; }

        public FrontendHitRegion HitTest(float x, float y)
        {
            for (int i = 0; i < HitRegions.Count; i++)
            {
                if (HitRegions[i].Contains(x, y))
                    return HitRegions[i];
            }
            return null;
        }
    }

    public static class IIIFrontendLayout
    {
        private const uint White = 0xFFFFFFFFu;
        private const uint Orange = 0xFFEBAA32u;
        private const uint Selected = 0xFFFFD96Au;
        private const uint Shadow = 0x5A000000u;
        private const uint SelectionBackground = 0x3264C832u;
        private const uint SliderOff = 0xFFB97800u;

        public static FrontendLayout Build(FrontendMenuModel menu, IFrontendText text,
            Gta3Font font, int width, int height, bool showMouse, float mouseX, float mouseY)
        {
            if (menu == null) throw new ArgumentNullException("menu");
            if (text == null) throw new ArgumentNullException("text");
            if (font == null) throw new ArgumentNullException("font");
            if (width <= 0) throw new ArgumentOutOfRangeException("width");
            if (height <= 0) throw new ArgumentOutOfRangeException("height");

            List<FrontendDrawCommand> commands = new List<FrontendDrawCommand>();
            List<FrontendHitRegion> hitRegions = new List<FrontendHitRegion>();
            FrontendPageDefinition page = menu.CurrentDefinition;
            float unitX = width / 640.0f;
            float unitY = height / 480.0f;

            AddSprite(commands, page.BackgroundTexture, 0, 0, width, height, White);
            if (menu.CurrentPage == FrontendPage.StartMenu)
            {
                AddSprite(commands, "gta3logo256", Sx(205, unitX), Sy(70, unitY),
                    Sx(435, unitX), Sy(180, unitY), White);
            }
            if (!string.IsNullOrEmpty(page.HeaderKey))
            {
                AddText(commands, font, text.Get(page.HeaderKey), Gta3Font.Heading,
                    width - Sx(35, unitX), height - Sy(93, unitY),
                    Sx(0.84f, unitX), Sy(1.6f, unitY), 0xFF000000u, FrontendTextAlign.Right);
            }
            if (!string.IsNullOrEmpty(page.PromptKey))
            {
                AddText(commands, font, text.Get(page.PromptKey), Gta3Font.Bank,
                    Sx(40, unitX), Sy(60, unitY), Sx(0.405f, unitX), Sy(0.63f, unitY),
                    Orange, FrontendTextAlign.Left);
            }

            int headerHeight;
            int columnWidth;
            int lineHeight;
            int fontStyle;
            float optionScaleX;
            float optionScaleY;
            bool centered;
            GetPageStyle(menu.CurrentPage, out headerHeight, out columnWidth, out lineHeight,
                out fontStyle, out optionScaleX, out optionScaleY, out centered);
            float nextY = string.IsNullOrEmpty(page.HeaderKey) ? 40.0f : 74.0f;
            float scaleX = Sx(optionScaleX, unitX);
            float scaleY = Sy(optionScaleY, unitY);

            for (int i = 0; i < page.Options.Length; i++)
            {
                FrontendOption option = page.Options[i];
                float itemY = nextY + headerHeight;
                float top = itemY - 2.0f;
                float barHeight = lineHeight * 0.9f;
                if (i == menu.SelectedIndex)
                {
                    AddRectangle(commands, Sx(10, unitX), Sy(top, unitY), width - Sx(11, unitX),
                        Sy(itemY + barHeight, unitY), SelectionBackground);
                }

                float textX = Sx(columnWidth, unitX);
                FrontendTextAlign align = centered ? FrontendTextAlign.Center : FrontendTextAlign.Left;
                string label = text.Get(option.LabelKey);
                uint color = i == menu.SelectedIndex ? Selected : Orange;
                AddText(commands, font, label, fontStyle, textX + 1, Sy(itemY + 1, unitY),
                    scaleX, scaleY, Shadow, align);
                AddText(commands, font, label, fontStyle, textX, Sy(itemY, unitY),
                    scaleX, scaleY, color, align);

                float sliderLeft = 0.0f;
                float sliderRight = 0.0f;
                if (IsSlider(option.Setting))
                {
                    sliderLeft = width - Sx(256 + columnWidth, unitX);
                    sliderRight = width - Sx(columnWidth, unitX);
                    float value = menu.Settings.GetSliderValue(option.Setting);
                    float sliderWidth = sliderRight - sliderLeft;
                    for (int bar = 0; bar < 16; bar++)
                    {
                        float left = sliderLeft + sliderWidth * bar / 16.0f + Sx(1, unitX);
                        float right = sliderLeft + sliderWidth * (bar + 1) / 16.0f - Sx(1, unitX);
                        float smallHeight = Sy(2.0f + 19.6f * (bar + 1) / 16.0f, unitY);
                        float bottom = Sy(itemY + barHeight - 2.0f, unitY);
                        AddRectangle(commands, left, bottom - smallHeight, right, bottom,
                            value * 16.0f > bar ? color : SliderOff);
                    }
                }
                else
                {
                    string valueText = GetValueText(menu, option, text);
                    if (valueText != null)
                    {
                        float right = width - Sx(columnWidth, unitX);
                        AddText(commands, font, valueText, fontStyle, right + 1, Sy(itemY + 1, unitY),
                            scaleX, scaleY, Shadow, FrontendTextAlign.Right);
                        AddText(commands, font, valueText, fontStyle, right, Sy(itemY, unitY),
                            scaleX, scaleY, color, FrontendTextAlign.Right);
                    }
                }
                hitRegions.Add(new FrontendHitRegion(i, option.Setting,
                    Sx(10, unitX), Sy(top, unitY), width - Sx(11, unitX), Sy(itemY + barHeight, unitY),
                    sliderLeft, sliderRight));
                nextY += lineHeight;
            }

            if (!string.IsNullOrEmpty(menu.Notice))
            {
                float noticeScaleX = Sx(0.32f, unitX);
                float noticeScaleY = Sy(0.48f, unitY);
                AddText(commands, font, menu.Notice, Gta3Font.Bank, Sx(321, unitX), height - Sy(21, unitY),
                    noticeScaleX, noticeScaleY, 0xB0000000u, FrontendTextAlign.Center);
                AddText(commands, font, menu.Notice, Gta3Font.Bank, Sx(320, unitX), height - Sy(22, unitY),
                    noticeScaleX, noticeScaleY, White, FrontendTextAlign.Center);
            }
            if (showMouse)
            {
                AddSprite(commands, "mouse", mouseX, mouseY,
                    mouseX + Sx(75, unitX), mouseY + Sy(75, unitY), White);
            }
            return new FrontendLayout(commands, hitRegions);
        }

        private static void GetPageStyle(FrontendPage page, out int headerHeight,
            out int columnWidth, out int lineHeight, out int fontStyle,
            out float scaleX, out float scaleY, out bool centered)
        {
            headerHeight = 40;
            columnWidth = 320;
            lineHeight = 24;
            fontStyle = Gta3Font.Heading;
            scaleX = 0.75f;
            scaleY = 0.9f;
            centered = true;
            switch (page)
            {
                case FrontendPage.StartMenu:
                    headerHeight = 140;
                    break;
                case FrontendPage.NewGameConfirm:
                case FrontendPage.Exit:
                    headerHeight = 60;
                    break;
                case FrontendPage.Controls:
                case FrontendPage.Sound:
                case FrontendPage.Display:
                    headerHeight = 0;
                    columnWidth = 50;
                    lineHeight = 20;
                    scaleX = 0.55f;
                    scaleY = 0.8f;
                    centered = false;
                    break;
                case FrontendPage.LoadGame:
                case FrontendPage.DeleteGame:
                    headerHeight = 38;
                    columnWidth = 120;
                    lineHeight = 20;
                    fontStyle = Gta3Font.Bank;
                    scaleX = 0.45f;
                    scaleY = 0.7f;
                    centered = false;
                    break;
            }
        }

        private static string GetValueText(FrontendMenuModel menu, FrontendOption option, IFrontendText text)
        {
            if (option.Setting == FrontendSetting.ScreenResolution)
                return menu.Settings.ResolutionText;
            if (menu.Settings.IsToggle(option.Setting))
                return text.Get(menu.Settings.GetToggleValue(option.Setting) ? "FEM_ON" : "FEM_OFF");
            return null;
        }

        private static bool IsSlider(FrontendSetting setting)
        {
            return setting == FrontendSetting.Brightness || setting == FrontendSetting.DrawDistance ||
                setting == FrontendSetting.MusicVolume || setting == FrontendSetting.SfxVolume ||
                setting == FrontendSetting.MouseSensitivity;
        }

        private static void AddSprite(List<FrontendDrawCommand> commands, string texture,
            float x, float y, float right, float bottom, uint color)
        {
            commands.Add(new FrontendDrawCommand(FrontendDrawKind.Sprite, texture, null, 0,
                x, y, right, bottom, 0, 0, 1, 1, 0, 0, color, FrontendTextAlign.Left));
        }

        private static void AddRectangle(List<FrontendDrawCommand> commands,
            float x, float y, float right, float bottom, uint color)
        {
            commands.Add(new FrontendDrawCommand(FrontendDrawKind.Rectangle, null, null, 0,
                x, y, right, bottom, 0, 0, 1, 1, 0, 0, color, FrontendTextAlign.Left));
        }

        private static void AddText(List<FrontendDrawCommand> commands, Gta3Font font, string text,
            int style, float x, float y, float scaleX, float scaleY, uint color, FrontendTextAlign align)
        {
            commands.Add(new FrontendDrawCommand(FrontendDrawKind.Text, font.GetTextureName(style), text, style,
                x, y, x, y, 0, 0, 1, 1, scaleX, scaleY, color, align));
        }

        private static float Sx(float value, float unit) { return value * unit; }
        private static float Sy(float value, float unit) { return value * unit; }
    }
}
