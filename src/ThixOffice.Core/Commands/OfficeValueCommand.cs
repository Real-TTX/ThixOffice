namespace ThixOffice.Core.Commands;

/// <summary>What kind of value an <see cref="OfficeValueCommand"/> sets (how a ribbon draws it, how the search offers it).</summary>
public enum OfficeValueKind
{
    /// <summary>A text out of a list or typed in: Schriftart, Schriftgrad (a combo box).</summary>
    Text = 0,

    /// <summary>A number with a unit: Abstand vor, Einzug (a number field); the value is written invariantly ("6", "1.5").</summary>
    Number = 1,

    /// <summary>A colour of the document, <c>#rrggbb</c>, or null for "Automatisch": Schriftfarbe, Hervorhebung (a colour picker).</summary>
    Color = 2,

    /// <summary>The id of one entry of a list: Formatvorlagen (a gallery).</summary>
    Choice = 3,
}

/// <summary>
/// A command that sets a value (UI-free, like <see cref="OfficeCommand"/>): the font, the size, the colour, the style of the
/// selection. <see cref="Value"/> is what applies now (the ribbon shows it; null: none, e.g. a selection of several fonts),
/// <see cref="ApplyAsync"/> sets one. Running it as a plain command (a key, the colour button itself) applies the value it
/// has now – a colour picker's "this colour again". The search finds it like any command; the desktop then puts the
/// keyboard into its field in the ribbon.
/// </summary>
public class OfficeValueCommand : OfficeCommand
{
    private readonly Func<string?> _value;
    private readonly Func<string?, Task> _apply;

    /// <summary>A value command that reads its value with <paramref name="value"/> and sets one with <paramref name="apply"/>.</summary>
    public OfficeValueCommand(string id, string label, OfficeValueKind kind, Func<string?> value, Action<string?> apply)
        : this(id, label, kind, value, text =>
        {
            apply(text);
            return Task.CompletedTask;
        })
    {
    }

    /// <summary>A value command whose setting is asynchronous.</summary>
    public OfficeValueCommand(string id, string label, OfficeValueKind kind, Func<string?> value, Func<string?, Task> apply)
        : base(id, label, () => apply(value()))
    {
        Kind = kind;
        _value = value;
        _apply = apply;
    }

    public OfficeValueKind Kind { get; }

    /// <summary>The value that applies now, or null.</summary>
    public string? Value => _value();

    /// <summary>The values to choose from (fonts, sizes, style ids), when the command has a fixed list; empty otherwise.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Sets <paramref name="value"/> when the command is enabled; false when it was not.</summary>
    public async Task<bool> ApplyAsync(string? value)
    {
        if (!IsEnabled)
        {
            return false;
        }

        await _apply(value);
        return true;
    }
}
