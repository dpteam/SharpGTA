using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpGTA.IO
{
    /// <summary>
    /// Read-only, root-scoped game file access. It never changes the process current
    /// directory and accepts only relative paths without traversal or volume syntax.
    /// Existing path components are matched case-insensitively for original PC data.
    /// </summary>
    public sealed class PhysicalGameFileSystem : IGameFileSystem
    {
        public const int DefaultTextLimit = 64 * 1024 * 1024;
        private readonly string root;
        private readonly string rootPrefix;
        private readonly Dictionary<string, string> resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public PhysicalGameFileSystem(string root)
        {
            if (root == null)
                throw new ArgumentNullException("root");
            this.root = Path.GetFullPath(root);
            if (!Directory.Exists(this.root))
                throw new DirectoryNotFoundException("Game data root does not exist: " + this.root);
            rootPrefix = this.root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        public string Root
        {
            get { return root; }
        }

        public bool FileExists(string relativePath)
        {
            string fullPath = Resolve(relativePath);
            return fullPath != null;
        }

        public Stream OpenRead(string relativePath)
        {
            string fullPath = Resolve(relativePath);
            if (fullPath == null)
                throw new FileNotFoundException("Game file was not found under " + root + ": " + relativePath, relativePath);
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, false);
        }

        public byte[] ReadAllBytes(string relativePath, int maximumLength)
        {
            using (Stream stream = OpenRead(relativePath))
                return StreamUtilities.ReadAllBytes(stream, maximumLength);
        }

        /// <summary>
        /// Reads original data files as one-byte characters. Structural syntax and names
        /// are ASCII; bytes above 127 are preserved rather than decoded using the locale.
        /// </summary>
        public string ReadAllText(string relativePath, int maximumLength)
        {
            byte[] bytes = ReadAllBytes(relativePath, maximumLength);
            char[] characters = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
                characters[i] = (char)bytes[i];
            return new string(characters);
        }

        public string ReadAllText(string relativePath)
        {
            return ReadAllText(relativePath, DefaultTextLimit);
        }

        public string NormalizePath(string relativePath)
        {
            if (relativePath == null)
                throw new ArgumentNullException("relativePath");
            if (relativePath.Length == 0 || Path.IsPathRooted(relativePath) || relativePath.IndexOf(':') >= 0 || relativePath.IndexOf('\0') >= 0)
                throw new ArgumentException("Asset paths must be non-empty relative paths without volume syntax.", "relativePath");

            string path = relativePath.Replace('\\', '/');
            string[] parts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new ArgumentException("Asset path is empty.", "relativePath");
            StringBuilder normalized = new StringBuilder(path.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "." || parts[i] == "..")
                    throw new ArgumentException("Asset paths cannot contain traversal components.", "relativePath");
                if (i != 0)
                    normalized.Append('/');
                normalized.Append(parts[i]);
            }
            return normalized.ToString();
        }

        private string Resolve(string relativePath)
        {
            string normalized = NormalizePath(relativePath);
            string cached;
            lock (resolved)
            {
                if (resolved.TryGetValue(normalized, out cached))
                    return cached.Length == 0 ? null : cached;
            }

            string candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderRoot(candidate))
                throw new ArgumentException("Asset path leaves the game root.", "relativePath");

            string result = File.Exists(candidate) ? candidate : ResolveCaseInsensitive(normalized);
            if (result != null)
                RejectReparsePoints(result, relativePath);
            lock (resolved)
                resolved[normalized] = result ?? "";
            return result;
        }

        private string ResolveCaseInsensitive(string normalized)
        {
            string current = root;
            string[] parts = normalized.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string[] choices = last ? Directory.GetFiles(current) : Directory.GetDirectories(current);
                string match = null;
                for (int j = 0; j < choices.Length; j++)
                {
                    string name = Path.GetFileName(choices[j]);
                    if (string.Equals(name, parts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        match = choices[j];
                        break;
                    }
                }
                if (match == null)
                    return null;
                current = match;
            }
            return File.Exists(current) && IsUnderRoot(current) ? current : null;
        }

        private bool IsUnderRoot(string path)
        {
            return path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void RejectReparsePoints(string fullPath, string relativePath)
        {
            if (!IsUnderRoot(fullPath))
                throw new ArgumentException("Resolved asset path leaves the game root.", "relativePath");
            string relative = fullPath.Substring(rootPrefix.Length);
            string[] parts = relative.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                if (!File.Exists(current) && !Directory.Exists(current))
                    return;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Reparse points are not allowed in game asset paths: " +
                        relativePath + " (component " + parts[i] + ").");
                }
            }
        }
    }
}
