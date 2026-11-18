namespace ThixOffice.Core.Commands;

/// <summary>
/// One thing the user can do in a document window ("Speichern", "Rückgängig", later "Fett"), UI-free: the search box of
/// the title bar, the ribbon, the quick-access buttons and the keyboard all run the same command. The ribbon (MatRibbon,
/// MatDesktop 0.3.0) and the web apps bind to it by <see cref="Id"/>; nothing here knows Avalonia.
/// </summary>
public sealed class OfficeCommand
{
    private readonly Func<Task> _execute;

    /// <summary>A command that runs <paramref name="execute"/>.</summary>
    public OfficeCommand(string id, string label, Action execute)
        : this(id, label, () =>
        {
            execute();
            return Task.CompletedTask;
        })
    {
    }

    /// <summary>A command that runs <paramref name="execute"/> (a dialog, a file: asynchronous).</summary>
    public OfficeCommand(string id, string label, Func<Task> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Label = label;
        _execute = execute;
    }

    /// <summary>The stable name: <c>file.save</c>, <c>edit.undo</c>, <c>write.bold</c>.</summary>
    public string Id { get; }

    /// <summary>The German label ("Speichern").</summary>
    public string Label { get; }

    /// <summary>The name of the icon in MatDesktop's sprite ("save"), or null.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// The shortcut in neutral form: <c>Ctrl+S</c>, <c>Ctrl+Shift+S</c>, <c>F12</c>. "Ctrl" is the command key of the
    /// system (⌘ on macOS); the desktop names it "Strg+S" / "⌘S" in tooltips and binds it as a key gesture.
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>A sentence for the search result and the tooltip ("Speichert das Dokument unter seinem Namen.").</summary>
    public string? Description { get; init; }

    /// <summary>Other words the search finds it by ("sichern", "ablegen").</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Whether it can run now (null: always).</summary>
    public Func<bool>? CanExecute { get; init; }

    /// <summary>For an on/off command (bold, ruler): whether it is on now; null for a plain command.</summary>
    public Func<bool>? IsChecked { get; init; }

    /// <summary>False when the search should not offer it (the backstage toggle itself, internal steps).</summary>
    public bool IsSearchable { get; init; } = true;

    public bool IsEnabled => CanExecute?.Invoke() ?? true;

    public bool IsCheckable => IsChecked is not null;

    /// <summary>On or off (false for a plain command).</summary>
    public bool Checked => IsChecked?.Invoke() ?? false;

    /// <summary>Runs the command when it is enabled; false when it was not.</summary>
    public async Task<bool> ExecuteAsync()
    {
        if (!IsEnabled)
        {
            return false;
        }

        await _execute();
        return true;
    }

    public override string ToString() => $"{Id} ({Label})";
}
