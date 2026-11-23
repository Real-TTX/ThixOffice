using System.Globalization;
using System.Text.RegularExpressions;
using ThixOffice.Core.Formats.Odf;
using ThixShow.Core.Model;

namespace ThixShow.Core.Formats.Odp;

/// <summary>
/// What the odp reader and writer share: the mimetype, lengths in EMU ⇄ "cm" without losing an EMU, the placeholder classes of
/// ODF (presentation:class), numbering formats, underline styles, dash patterns, line end markers and slide transitions.
/// </summary>
internal static partial class OdpNames
{
    public const string MimeType = "application/vnd.oasis.opendocument.presentation";

    public const string TemplateMimeType = "application/vnd.oasis.opendocument.presentation-template";

    public const string InvalidMessage = "Die Datei ist keine gültige OpenDocument-Präsentation (.odp).";

    /// <summary>
    /// The graphic style every shape ThixShow writes is based on: the model's defaults (no fill, no line, ThixShow's insets, text at
    /// the top). Its values are not read back into a shape – they are what "nothing set" means.
    /// </summary>
    public const string BaseStyleName = "ThixShow";

    /// <summary>The field ids of slide numbers and dates read from odp (stable, so a read file compares equal to itself).</summary>
    public const string SlideNumberFieldId = "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}";

    public const string DateFieldId = "{2A3B9F61-5D0C-4E5A-9C0E-6A1D0B7E4C21}";

    // ---------------------------------------------------------------- lengths

    /// <summary>A length in EMU as centimetres with six decimals (1 EMU = 0.0000028 cm: read back to the same EMU).</summary>
    public static string Cm(long emu) =>
        (emu / (double)Units.EmuPerCentimeter).ToString("0.######", CultureInfo.InvariantCulture) + "cm";

    /// <summary>A length in EMU of a double value (rotation offsets).</summary>
    public static string Cm(double emu) =>
        (emu / Units.EmuPerCentimeter).ToString("0.########", CultureInfo.InvariantCulture) + "cm";

    /// <summary>A length of ODF ("2.5cm", "12pt" …) in EMU, or null.</summary>
    public static long? Emu(string? value) => OdfXml.Points(value) is { } points ? (long)Math.Round(points * Units.EmuPerPoint) : null;

    public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>An NCName for a style or id ("Titel und Inhalt" → "Titel_und_Inhalt").</summary>
    public static string StyleName(string text)
    {
        var name = NotName().Replace(text, "_");
        return name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ? "S" + name : name;
    }

    [GeneratedRegex("[^A-Za-z0-9_.\\-]")]
    private static partial Regex NotName();

    // ---------------------------------------------------------------- placeholders

    /// <summary>The presentation:class of a placeholder (null: not one ODF knows).</summary>
    public static string? ClassOf(PlaceholderInfo placeholder) => placeholder.Type switch
    {
        PlaceholderType.Title or PlaceholderType.CenteredTitle => "title",
        PlaceholderType.Subtitle => "subtitle",
        PlaceholderType.Body or PlaceholderType.Object => "outline",
        PlaceholderType.Picture or PlaceholderType.ClipArt => "graphic",
        PlaceholderType.Table => "table",
        PlaceholderType.Chart => "chart",
        PlaceholderType.Diagram => "orgchart",
        PlaceholderType.Media => "object",
        PlaceholderType.DateAndTime => "date-time",
        PlaceholderType.Footer => "footer",
        PlaceholderType.SlideNumber => "page-number",
        PlaceholderType.Header => "header",
        PlaceholderType.SlideImage => "page",
        _ => null,
    };

    // ---------------------------------------------------------------- numbering

