using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Assets
{
    public sealed class TxdScanFailure
    {
        internal TxdScanFailure(string name, string message)
        {
            Name = name;
            Message = message;
        }

        public string Name { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class TxdScanReport
    {
        private readonly List<TxdScanFailure> failures = new List<TxdScanFailure>();
        private readonly ReadOnlyCollection<TxdScanFailure> readOnlyFailures;

        public TxdScanReport()
        {
            readOnlyFailures = failures.AsReadOnly();
        }

        public int FileCount { get; internal set; }
        public int FailedFileCount { get; internal set; }
        public int EmptyFileCount { get; internal set; }
        public int DeclaredTextureCount { get; internal set; }
        public int SkippedTextureCount { get; internal set; }
        public int TextureCount { get; internal set; }
        public int LevelCount { get; internal set; }
        public int DecodedLevelCount { get; internal set; }
        public long DecodedPixelCount { get; internal set; }
        public int UncompressedCount { get; internal set; }
        public int Dxt1Count { get; internal set; }
        public int Dxt3Count { get; internal set; }
        public int OtherCompressionCount { get; internal set; }
        public ReadOnlyCollection<TxdScanFailure> Failures { get { return readOnlyFailures; } }

        internal void AddFailure(string name, string message)
        {
            FailedFileCount++;
            if (failures.Count < 32)
                failures.Add(new TxdScanFailure(name, message));
        }
    }
}
