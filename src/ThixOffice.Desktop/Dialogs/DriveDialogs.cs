using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop.Dialogs;

/// <summary>The answers when a ThixDrive file was saved by somebody else in between.</summary>
public enum DriveConflictChoice
{
    /// <summary>The document becomes a new file next to the original ("Bericht (2).docx"); nothing is replaced.</summary>
    SaveCopy,

    /// <summary>Decide later: the document stays saved on this computer, marked "Konflikt".</summary>
    Later,

    /// <summary>The document becomes the current version; the one saved in between stays an older version of the file.</summary>
    Overwrite,
}

/// <summary>The answers when a window with a document that is not in ThixDrive yet closes.</summary>
public enum PendingUploadChoice
{
    Retry,
    Cancel,
    CloseAnyway,
}

/// <summary>
/// The questions of ThixDrive, by the house rule for buttons (one row, positive → negative, the destructive one set apart):
///  - "Datei wurde inzwischen geändert": Als Kopie speichern (the safe default: both versions stay, nobody's work is replaced) ·
///    Später · gap · Überschreiben (danger: the other version stops being the current one – it stays in the file's versions);
///  - "Name schon vergeben": Anderen Namen wählen · gap · Ersetzen (danger);
///  - "Noch nicht hochgeladen" when closing: Erneut versuchen · Abbrechen · gap · Trotzdem schließen (the changes stay on this computer
///    and are uploaded when the document is opened again).
/// </summary>
public static class DriveDialogs
{
    public const string ConflictTitle = "Datei wurde inzwischen geändert";

    public static async Task<DriveConflictChoice> AskConflictAsync(Window owner, string fileName, string message) =>
        await QuestionDialog.AskAsync(owner, ConflictTitle,
            $"{message} Ihre Fassung ist auf diesem Computer gespeichert. Wie möchten Sie „{fileName}“ in ThixDrive speichern?",
            new DialogChoice("Als Kopie speichern", "copy", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Später", "clock", IsCancel: true),
            new DialogChoice("Überschreiben", "upload", "MatButton.Danger", IsSeparated: true)) switch
        {
            0 => DriveConflictChoice.SaveCopy,
            2 => DriveConflictChoice.Overwrite,
            _ => DriveConflictChoice.Later,
        };

    /// <summary>"Speichern unter" met a file of the same name: true = replace it.</summary>
    public static async Task<bool> AskReplaceAsync(Window owner, string fileName) =>
        await QuestionDialog.AskAsync(owner, "Name schon vergeben",
            $"In diesem Ordner von ThixDrive gibt es schon „{fileName}“. Möchten Sie die Datei ersetzen? Die bisherige Fassung bleibt als ältere Version erhalten.",
            new DialogChoice("Anderen Namen wählen", "edit", "MatButton.Primary", IsDefault: true, IsCancel: true),
            new DialogChoice("Ersetzen", "upload", "MatButton.Danger", IsSeparated: true)) == 1;

    public static async Task<PendingUploadChoice> AskPendingUploadAsync(Window owner, string fileName, string message) =>
        await QuestionDialog.AskAsync(owner, "Noch nicht in ThixDrive",
            $"„{fileName}“ ist auf diesem Computer gespeichert, aber noch nicht in ThixDrive hochgeladen. {message}".TrimEnd() +
            " Wenn Sie trotzdem schließen, wird es hochgeladen, sobald Sie es wieder öffnen.",
            new DialogChoice("Erneut versuchen", "upload", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "back", IsCancel: true),
            new DialogChoice("Trotzdem schließen", "x", "MatButton.Danger", IsSeparated: true)) switch
        {
            0 => PendingUploadChoice.Retry,
            2 => PendingUploadChoice.CloseAnyway,
            _ => PendingUploadChoice.Cancel,
        };
}

/// <summary>
/// One line of text to enter (Neuer Ordner, Umbenennen): a field with its label, then OK and Abbrechen in one row below. Returns the
/// text, or null when cancelled. The caller checks it in the business layer and asks again with the error at the field.
/// </summary>
public sealed class TextInputDialog : MatWindow
{
    private readonly TextBox _box = new();
    private readonly MatFormField _field;
    private string? _answer;

    private TextInputDialog(string title, string label, string text, string confirmText, string? error)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Use(BackgroundProperty, "Mat.Surface");

        _box.Text = text;
        Avalonia.Automation.AutomationProperties.SetName(_box, label);
        _field = new MatFormField { Label = label, Content = _box, Error = error };
        var ok = new Button { Content = new IconLabel { Icon = "check", Text = confirmText }, IsDefault = true }.Themed("MatButton.Primary");
        var cancel = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" }, IsCancel = true };
        ok.Click += (_, _) =>
        {
            _answer = _box.Text ?? "";
            Close();
        };
        cancel.Click += (_, _) => Close();
        var heading = new TextBlock { Text = title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        Content = new StackPanel { Margin = new Thickness(24, 22), Children = { heading, _field, new ActionBar { Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel } } } };
        Opened += (_, _) =>
        {
            _box.Focus();
            var stem = Path.GetFileNameWithoutExtension(text);
            _box.SelectionStart = 0;
            _box.SelectionEnd = stem.Length > 0 ? stem.Length : text.Length;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>The field (tests type into it).</summary>
    public TextBox Box => _box;

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string label, string text, string confirmText = "OK", string? error = null)
    {
        var dialog = new TextInputDialog(title, label, text, confirmText, error);
        await dialog.ShowDialog(owner);
        return dialog._answer;
    }
}
