using System;
using System.Collections.Generic;
using System.IO;
using SharpGTA.IO;

namespace SharpGTA.Assets
{
    /// <summary>
    /// An IMG image paired with its directory. Files can remain on disk; only the
    /// requested sector range is read, never the whole image.
    /// </summary>
    public sealed class ImgArchive : IDisposable
    {
        private readonly byte[] image;
        private readonly ImgDirectory directory;
        private readonly Stream imageStream;
        private readonly bool leaveOpen;
        private readonly object streamLock = new object();
        private bool disposed;

        public ImgArchive(byte[] image, ImgDirectory directory)
        {
            if (image == null)
                throw new ArgumentNullException("image");
            if (directory == null)
                throw new ArgumentNullException("directory");
            this.image = image;
            this.directory = directory;
        }

        public ImgArchive(Stream imageStream, ImgDirectory directory, bool leaveOpen)
        {
            if (imageStream == null)
                throw new ArgumentNullException("imageStream");
            if (directory == null)
                throw new ArgumentNullException("directory");
            if (!imageStream.CanRead || !imageStream.CanSeek)
                throw new ArgumentException("IMG input must be a readable, seekable stream.", "imageStream");
            this.imageStream = imageStream;
            this.directory = directory;
            this.leaveOpen = leaveOpen;
        }

        public ImgDirectory Directory
        {
            get { return directory; }
        }

        /// <summary>Reads the first case-insensitively matching entry, including its sector padding.</summary>
        public byte[] Read(string name)
        {
            ImgEntry entry = directory.Find(name);
            if (entry == null)
                throw new KeyNotFoundException("IMG entry '" + name + "' was not found.");
            return Read(entry);
        }

        /// <summary>Reads an entry from this image. The image ID bits do not form part of its byte offset.</summary>
        public byte[] Read(ImgEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException("entry");
            ThrowIfDisposed();
            long offset = entry.ByteOffset;
            long length = entry.ByteLength;
            long imageLength = image == null ? imageStream.Length : image.Length;
            if (length > int.MaxValue)
                throw new AssetFormatException("IMG entry '" + entry.Name + "' exceeds the maximum single-allocation size.");
            if (offset < 0 || offset > imageLength || length > imageLength - offset)
                throw new AssetFormatException("IMG entry '" + entry.Name + "' range [" + offset + ", " + (offset + length) + ") exceeds image length " + imageLength + ".");

            byte[] result = new byte[(int)length];
            if (length == 0)
                return result;
            if (image != null)
            {
                Buffer.BlockCopy(image, (int)offset, result, 0, (int)length);
                return result;
            }

            lock (streamLock)
            {
                ThrowIfDisposed();
                imageStream.Position = offset;
                try
                {
                    StreamUtilities.ReadExact(imageStream, result, 0, result.Length);
                }
                catch (EndOfStreamException exception)
                {
                    throw new AssetFormatException("IMG entry '" + entry.Name + "' was truncated while reading.", offset, exception);
                }
            }
            return result;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (imageStream != null && !leaveOpen)
                imageStream.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("ImgArchive");
        }
    }
}
