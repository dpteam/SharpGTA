using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Assets
{
    public enum LoadIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class LoadIssue
    {
        internal LoadIssue(LoadIssueSeverity severity, string category, string path, int line, string message)
        {
            Severity = severity;
            Category = category;
            Path = path;
            Line = line;
            Message = message;
        }

        public LoadIssueSeverity Severity { get; private set; }
        public string Category { get; private set; }
        public string Path { get; private set; }
        public int Line { get; private set; }
        public string Message { get; private set; }
    }

    /// <summary>
    /// A bounded diagnostic list. TotalIssueCount includes every Add call; after the
    /// 128 retained items, DroppedIssueCount records the overflow instead of allocating.
    /// </summary>
    public sealed class LoadReport
    {
        public const int IssueLimit = 128;

        private readonly List<LoadIssue> issues = new List<LoadIssue>(IssueLimit);
        private readonly ReadOnlyCollection<LoadIssue> readOnlyIssues;

        public LoadReport()
        {
            readOnlyIssues = issues.AsReadOnly();
        }

        public int Count { get { return issues.Count; } }
        public int TotalIssueCount { get; private set; }
        public int DroppedIssueCount { get; private set; }
        public int OverflowCount { get { return DroppedIssueCount; } }
        public IList<LoadIssue> Items { get { return readOnlyIssues; } }

        public void Clear()
        {
            issues.Clear();
            TotalIssueCount = 0;
            DroppedIssueCount = 0;
        }

        public void Add(LoadIssueSeverity severity, string category, string path, int line, string message)
        {
            if (category == null)
                throw new ArgumentNullException("category");
            if (message == null)
                throw new ArgumentNullException("message");
            if (line < 0)
                throw new ArgumentOutOfRangeException("line");

            TotalIssueCount++;
            if (issues.Count == IssueLimit)
            {
                DroppedIssueCount++;
                return;
            }
            issues.Add(new LoadIssue(severity, category, path ?? string.Empty, line, message));
        }
    }
}
