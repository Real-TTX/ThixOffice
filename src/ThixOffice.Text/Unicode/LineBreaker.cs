using System.Globalization;
using System.Text;

namespace ThixOffice.Text.Unicode;

/// <summary>Whether a line may end before a code unit.</summary>
public enum LineBreakKind : byte
{
    None,
    Allowed,
    Mandatory,
}

/// <summary>
/// Line-break opportunities after the essentials of UAX #14 (Unicode line breaking algorithm). The classes come from the
/// Unicode general categories plus the explicit lists of the standard for the characters that matter in European and CJK
/// text (spaces, hyphens, dashes, quotes, brackets, number punctuation, glue, ideographs); the pair rules LB4–LB31 are
/// applied in their order, with combining marks attached to their base (LB9). Not covered: Hangul syllable sequences
/// (treated as ideographs), the regional-indicator pairing (LB30a) and the Southeast-Asian dictionary breaks (LB1 SA).
/// </summary>
public static class LineBreaker
{
    internal enum BreakClass : byte
    {
        AL, BA, BB, B2, BK, CB, CL, CM, CP, CR, EX, GL, HY, ID, IN, IS, LF, NL, NS, NU, OP, PO, PR, QU, SP, SY, WJ, ZW, ZWJ,
    }

    /// <summary>
    /// For each index i in 0..text.Length: may a line end before text[i] (i.e. after text[i-1])? Index 0 is never a break,
    /// text.Length is always <see cref="LineBreakKind.Mandatory"/> (the end of the text).
    /// </summary>
    public static LineBreakKind[] GetBreaks(string text)
    {
        var breaks = new LineBreakKind[text.Length + 1];
        if (text.Length == 0)
        {
            breaks[0] = LineBreakKind.Mandatory;
            return breaks;
        }

        var classes = Classify(text);
        breaks[text.Length] = LineBreakKind.Mandatory;

        // The class the pair rules see before position i: combining marks take their base's class (LB9).
        var before = classes[0] == BreakClass.CM || classes[0] == BreakClass.ZWJ ? BreakClass.AL : classes[0];
        var spacesSince = before == BreakClass.SP;
        var lastBeforeSpaces = before;

        for (var index = 1; index < text.Length; index++)
        {
            var current = classes[index];
            if (char.IsLowSurrogate(text[index]))
            {
                breaks[index] = LineBreakKind.None;
                continue;
            }

            var attaches = (current is BreakClass.CM or BreakClass.ZWJ) && before is not (BreakClass.BK or BreakClass.CR or BreakClass.LF
                or BreakClass.NL or BreakClass.SP or BreakClass.ZW);
            if (!attaches && current is BreakClass.CM or BreakClass.ZWJ)
            {
                // LB10: a mark without a base (after a space or a break) is an alphabetic character.
                current = BreakClass.AL;
            }

            breaks[index] = Decide(before, current, lastBeforeSpaces, spacesSince);
            if (attaches)
            {
                continue;
            }

            if (current == BreakClass.SP)
            {
                if (!spacesSince)
                {
                    lastBeforeSpaces = before;
                }

                spacesSince = true;
            }
            else
            {
                spacesSince = false;
                lastBeforeSpaces = current;
            }

            before = current;
        }

        return breaks;
    }

