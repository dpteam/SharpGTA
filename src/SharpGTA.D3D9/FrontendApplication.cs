using System;
using System.IO;
using System.Threading;
using SharpGTA.Frontend;
using SharpGTA.IO;

namespace SharpGTA.D3D9
{
    internal sealed class FrontendApplication : IDisposable
    {
        private readonly Win32Window window;
        private readonly D3D9Device device;
        private readonly FrontendMenuModel menu;
        private readonly FrontendText text;
        private readonly Gta3Font font;
        private readonly D3D9FrontendRenderer renderer;
        private FrontendLayout layout;
        private bool showMouse;
        private float mouseX = 320.0f;
        private float mouseY = 240.0f;
        private bool disposed;

        internal FrontendApplication(Win32Window window, D3D9Device device, IGameFileSystem files,
            FrontendSettings initialSettings)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (device == null) throw new ArgumentNullException("device");
            if (files == null) throw new ArgumentNullException("files");
            if (initialSettings == null) throw new ArgumentNullException("initialSettings");
            this.window = window;
            this.device = device;
            menu = new FrontendMenuModel();
            initialSettings.CopyTo(menu.Settings);
            text = new FrontendText(files, menu.Settings.Language);
            font = new Gta3Font();
            IIIFrontendAssets assets = IIIFrontendAssets.Load(files);
            renderer = new D3D9FrontendRenderer(device, assets, font);
            layout = BuildLayout();
        }

        internal FrontendMenuModel Menu { get { return menu; } }

        internal int Run(int frames, string screenshot, bool exerciseReset)
        {
            if (screenshot != null && frames <= 0)
                throw new ArgumentException("A finite positive --frames value is required with --screenshot.");
            bool cursorHidden = false;
            try
            {
                NativeMethods.ShowCursor(false);
                cursorHidden = true;
                for (int frame = 0; frames == 0 || frame < frames; frame++)
                {
                    if (!window.ProcessMessages())
                        break;
                    ProcessInput();
                    if (menu.ExitRequested || menu.ModelPreviewRequested)
                        break;
                    ApplyPendingChanges();
                    if (exerciseReset && frame > 0)
                        device.Reset(window.Handle, device.Width, device.Height);

                    layout = BuildLayout();
                    renderer.Render(layout);
                    if (screenshot != null && frame == frames - 1)
                        SaveScreenshot(screenshot);
                    device.Present();
                    Thread.Sleep(menu.Settings.FrameLimiter ? 15 : 1);
                }
                return 0;
            }
            finally
            {
                if (cursorHidden)
                    NativeMethods.ShowCursor(true);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            renderer.Dispose();
        }

        private void ProcessInput()
        {
            WindowInputEvent input;
            while (window.TryReadInput(out input))
            {
                if (input.Type == WindowInputType.MouseMove)
                {
                    mouseX = input.X;
                    mouseY = input.Y;
                    showMouse = true;
                    SelectAt(mouseX, mouseY);
                }
                else if (input.Type == WindowInputType.LeftButtonDown)
                {
                    mouseX = input.X;
                    mouseY = input.Y;
                    showMouse = true;
                    FrontendHitRegion region = SelectAt(mouseX, mouseY);
                    if (region != null)
                    {
                        if (region.IsSlider && mouseX >= region.SliderLeft && mouseX <= region.SliderRight)
                            menu.SetSlider(region.Setting, region.GetSliderValue(mouseX));
                        else
                            menu.Activate();
                    }
                }
                else if (input.Type == WindowInputType.KeyDown)
                {
                    showMouse = false;
                    switch (input.Key)
                    {
                        case 38: menu.MoveSelection(-1); break; // Up
                        case 40: menu.MoveSelection(1); break;  // Down
                        case 37: Adjust(-1); break;
                        case 39: Adjust(1); break;
                        case 13: menu.Activate(); break;        // Enter
                        case 27: menu.Back(); break;            // Escape
                    }
                }
            }
        }

        private FrontendHitRegion SelectAt(float x, float y)
        {
            if (layout == null)
                return null;
            FrontendHitRegion region = layout.HitTest(x, y);
            if (region != null)
                menu.Select(region.OptionIndex);
            return region;
        }

        private void Adjust(int direction)
        {
            FrontendOption option = menu.SelectedOption;
            if (option.Action == FrontendOptionAction.Adjust)
                menu.Adjust(option.Setting, direction);
        }

        private void ApplyPendingChanges()
        {
            if (menu.LanguageChanged)
            {
                text.SetLanguage(menu.Settings.Language);
                menu.ClearLanguageChanged();
                layout = null;
            }
            bool reset = false;
            if (menu.ResolutionChanged)
            {
                window.ResizeClient(menu.Settings.ResolutionWidth, menu.Settings.ResolutionHeight);
                menu.ClearResolutionChanged();
                layout = null;
                reset = true;
            }
            if (menu.PresentationChanged)
            {
                device.VerticalSync = menu.Settings.VSync;
                menu.ClearPresentationChanged();
                reset = true;
            }
            if (window.ClientWidth > 0 && window.ClientHeight > 0 &&
                (reset || window.ClientWidth != device.Width || window.ClientHeight != device.Height))
                device.Reset(window.Handle, window.ClientWidth, window.ClientHeight);
        }

        private FrontendLayout BuildLayout()
        {
            return IIIFrontendLayout.Build(menu, text, font, device.Width, device.Height,
                showMouse, mouseX, mouseY);
        }

        private void SaveScreenshot(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            byte[] pixels = device.ReadBackBufferBgra(device.Width, device.Height);
            BmpWriter.WriteBgra(fullPath, device.Width, device.Height, pixels);
            Console.WriteLine("Screenshot: " + fullPath);
        }
    }
}