    /// <summary>The ODF numbering of a DrawingML auto-number scheme: format, prefix, suffix.</summary>
    public static (string Format, string Prefix, string Suffix) Numbering(string scheme)
    {
        var format = scheme.StartsWith("alphaLc", StringComparison.Ordinal) ? "a"
            : scheme.StartsWith("alphaUc", StringComparison.Ordinal) ? "A"
            : scheme.StartsWith("romanLc", StringComparison.Ordinal) ? "i"
            : scheme.StartsWith("romanUc", StringComparison.Ordinal) ? "I"
            : "1";
        var (prefix, suffix) = scheme.EndsWith("ParenBoth", StringComparison.Ordinal) ? ("(", ")")
            : scheme.EndsWith("ParenR", StringComparison.Ordinal) ? ("", ")")
            : scheme.EndsWith("Plain", StringComparison.Ordinal) ? ("", "")
            : scheme.EndsWith("Minus", StringComparison.Ordinal) ? ("- ", "")
            : ("", ".");
        return (format, prefix, suffix);
    }

    /// <summary>The DrawingML scheme of an ODF numbering.</summary>
    public static string SchemeOf(string? format, string? prefix, string? suffix)
    {
        var name = format switch
        {
            "a" => "alphaLc",
            "A" => "alphaUc",
            "i" => "romanLc",
            "I" => "romanUc",
            _ => "arabic",
        };
        var style = (prefix ?? "", suffix ?? "") switch
        {
            ("(", ")") => "ParenBoth",
            ("", ")") => "ParenR",
            ("", "") => "Plain",
            _ => "Period",
        };
        return name + style;
    }

    // ---------------------------------------------------------------- underline

    public static (string Style, bool Double) Underline(string underline) => underline switch
    {
        "none" => ("none", false),
        "dbl" => ("solid", true),
        "dotted" or "dottedHeavy" => ("dotted", false),
        "dash" or "dashHeavy" => ("dash", false),
        "dashLong" or "dashLongHeavy" => ("long-dash", false),
        "dotDash" or "dotDashHeavy" => ("dot-dash", false),
        "dotDotDash" or "dotDotDashHeavy" => ("dot-dot-dash", false),
        "wavy" or "wavyHeavy" or "wavyDbl" => ("wave", false),
        _ => ("solid", false),
    };

    public static string? UnderlineOf(string? style, bool isDouble) => style switch
    {
        null => null,
        "none" => "none",
        "solid" => isDouble ? "dbl" : "sng",
        "dotted" => "dotted",
        "dash" => "dash",
        "long-dash" => "dashLong",
        "dot-dash" => "dotDash",
        "dot-dot-dash" => "dotDotDash",
        "wave" => "wavy",
        _ => "sng",
    };

    // ---------------------------------------------------------------- dashes and markers

    /// <summary>The DrawingML dash presets, each as a pattern in multiples of the line width.</summary>
    public static readonly string[] DashPresets = ["dot", "dash", "lgDash", "dashDot", "lgDashDot", "lgDashDotDot", "sysDash", "sysDot", "sysDashDot", "sysDashDotDot"];

    /// <summary>The ODF marker name of a line end (the names LibreOffice gives its markers).</summary>
    public static string MarkerName(LineEndKind kind) => kind switch
    {
        LineEndKind.Stealth => "Arrow_20_concave",
        LineEndKind.Diamond => "Square_20_45",
        LineEndKind.Oval => "Circle",
        LineEndKind.Arrow => "Line_20_Arrow",
        _ => "Arrow",
    };

    public static string MarkerDisplayName(LineEndKind kind) => kind switch
    {
        LineEndKind.Stealth => "Arrow concave",
        LineEndKind.Diamond => "Square 45",
        LineEndKind.Oval => "Circle",
        LineEndKind.Arrow => "Line Arrow",
        _ => "Arrow",
    };

