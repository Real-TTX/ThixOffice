using System.Text;

namespace ThixWrite.Core.Formats.Html;

public static partial class HtmlImport
{
    /// <summary>
    /// The &lt;style&gt; blocks of a mail or a pasted page as rules for its elements: simple selectors only (tag, .class, #id, tag.class, * and
    /// descendants of them, groups with commas); @media, @font-face and other at-rules, pseudo classes, attribute and child selectors are left out
    /// (newsletters put what only phones should see into @media). The matched declarations go in front of an element's own style attribute in the
    /// order of specificity, so the import's inline mapping reads them; !important ones go after it. Text only – nothing is fetched.
    /// </summary>
    private static class HtmlCss
    {
        private const int MaximumRules = 4000;

        internal sealed record Compound(string? Tag, string? Id, string[] Classes)
        {
            public bool Matches(Node node)
            {
                if (Tag is not null && !string.Equals(Tag, node.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (Id is not null && !string.Equals(node["id"], Id, StringComparison.Ordinal))
                {
                    return false;
                }

                if (Classes.Length == 0)
                {
                    return true;
                }

                var own = (node["class"] ?? "").Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                return Classes.All(name => own.Contains(name, StringComparer.OrdinalIgnoreCase));
            }
        }

        internal sealed record Rule(Compound[] Selector, int Specificity, int Order, string Declarations, string Important);

        public static List<Rule> ParseSheets(IEnumerable<string> sheets)
        {
            var rules = new List<Rule>();
            foreach (var sheet in sheets)
            {
                var text = StripComments(sheet);
                var index = 0;
                while (index < text.Length && rules.Count < MaximumRules)
                {
                    var open = text.IndexOf('{', index);
                    if (open < 0)
                    {
                        break;
                    }

                    var prelude = text[index..open].Trim();
                    var close = MatchingBrace(text, open);
                    var body = text[(open + 1)..Math.Max(open + 1, close)];
                    index = close < 0 ? text.Length : close + 1;

                    // an at-rule before it (@charset "…"; @import …;) ends with a semicolon: only what follows is the selector
                    var semicolon = prelude.LastIndexOf(';');
                    if (semicolon >= 0)
                    {
                        prelude = prelude[(semicolon + 1)..].Trim();
                    }

                    if (prelude.StartsWith('@'))
                    {
                        continue; // @media, @font-face, @supports … with everything inside
                    }

                    var (declarations, important) = SplitImportant(body);
                    if (declarations.Length == 0 && important.Length == 0)
                    {
                        continue;
                    }

                    foreach (var selectorText in prelude.Split(','))
                    {
                        if (ParseSelector(selectorText) is { } selector)
                        {
                            rules.Add(new Rule(selector, Specificity(selector), rules.Count, declarations, important));
                        }
                    }
                }
            }

            return rules;
        }

        /// <summary>Puts the rules matching each element in front of (and their !important declarations after) its style attribute.</summary>
        public static void Apply(Node root, List<Rule> rules)
        {
            if (rules.Count == 0)
            {
                return;
            }

            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                foreach (var child in node.Children)
                {
                    if (!child.IsText)
                    {
                        stack.Push(child);
                    }
                }

                if (node.IsText || node.Tag == "#root")
                {
                    continue;
                }

                List<Rule>? matched = null;
                foreach (var rule in rules)
                {
                    if (Matches(rule.Selector, node))
                    {
                        (matched ??= []).Add(rule);
                    }
                }

                if (matched is null)
                {
                    continue;
                }

                matched.Sort((a, b) => a.Specificity != b.Specificity ? a.Specificity.CompareTo(b.Specificity) : a.Order.CompareTo(b.Order));
                var css = new StringBuilder();
                foreach (var rule in matched)
                {
                    css.Append(rule.Declarations).Append(';');
                }

                css.Append(node["style"] ?? "").Append(';');
                foreach (var rule in matched)
                {
                    css.Append(rule.Important).Append(';');
                }

                node.Attributes["style"] = css.ToString();
            }
        }

        private static bool Matches(Compound[] selector, Node node)
        {
            if (!selector[^1].Matches(node))
            {
                return false;
            }

            var ancestor = node.Parent;
            for (var index = selector.Length - 2; index >= 0; index--)
            {
                while (ancestor is not null && !selector[index].Matches(ancestor))
                {
                    ancestor = ancestor.Parent;
                }

                if (ancestor is null)
                {
                    return false;
                }

                ancestor = ancestor.Parent;
            }

            return true;
        }

        private static Compound[]? ParseSelector(string text)
        {
            var parts = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 6)
            {
                return null;
            }

            var compounds = new Compound[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                if (ParseCompound(parts[index]) is not { } compound)
                {
                    return null;
                }

                compounds[index] = compound;
            }

            return compounds;
        }

        private static Compound? ParseCompound(string text)
        {
            string? tag = null;
            string? id = null;
            var classes = new List<string>();
            var index = 0;
            var start = index;
            while (index < text.Length && IsNameCharacter(text[index]))
            {
                index++;
            }

            if (index > start)
            {
                tag = text[start..index].ToLowerInvariant();
            }
            else if (index < text.Length && text[index] == '*')
            {
                index++;
            }

            while (index < text.Length)
            {
                var kind = text[index];
                if (kind is not ('.' or '#'))
                {
                    return null; // :hover, [attr], >, + … are not read
                }

                start = ++index;
                while (index < text.Length && IsNameCharacter(text[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    return null;
                }

                var name = text[start..index];
                if (kind == '.')
                {
                    classes.Add(name);
                }
                else
                {
                    id = name;
                }
            }

            return new Compound(tag, id, [.. classes]);
        }

        private static bool IsNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

        private static int Specificity(Compound[] selector) =>
            selector.Sum(compound => (compound.Id is null ? 0 : 100) + (compound.Classes.Length * 10) + (compound.Tag is null ? 0 : 1));

        private static (string Declarations, string Important) SplitImportant(string body)
        {
            var normal = new List<string>();
            var important = new List<string>();
            foreach (var declaration in body.Split(';'))
            {
                var text = declaration.Trim();
                if (text.Length == 0 || !text.Contains(':', StringComparison.Ordinal) || text.Contains("url(", StringComparison.OrdinalIgnoreCase) || text.Contains("expression(", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // nothing that could load or compute something
                }

                var at = text.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    important.Add(text[..at].Trim());
                }
                else
                {
                    normal.Add(text);
                }
            }

            return (string.Join(';', normal), string.Join(';', important));
        }

        private static string StripComments(string sheet)
        {
            var text = sheet.Replace("<!--", " ", StringComparison.Ordinal).Replace("-->", " ", StringComparison.Ordinal);
            var result = new StringBuilder(text.Length);
            var index = 0;
            while (index < text.Length)
            {
                var start = text.IndexOf("/*", index, StringComparison.Ordinal);
                if (start < 0)
                {
                    result.Append(text, index, text.Length - index);
                    break;
                }

                result.Append(text, index, start - index);
                var end = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 2;
            }

            return result.ToString();
        }

        private static int MatchingBrace(string text, int open)
        {
            var depth = 0;
            for (var index = open; index < text.Length; index++)
            {
                if (text[index] == '{')
                {
                    depth++;
                }
                else if (text[index] == '}' && --depth == 0)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
