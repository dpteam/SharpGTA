using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SharpGTA.Launcher
{
    internal static class GameRunner
    {
        internal static string FindExecutable(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return Path.GetFullPath(configured);
            string directory = Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_BIN_DIR");
            if (string.IsNullOrEmpty(directory))
                directory = Path.GetDirectoryName(Application.ExecutablePath);
            string direct = Path.Combine(directory, "SharpGTA.D3D9.exe");
            if (File.Exists(direct))
                return direct;
            DirectoryInfo parent = new DirectoryInfo(directory);
            for (int depth = 0; depth < 6 && parent != null; depth++, parent = parent.Parent)
            {
                string candidate = Path.Combine(parent.FullName,
                    @"SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        internal static string BuildArguments(LauncherState state)
        {
            return BuildArguments(state, Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_SMOKE") == "1");
        }

        internal static string BuildArguments(LauncherState state, bool smoke)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            StringBuilder arguments = new StringBuilder();
            if (state.Game == "iii")
                Append(arguments, "--menu");
            Append(arguments, "--game", state.Game);
            Append(arguments, "--root", state.Root);
            Append(arguments, "--language", ((int)state.Language).ToString(CultureInfo.InvariantCulture));
            Append(arguments, "--resolution", state.ResolutionWidth.ToString(CultureInfo.InvariantCulture) + "x" +
                state.ResolutionHeight.ToString(CultureInfo.InvariantCulture));
            if (!state.VSync)
                Append(arguments, "--no-vsync");
            if (!state.FrameLimiter)
                Append(arguments, "--no-frame-limit");
            if (smoke)
            {
                Append(arguments, "--hidden");
                Append(arguments, "--frames", "1");
                Append(arguments, "--screenshot", Path.Combine(LauncherSettings.DataDirectory, "launcher-smoke.bmp"));
            }
            return arguments.ToString().TrimEnd();
        }

        internal static Process Start(LauncherState state)
        {
            state.Validate();
            string executable = FindExecutable(null);
            if (executable == null)
                throw new FileNotFoundException("SharpGTA.D3D9.exe was not found beside the launcher or in its build directory.");
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = executable;
            start.Arguments = BuildArguments(state);
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            start.UseShellExecute = false;
            start.CreateNoWindow = false;
            Process process = Process.Start(start);
            if (Environment.GetEnvironmentVariable("SHARPGTA_LAUNCHER_SMOKE") == "1")
            {
                try
                {
                    process.WaitForExit(10000);
                    if (!process.HasExited)
                        throw new TimeoutException("The launcher smoke frontend did not exit within 10 seconds.");
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("The launcher smoke frontend exited with code " + process.ExitCode + ".");
                }
                finally { if (!process.HasExited) process.Kill(); }
            }
            return process;
        }

        private static void Append(StringBuilder arguments, string name)
        {
            if (arguments.Length > 0)
                arguments.Append(' ');
            arguments.Append(name);
        }

        private static void Append(StringBuilder arguments, string name, string value)
        {
            Append(arguments, name);
            arguments.Append(' ');
            if (value == null || (value.IndexOf(' ') < 0 && value.IndexOf('\t') < 0 && value.Length > 0))
                arguments.Append(value ?? "");
            else
                arguments.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }
    }
}
