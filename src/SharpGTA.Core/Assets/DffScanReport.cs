using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Assets
{
    public sealed class DffScanFailure
    {
        internal DffScanFailure(string name, string message)
        {
            Name = name;
            Message = message;
        }

        public string Name { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class DffScanReport
    {
        private readonly List<DffScanFailure> failures = new List<DffScanFailure>();
        private readonly ReadOnlyCollection<DffScanFailure> readOnlyFailures;

        public DffScanReport()
        {
            readOnlyFailures = failures.AsReadOnly();
        }

        public int FileCount { get; internal set; }
        public int FailedFileCount { get; internal set; }
        public int EmptyFileCount { get; internal set; }
        public int ClumpCount { get; internal set; }
        public int GeometryCount { get; internal set; }
        public int FrameCount { get; internal set; }
        public int AtomicCount { get; internal set; }
        public int TriangleCount { get; internal set; }
        public int MaterialCount { get; internal set; }
        public int LengthMismatchCount { get; internal set; }
        public ReadOnlyCollection<DffScanFailure> Failures { get { return readOnlyFailures; } }

        internal void AddFailure(string name, string message)
        {
            FailedFileCount++;
            if (failures.Count < 32)
                failures.Add(new DffScanFailure(name, message));
        }
    }
}
