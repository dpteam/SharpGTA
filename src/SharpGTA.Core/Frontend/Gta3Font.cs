using System;
using System.Collections.Generic;

namespace SharpGTA.Frontend
{
    /// <summary>GTA III PC font metrics and 16-column atlas mapping from CFont.</summary>
    public sealed class Gta3Font
    {
        public const int Bank = 0;
        public const int Pager = 1;
        public const int Heading = 2;

        private static readonly short[][] widths = {
            new short[] {
                13,12,31,35,23,35,31,9,14,15,25,30,11,17,13,31,
                23,16,22,21,24,23,23,20,23,22,10,35,26,26,26,26,
                30,26,24,23,24,22,21,24,26,10,20,26,22,29,26,25,
                23,25,24,24,22,25,24,29,29,23,25,37,22,37,35,37,
                35,21,22,21,21,22,13,22,21,10,16,22,11,32,21,21,
                23,22,16,20,14,21,20,30,25,21,21,33,33,33,33,35,
                27,27,27,27,32,24,23,23,23,23,11,11,11,11,26,26,
                26,26,26,26,26,25,26,21,21,21,21,32,23,22,22,22,
                22,11,11,11,11,22,22,22,22,22,22,22,22,26,21,24,
                12,26,26,26,26,26,26,26,26,26,26,26,26,26,26,26,
                26,26,26,26,26,26,26,26,26,26,26,26,26,18,26,26,
                26,26,26,26,26,26,26,26,26,26,26,26,26,26,26,26,
                20
            },
            new short[] {
                13,9,21,35,23,35,35,11,35,35,25,35,11,17,13,33,
                28,14,22,21,24,23,23,21,23,22,10,35,13,35,13,33,
                5,25,22,23,24,21,21,24,24,9,20,24,21,27,25,25,
                22,25,23,20,23,23,23,31,23,23,23,37,33,37,35,37,
                35,21,19,19,21,19,17,21,21,8,17,18,14,24,21,21,
                20,22,19,20,20,19,20,26,21,20,21,33,33,33,33,35,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                16
            },
            new short[] {
                15,14,16,25,19,26,22,11,18,18,27,26,13,19,9,27,
                19,18,19,19,22,19,20,18,19,20,12,32,15,32,15,35,
                15,19,19,19,19,19,16,19,20,9,19,20,14,29,19,20,
                19,19,19,19,21,19,20,32,20,19,19,33,31,39,37,39,
                37,21,21,21,23,21,19,23,23,10,19,20,16,26,23,23,
                20,20,20,22,21,22,22,26,22,22,23,35,35,35,35,37,
                19,19,19,19,29,19,19,19,19,19,9,9,9,9,19,19,
                19,19,19,19,19,19,19,19,19,19,19,30,19,19,19,19,
                19,10,10,10,10,19,19,19,19,19,19,19,19,19,23,35,
                12,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,11,19,19,
                19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,19,
                19
            }
        };

        public int GetCharacterWidth(int style, int glyph)
        {
            if (style < 0 || style >= widths.Length)
                throw new ArgumentOutOfRangeException("style");
            if (glyph < 0 || glyph >= widths[style].Length)
                throw new ArgumentOutOfRangeException("glyph");
            return widths[style][glyph];
        }

        public string GetTextureName(int style)
        {
            if (style == Bank) return "font2";
            if (style == Pager) return "pager";
            if (style == Heading) return "font1";
            throw new ArgumentOutOfRangeException("style");
        }

        public int[] GetGlyphs(string text)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            List<int> glyphs = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '~')
                {
                    int end = text.IndexOf('~', i + 1);
                    if (end > i)
                    {
                        i = end;
                        continue;
                    }
                    continue;
                }
                int glyph = text[i] - ' ';
                if (glyph < 0 || glyph >= 193)
                    glyph = '?' - ' ';
                glyphs.Add(glyph);
            }
            return glyphs.ToArray();
        }

        public float Measure(string text, int style, float scaleX)
        {
            int[] glyphs = GetGlyphs(text);
            float width = 0.0f;
            for (int i = 0; i < glyphs.Length; i++)
                width += GetCharacterWidth(style, glyphs[i]) * scaleX;
            return width;
        }
    }
}
