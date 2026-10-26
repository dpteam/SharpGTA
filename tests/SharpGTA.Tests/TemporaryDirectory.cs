using System;
using System.IO;

namespace SharpGTA.Tests
{
    internal sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            string root = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "test-temp");
            Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string WriteBytes(string relativePath, byte[] bytes)
        {
            string fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string directory = System.IO.Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(fullPath, bytes);
            return fullPath;
        }

        public string WriteText(string relativePath, string text)
        {
            return WriteBytes(relativePath, new System.Text.ASCIIEncoding().GetBytes(text));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
