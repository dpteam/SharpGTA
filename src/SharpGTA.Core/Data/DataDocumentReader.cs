using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SharpGTA.IO;

namespace SharpGTA.Data
{
    public sealed class DataLine
    {
        private readonly IList<string> tokens;

        public DataLine(int lineNumber, string text, IList<string> tokens)
        {
            if (lineNumber < 0)
                throw new ArgumentOutOfRangeException("lineNumber");
            if (text == null)
                throw new ArgumentNullException("text");
            if (tokens == null)
                throw new ArgumentNullException("tokens");

            LineNumber = lineNumber;
            Text = text;
            this.tokens = new List<string>(tokens).AsReadOnly();
        }

        public int LineNumber { get; private set; }
        public string Text { get; private set; }
        public IList<string> Tokens
        {
            get { return tokens; }
        }
    }

    public static class DataTokenizer
    {
        public static string Normalize(string text)
        {
            if (text == null)
                throw new ArgumentNullException("text");

            int comment = text.IndexOf('#');
            string content = comment >= 0 ? text.Substring(0, comment) : text;
            char[] characters = content.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] < ' ' || characters[i] == ',')
                    characters[i] = ' ';
            }
            return new string(characters).Trim();
        }

        public static IList<string> Tokenize(string text)
        {
            return Tokenize(text, 0, DataSchema.DefaultMaxTokens);
        }

        public static IList<string> Tokenize(string text, int lineNumber, int maxTokens)
        {
            return Tokenize(text, lineNumber, maxTokens, "DATA");
        }

        internal static IList<string> Tokenize(string text, int lineNumber, int maxTokens, string sourceName)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            if (maxTokens <= 0)
                throw new ArgumentOutOfRangeException("maxTokens");

            string normalized = Normalize(text);
            List<string> tokens = new List<string>();
            int position = 0;
            while (position < normalized.Length)
            {
                while (position < normalized.Length && char.IsWhiteSpace(normalized[position]))
                    position++;
                if (position == normalized.Length)
                    break;

                int start = position;
                while (position < normalized.Length && !char.IsWhiteSpace(normalized[position]))
                    position++;
                if (tokens.Count == maxTokens)
                {
                    throw DataParsing.Error(sourceName, lineNumber,
                        "Too many tokens; the maximum is " + maxTokens.ToString(CultureInfo.InvariantCulture) + ".");
                }
                tokens.Add(normalized.Substring(start, position - start));
            }
            return tokens.AsReadOnly();
        }
    }

    /// <summary>
    /// Reads bounded, non-blank lines. The caller retains ownership of TextReader instances.
    /// </summary>
    public sealed class DataDocumentReader
    {
        private readonly DataSchema schema;
        private readonly string sourceName;

        public DataDocumentReader(DataSchema schema)
            : this(schema, "DATA")
        {
        }

        public DataDocumentReader(DataSchema schema, string sourceName)
        {
            if (schema == null)
                throw new ArgumentNullException("schema");
            this.schema = schema;
            this.sourceName = string.IsNullOrEmpty(sourceName) ? "DATA" : sourceName;
        }

        public IList<DataLine> Read(string text)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            using (StringReader reader = new StringReader(text))
                return Read(reader);
        }

        public IList<DataLine> Read(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");
            List<DataLine> lines = new List<DataLine>();
            foreach (DataLine line in ReadLines(reader))
                lines.Add(line);
            return lines.AsReadOnly();
        }

        public IEnumerable<DataLine> ReadLines(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");

            int lineNumber = 0;
            while (true)
            {
                string text = ReadBoundedLine(reader, schema.MaxLineLength, lineNumber + 1, sourceName);
                if (text == null)
                    yield break;
                lineNumber++;

                IList<string> tokens = DataTokenizer.Tokenize(text, lineNumber, schema.MaxTokens, sourceName);
                if (tokens.Count != 0)
                    yield return new DataLine(lineNumber, text, tokens);
            }
        }

        private static string ReadBoundedLine(TextReader reader, int maxLineLength, int lineNumber, string sourceName)
        {
            StringBuilder text = new StringBuilder(Math.Min(maxLineLength, 256));
            bool hasCharacters = false;
            while (true)
            {
                int value = reader.Read();
                if (value < 0)
                    return hasCharacters ? text.ToString() : null;

                hasCharacters = true;
                char character = (char)value;
                if (character == '\n')
                    return text.ToString();
                if (character == '\r')
                {
                    if (reader.Peek() == '\n')
                        reader.Read();
                    return text.ToString();
                }

                if (text.Length == maxLineLength)
                {
                    throw DataParsing.Error(sourceName, lineNumber,
                        "Line is too long; the maximum is " + maxLineLength.ToString(CultureInfo.InvariantCulture) + " characters.");
                }
                text.Append(character);
            }
        }
    }

    internal static class DataParsing
    {
        internal static AssetFormatException Error(string sourceName, int lineNumber, string message)
        {
            return new AssetFormatException(sourceName + " line " +
                lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }

        internal static AssetFormatException Error(string sourceName, DataLine line, string message)
        {
            return Error(sourceName, line.LineNumber, message);
        }

        internal static void RequireCount(string sourceName, DataLine line, int expected, string layout)
        {
            if (line.Tokens.Count != expected)
            {
                throw Error(sourceName, line, "Expected exactly " +
                    expected.ToString(CultureInfo.InvariantCulture) + " fields for " + layout +
                    ", but got " + line.Tokens.Count.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        internal static int ParseInt32(string sourceName, DataLine line, int index, string fieldName)
        {
            string token = line.Tokens[index];
            int result;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw InvalidNumber(sourceName, line, fieldName, token, "integer");
            return result;
        }

        internal static uint ParseUInt32(string sourceName, DataLine line, int index, string fieldName)
        {
            string token = line.Tokens[index];
            uint result;
            if (!uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw InvalidNumber(sourceName, line, fieldName, token, "unsigned integer");
            return result;
        }

        internal static uint ParseUInt32Hex(string sourceName, DataLine line, int index, string fieldName)
        {
            string token = line.Tokens[index];
            string digits = token;
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                digits = digits.Substring(2);

            uint result;
            if (digits.Length == 0 ||
                !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
                throw InvalidNumber(sourceName, line, fieldName, token, "hexadecimal integer");
            return result;
        }

        internal static float ParseSingle(string sourceName, DataLine line, int index, string fieldName)
        {
            string token = line.Tokens[index];
            float result;
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                float.IsNaN(result) || float.IsInfinity(result))
                throw InvalidNumber(sourceName, line, fieldName, token, "floating-point number");
            return result;
        }

        private static AssetFormatException InvalidNumber(
            string sourceName, DataLine line, string fieldName, string token, string expected)
        {
            return Error(sourceName, line, "Invalid " + expected + " for " + fieldName + ": '" + token + "'.");
        }
    }
}