    private static LineBreakKind Decide(BreakClass before, BreakClass current, BreakClass beforeSpaces, bool afterSpaces)
    {
        // LB4, LB5: always break after hard line breaks; CR × LF.
        if (before == BreakClass.CR && current == BreakClass.LF)
        {
            return LineBreakKind.None;
        }

        if (before is BreakClass.BK or BreakClass.CR or BreakClass.LF or BreakClass.NL)
        {
            return LineBreakKind.Mandatory;
        }

        // LB6, LB7: no break before hard breaks, spaces and zero width space.
        if (current is BreakClass.BK or BreakClass.CR or BreakClass.LF or BreakClass.NL or BreakClass.SP or BreakClass.ZW)
        {
            return LineBreakKind.None;
        }

        // LB8: break after ZW (and the spaces after it).
        if (before == BreakClass.ZW || (afterSpaces && beforeSpaces == BreakClass.ZW))
        {
            return LineBreakKind.Allowed;
        }

        // LB8a: ZWJ × ; LB9: X × CM.
        if (before == BreakClass.ZWJ || current is BreakClass.CM or BreakClass.ZWJ)
        {
            return LineBreakKind.None;
        }

        // LB11: × WJ ×
        if (before == BreakClass.WJ || current == BreakClass.WJ)
        {
            return LineBreakKind.None;
        }

        // LB12: GL × ; LB12a: [^SP BA HY] × GL
        if (before == BreakClass.GL)
        {
            return LineBreakKind.None;
        }

        if (current == BreakClass.GL && before is not (BreakClass.SP or BreakClass.BA or BreakClass.HY))
        {
            return LineBreakKind.None;
        }

        // LB13: × CL, × CP, × EX, × IS, × SY
        if (current is BreakClass.CL or BreakClass.CP or BreakClass.EX or BreakClass.IS or BreakClass.SY)
        {
            return LineBreakKind.None;
        }

        // LB14: OP SP* ×
        var effectiveBefore = afterSpaces ? beforeSpaces : before;
        if (effectiveBefore == BreakClass.OP)
        {
            return LineBreakKind.None;
        }

        // LB15: QU SP* × OP
        if (effectiveBefore == BreakClass.QU && current == BreakClass.OP)
        {
            return LineBreakKind.None;
        }

        // LB16: (CL | CP) SP* × NS
        if (effectiveBefore is BreakClass.CL or BreakClass.CP && current == BreakClass.NS)
        {
            return LineBreakKind.None;
        }

        // LB17: B2 SP* × B2
        if (effectiveBefore == BreakClass.B2 && current == BreakClass.B2)
        {
            return LineBreakKind.None;
        }

        // LB18: SP ÷
        if (before == BreakClass.SP)
        {
            return LineBreakKind.Allowed;
        }

        // LB19: × QU, QU ×
        if (current == BreakClass.QU || before == BreakClass.QU)
        {
            return LineBreakKind.None;
        }

        // LB20: ÷ CB, CB ÷
        if (current == BreakClass.CB || before == BreakClass.CB)
        {
            return LineBreakKind.Allowed;
        }

        // LB21: × BA, × HY, × NS, BB ×
        if (current is BreakClass.BA or BreakClass.HY or BreakClass.NS || before == BreakClass.BB)
        {
            return LineBreakKind.None;
        }

        // LB22: × IN
        if (current == BreakClass.IN)
        {
            return LineBreakKind.None;
        }

        // LB23: (AL | HL) × NU, NU × (AL | HL); LB23a: PR × ID, ID × PO
        if ((before == BreakClass.AL && current == BreakClass.NU) || (before == BreakClass.NU && current == BreakClass.AL))
        {
            return LineBreakKind.None;
        }

        if ((before == BreakClass.PR && current == BreakClass.ID) || (before == BreakClass.ID && current == BreakClass.PO))
        {
            return LineBreakKind.None;
        }

        // LB24: (PR | PO) × (AL | HL); (AL | HL) × (PR | PO)
        if ((before is BreakClass.PR or BreakClass.PO && current == BreakClass.AL)
            || (before == BreakClass.AL && current is BreakClass.PR or BreakClass.PO))
        {
            return LineBreakKind.None;
        }

        // LB25 (essentials): numbers with their prefixes, suffixes and separators stay together.
        if (current == BreakClass.NU && before is BreakClass.PR or BreakClass.PO or BreakClass.OP or BreakClass.HY or BreakClass.IS
                or BreakClass.SY or BreakClass.NU)
        {
            return LineBreakKind.None;
        }

        if (before is BreakClass.NU or BreakClass.CL or BreakClass.CP && current is BreakClass.PO or BreakClass.PR)
        {
            return LineBreakKind.None;
        }

        // LB28: (AL | HL) × (AL | HL); LB29: IS × (AL | HL)
        if (before is BreakClass.AL or BreakClass.IS && current == BreakClass.AL)
        {
            return LineBreakKind.None;
        }

        // LB30: (AL | HL | NU) × OP; CP × (AL | HL | NU)
        if ((before is BreakClass.AL or BreakClass.NU && current == BreakClass.OP)
            || (before == BreakClass.CP && current is BreakClass.AL or BreakClass.NU))
        {
            return LineBreakKind.None;
        }

        // LB31: break everywhere else.
        return LineBreakKind.Allowed;
    }

    internal static BreakClass[] Classify(string text)
    {
        var classes = new BreakClass[text.Length];
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var breakClass = ClassOf(rune);
            classes[index] = breakClass;
            if (rune.Utf16SequenceLength == 2)
            {
                classes[index + 1] = BreakClass.CM;
            }

            index += rune.Utf16SequenceLength;
        }

