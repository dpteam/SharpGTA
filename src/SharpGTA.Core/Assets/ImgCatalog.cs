using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Assets
{
    /// <summary>
    /// Combines up to 256 images. Add assigns each image an ID and stamps that ID into
    /// the catalog's entries, matching the way the games combine individual DIR files.
    /// </summary>
    public sealed class ImgCatalog : IDisposable
    {
        private readonly List<ImgArchive> archives = new List<ImgArchive>();
        private readonly List<ImgEntry> entries = new List<ImgEntry>();
        private readonly ReadOnlyCollection<ImgEntry> readOnlyEntries;
        private bool disposed;

        public ImgCatalog()
        {
            readOnlyEntries = entries.AsReadOnly();
        }

        /// <summary>Catalog entries in archive/directory order, with assigned image IDs.</summary>
        public ReadOnlyCollection<ImgEntry> Entries
        {
            get { return readOnlyEntries; }
        }

        public int Count
        {
            get { return archives.Count; }
        }

        /// <summary>Adds an image and returns its assigned ID (0 through 255).</summary>
        public int Add(ImgArchive archive)
        {
            if (archive == null)
                throw new ArgumentNullException("archive");
            ThrowIfDisposed();
            if (archives.Count == 256)
                throw new InvalidOperationException("An IMG catalog cannot contain more than 256 images.");

            int imageIndex = archives.Count;
            archives.Add(archive);
            for (int i = 0; i < archive.Directory.Entries.Count; i++)
                entries.Add(archive.Directory.Entries[i].WithImageIndex(imageIndex));
            return imageIndex;
        }

        /// <summary>
        /// Finds a resource as CStreaming does: the last registered image wins across
        /// images, while the first duplicate within one DIR wins inside that image.
        /// </summary>
        public ImgEntry Find(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            ThrowIfDisposed();
            for (int i = archives.Count - 1; i >= 0; i--)
            {
                ImgEntry entry = archives[i].Directory.Find(name);
                if (entry != null)
                    return entry.WithImageIndex(i);
            }
            return null;
        }

        public ImgArchive GetImage(int imageIndex)
        {
            ThrowIfDisposed();
            if (imageIndex < 0 || imageIndex >= archives.Count)
                throw new ArgumentOutOfRangeException("imageIndex");
            return archives[imageIndex];
        }

        public byte[] Read(string name)
        {
            ImgEntry entry = Find(name);
            if (entry == null)
                throw new KeyNotFoundException("IMG catalog entry '" + name + "' was not found.");
            return Read(entry);
        }

        public byte[] Read(ImgEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");
            ThrowIfDisposed();
            if (entry.ImageIndex < 0 || entry.ImageIndex >= archives.Count)
                throw new ArgumentOutOfRangeException("entry", "IMG entry refers to an image outside this catalog.");
            // Only the low 24 bits are a sector offset; the high byte selects the image.
            return archives[entry.ImageIndex].Read(entry);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            for (int i = 0; i < archives.Count; i++)
                archives[i].Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("ImgCatalog");
        }
    }
}
