using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// Sets a formula (<see cref="MathNode"/>) as boxes, the way Word's math looks in outline: text in Cambria Math (or its substitute) –
/// variables italic, numbers and operators upright with space around operators –, fractions stacked on the math axis, scripts smaller
/// and raised / lowered, radicals with their sign, big operators with limits under/over or beside them, delimiters stretched to their
/// content, matrices, accents, bars, braces, functions, limits, frames and equation arrays. Coordinates: x from the left of the box, y
/// from its baseline (down positive). Pure and read-only: the formula is not editable.
/// </summary>
internal static class MathLayouter
{
    public const string Font = "Cambria Math";

    private const double ScriptScale = 0.7;
    private const double MinimumScale = 0.5;

    public static MathBox Layout(MathNode node, double size, bool display, ITextMeasurer measurer) =>
        new Setter(measurer, size).Set(node, 1, display);

    private sealed class Setter(ITextMeasurer measurer, double baseSize)
    {
        public MathBox Set(MathNode node, double scale, bool display)
        {
            var em = baseSize * scale;
            return node switch
            {
                MathText text => Text(text, scale, atStart: true),
                MathRow row => Row(row.Items, scale, display),
                MathFraction fraction => Fraction(fraction, scale, display),
                MathScripts scripts => Scripts(scripts, scale, display),
                MathRadical radical => Radical(radical, scale, display),
                MathNary nary => Nary(nary, scale, display),
                MathDelimiter delimiter => Delimiter(delimiter, scale, display),
                MathMatrix matrix => Matrix(matrix, scale, display),
                MathAccent accent => Accent(accent, scale, display),
                MathBar bar => Bar(bar, scale, display, em),
                MathGroupChar group => Group(group, scale, display, em),
                MathFunction function => Row([function.Name, new MathText(" ", MathStyle.Plain), function.Argument], scale, display),
                MathLimit limit => Limit(limit, scale, display, em),
                MathBorderBox box => Frame(Set(box.Base, scale, display), em),
                MathEquationArray array => Stack([.. array.Rows.Select(row => Set(row, scale, display))], em * 0.25, em),
                _ => new MathBox(),
            };
        }

        private static double Smaller(double scale) => Math.Max(MinimumScale, scale * ScriptScale);

        private double Axis(double scale) => baseSize * scale * 0.25;

        // ------------------------------------------------------------ text

        private static bool IsBinary(string text) => text is "+" or "−" or "-" or "±" or "∓" or "×" or "÷" or "·" or "∗" or "∘" or "⋅" or "∪" or "∩";

        private static bool IsRelation(string text) => text is "=" or "<" or ">" or "≤" or "≥" or "≠" or "≈" or "≡" or "∼" or "≅" or "→" or "←" or "↔" or "⇒" or "⇐"
            or "⇔" or "∈" or "∉" or "⊂" or "⊆" or "⊃" or "⊇" or "∝" or "≪" or "≫" or ":=";

        private MathBox Text(MathText text, double scale, bool atStart)
        {
            var box = new MathBox();
            var size = baseSize * scale;
            var value = text.Text.Replace('-', '−');
            var index = 0;
            while (index < value.Length)
            {
                // Pieces: a run of letters (italic as variables), a run of digits and others (upright), one operator.
                var start = index;
                var letter = char.IsLetter(value[index]);
                var operatorPiece = IsBinary(value[index].ToString()) || IsRelation(value[index].ToString());
                index++;
                while (!operatorPiece && index < value.Length && char.IsLetter(value[index]) == letter && !IsBinary(value[index].ToString()) && !IsRelation(value[index].ToString()))
                {
                    index++;
                }

                var piece = value[start..index];
                var font = new FontSpec(Font, size);

                // Spaces around operators as Word sets them: none in scripts, none after a sign at the start (a unary minus).
                var unary = IsBinary(piece) && box.Width == 0 && atStart;
                var gap = text.Style == MathStyle.NormalText || scale < 1 || unary ? 0 : IsRelation(piece) ? size * 0.28 : IsBinary(piece) ? size * 0.22 : piece == "," ? size * 0.17 : 0;
                box.Width += piece == "," ? 0 : gap;
                var shaped = measurer.Shape(letter && text.Style != MathStyle.NormalText ? MathAlphabet(piece, text.Style) : piece, font);
                box.Pieces.Add(new MathGlyphs(box.Width, 0, shaped, font));
                box.Width += shaped.Width + gap;
                box.Ascent = Math.Max(box.Ascent, size * 0.72);
                box.Descent = Math.Max(box.Descent, size * 0.22);
            }

            return box;
        }

