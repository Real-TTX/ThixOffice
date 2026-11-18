using System.Text;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>The label of a list item: its text and the list level it belongs to.</summary>
public sealed record ListLabel(string Text, NumberingLevel Level);

/// <summary>
/// Computes the labels of all list items in document order, like Word: counters per list definition (instances of the same
/// definition continue each other), a start override of an instance restarts its level the first time it is used, a new item
/// restarts the deeper levels, "%1.%2." patterns take every level's number in its own format.
/// </summary>
public static class ListNumbering
{
    private const int Levels = 9;

    /// <summary>The label of every block (null for blocks that are not list items).</summary>
    public static ListLabel?[] Compute(Document document, StyleResolver resolver)
    {
        var labels = new ListLabel?[document.Blocks.Count];
        var counters = new Dictionary<int, int[]>();
        var restarted = new HashSet<(int Instance, int Level)>();

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            if (document.Blocks[index] is not Paragraph paragraph)
            {
                continue;
            }

            var format = resolver.ResolveParagraph(paragraph);
            if (format.Numbering is not { } reference || document.Numbering.Instance(reference.NumberingId) is not { } instance)
            {
                continue;
            }

            var levelIndex = Math.Clamp(reference.Level, 0, Levels - 1);
            var level = document.Numbering.ResolveLevel(instance.Id, levelIndex);
            if (level is null)
            {
                continue;
            }

            if (!counters.TryGetValue(instance.AbstractId, out var values))
            {
                values = new int[Levels];
                counters[instance.AbstractId] = values;
            }

            if (instance.OverrideAt(levelIndex)?.StartOverride is { } start && restarted.Add((instance.Id, levelIndex)))
            {
                values[levelIndex] = start - 1;
                ResetDeeper(document, instance.Id, values, levelIndex);
            }

            values[levelIndex] = values[levelIndex] == 0 ? level.Start : values[levelIndex] + 1;
            ResetDeeper(document, instance.Id, values, levelIndex);

            labels[index] = new ListLabel(LabelText(document, instance.Id, level, values), level);
        }

        return labels;
    }

    /// <summary>A number in a list format: 3 → "3", "c", "C", "iii", "III", "03".</summary>
    public static string Format(int value, NumberFormat format) => format switch
    {
        NumberFormat.None or NumberFormat.Bullet => "",
        NumberFormat.DecimalZero => value < 10 ? $"0{value}" : value.ToString(),
        NumberFormat.LowerLetter => Letters(value).ToLowerInvariant(),
        NumberFormat.UpperLetter => Letters(value),
        NumberFormat.LowerRoman => Roman(value).ToLowerInvariant(),
        NumberFormat.UpperRoman => Roman(value),
        _ => value.ToString(),
    };

    private static void ResetDeeper(Document document, int instanceId, int[] values, int level)
    {
        for (var deeper = level + 1; deeper < Levels; deeper++)
        {
            var restartAfter = document.Numbering.ResolveLevel(instanceId, deeper)?.RestartAfterLevel;
            if (restartAfter == 0)
            {
                continue;
            }

            values[deeper] = 0;
        }
    }

    private static string LabelText(Document document, int instanceId, NumberingLevel level, int[] values)
    {
        if (level.Format == NumberFormat.Bullet)
        {
            return level.Text;
        }

        var text = new StringBuilder();
        var pattern = level.Text;
        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] == '%' && index + 1 < pattern.Length && pattern[index + 1] is >= '1' and <= '9')
            {
                var referenced = pattern[index + 1] - '1';
                var referencedLevel = document.Numbering.ResolveLevel(instanceId, referenced);
                var value = values[referenced] == 0 ? referencedLevel?.Start ?? 1 : values[referenced];
                var format = level.IsLegal && referenced != level.Level ? NumberFormat.Decimal : referencedLevel?.Format ?? NumberFormat.Decimal;
                text.Append(Format(value, format));
                index++;
                continue;
            }

            text.Append(pattern[index]);
        }

        return text.ToString();
    }

    private static string Letters(int value)
    {
        if (value <= 0)
        {
            return "";
        }

        // Word repeats the letter: a … z, aa … zz, aaa …
        var letter = (char)('A' + (value - 1) % 26);
        return new string(letter, (value - 1) / 26 + 1);
    }

    private static string Roman(int value)
    {
        if (value <= 0 || value >= 4000)
        {
            return value.ToString();
        }

        (int Value, string Text)[] numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var text = new StringBuilder();
        var rest = value;
        foreach (var (amount, numeral) in numerals)
        {
            while (rest >= amount)
            {
                text.Append(numeral);
                rest -= amount;
            }
        }

        return text.ToString();
    }
}