        return classes;
    }

    internal static BreakClass ClassOf(Rune rune)
    {
        var value = rune.Value;
        switch (value)
        {
            case 0x0A:
                return BreakClass.LF;
            case 0x0D:
                return BreakClass.CR;
            case 0x0B or 0x0C or 0x2028 or 0x2029:
                return BreakClass.BK;
            case 0x85:
                return BreakClass.NL;
            case 0x20:
                return BreakClass.SP;
            case 0x09:
                return BreakClass.BA;
            case 0x200B:
                return BreakClass.ZW;
            case 0x200D:
                return BreakClass.ZWJ;
            case 0x2060 or 0xFEFF:
                return BreakClass.WJ;
            case 0x00A0 or 0x202F or 0x2007 or 0x2011 or 0x0F0C or 0x034F or 0x180E:
                return BreakClass.GL;
            case 0x00AD or 0x2010 or 0x2012 or 0x2013 or 0x05BE or 0x0F0B or 0x1361 or 0x17D8 or 0x17DA or 0x2027 or 0x007C:
                return BreakClass.BA;
            case >= 0x2000 and <= 0x2006 or >= 0x2008 and <= 0x200A or 0x205F or 0x3000 or 0x1680:
                return BreakClass.BA;
            case 0x00B4 or 0x02C8 or 0x02CC or 0x02DF:
                return BreakClass.BB;
            case 0x2014:
                return BreakClass.B2;
            case 0x002D:
                return BreakClass.HY;
            case 0xFFFC:
                return BreakClass.CB;
            case 0x0029 or 0x005D:
                return BreakClass.CP;
            case 0x0021 or 0x003F or 0x00A1 or 0x00BF or 0xFF01 or 0xFF1F:
                return value is 0x00A1 or 0x00BF ? BreakClass.OP : BreakClass.EX;
            case 0x002C or 0x002E or 0x003A or 0x003B or 0x037E or 0x0589:
                return BreakClass.IS;
            case 0x002F:
                return BreakClass.SY;
            case 0x0022 or 0x0027 or 0x00AB or 0x00BB or 0x2018 or 0x2019 or 0x201B or 0x201C or 0x201D or 0x201F
                or 0x2039 or 0x203A or 0x275B or 0x275C or 0x275D or 0x275E:
                return BreakClass.QU;
            case 0x201A or 0x201E:
                return BreakClass.OP;
            case 0x2024 or 0x2025 or 0x2026 or 0xFE19:
                return BreakClass.IN;
            case 0x0025 or 0x00A2 or 0x00B0 or 0x2030 or 0x2031 or 0x2032 or 0x2033 or 0x2034 or 0x2103 or 0x2109 or 0xFF05:
                return BreakClass.PO;
            case 0x002B or 0x005C or 0x00B1 or 0x2116 or 0x2212 or 0x2213:
                return BreakClass.PR;
            case 0x3001 or 0x3002 or 0xFF0C or 0xFF0E:
                return BreakClass.CL;
            case 0x3005 or 0x303B or 0x309D or 0x309E or 0x30FC or 0x30FD or 0x30FE or 0x3041 or 0x3043 or 0x3045 or 0x3047
                or 0x3049 or 0x3063 or 0x3083 or 0x3085 or 0x3087 or 0x308E or 0x30A1 or 0x30A3 or 0x30A5 or 0x30A7 or 0x30A9
                or 0x30C3 or 0x30E3 or 0x30E5 or 0x30E7 or 0x30EE or 0x30F5 or 0x30F6:
                return BreakClass.NS;
        }

        if (IsIdeographic(value))
        {
            return BreakClass.ID;
        }

        var category = Rune.GetUnicodeCategory(rune);
        return category switch
        {
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => BreakClass.CM,
            UnicodeCategory.Control => BreakClass.CM,
            UnicodeCategory.DecimalDigitNumber => BreakClass.NU,
            UnicodeCategory.OpenPunctuation => BreakClass.OP,
            UnicodeCategory.ClosePunctuation => BreakClass.CL,
            UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation => BreakClass.QU,
            UnicodeCategory.CurrencySymbol => BreakClass.PR,
            UnicodeCategory.SpaceSeparator => BreakClass.BA,
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => BreakClass.BK,
            _ => BreakClass.AL,
        };
    }

    private static bool IsIdeographic(int value) =>
        value is >= 0x2E80 and <= 0x2FFF
            or >= 0x3040 and <= 0x30FF
            or >= 0x3130 and <= 0x318F
            or >= 0x31F0 and <= 0x31FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xA000 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7AF
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE4F
            or >= 0xFF00 and <= 0xFF60
            or >= 0x1F000 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;
}
