namespace ThixOffice.Pim.Desktop.People;

/// <summary>
/// The colours of categories (Outlook gives each category a colour): from MatDesktop's tokens only, chosen by the name (the same category has the same
/// colour on every computer and in light and dark – the tokens follow the theme).
/// </summary>
public static class CategoryColors
{
    /// <summary>The tokens a category can get.</summary>
    public static readonly IReadOnlyList<string> Tokens = ["Mat.Accent", "Mat.Danger", "Mat.Warning", "Mat.SuccessText", "Mat.AccentText", "Mat.DangerText", "Mat.WarningText", "Mat.TextMuted"];

    /// <summary>The token of a category (stable: a hash of its name in small letters).</summary>
    public static string TokenOf(string category)
    {
        var hash = 17;
        foreach (var c in category.Trim().ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        return Tokens[(int)((uint)hash % (uint)Tokens.Count)];
    }
}