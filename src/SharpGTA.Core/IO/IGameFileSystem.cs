using System.IO;

namespace SharpGTA.IO
{
    public interface IGameFileSystem
    {
        string Root { get; }
        bool FileExists(string relativePath);
        Stream OpenRead(string relativePath);
        byte[] ReadAllBytes(string relativePath, int maximumLength);
        string ReadAllText(string relativePath, int maximumLength);
    }
}
