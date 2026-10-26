using System;
using System.Collections.Generic;
using System.IO;

namespace SharpGTA.Data
{
    public sealed class DatDirective
    {
        private static readonly string[] SupportedCommands = {
            "IMAGEPATH", "TEXDICTION", "COLFILE", "MODELFILE", "HIERFILE",
            "IDE", "IPL", "MAPZONE", "SPLASH", "CDIMAGE"
        };

        private readonly IList<string> tokens;
        private readonly IList<string> arguments;

        internal DatDirective(DataLine line)
        {
            LineNumber = line.LineNumber;
            Text = line.Text;
            tokens = new List<string>(line.Tokens).AsReadOnly();
            Command = line.Tokens[0].ToUpperInvariant();
            IsSupported = IsSupportedCommand(Command);

            List<string> args = new List<string>();
            for (int i = 1; i < line.Tokens.Count; i++)
                args.Add(line.Tokens[i]);
            arguments = args.AsReadOnly();

            if (IsSupported)
            {
                int pathArgument = string.Equals(Command, "COLFILE", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (args.Count > pathArgument)
                    Path = string.Join(" ", args.ToArray(), pathArgument, args.Count - pathArgument);
            }
        }

        public int LineNumber { get; private set; }
        public string Text { get; private set; }
        public string Command { get; private set; }
        public string Path { get; private set; }
        public bool IsSupported { get; private set; }

        public IList<string> Tokens
        {
            get { return tokens; }
        }

        public IList<string> RawFields
        {
            get { return tokens; }
        }

        public IList<string> Arguments
        {
            get { return arguments; }
        }

        public IList<string> Args
        {
            get { return arguments; }
        }

        private static bool IsSupportedCommand(string command)
        {
            for (int i = 0; i < SupportedCommands.Length; i++)
            {
                if (string.Equals(command, SupportedCommands[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    public sealed class DatDocument
    {
        private readonly IList<DatDirective> directives;

        internal DatDocument(IList<DatDirective> directives, bool terminatedByExit)
        {
            this.directives = new List<DatDirective>(directives).AsReadOnly();
            TerminatedByExit = terminatedByExit;
        }

        public IList<DatDirective> Directives
        {
            get { return directives; }
        }

        public bool TerminatedByExit { get; private set; }
    }

    public sealed class DatReader
    {
        private readonly DataSchema schema;

        public DatReader()
            : this(DataSchema.III)
        {
        }

        public DatReader(DataSchema schema)
        {
            if (schema == null)
                throw new ArgumentNullException("schema");
            this.schema = schema;
        }

        public DatDocument Read(string text)
        {
            return Read(text, "DAT");
        }

        public DatDocument Read(string text, string sourceName)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            using (StringReader reader = new StringReader(text))
                return Read(reader, sourceName);
        }

        public DatDocument Read(TextReader reader)
        {
            return Read(reader, "DAT");
        }

        public DatDocument Read(TextReader reader, string sourceName)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");
            string source = string.IsNullOrEmpty(sourceName) ? "DAT" : sourceName;
            List<DatDirective> directives = new List<DatDirective>();
            bool terminatedByExit = false;
            int recordCount = 0;
            DataDocumentReader documentReader = new DataDocumentReader(schema, source);

            foreach (DataLine line in documentReader.ReadLines(reader))
            {
                if (string.Equals(line.Tokens[0], "EXIT", StringComparison.OrdinalIgnoreCase))
                {
                    terminatedByExit = true;
                    break;
                }

                if (recordCount == schema.MaxRecords)
                {
                    throw DataParsing.Error(source, line, "Too many records; the maximum is " +
                        schema.MaxRecords.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }
                recordCount++;
                directives.Add(new DatDirective(line));
            }
            return new DatDocument(directives, terminatedByExit);
        }
    }
}
