using System.Text;
using ThixCalc.Core.Globalization;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// A formula that is one function call, as text (Funktionsargumente): "=SVERWEIS(A1;B:C;2;FALSCH)" ↔ the name and the argument
/// texts. Separators inside quotes, parentheses, braces and sheet names in apostrophes do not split.
/// </summary>
public static class FunctionCallText
{
    /// <summary>The name and arguments of "=NAME(…)" when the whole formula is that one call, else null.</summary>
    public static (string Name, IReadOnlyList<string> Arguments)? Split(string formula, CalcLocale locale)
    {
        var text = formula.Trim();
        if (!text.StartsWith('='))
        {
            return null;
        }

        text = text[1..].TrimStart();
        var open = text.IndexOf('(');
        if (open <= 0 || !text.EndsWith(')'))
        {
            return null;
        }

        var name = text[..open].Trim();
        if (name.Length == 0 || !name.All(character => char.IsLetterOrDigit(character) || character is '.' or '_'))
        {
            return null;
        }

        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;
        var inSheet = false;
        for (var index = open + 1; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                current.Append(character);
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inString = false;
                    }
                }

                continue;
            }

            if (inSheet)
            {
                inSheet = character != '\'';
                current.Append(character);
                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inSheet = true;
                    break;
                case '(' or '{' or '[':
                    depth++;
                    break;
                case ')' or '}' or ']':
                    if (depth == 0)
                    {
                        // The closing parenthesis of the call: it must be the last character.
                        if (index != text.Length - 1)
                        {
                            return null;
                        }

                        if (arguments.Count > 0 || current.Length > 0)
                        {
                            arguments.Add(current.ToString().Trim());
                        }

                        return (name, arguments);
                    }

                    depth--;
                    break;
                default:
                    if (depth == 0 && character == locale.ListSeparator)
                    {
                        arguments.Add(current.ToString().Trim());
                        current.Clear();
                        continue;
                    }

                    break;
            }

            current.Append(character);
        }

        return null;
    }

    /// <summary>"NAME(a; b; …)" without "=": trailing empty arguments are left out, empty ones between are kept.</summary>
    public static string Join(string name, IEnumerable<string> arguments, CalcLocale locale)
    {
        var list = arguments.Select(argument => argument.Trim()).ToList();
        while (list.Count > 0 && list[^1].Length == 0)
        {
            list.RemoveAt(list.Count - 1);
        }

        return $"{name}({string.Join(locale.ListSeparator, list)})";
    }
}