    /// <summary>The marker's view box and path (in its own units).</summary>
    public static (string ViewBox, string Path) MarkerShape(LineEndKind kind) => kind switch
    {
        LineEndKind.Stealth => ("0 0 1131 1580", "M1013 1491l118 89-567-1580-564 1580 114-85 136-68 148-46 161-17 161 13 153 46z"),
        LineEndKind.Diamond => ("0 0 1131 1131", "M0 564l564 567 567-567-567-564z"),
        LineEndKind.Oval => ("0 0 1131 1131", "M462 1118l-102-29-102-51-93-72-72-93-51-102-29-102-13-105 13-102 29-106 51-102 72-89 93-72 102-50 102-34 106-9 101 9 106 34 98 50 93 72 72 89 51 102 29 106 13 102-13 105-29 102-51 102-72 93-93 72-98 51-106 29-101 13z"),
        LineEndKind.Arrow => ("0 0 1122 2243", "M0 2108v17 17l12 42 30 34 38 21 43 4 29-8 30-21 25-26 13-34 343-1532 339 1520 13 42 29 34 39 21 42 4 42-12 34-30 21-42v-39-12l-4 4-440-1998-9-42-25-39-38-25-43-8-42 8-38 25-26 39-8 42z"),
        _ => ("0 0 20 30", "M10 0l-10 30h20z"),
    };

    /// <summary>The line end of a marker name (LibreOffice's names and ours), or null.</summary>
    public static LineEndKind? MarkerKind(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var plain = name.Replace("_20_", " ", StringComparison.Ordinal).ToLowerInvariant();
        return plain switch
        {
            _ when plain.Contains("concave", StringComparison.Ordinal) || plain.Contains("stealth", StringComparison.Ordinal) => LineEndKind.Stealth,
            _ when plain.Contains("line arrow", StringComparison.Ordinal) || plain.Contains("line_arrow", StringComparison.Ordinal) => LineEndKind.Arrow,
            _ when plain.Contains("square 45", StringComparison.Ordinal) || plain.Contains("diamond", StringComparison.Ordinal) => LineEndKind.Diamond,
            _ when plain.Contains("circle", StringComparison.Ordinal) || plain.Contains("oval", StringComparison.Ordinal) => LineEndKind.Oval,
            _ => LineEndKind.Triangle,
        };
    }

    /// <summary>The marker width as a multiple of the line width per line end width.</summary>
    public static double MarkerFactor(LineEndSize size) => size switch
    {
        LineEndSize.Small => 2,
        LineEndSize.Large => 5,
        _ => 3,
    };

    public static LineEndSize MarkerSizeOf(double factor) => factor < 2.5 ? LineEndSize.Small : factor > 4 ? LineEndSize.Large : LineEndSize.Medium;

    // ---------------------------------------------------------------- transitions

    /// <summary>The SMIL type, subtype and direction of a transition (null: ODF has none for it).</summary>
    public static (string Type, string Subtype, bool Reverse)? Transition(SlideTransition transition) => transition.Kind switch
    {
        TransitionKind.Fade => ("fade", transition.ThroughBlack ? "fadeOverColor" : "crossfade", false),
        TransitionKind.Push => ("pushWipe", transition.Direction switch
        {
            "r" => "fromLeft",
            "u" => "fromBottom",
            "d" => "fromTop",
            _ => "fromRight",
        }, false),
        TransitionKind.Wipe => transition.Direction switch
        {
            "l" => ("barWipe", "leftToRight", true),
            "u" => ("barWipe", "topToBottom", true),
            "d" => ("barWipe", "topToBottom", false),
            _ => ("barWipe", "leftToRight", false),
        },
        TransitionKind.Cover => ("slideWipe", SlideSubtype(transition.Direction), false),
        TransitionKind.Uncover => ("slideWipe", SlideSubtype(transition.Direction), true),
        TransitionKind.Split => ("barnDoorWipe", transition.Direction?.StartsWith("horz", StringComparison.Ordinal) == true ? "horizontal" : "vertical", transition.Direction?.EndsWith("in", StringComparison.Ordinal) == true),
        TransitionKind.Dissolve => ("dissolve", "default", false),
        TransitionKind.RandomBars => ("randomBarWipe", transition.Direction == "vert" ? "vertical" : "horizontal", false),
        TransitionKind.Zoom => ("fade", "crossfade", false),
        _ => null,
    };

