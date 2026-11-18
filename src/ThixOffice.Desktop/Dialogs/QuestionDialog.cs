using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop.Dialogs;

/// <summary>A button of a <see cref="QuestionDialog"/>: its words, icon, look and role.</summary>
public sealed record DialogChoice(string Text, string Icon, string? Theme = null, bool IsDefault = false, bool IsCancel = false, bool IsSeparated = false);

/// <summary>
/// A short question with its answers as buttons in one row below, left-aligned, from positive to negative (a destructive
/// answer separated by a gap). Escape is the cancel answer, Enter the default one. Used for "Änderungen speichern?",
/// confirmations and messages; <see cref="AskAsync"/> returns the index of the answer (-1: closed without one).
/// </summary>
public sealed class QuestionDialog : MatWindow
{
    private readonly List<Button> _buttons = [];
    private int _answer = -1;

    private QuestionDialog(string title, string message, IReadOnlyList<DialogChoice> choices)
    {
        Title = title;
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Use(BackgroundProperty, "Mat.Surface");

        var actions = new ActionBar();
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var answer = index;
            var button = new Button
            {
                Content = new IconLabel { Icon = choice.Icon, Text = choice.Text },
                IsDefault = choice.IsDefault,
                IsCancel = choice.IsCancel,
            };
            if (choice.Theme is not null)
            {
                button.Themed(choice.Theme);
            }

            ActionBar.SetSeparated(button, choice.IsSeparated);
            button.Click += (_, _) =>
            {
                _answer = answer;
                Close();
            };
            _buttons.Add(button);
            actions.Children.Add(button);
        }

        var heading = new TextBlock { Text = title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) }
            .Use(SelectableTextBlock.ForegroundProperty, "Mat.TextMuted");
        Content = new StackPanel { Margin = new Thickness(24, 22), Children = { heading, text, actions } };
        Opened += (_, _) => (_buttons.FirstOrDefault(button => button.IsDefault) ?? _buttons.FirstOrDefault())?.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _answer = choices.ToList().FindIndex(choice => choice.IsCancel);
                Close();
            }
        };
    }

    /// <summary>The buttons in their order (tests click them).</summary>
    public IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>The words of the question.</summary>
    public string Message => ((SelectableTextBlock)((StackPanel)Content!).Children[1]).Text ?? "";

    /// <summary>Asks; returns the index of the answer, -1 when the dialog was closed without one.</summary>
    public static async Task<int> AskAsync(Window owner, string title, string message, params DialogChoice[] choices)
    {
        var dialog = new QuestionDialog(title, message, choices);
        if (owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
        }

        return dialog._answer;
    }

    /// <summary>A message with only "OK" (an error of the business layer, a hint).</summary>
    public static Task ShowMessageAsync(Window owner, string title, string message) =>
        AskAsync(owner, title, message, new DialogChoice("OK", "check", "MatButton.Primary", IsDefault: true, IsCancel: true));

    /// <summary>A yes/no question; true when confirmed. A destructive one gets the danger look, and Cancel is the default.</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, string confirmText, bool isDestructive = false) =>
        await AskAsync(owner, title, message,
            new DialogChoice(confirmText, isDestructive ? "trash" : "check", isDestructive ? "MatButton.Danger" : "MatButton.Primary", IsDefault: !isDestructive),
            new DialogChoice("Abbrechen", "x", IsDefault: isDestructive, IsCancel: true)) == 0;
}

/// <summary>The answers of "Änderungen speichern?".</summary>
public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// "Möchten Sie die Änderungen an „Dokument1“ speichern?" – the answers of Word, in the order of the house rules: Speichern,
/// Abbrechen, and – set apart, in the danger look, because it throws the changes away – Nicht speichern.
/// </summary>
public static class SaveChangesDialog
{
    public const string Title = "Änderungen speichern?";

    public static string MessageFor(string documentTitle) =>
        $"Möchten Sie die Änderungen an „{documentTitle}“ speichern? Ohne Speichern gehen sie verloren.";

    public static async Task<SaveChoice> AskAsync(Window owner, string documentTitle) =>
        await QuestionDialog.AskAsync(owner, Title, MessageFor(documentTitle),
            new DialogChoice("Speichern", "save", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "back", IsCancel: true),
            new DialogChoice("Nicht speichern", "trash", "MatButton.Danger", IsSeparated: true)) switch
        {
            0 => SaveChoice.Save,
            2 => SaveChoice.DontSave,
            _ => SaveChoice.Cancel,
        };
}