        /// <summary>
        /// Letters as the math alphabets have them (as Word writes them into the math font): italic 𝑎…𝑧, bold 𝐚…𝐳, bold italic 𝒂…𝒛, Greek
        /// italic 𝛼…𝜔; upright text stays as it is.
        /// </summary>
        private static string MathAlphabet(string text, MathStyle style)
        {
            if (style == MathStyle.Plain)
            {
                return text;
            }

            var builder = new System.Text.StringBuilder(text.Length * 2);
            foreach (var character in text)
            {
                int? mapped = (style, character) switch
                {
                    (MathStyle.Italic, 'h') => 0x210E,
                    (MathStyle.Italic, >= 'a' and <= 'z') => 0x1D44E + (character - 'a'),
                    (MathStyle.Italic, >= 'A' and <= 'Z') => 0x1D434 + (character - 'A'),
                    (MathStyle.Italic, >= 'α' and <= 'ω') => 0x1D6FC + (character - 'α'),
                    (MathStyle.Italic, >= 'Α' and <= 'Ω') => 0x1D6E2 + (character - 'Α'),
                    (MathStyle.Bold, >= 'a' and <= 'z') => 0x1D41A + (character - 'a'),
                    (MathStyle.Bold, >= 'A' and <= 'Z') => 0x1D400 + (character - 'A'),
                    (MathStyle.BoldItalic, >= 'a' and <= 'z') => 0x1D482 + (character - 'a'),
                    (MathStyle.BoldItalic, >= 'A' and <= 'Z') => 0x1D468 + (character - 'A'),
                    _ => null,
                };
                if (mapped is { } codePoint)
                {
                    builder.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private MathBox Glyph(string text, double size)
        {
            var font = new FontSpec(Font, size);
            var shaped = measurer.Shape(text, font);
            var box = new MathBox { Width = shaped.Width, Ascent = size * 0.72, Descent = size * 0.22 };
            box.Pieces.Add(new MathGlyphs(0, 0, shaped, font));
            return box;
        }

        private MathBox Row(IReadOnlyList<MathNode> items, double scale, bool display)
        {
            var row = new MathBox();
            if (items.Count == 0)
            {
                // An empty slot keeps a little room (a missing exponent, an empty argument).
                row.Width = baseSize * scale * 0.2;
                row.Ascent = baseSize * scale * 0.5;
                return row;
            }

            foreach (var item in items)
            {
                var box = item is MathText text ? Text(text, scale, atStart: row.Width == 0) : Set(item, scale, display);
                row.Append(box);
            }

            return row;
        }

        // ------------------------------------------------------------ structures

        private MathBox Fraction(MathFraction fraction, double scale, bool display)
        {
            var em = baseSize * scale;
            if (fraction.Type is "lin" or "skw")
            {
                return Row([fraction.Numerator, new MathText("/", MathStyle.Plain), fraction.Denominator], scale, display);
            }

            var inner = display ? scale : Math.Max(MinimumScale, scale * 0.85);
            var numerator = Set(fraction.Numerator, inner, false);
            var denominator = Set(fraction.Denominator, inner, false);
            var axis = Axis(scale);
            var thickness = Math.Max(0.5, em * 0.05);
            var gap = em * 0.12;
            var width = Math.Max(numerator.Width, denominator.Width) + em * 0.2;
            var numeratorBaseline = -(axis + thickness / 2 + gap + numerator.Descent);
            var denominatorBaseline = -axis + thickness / 2 + gap + denominator.Ascent;
            var box = new MathBox { Width = width, Ascent = -numeratorBaseline + numerator.Ascent, Descent = denominatorBaseline + denominator.Descent };
            box.Add(numerator, (width - numerator.Width) / 2, numeratorBaseline);
            box.Add(denominator, (width - denominator.Width) / 2, denominatorBaseline);
            if (fraction.Type != "noBar")
            {
                box.Pieces.Add(new MathRule(em * 0.05, -axis, width - em * 0.05, -axis, thickness));
            }

            return box;
        }

        private MathBox Scripts(MathScripts scripts, double scale, bool display)
        {
            var em = baseSize * scale;
            var body = Set(scripts.Base, scale, display);
            var small = Smaller(scale);
            var sub = scripts.Sub is { } subNode ? Set(subNode, small, false) : null;
            var sup = scripts.Sup is { } supNode ? Set(supNode, small, false) : null;
            var supShift = Math.Max(em * 0.38, body.Ascent - (sup?.Ascent ?? 0) * 0.6);
            var subShift = Math.Max(em * 0.18, body.Descent + (sub?.Ascent ?? 0) * 0.3 - em * 0.05);
            if (sub is not null && sup is not null)
            {
                var clearance = (supShift - sup.Descent) - (sub.Ascent - subShift);
                if (clearance < em * 0.1)
                {
                    subShift += em * 0.1 - clearance;
                }
            }

            var scriptsWidth = Math.Max(sub?.Width ?? 0, sup?.Width ?? 0);
            var box = new MathBox { Ascent = body.Ascent, Descent = body.Descent };
            var bodyX = scripts.Pre ? scriptsWidth + em * 0.05 : 0;
            var scriptX = scripts.Pre ? 0 : body.Width + em * 0.03;
            box.Add(body, bodyX, 0);
            if (sup is not null)
            {
                box.Add(sup, scripts.Pre ? scriptsWidth - sup.Width : scriptX, -supShift);
                box.Ascent = Math.Max(box.Ascent, supShift + sup.Ascent);
            }

            if (sub is not null)
            {
                box.Add(sub, scripts.Pre ? scriptsWidth - sub.Width : scriptX, subShift);
                box.Descent = Math.Max(box.Descent, subShift + sub.Descent);
            }

            box.Width = scripts.Pre ? bodyX + body.Width : scriptX + scriptsWidth + em * 0.05;
            return box;
        }

        private MathBox Radical(MathRadical radical, double scale, bool display)
        {
            var em = baseSize * scale;
            var body = Set(radical.Base, scale, display);
            var thickness = Math.Max(0.5, em * 0.05);
            var top = -(body.Ascent + em * 0.12);
            var bottom = body.Descent + em * 0.05;
            var degree = radical.Degree is { } degreeNode ? Set(degreeNode, Math.Max(MinimumScale, scale * 0.55), false) : null;
            var offset = degree is null ? 0 : Math.Max(0, degree.Width - em * 0.3);
            var middle = (top + bottom) / 2;
            var signWidth = em * 0.6;
            var path = new VectorPathBuilder()
                .MoveTo(offset, middle + em * 0.08)
                .LineTo(offset + em * 0.15, middle - em * 0.02)
                .LineTo(offset + em * 0.33, bottom)
                .LineTo(offset + signWidth, top)
                .LineTo(offset + signWidth + em * 0.08 + body.Width + em * 0.05, top)
                .Build();
            var box = new MathBox { Width = offset + signWidth + em * 0.08 + body.Width + em * 0.1, Ascent = -top + thickness, Descent = bottom + thickness };
            box.Pieces.Add(new MathStroke(path, thickness));
            box.Add(body, offset + signWidth + em * 0.08, 0);
            if (degree is not null)
            {
                var degreeBaseline = middle - em * 0.1;
                box.Add(degree, offset + em * 0.3 - degree.Width + (degree.Width > em * 0.3 ? 0 : 0), degreeBaseline - degree.Descent);
                box.Ascent = Math.Max(box.Ascent, -(degreeBaseline - degree.Descent) + degree.Ascent);
            }

            return box;
        }

        private MathBox Nary(MathNary nary, double scale, bool display)
        {
            var em = baseSize * scale;
            var integral = nary.Operator is "∫" or "∬" or "∭" or "∮" or "∯" or "∰";
            var operatorSize = em * (integral ? display ? 1.9 : 1.3 : display ? 1.45 : 1.1);
            var glyph = Glyph(nary.Operator, operatorSize);
            var axis = Axis(scale);
            var operatorBaseline = -axis + operatorSize * 0.3; // the operator centred on the math axis
            var operatorBox = new MathBox { Width = glyph.Width, Ascent = -operatorBaseline + operatorSize * 0.72, Descent = operatorBaseline + operatorSize * 0.22 };
            operatorBox.Add(glyph, 0, operatorBaseline);
            var small = Smaller(scale);
            var sub = nary.Sub is { } subNode ? Set(subNode, small, false) : null;
            var sup = nary.Sup is { } supNode ? Set(supNode, small, false) : null;
            var box = new MathBox();
            if (nary.Limits)
            {
                var width = Math.Max(operatorBox.Width, Math.Max(sub?.Width ?? 0, sup?.Width ?? 0));
                box.Add(operatorBox, (width - operatorBox.Width) / 2, 0);
                box.Ascent = operatorBox.Ascent;
                box.Descent = operatorBox.Descent;
                if (sup is not null)
                {
                    var baseline = -(operatorBox.Ascent + em * 0.08 + sup.Descent);
                    box.Add(sup, (width - sup.Width) / 2, baseline);
                    box.Ascent = -baseline + sup.Ascent;
                }

                if (sub is not null)
                {
                    var baseline = operatorBox.Descent + em * 0.08 + sub.Ascent;
                    box.Add(sub, (width - sub.Width) / 2, baseline);
                    box.Descent = baseline + sub.Descent;
                }

                box.Width = width;
            }
            else
            {
                box.Add(operatorBox, 0, 0);
                box.Ascent = operatorBox.Ascent;
                box.Descent = operatorBox.Descent;
                var x = operatorBox.Width + em * 0.05;
                if (sup is not null)
                {
                    var baseline = -(operatorBox.Ascent - sup.Ascent);
                    box.Add(sup, x + em * 0.1, baseline);
                }

                if (sub is not null)
                {
                    var baseline = operatorBox.Descent - sub.Descent;
                    box.Add(sub, x, baseline);
                }

                box.Width = x + Math.Max(sub?.Width ?? 0, (sup?.Width ?? 0) + em * 0.1);
            }

            box.Width += em * 0.15;
            var body = Set(nary.Base, scale, display);
            box.Append(body);
            return box;
        }

        private MathBox Delimiter(MathDelimiter delimiter, double scale, bool display)
        {
            var em = baseSize * scale;
            var content = new MathBox();
            for (var index = 0; index < delimiter.Items.Count; index++)
            {
                if (index > 0 && delimiter.Separator.Length > 0)
                {
                    content.Append(Glyph(delimiter.Separator, em));
                }

                content.Append(Set(delimiter.Items[index], scale, display));
            }

            var axis = Axis(scale);
            var half = Math.Max(content.Ascent - axis, content.Descent + axis);
            var height = Math.Max(em * 0.94, 2 * half + em * 0.1);
            var box = new MathBox();
            void Fence(string character)
            {
                if (character.Length == 0)
                {
                    return;
                }

                var glyph = Glyph(character, em);
                var stretch = height / (em * 0.94);
                if (stretch <= 1.1)
                {
                    box.Append(glyph);
                    return;
                }

                // A taller fence: the glyph stretched vertically about the math axis.
                var baseline = -axis + em * 0.25 * stretch;
                var fence = new MathBox { Width = glyph.Width, Ascent = axis + height / 2, Descent = height / 2 - axis };
                foreach (var piece in glyph.Pieces.OfType<MathGlyphs>())
                {
                    fence.Pieces.Add(piece with { Y = baseline, ScaleY = stretch });
                }

                box.Append(fence);
            }

            Fence(delimiter.Begin);
            box.Append(content);
            Fence(delimiter.End);
            box.Ascent = Math.Max(box.Ascent, content.Ascent);
            box.Descent = Math.Max(box.Descent, content.Descent);
            return box;
        }

        private MathBox Matrix(MathMatrix matrix, double scale, bool display)
        {
            var em = baseSize * scale;
            var cells = matrix.Rows.Select(row => row.Items.Select(cell => Set(cell, scale, display)).ToList()).ToList();
            var columns = cells.Select(row => row.Count).DefaultIfEmpty(0).Max();
            var widths = Enumerable.Range(0, columns).Select(column => cells.Where(row => column < row.Count).Select(row => row[column].Width).DefaultIfEmpty(0).Max()).ToList();
            var rows = cells.Select(row =>
            {
                var box = new MathBox();
                var x = 0.0;
                for (var column = 0; column < row.Count; column++)
                {
                    box.Add(row[column], x + (widths[column] - row[column].Width) / 2, 0);
                    box.Ascent = Math.Max(box.Ascent, row[column].Ascent);
                    box.Descent = Math.Max(box.Descent, row[column].Descent);
                    x += widths[column] + em * 0.6;
                }

                box.Width = Math.Max(0, x - em * 0.6);
                return box;
            }).ToList();
            return Stack(rows, em * 0.25, em);
        }

        /// <summary>Boxes under each other, centred, the whole centred on the math axis.</summary>
        private MathBox Stack(IReadOnlyList<MathBox> rows, double gap, double em)
        {
            var box = new MathBox();
            if (rows.Count == 0)
            {
                return box;
            }

            var width = rows.Max(row => row.Width);
            var y = 0.0;
            var baselines = new List<double>();
            for (var index = 0; index < rows.Count; index++)
            {
                if (index > 0)
                {
                    y += rows[index - 1].Descent + gap + rows[index].Ascent;
                }

                baselines.Add(y);
            }

            var total = baselines[^1] + rows[^1].Descent + rows[0].Ascent;
            var shift = -(em * 0.25) - total / 2 + rows[0].Ascent; // the middle of the stack on the axis
            for (var index = 0; index < rows.Count; index++)
            {
                box.Add(rows[index], (width - rows[index].Width) / 2, baselines[index] + shift);
            }

            box.Width = width;
            box.Ascent = rows[0].Ascent - shift;
            box.Descent = baselines[^1] + shift + rows[^1].Descent;
            return box;
        }

        private MathBox Accent(MathAccent accent, double scale, bool display)
        {
            var em = baseSize * scale;
            var body = Set(accent.Base, scale, display);
            var box = new MathBox { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent };
            box.Add(body, 0, 0);
            var (character, arrow) = accent.Accent switch
            {
                "̂" => ("ˆ", false),
                "̃" => ("˜", false),
                "̇" => ("˙", false),
                "̈" => ("¨", false),
                "̄" or "̅" or "¯" => ("¯", false),
                "́" => ("´", false),
                "̀" => ("`", false),
                "̌" => ("ˇ", false),
                "̆" => ("˘", false),
                "⃗" or "⃖" or "⃡" => ("→", true),
                var other => (other, false),
            };
            var glyph = Glyph(character, arrow ? em * 0.6 : em);
            var baseline = arrow ? -(body.Ascent + em * 0.05) : -(body.Ascent - em * 0.42);
            box.Add(glyph, Math.Max(0, (body.Width - glyph.Width) / 2), baseline);
            box.Ascent = body.Ascent + em * (arrow ? 0.4 : 0.3);
            box.Width = Math.Max(body.Width, glyph.Width);
            return box;
        }

        private MathBox Bar(MathBar bar, double scale, bool display, double em)
        {
            var body = Set(bar.Base, scale, display);
            var thickness = Math.Max(0.5, em * 0.05);
            var box = new MathBox { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent };
            box.Add(body, 0, 0);
            var y = bar.Top ? -(body.Ascent + em * 0.08) : body.Descent + em * 0.08;
            box.Pieces.Add(new MathRule(0, y, body.Width, y, thickness));
            if (bar.Top)
            {
                box.Ascent += em * 0.12;
            }
            else
            {
                box.Descent += em * 0.12;
            }

            return box;
        }

        private MathBox Group(MathGroupChar group, double scale, bool display, double em)
        {
            var body = Set(group.Base, scale, display);
            var thickness = Math.Max(0.5, em * 0.05);
            var box = new MathBox { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent };
            box.Add(body, 0, 0);
            var depth = em * 0.18;
            var edge = group.Top ? -(body.Ascent + em * 0.08) : body.Descent + em * 0.08;
            var tip = group.Top ? edge - depth : edge + depth;
            var middle = (edge + tip) / 2;
            var width = body.Width;
            var path = new VectorPathBuilder()
                .MoveTo(0, edge).QuadTo(0, middle, width * 0.1, middle).LineTo(width * 0.45, middle).QuadTo(width / 2, middle, width / 2, tip)
                .QuadTo(width / 2, middle, width * 0.55, middle).LineTo(width * 0.9, middle).QuadTo(width, middle, width, edge)
                .Build();
            box.Pieces.Add(new MathStroke(path, thickness));
            if (group.Top)
            {
                box.Ascent += em * 0.08 + depth;
            }
            else
            {
                box.Descent += em * 0.08 + depth;
            }

            return box;
        }

        private MathBox Limit(MathLimit limit, double scale, bool display, double em)
        {
            var body = Set(limit.Base, scale, display);
            var under = Set(limit.Limit, Smaller(scale), false);
            var width = Math.Max(body.Width, under.Width);
            var box = new MathBox { Width = width, Ascent = body.Ascent, Descent = body.Descent };
            box.Add(body, (width - body.Width) / 2, 0);
            if (limit.Upper)
            {
                var baseline = -(body.Ascent + em * 0.05 + under.Descent);
                box.Add(under, (width - under.Width) / 2, baseline);
                box.Ascent = -baseline + under.Ascent;
            }
            else
            {
                var baseline = body.Descent + em * 0.05 + under.Ascent;
                box.Add(under, (width - under.Width) / 2, baseline);
                box.Descent = baseline + under.Descent;
            }

            return box;
        }

        private static MathBox Frame(MathBox body, double em)
        {
            var pad = em * 0.1;
            var thickness = Math.Max(0.5, em * 0.04);
            var box = new MathBox { Width = body.Width + 2 * pad, Ascent = body.Ascent + pad, Descent = body.Descent + pad };
            box.Add(body, pad, 0);
            var (left, top, right, bottom) = (0.0, -box.Ascent, box.Width, box.Descent);
            box.Pieces.Add(new MathRule(left, top, right, top, thickness));
            box.Pieces.Add(new MathRule(left, bottom, right, bottom, thickness));
            box.Pieces.Add(new MathRule(left, top, left, bottom, thickness));
            box.Pieces.Add(new MathRule(right, top, right, bottom, thickness));
            return box;
        }
    }
}

/// <summary>A set formula or part of one: its size around the baseline and what is drawn (x from its left, y from its baseline).</summary>
internal sealed class MathBox
{
    public double Width { get; set; }

    public double Ascent { get; set; }

    public double Descent { get; set; }

    public List<MathPiece> Pieces { get; } = [];

    /// <summary>Puts <paramref name="box"/> at (<paramref name="x"/>, <paramref name="y"/>) into this box (the size is the caller's).</summary>
    public void Add(MathBox box, double x, double y)
    {
        foreach (var piece in box.Pieces)
        {
            Pieces.Add(piece.Moved(x, y));
        }
    }

    /// <summary>Puts <paramref name="box"/> after this box on its baseline and grows it.</summary>
    public void Append(MathBox box)
    {
        Add(box, Width, 0);
        Width += box.Width;
        Ascent = Math.Max(Ascent, box.Ascent);
        Descent = Math.Max(Descent, box.Descent);
    }
}

internal abstract record MathPiece
{
    public abstract MathPiece Moved(double x, double y);
}

/// <summary>Shaped text with its baseline at (<see cref="X"/>, <see cref="Y"/>); stretched by <see cref="ScaleY"/> about that point.</summary>
internal sealed record MathGlyphs(double X, double Y, ShapedText Shaped, FontSpec Font) : MathPiece
{
    public double ScaleY { get; init; } = 1;

    public override MathPiece Moved(double x, double y) => this with { X = X + x, Y = Y + y };
}

/// <summary>A straight line (fraction bar, overline, frame).</summary>
internal sealed record MathRule(double X1, double Y1, double X2, double Y2, double Thickness) : MathPiece
{
    public override MathPiece Moved(double x, double y) => new MathRule(X1 + x, Y1 + y, X2 + x, Y2 + y, Thickness);
}

/// <summary>A stroked path (radical sign, braces).</summary>
internal sealed record MathStroke(VectorPath Path, double Thickness) : MathPiece
{
    public override MathPiece Moved(double x, double y) => new MathStroke(Path.Transform(Matrix2D.Translation(x, y)), Thickness);
}
