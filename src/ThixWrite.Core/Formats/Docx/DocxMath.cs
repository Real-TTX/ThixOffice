using System.Text;
using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Reads OMML (m:oMathPara / m:oMath) into the display tree of <see cref="MathNode"/>s. Tolerant: an element it does not know shows its
/// children; properties it does not know are ignored. The formula itself is kept as XML by the caller.
/// </summary>
internal static class DocxMath
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    /// <summary>The display tree of an m:oMathPara or m:oMath (null when it shows nothing).</summary>
    public static MathNode? Read(OpenXmlElement element)
    {
        var node = element.LocalName == "oMathPara"
            ? new MathEquationArray([.. element.ChildElements.Where(child => child.LocalName == "oMath").Select(Row)])
            : (MathNode)Row(element);
        return node is MathEquationArray { Rows.Count: 1 } single ? single.Rows[0] : node;
    }

    private static MathRow Row(OpenXmlElement? element)
    {
        if (element is null)
        {
            return MathRow.Empty;
        }

        var items = new List<MathNode>();
        foreach (var child in element.ChildElements)
        {
            if (Node(child) is { } node)
            {
                if (node is MathRow row)
                {
                    items.AddRange(row.Items);
                }
                else
                {
                    items.Add(node);
                }
            }
        }

        return new MathRow([.. items]);
    }

    private static OpenXmlElement? Child(OpenXmlElement element, string name) =>
        element.ChildElements.FirstOrDefault(child => child.LocalName == name && child.NamespaceUri == MathNamespace);

    /// <summary>The value of property <paramref name="property"/> in the element's properties (m:fPr, m:naryPr …): its m:val, or null.</summary>
    private static string? Property(OpenXmlElement element, string properties, string property) =>
        Child(element, properties) is { } bag && Child(bag, property) is { } value ? Value(value) : null;

    private static string? Value(OpenXmlElement element) =>
        element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == "val").Value;

    private static bool IsOn(string? value) => value is null or "1" or "on" or "true";

    private static MathNode? Node(OpenXmlElement element)
    {
        if (element.NamespaceUri != MathNamespace)
        {
            // w:r inside a formula (text Word keeps as a normal run), bookmarks, proofing marks …
            return element.LocalName == "r" ? Text(element, MathStyle.NormalText) : null;
        }

        switch (element.LocalName)
        {
            case "r":
                var style = MathStyle.Italic;
                if (Child(element, "rPr") is { } runProperties)
                {
                    if (Child(runProperties, "nor") is { } normal && IsOn(Value(normal)))
                    {
                        style = MathStyle.NormalText;
                    }
                    else if (Child(runProperties, "sty") is { } sty)
                    {
                        style = Value(sty) switch
                        {
                            "p" => MathStyle.Plain,
                            "b" => MathStyle.Bold,
                            "bi" => MathStyle.BoldItalic,
                            _ => MathStyle.Italic,
                        };
                    }
                }

                return Text(element, style);
            case "f":
                return new MathFraction(Row(Child(element, "num")), Row(Child(element, "den")), Property(element, "fPr", "type") ?? "bar");
            case "sSub":
                return new MathScripts(Row(Child(element, "e")), Row(Child(element, "sub")), null);
            case "sSup":
                return new MathScripts(Row(Child(element, "e")), null, Row(Child(element, "sup")));
            case "sSubSup":
                return new MathScripts(Row(Child(element, "e")), Row(Child(element, "sub")), Row(Child(element, "sup")));
            case "sPre":
                return new MathScripts(Row(Child(element, "e")), Row(Child(element, "sub")), Row(Child(element, "sup")), Pre: true);
            case "rad":
                var hideDegree = Property(element, "radPr", "degHide") is { } hide && IsOn(hide);
                var degree = Row(Child(element, "deg"));
                return new MathRadical(Row(Child(element, "e")), hideDegree || degree.Items.Count == 0 ? null : degree);
            case "nary":
                var character = Property(element, "naryPr", "chr") ?? "∫";
                var location = Property(element, "naryPr", "limLoc") ?? (character is "∫" or "∬" or "∭" or "∮" or "∯" or "∰" ? "subSup" : "undOvr");
                var subHidden = Property(element, "naryPr", "subHide") is { } subHide && IsOn(subHide);
                var supHidden = Property(element, "naryPr", "supHide") is { } supHide && IsOn(supHide);
                var sub = Row(Child(element, "sub"));
                var sup = Row(Child(element, "sup"));
                return new MathNary(character, subHidden || sub.Items.Count == 0 ? null : sub, supHidden || sup.Items.Count == 0 ? null : sup, Row(Child(element, "e")), location == "undOvr");
            case "d":
                return new MathDelimiter(
                    Property(element, "dPr", "begChr") ?? "(",
                    Property(element, "dPr", "endChr") ?? ")",
                    Property(element, "dPr", "sepChr") ?? "|",
                    [.. element.ChildElements.Where(child => child.LocalName == "e").Select(child => (MathNode)Row(child))]);
            case "m":
                return new MathMatrix([.. element.ChildElements.Where(child => child.LocalName == "mr")
                    .Select(row => new MathRow([.. row.ChildElements.Where(cell => cell.LocalName == "e").Select(cell => (MathNode)Row(cell))]))]);
            case "acc":
                return new MathAccent(Property(element, "accPr", "chr") ?? "̂", Row(Child(element, "e")));
            case "bar":
                return new MathBar(Row(Child(element, "e")), Property(element, "barPr", "pos") == "top");
            case "groupChr":
                return new MathGroupChar(Property(element, "groupChrPr", "chr") ?? "⏟", Row(Child(element, "e")), Property(element, "groupChrPr", "pos") == "top");
            case "func":
                return new MathFunction(Plain(Row(Child(element, "fName"))), Row(Child(element, "e")));
            case "limLow":
                return new MathLimit(Row(Child(element, "e")), Row(Child(element, "lim")), Upper: false);
            case "limUpp":
                return new MathLimit(Row(Child(element, "e")), Row(Child(element, "lim")), Upper: true);
            case "borderBox":
                return new MathBorderBox(Row(Child(element, "e")));
            case "box" or "phant":
                return Row(Child(element, "e"));
            case "eqArr":
                return new MathEquationArray([.. element.ChildElements.Where(child => child.LocalName == "e").Select(child => (MathNode)Row(child))]);
            case "oMath":
                return Row(element);
            case "rPr" or "ctrlPr" or "argPr" or "oMathParaPr" or "fPr" or "naryPr" or "dPr" or "mPr" or "accPr" or "barPr" or "radPr" or "groupChrPr"
                or "funcPr" or "limLowPr" or "limUppPr" or "sSubPr" or "sSupPr" or "sSubSupPr" or "sPrePr" or "boxPr" or "borderBoxPr" or "eqArrPr" or "phantPr":
                return null;
            default:
                return Row(element);
        }
    }

    /// <summary>A function name is upright (sin, log, lim).</summary>
    private static MathRow Plain(MathRow row) =>
        new([.. row.Items.Select(item => item is MathText { Style: MathStyle.Italic } text ? text with { Style = MathStyle.Plain } : item)]);

    private static MathText? Text(OpenXmlElement run, MathStyle style)
    {
        var text = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            if (child.LocalName == "t")
            {
                text.Append(child.InnerText);
            }
            else if (child.LocalName == "tab")
            {
                text.Append(' ');
            }
        }

        return text.Length == 0 ? null : new MathText(text.ToString(), style);
    }
}
