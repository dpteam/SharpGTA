using System;

namespace SharpGTA.IO
{
    public sealed class AssetFormatException : Exception
    {
        public AssetFormatException(string message)
            : base(message)
        {
            Offset = -1;
        }

        public AssetFormatException(string message, long offset)
            : base(message + (offset >= 0 ? " (offset " + offset + ")" : ""))
        {
            Offset = offset;
        }

        public AssetFormatException(string message, long offset, Exception innerException)
            : base(message + (offset >= 0 ? " (offset " + offset + ")" : ""), innerException)
        {
            Offset = offset;
        }

        internal AssetFormatException(string formattedMessage, Exception innerException, long offset)
            : base(formattedMessage, innerException)
        {
            Offset = offset;
        }

        public long Offset { get; private set; }
    }
}
