using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SharpGTA.Launcher
{
    /// <summary>Standalone local HTML launcher hosted by the system MSHTML control.</summary>
    public sealed class LauncherForm : Form
    {
        internal const string BridgeScheme = "sharpgta://";
        private readonly LauncherSettings settings;
        private LauncherState state;
        private WebBrowser browser;
        private Process gameProcess;

        public LauncherForm() : this(LauncherSettings.Load())
        {
        }

        internal LauncherForm(LauncherSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            this.settings = settings;
            state = settings.State;
            Text = Branding.Name + " Launcher";
            ClientSize = new Size(940, 680);
            MinimumSize = new Size(780, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x13, 0x15, 0x1D);
            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.WebBrowserShortcutsEnabled = true;
            browser.ScriptErrorsSuppressed = true;
            browser.Navigating += OnNavigating;
            Controls.Add(browser);
            LoadPage();
        }

        internal LauncherState State { get { return state; } }
        internal Process GameProcess { get { return gameProcess; } }
        public bool HasExitedGame
        {
            get { return gameProcess != null && gameProcess.HasExited && gameProcess.ExitCode == 0; }
        }

        public static string AssemblePage()
        {
            string html = ReadAsset("ui.html");
            html = html.Replace("/*__STYLE__*/", Brand(ReadAsset("theme.css")));
            html = html.Replace("/*__SCRIPT__*/", ReadAsset("app.js"));
            return Brand(html);
        }

        public void HandleVerb(string verb, string payload)
        {
            string name = (verb ?? "").Trim().ToLowerInvariant();
            switch (name)
            {
                case "ready":
                    SendState();
                    SendStatus("Ready. Select an original GTA III data folder.", false);
                    Send("storage", (LauncherSettings.IsPortable ? "PORTABLE · " : "LOCALAPPDATA · ") +
                        LauncherSettings.DataDirectory);
                    SendRunning();
                    break;
                case "preview":
                    state = LauncherState.Decode(payload);
                    try { Send("preview", GameRunner.BuildArguments(state)); }
                    catch (Exception exception) { Send("preview", exception.Message); }
                    break;
                case "browse":
                    string[] browseParts = (payload ?? "").Split(new char[] { '\n' }, 2);
                    Browse(browseParts[0], browseParts.Length > 1 ? browseParts[1] : "");
                    break;
                case "save":
                    state = LauncherState.Decode(payload);
                    SaveState();
                    SendStatus("Settings saved.", false);
                    break;
                case "launch":
                    state = LauncherState.Decode(payload);
                    Launch();
                    break;
                default:
                    throw new InvalidOperationException("Unknown launcher bridge verb: " + verb);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (gameProcess != null)
                    gameProcess.Dispose();
                if (browser != null)
                    browser.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string Brand(string asset)
        {
            return asset.Replace("__BRAND_NAME__", Branding.Name)
                .Replace("__BRAND_MARKED__", Branding.Name)
                .Replace("__BRAND_TITLE__", Branding.Name + " Launcher")
                .Replace("__BRAND_TAGLINE__", Branding.Tagline)
                .Replace("__BRAND_VERSION__", Branding.Version)
                .Replace("__BRAND_PRIMARY__", Branding.PrimaryColor)
                .Replace("__BRAND_SECONDARY__", Branding.SecondaryColor)
                .Replace("__BRAND_DARK__", Branding.DarkColor)
                .Replace("__BRAND_ONDARK__", Branding.OnDarkColor);
        }

        private static string ReadAsset(string name)
        {
            using (Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream(
                "SharpGTA.Launcher.Assets." + name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded launcher asset: " + name);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private void LoadPage()
        {
            string directory = Path.Combine(LauncherSettings.DataDirectory, "webview");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "launcher.html");
            File.WriteAllText(path, AssemblePage(), new UTF8Encoding(false));
            browser.Navigate(path);
        }

        private void OnNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            string url = e.Url == null ? "" : e.Url.AbsoluteUri;
            if (!url.StartsWith(BridgeScheme, StringComparison.OrdinalIgnoreCase))
                return;
            e.Cancel = true;
            string verb = url.Substring(BridgeScheme.Length).Trim('/');
            int query = verb.IndexOf('?');
            if (query >= 0)
                verb = verb.Substring(0, query);
            try
            {
                HandleVerb(verb, TakePayload());
            }
            catch (Exception exception)
            {
                SendStatus("Launcher error: " + exception.Message, true);
            }
        }

        private string TakePayload()
        {
            if (browser.Document == null)
                return "";
            try { return Convert.ToString(browser.Document.InvokeScript("sharpGtaTakePayload")) ?? ""; }
            catch { return ""; }
        }

        internal void Send(string verb, string payload)
        {
            if (browser.Document == null)
                return;
            try { browser.Document.InvokeScript("sharpGtaReceive", new object[] { verb, payload ?? "" }); }
            catch { /* The document may be navigating or closing. */ }
        }

        private void SendState()
        {
            Send("state", state.Encode());
            Send("theme", state.Theme);
        }

        private void SendStatus(string text, bool error)
        {
            Send("status", (text ?? "") + "\n" + (error ? "1" : "0"));
        }

        private void SendRunning()
        {
            bool running = false;
            try { running = gameProcess != null && !gameProcess.HasExited; }
            catch { }
            Send("running", running ? "1" : "0");
        }

        private void Browse(string kind, string payload)
        {
            state = LauncherState.Decode(payload);
            if ((kind ?? "").ToLowerInvariant() != "root")
                throw new ArgumentException("Unknown browse target: " + kind);
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the original GTA III or Vice City data folder";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(state.Root) && Directory.Exists(state.Root))
                    dialog.SelectedPath = state.Root;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    state.Root = dialog.SelectedPath;
                    SendState();
                    Send("preview", GameRunner.BuildArguments(state));
                    SaveState();
                }
            }
        }

        private void SaveState()
        {
            settings.State = state;
            settings.Save();
        }

        private void Launch()
        {
            if (gameProcess != null && !gameProcess.HasExited)
                throw new InvalidOperationException("The game is already running.");
            state.Validate();
            if (gameProcess != null)
                gameProcess.Dispose();
            gameProcess = GameRunner.Start(state);
            SaveState();
            SendStatus("Game launched. The launcher remains available after it exits.", false);
            SendRunning();
        }
    }
}