    private static string SlideSubtype(string? direction) => direction switch
    {
        "r" => "fromLeft",
        "u" => "fromBottom",
        "d" => "fromTop",
        "lu" => "fromBottomRight",
        "ru" => "fromBottomLeft",
        "ld" => "fromTopRight",
        "rd" => "fromTopLeft",
        _ => "fromRight",
    };

    private static string SlideDirection(string? subtype) => subtype switch
    {
        "fromLeft" => "r",
        "fromBottom" => "u",
        "fromTop" => "d",
        "fromBottomRight" => "lu",
        "fromBottomLeft" => "ru",
        "fromTopRight" => "ld",
        "fromTopLeft" => "rd",
        _ => "l",
    };

    /// <summary>The transition kind and direction of SMIL values (null kind: ODF's transition has no ThixShow equivalent).</summary>
    public static (TransitionKind? Kind, string? Direction, bool ThroughBlack) TransitionOf(string? type, string? subtype, bool reverse) => type switch
    {
        null or "" or "none" => (TransitionKind.None, null, false),
        "fade" => (TransitionKind.Fade, null, subtype is "fadeOverColor" or "fadeToColor" or "fadeFromColor"),
        "pushWipe" => (TransitionKind.Push, SlideDirection(subtype), false),
        "barWipe" => (TransitionKind.Wipe, subtype == "topToBottom" ? reverse ? "u" : "d" : reverse ? "l" : "r", false),
        "slideWipe" => (reverse ? TransitionKind.Uncover : TransitionKind.Cover, SlideDirection(subtype), false),
        "barnDoorWipe" => (TransitionKind.Split, (subtype == "horizontal" ? "horz" : "vert") + (reverse ? "in" : "out"), false),
        "dissolve" => (TransitionKind.Dissolve, null, false),
        "randomBarWipe" => (TransitionKind.RandomBars, subtype == "vertical" ? "vert" : "horz", false),
        _ => (null, null, false),
    };

    /// <summary>The speed word of a duration (what the transition-speed of an exact duration says).</summary>
    public static string SpeedOf(int milliseconds) => milliseconds <= 500 ? "fast" : milliseconds <= 800 ? "medium" : "slow";

    public static string Seconds(int milliseconds) => (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "s";

    /// <summary>The preset-sub-type of a fly effect: the side it comes from (in) or goes to (out), as LibreOffice names it ("from-bottom" …).</summary>
    public static string FlySubtype(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => "from-left",
        AnimationDirection.Right => "from-right",
        AnimationDirection.Top => "from-top",
        _ => "from-bottom",
    };

    /// <summary>The side of a fly effect's preset-sub-type ("from-left", also "to-left" or "left"); bottom when it names none.</summary>
    public static AnimationDirection FlyDirection(string? subtype) => subtype switch
    {
        _ when subtype is null => AnimationDirection.Bottom,
        _ when subtype.EndsWith("left", StringComparison.Ordinal) => AnimationDirection.Left,
        _ when subtype.EndsWith("right", StringComparison.Ordinal) => AnimationDirection.Right,
        _ when subtype.EndsWith("top", StringComparison.Ordinal) => AnimationDirection.Top,
        _ => AnimationDirection.Bottom,
    };

    /// <summary>"0.5s", "500ms", "PT0.5S", "indefinite" in milliseconds, or null.</summary>
    public static int? Milliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.StartsWith('P'))
        {
            return OdfXml.DurationDays(value) is { } days ? (int)Math.Round(days * 86400000) : null;
        }

        var (text, factor) = value.EndsWith("ms", StringComparison.Ordinal) ? (value[..^2], 1.0)
            : value.EndsWith("min", StringComparison.Ordinal) ? (value[..^3], 60000.0)
            : value.EndsWith('h') ? (value[..^1], 3600000.0)
            : value.EndsWith('s') ? (value[..^1], 1000.0)
            : (value, 1000.0);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (int)Math.Round(number * factor) : null;
    }
}
