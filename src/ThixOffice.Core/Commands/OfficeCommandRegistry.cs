using System.Globalization;
using System.Text;

namespace ThixOffice.Core.Commands;

/// <summary>
/// The commands of one document window. The shell registers the common ones (<see cref="CommonCommandIds"/>), the app adds
/// its own; the search box, the ribbon, the quick-access buttons and the keyboard read them from here. After something
/// changed that may enable or check a command (typing, saving, undo), <see cref="Invalidate"/> tells every view to look again.
/// </summary>
public sealed class OfficeCommandRegistry
{
    private readonly List<OfficeCommand> _commands = [];
    private readonly Dictionary<string, OfficeCommand> _byId = new(StringComparer.Ordinal);

    /// <summary>Raised by <see cref="Invalidate"/> and when a command is added: views refresh enabled and checked states.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Every command in the order it was added.</summary>
    public IReadOnlyList<OfficeCommand> All => _commands;

    /// <summary>Adds a command; an id may exist once.</summary>
    public OfficeCommand Add(OfficeCommand command)
    {
        if (!_byId.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"The command {command.Id} exists already.");
        }

        _commands.Add(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return command;
    }

    /// <summary>The command with this id, or null.</summary>
    public OfficeCommand? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The command with this id; throws when there is none.</summary>
    public OfficeCommand this[string id] => Find(id) ?? throw new KeyNotFoundException($"No command {id} is registered.");

    /// <summary>Runs the command when it exists and is enabled.</summary>
    public Task<bool> ExecuteAsync(string id) => Find(id) is { } command ? command.ExecuteAsync() : Task.FromResult(false);

    /// <summary>Sets a value through the <see cref="OfficeValueCommand"/> with this id when it exists and is enabled.</summary>
    public Task<bool> ApplyAsync(string id, string? value) => Find(id) is OfficeValueCommand command ? command.ApplyAsync(value) : Task.FromResult(false);

    /// <summary>Enabled and checked states may have changed.</summary>
    public void Invalidate() => StateChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// "Was möchten Sie tun?": the searchable commands that match every word of <paramref name="query"/> in their label,
    /// keywords or description, best first (label start, word start, anywhere in the label, keyword, description).
    /// Umlauts and case do not matter ("schriftgroesse" finds "Schriftgröße").
    /// </summary>
    public IReadOnlyList<OfficeCommand> Search(string query, int maximum = 8)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        return _commands
            .Where(command => command.IsSearchable)
            .Select((command, index) => (command, index, score: Score(command, words) is var score && score < int.MaxValue ? score + command.SearchPenalty : score))
            .Where(match => match.score < int.MaxValue)
            .OrderBy(match => match.score)
            .ThenBy(match => match.index)
            .Take(maximum)
            .Select(match => match.command)
            .ToList();
    }

    private static int Score(OfficeCommand command, string[] words)
    {
        var label = Normalize(command.Label);
        var keywords = string.Join(' ', command.Keywords.Select(Normalize));
        var description = Normalize(command.Description ?? "");
        var total = 0;
        foreach (var word in words)
        {
            int score;
            if (label.StartsWith(word, StringComparison.Ordinal))
            {
                score = 0;
            }
            else if (label.Contains(' ' + word, StringComparison.Ordinal))
            {
                score = 1;
            }
            else if (label.Contains(word, StringComparison.Ordinal))
            {
                score = 2;
            }
            else if (keywords.Contains(word, StringComparison.Ordinal))
            {
                score = 3;
            }
            else if (description.Contains(word, StringComparison.Ordinal))
            {
                score = 4;
            }
            else
            {
                return int.MaxValue;
            }

            total += score;
        }

        return total;
    }

    /// <summary>Lower case, umlauts as their two letters (ä = ae), ß = ss, other accents dropped, punctuation as spaces.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length + 4);
        foreach (var character in text.ToLowerInvariant())
        {
            switch (character)
            {
                case 'ä':
                    builder.Append("ae");
                    break;
                case 'ö':
                    builder.Append("oe");
                    break;
                case 'ü':
                    builder.Append("ue");
                    break;
                case 'ß':
                    builder.Append("ss");
                    break;
                default:
                    var plain = character.ToString().Normalize(NormalizationForm.FormD)[0];
                    builder.Append(char.IsLetterOrDigit(plain) ? plain : ' ');
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>The ids of the commands every document window has (the shell registers them; an app never reuses them).</summary>
public static class CommonCommandIds
{
    public const string New = "file.new";
    public const string Open = "file.open";
    public const string Save = "file.save";
    public const string SaveAs = "file.saveAs";
    public const string SaveAsPdf = "file.saveAsPdf";
    public const string Close = "file.close";
    public const string Backstage = "file.backstage";
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string ToggleRibbon = "view.toggleRibbon";
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string Zoom100 = "view.zoom100";
    public const string Settings = "app.settings";
    public const string About = "app.about";
    public const string CheckForUpdates = "app.checkForUpdates";
}
