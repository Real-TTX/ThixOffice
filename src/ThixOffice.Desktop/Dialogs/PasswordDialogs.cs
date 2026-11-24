using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop.Dialogs;

/// <summary>
/// "Kennwort": the password of a protected document, asked before it opens (or comes back from its recovery file). OK runs
/// <c>attempt</c> with the typed password – the open itself; a wrong password shows at the field and the dialog stays for another try,
/// any other refusal closes it (<see cref="Failure"/>). Abbrechen opens nothing. The recovery adds "Verwerfen" (<see cref="IsDiscarded"/>).
/// </summary>
public sealed class DocumentPasswordDialog : OfficeDialog
{
    private readonly Func<string, Task> _attempt;
    private readonly MatFormField _field;
    private bool _busy;

    private DocumentPasswordDialog(string message, Func<string, Task> attempt, bool offersDiscard)
        : base("Kennwort", 460)
    {
        _attempt = attempt;
        Message = message;
        PasswordBox = new TextBox { PasswordChar = '•', MaxLength = OfficeEncryption.MaxPasswordLength };
        AutomationProperties.SetName(PasswordBox, "Kennwort");
        _field = Field("Kennwort", PasswordBox);
        FormErrors.SetField(_field, OfficeEncryption.PasswordField);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel { Spacing = 14, Children = { text, _field } });
        if (offersDiscard)
        {
            DiscardButton = new Button { Content = new IconLabel { Icon = "trash", Text = "Verwerfen" } }.Themed("MatButton.Danger");
            DiscardButton.Click += (_, _) =>
            {
                IsDiscarded = true;
                Close();
            };
            AddAction(DiscardButton, separated: true);
        }

        Opened += (_, _) => PasswordBox.Focus();
    }

    /// <summary>"„Bericht.docx“ ist geschützt. …" – the words above the field.</summary>
    public string Message { get; }

    public TextBox PasswordBox { get; }

    /// <summary>"Verwerfen" of the recovery (null when the dialog has none).</summary>
    public Button? DiscardButton { get; }

    /// <summary>True when the person chose "Verwerfen".</summary>
    public bool IsDiscarded { get; private set; }

    /// <summary>The refusal that ended the dialog (a damaged file …), shown by the caller; null after OK or Abbrechen.</summary>
    public BusinessException? Failure { get; private set; }

    /// <summary>The error shown at the field (tests).</summary>
    public string? Error => _field.Error;

    /// <summary>The dialog before a protected file opens.</summary>
    public static DocumentPasswordDialog ForOpening(string fileName, Func<string, Task> attempt) =>
        new($"„{fileName}“ ist geschützt. Geben Sie das Kennwort ein, um die Datei zu öffnen.", attempt, offersDiscard: false);

    /// <summary>The dialog before a protected document comes back from its recovery file (with "Verwerfen").</summary>
    public static DocumentPasswordDialog ForRecovery(string title, Func<string, Task> attempt) =>
        new($"„{title}“ wird wiederhergestellt. Geben Sie das Kennwort des Dokuments ein.", attempt, offersDiscard: true);

    /// <summary>OK starts the attempt; the dialog closes itself once it succeeded.</summary>
    protected override bool Apply()
    {
        if (!_busy)
        {
            _ = TryAsync();
        }

        return false;
    }

    /// <summary>Tries the typed password (what OK does; tests await it).</summary>
    public async Task TryAsync()
    {
        _busy = true;
        OkButton.IsEnabled = false;
        _field.Error = null;
        try
        {
            await _attempt(PasswordBox.Text ?? "");
            CloseConfirmed();
        }
        catch (PasswordRequiredException)
        {
            ShowAtField("Bitte geben Sie das Kennwort ein.");
        }
        catch (BusinessException exception) when (exception.Field == OfficeEncryption.PasswordField)
        {
            ShowAtField(exception.Message);
        }
        catch (BusinessException exception)
        {
            Failure = exception;
            Close();
        }
        finally
        {
            _busy = false;
            OkButton.IsEnabled = true;
        }
    }

    private void ShowAtField(string message)
    {
        _field.Error = message;
        PasswordBox.SelectAll();
        PasswordBox.Focus();
    }
}

/// <summary>
/// "Dokument verschlüsseln" (Word's Datei › Informationen › Dokument schützen › Mit Kennwort verschlüsseln): the password twice and Word's
/// warning. The business layer checks it (<see cref="DocumentService.SetPassword"/>); a protected document loses its protection when
/// both fields are left empty. The password applies from the next save on.
/// </summary>
public sealed class EncryptDocumentDialog : OfficeDialog
{
    public const string Warning = "Achtung: Wenn Sie das Kennwort verlieren oder vergessen, kann das Dokument nicht wiederhergestellt werden. "
        + "Bewahren Sie Kennwörter an einem sicheren Ort auf. (Beachten Sie, dass bei Kennwörtern zwischen Groß- und Kleinschreibung unterschieden wird.)";

    private readonly OfficeDocument _document;

    public EncryptDocumentDialog(OfficeDocument document)
        : base("Dokument verschlüsseln", 480)
    {
        _document = document;
        PasswordBox = new TextBox { PasswordChar = '•', MaxLength = OfficeEncryption.MaxPasswordLength };
        ConfirmationBox = new TextBox { PasswordChar = '•', MaxLength = OfficeEncryption.MaxPasswordLength };
        AutomationProperties.SetName(PasswordBox, "Kennwort");
        AutomationProperties.SetName(ConfirmationBox, "Kennwort bestätigen");
        var password = Field("Kennwort", PasswordBox);
        var confirmation = Field("Kennwort bestätigen", ConfirmationBox);
        FormErrors.SetField(password, OfficeEncryption.PasswordField);
        FormErrors.SetField(confirmation, OfficeEncryption.ConfirmationField);
        var intro = new TextBlock
        {
            Text = document.HasPassword
                ? "Das Dokument ist mit einem Kennwort geschützt. Geben Sie ein neues Kennwort ein, oder lassen Sie beide Felder leer, um den Schutz zu entfernen."
                : "Verschlüsseln Sie den Inhalt dieser Datei. Ohne das Kennwort lässt sie sich nicht öffnen.",
            TextWrapping = TextWrapping.Wrap,
        };
        var warning = new TextBlock { Text = Warning, TextWrapping = TextWrapping.Wrap, FontSize = MatTokens.Type.FsSm, Margin = new Thickness(0, 4, 0, 0) }
            .Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel { Spacing = 12, Children = { intro, password, confirmation, warning } });
        Opened += (_, _) => PasswordBox.Focus();
    }

    public TextBox PasswordBox { get; }

    public TextBox ConfirmationBox { get; }

    protected override bool Apply()
    {
        try
        {
            DocumentService.SetPassword(_document, PasswordBox.Text, ConfirmationBox.Text);
            return true;
        }
        catch (BusinessException exception)
        {
            FormErrors.ShowError(this, exception.Field, exception.Message);
            return false;
        }
    }
}
