using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Html;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixMail.Compose;

/// <summary>
/// Writing a message, in a window of its own like Outlook's: a small ribbon (Nachricht | Einfügen | Text formatieren) bound to the window's own commands,
/// Von (the account), An / Cc / Bcc with the shared address field (suggestions from recent recipients and the account's contacts), Betreff, the
/// attachments (picker, drag and drop, a warning above 10 MB), and the body in ThixWrite's text surface (bold, italic, underline, lists, links,
/// pictures, colours, fonts). Senden = Strg+Eingabe (⌘↩ on the Mac); the draft is saved into Entwürfe every minute and when the window closes with
/// "Speichern". The body becomes HTML (ThixWrite.Core's HtmlExport, pictures by cid:) with a plain-text alternative.
/// </summary>
public sealed class ComposeWindow : MatWindow
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static DocumentFragment? _clipboardFragment;
    private static string? _clipboardText;

    private readonly MailDocument _document;
    private readonly Action<Draft>? _sent;
    private readonly List<DraftAttachment> _attachments = [];
    private readonly List<(KeyGesture Gesture, OfficeCommand Command)> _shortcuts = [];
    private readonly WrapPanel _attachmentStrip = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border _paper = new() { Padding = new Thickness(18, 14), Margin = new Thickness(16, 8, 16, 16) };
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly InfoBar _message = new() { Margin = new Thickness(16, 8, 16, 0) };
    private Draft _draft;
    private bool _dirty;
    private bool _closeConfirmed;
    private bool _closed;
    private long _savedVersion;

    public ComposeWindow(MailDocument document, Draft draft, IImage? logo, Action<Draft>? sent = null)
    {
        _document = document;
        _draft = draft;
        _sent = sent;
        _attachments.AddRange(draft.Attachments);
        Width = 900;
        Height = 760;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseOwnTitleBar();

        Surface = CreateSurface(draft);
        Spelling = new SpellingSupport(() => Surface.Session, () => Surface.Layout, Surface.InvalidateVisual);
        Surface.Misspelled = pages => Spelling.Misspelled(pages);
        Commands = new OfficeCommandRegistry();
        AddCommands();
        foreach (var command in Commands.All)
        {
            if (Ui.Gesture(command.CurrentShortcut) is { } gesture)
            {
                _shortcuts.Add((gesture, command));
            }
        }

        Ribbon = new MatRibbon { HandlesCollapseKey = false };
        Ribbon.FileRequested += (_, _) =>
        {
            // the compose window has no backstage: "Datei" keeps the draft
            if (SaveDraft())
            {
                _message.Show("Der Entwurf wurde in „Entwürfe“ gespeichert.", InfoSeverity.Success, isClosable: true);
            }
        };
        RibbonDefinition = CreateRibbon();
        RibbonBinding = OfficeRibbonBinding.Bind(RibbonDefinition, Commands, Ribbon);

        TitleBar = new MatTitleBar { AppName = "ThixMail", Logo = logo, WordMark = Ui.WordMark("Mail"), BrandWidth = 196 };
        var titleText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        TitleBar.Center = titleText;

        // the header: Senden, Von, An, Cc, Bcc, Betreff, attachments
        SendButton = new Button { Content = new StackPanel { Spacing = 4, Children = { new MatIcon { Icon = "arrow-right", Size = 22, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "Senden", HorizontalAlignment = HorizontalAlignment.Center } } }, Padding = new Thickness(14, 10), VerticalAlignment = VerticalAlignment.Stretch }.Themed("MatButton.Primary");
        AutomationProperties.SetName(SendButton, "Senden");
        ToolTip.SetTip(SendButton, Ui.Tooltip("Senden", OperatingSystem.IsMacOS() ? "Ctrl+Enter" : "Ctrl+Enter"));
        SendButton.Click += async (_, _) => await SendAsync();

        FromChoice = new ComboBox { ItemsSource = document.Accounts.Accounts.Select(a => $"{a.Sender.Full}").ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(FromChoice, "Von");
        FromChoice.SelectedIndex = Math.Max(0, document.Accounts.Accounts.ToList().FindIndex(a => a.Id == draft.AccountId));
        FromChoice.SelectionChanged += (_, _) => _dirty = true;
        To = Address("An", draft.To);
        Cc = Address("Cc", draft.Cc);
        Bcc = Address("Bcc", draft.Bcc);
        SubjectBox = new TextBox { Text = draft.Subject };
        AutomationProperties.SetName(SubjectBox, "Betreff");
        SubjectBox.TextChanged += (_, _) =>
        {
            _dirty = true;
            titleText.Text = SubjectText.Length > 0 ? SubjectText : "Unbenannt – Nachricht";
            Title = $"{titleText.Text} – Nachricht";
        };
        titleText.Text = draft.Subject.Length > 0 ? draft.Subject : "Unbenannt – Nachricht";
        Title = $"{titleText.Text} – Nachricht";
        var bccRow = Row("Bcc", Bcc);
        bccRow.IsVisible = draft.Bcc.Count > 0;
        BccToggle = new Button { Content = "Bcc", Padding = new Thickness(8, 2), MinHeight = 0, VerticalAlignment = VerticalAlignment.Center }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(BccToggle, "Bcc anzeigen");
        BccToggle.Click += (_, _) => bccRow.IsVisible = !bccRow.IsVisible;

        var fields = new StackPanel { Spacing = 6, Children = { Row("Von", FromChoice), Row("An", To, BccToggle), Row("Cc", Cc), bccRow, Row("Betreff", SubjectBox), _attachmentStrip } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16, 12, 16, 0) };
        SendButton.Margin = new Thickness(0, 0, 14, 0);
        SendButton.VerticalAlignment = VerticalAlignment.Top;
        SendButton.MinHeight = 96;
        Grid.SetColumn(fields, 1);
        header.Children.Add(SendButton);
        header.Children.Add(fields);

        // the body (made before the commands: their state reads its formatting)
        _paper.Background = new SolidColorBrush(DocumentPalette.Paper);
        _paper.CornerRadius = new CornerRadius(6);
        _paper.Use(Border.BorderBrushProperty, "Mat.Border");
        _paper.BorderThickness = new Thickness(1);
        _paper.Child = Surface;
        var scroller = new ScrollViewer { Content = _paper, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        scroller.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                FitWidth(e.NewSize.Width);
            }
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
        Grid.SetRow(Ribbon, 1);
        Grid.SetRow(_message, 2);
        Grid.SetRow(header, 3);
        Grid.SetRow(scroller, 4);
        layout.Children.Add(TitleBar);
        layout.Children.Add(Ribbon);
        layout.Children.Add(_message);
        layout.Children.Add(header);
        layout.Children.Add(scroller);
        Content = new Border { Child = layout }.Use(Border.BackgroundProperty, "Mat.Bg");

        RefreshAttachments();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (e.DataTransfer.TryGetFiles() is { Length: > 0 } files)
            {
                e.Handled = true;
                await AttachFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
            }
        });
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => (To.Box.Text?.Length > 0 ? (Control)Surface : To.Box).Focus(), DispatcherPriority.Input);
        _autosave.Tick += (_, _) => AutoSave();
        _autosave.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _autosave.Stop();
            Spelling.Dispose();
        };
        Surface.ContextMenu = BuildContextMenu();
        Surface.Session.ContentChanged += (_, _) => _dirty = true;
        Surface.Session.StateChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!_closed)
            {
                Commands.Invalidate(); // not after the window closed (the account store may be gone with the main window)
            }
        }, DispatcherPriority.Background);
        _savedVersion = Surface.Session.Document.Version;
        _dirty = false;
    }

    public OfficeCommandRegistry Commands { get; }

    public MatRibbon Ribbon { get; }

    public MatRibbonDefinition RibbonDefinition { get; }

    public OfficeRibbonBinding RibbonBinding { get; }

    public MatTitleBar TitleBar { get; }

    public Button SendButton { get; }

    public Button BccToggle { get; }

    public ComboBox FromChoice { get; }

    public AddressField To { get; }

    public AddressField Cc { get; }

    public AddressField Bcc { get; }

    public TextBox SubjectBox { get; }

    internal DocumentSurface Surface { get; }

    /// <summary>Spelling as you type (the system's checker through the shared seam): red wavy lines, suggestions in the context menu.</summary>
    internal SpellingSupport Spelling { get; }

    public InfoBar MessageBar => _message;

    public IReadOnlyList<DraftAttachment> Attachments => _attachments;

    public string SubjectText => (SubjectBox.Text ?? "").Trim();

    public Draft Draft => _draft;

    public bool IsDirty => _dirty;

    private AddressField Address(string name, IReadOnlyList<MailAddress> addresses)
    {
        var field = new AddressField { EntrySuggestions = text => [.. _document.SuggestAddresses(text).Select(a => a.Full)] };
        field.Box.Text = string.Join("; ", addresses.Select(a => a.Full));
        field.Box.Watermark = "";
        AutomationProperties.SetName(field.Box, name);
        field.Box.TextChanged += (_, _) => _dirty = true;
        return field;
    }

    private static Grid Row(string label, Control field, Control? extra = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,Auto") };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Grid.SetColumn(field, 1);
        row.Children.Add(text);
        row.Children.Add(field);
        if (extra is not null)
        {
            Grid.SetColumn(extra, 2);
            extra.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(extra);
        }

        return row;
    }

    // ===== the body =====

    private DocumentSurface CreateSurface(Draft draft)
    {
        var pictures = draft.InlineImages.Where(p => p.ContentId is not null).ToDictionary(p => p.ContentId!.Trim('<', '>'), p => p.Content, StringComparer.OrdinalIgnoreCase);
        ImageData? Resolve(string source) =>
            source.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) && pictures.TryGetValue(source[4..].Trim('<', '>'), out var bytes) ? ImageData.FromBytes(bytes) : null;
        var html = draft.Html.Length > 0 ? draft.Html : "<div><br></div>";
        var fragment = HtmlImport.Import(html, HtmlImportOptions.Mail(Resolve, 600));
        var document = MailHtml.Build(fragment, 600, WriteFonts.Default);
        var session = new EditingSession(document);
        var layout = new DocumentLayout(document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver);
        session.Navigator = layout;
        layout.Update();
        session.SetCaret(new DocumentPosition(0, 0));
        var surface = new DocumentSurface(session, layout) { IsStory = true, MinimumHeight = 220 };
        AutomationProperties.SetName(surface, "Nachrichtentext");
        surface.DropPicture = (path, _) =>
        {
            InsertPicture(path);
            return Task.CompletedTask;
        };
        return surface;
    }

    /// <summary>The body as wide as the window (the document's page follows the width; no undo step).</summary>
    private void FitWidth(double width)
    {
        var points = Math.Max(160, (width - _paper.Margin.Left - _paper.Margin.Right - _paper.Padding.Left - _paper.Padding.Right - 4) / DocumentSurface.PointsToDips);
        var document = Surface.Session.Document;
        if (Math.Abs(document.FinalSection.PageWidth - points) < 2)
        {
            return;
        }

        document.SetFinalSection(document.FinalSection with { PageWidth = points });
        Surface.Layout.Update();
        Surface.InvalidateMeasure();
    }

    private void InsertPicture(string path)
    {
        try
        {
            var image = ImageData.FromBytes(File.ReadAllBytes(path)) ?? throw new BusinessException("Diese Datei ist kein Bild (PNG, JPEG, GIF oder BMP).");
            Surface.Session.InsertImage(image);
        }
        catch (Exception exception) when (exception is BusinessException or IOException or UnauthorizedAccessException)
        {
            _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    // ===== attachments =====

    public async Task AttachFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }

                if (info.Length > MailSession.MaximumMessageSize)
                {
                    throw new BusinessException($"„{info.Name}“ ist größer als 25 MB – das nehmen die meisten Mailserver nicht an.");
                }

                _attachments.Add(new DraftAttachment(info.Name, ContentTypeOf(info.Name), await File.ReadAllBytesAsync(path)));
                _dirty = true;
            }
            catch (Exception exception) when (exception is BusinessException or IOException or UnauthorizedAccessException)
            {
                _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
            }
        }

        RefreshAttachments();
        var total = _attachments.Sum(a => a.Size);
        if (total > MailSession.LargeAttachmentWarning)
        {
            _message.Show($"Die Anlagen sind zusammen {MessageSizeText(total)} groß. Manche Empfänger erhalten so große Nachrichten nicht – erwägen Sie einen Link (z. B. ThixDrive).", InfoSeverity.Warning, isClosable: true);
        }
    }

    private void RefreshAttachments()
    {
        _attachmentStrip.Children.Clear();
        foreach (var attachment in _attachments.ToList())
        {
            var remove = new Button { Content = new MatIcon { Icon = "x", Size = 12 }, Padding = new Thickness(4), MinHeight = 0, Margin = new Thickness(6, 0, 0, 0) }.Themed("MatButton.Ghost");
            AutomationProperties.SetName(remove, $"{attachment.FileName} entfernen");
            remove.Click += (_, _) =>
            {
                _attachments.Remove(attachment);
                _dirty = true;
                RefreshAttachments();
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 4, 3),
                Margin = new Thickness(70, 0, 8, 4),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new IconLabel { Icon = "paperclip", Text = $"{attachment.FileName} ({MessageSizeText(attachment.Size)})" }, remove } },
            };
            chip.Use(Border.BorderBrushProperty, "Mat.Border");
            if (_attachmentStrip.Children.Count > 0)
            {
                chip.Margin = new Thickness(0, 0, 8, 4);
            }

            _attachmentStrip.Children.Add(chip);
        }

        _attachmentStrip.IsVisible = _attachments.Count > 0;
    }

    private static string MessageSizeText(long bytes) => bytes >= 1024 * 1024 ? $"{(bytes / 1024.0 / 1024.0).ToString("0.0", German)} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private static string ContentTypeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".zip" => "application/zip",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".ics" => "text/calendar",
        ".eml" => "message/rfc822",
        _ => "application/octet-stream",
    };

    // ===== the draft =====

    /// <summary>What the window holds now as a draft: the fields, the body as HTML with its pictures by cid: and as text, the attachments.</summary>
    public Draft CurrentDraft()
    {
        var accounts = _document.Accounts.Accounts;
        var account = FromChoice.SelectedIndex >= 0 && FromChoice.SelectedIndex < accounts.Count ? accounts[FromChoice.SelectedIndex] : accounts.FirstOrDefault();
        var pictures = new Dictionary<string, DraftAttachment>(StringComparer.Ordinal);
        var html = HtmlExport.Export(Surface.Session.Document, image =>
        {
            var id = $"{image.Key[..16]}@thixmail";
            pictures.TryAdd(image.Key, new DraftAttachment($"bild-{pictures.Count + 1}{image.Extension}", image.ContentType, image.Bytes, id));
            return "cid:" + id;
        }, WriteFonts.Default.Body);
        return _draft with
        {
            AccountId = account?.Id ?? "",
            From = account?.Sender ?? _draft.From,
            To = Parse(To),
            Cc = Parse(Cc),
            Bcc = Parse(Bcc),
            Subject = SubjectText,
            Html = html,
            Text = Surface.Session.Document.GetPlainText().Replace(SpecialCharacters.LineBreak, '\n').Replace(SpecialCharacters.Object.ToString(), "", StringComparison.Ordinal),
            InlineImages = [.. pictures.Values],
            Attachments = [.. _attachments],
        };
    }

    /// <summary>"Anna Berger &lt;anna@x.de&gt;; jonas@y.de" → the addresses (a name without an address stays as typed – the check names it).</summary>
    private static IReadOnlyList<MailAddress> Parse(AddressField field)
    {
        var result = new List<MailAddress>();
        foreach (var entry in field.Addresses)
        {
            if (MimeKit.MailboxAddress.TryParse(entry, out var mailbox))
            {
                result.Add(new MailAddress(mailbox.Name ?? "", mailbox.Address));
            }
            else
            {
                result.Add(new MailAddress("", entry));
            }
        }

        return result;
    }

    /// <summary>Senden: checked by the business layer (an error at its field), into the Postausgang, the window closes.</summary>
    public async Task<bool> SendAsync()
    {
        var draft = CurrentDraft();
        try
        {
            _document.Session.ValidateForSending(draft);
        }
        catch (BusinessException exception)
        {
            _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
            (exception.Field switch { "To" => To.Box, "Cc" => Cc.Box, "Bcc" => Bcc.Box, "From" => FromChoice, _ => (Control)Surface }).Focus();
            return false;
        }

        if (draft.Subject.Length == 0 && !await QuestionDialog.ConfirmAsync(this, "Ohne Betreff senden?", "Diese Nachricht hat keinen Betreff. Möchten Sie sie trotzdem senden?", "Senden"))
        {
            SubjectBox.Focus();
            return false;
        }

        try
        {
            _document.Session.Send(draft);
        }
        catch (BusinessException exception)
        {
            _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
            return false;
        }

        _sent?.Invoke(draft);
        _closeConfirmed = true;
        Close();
        return true;
    }

    /// <summary>Speichern (Strg+S): into Entwürfe (also offline: the server gets it later).</summary>
    public bool SaveDraft()
    {
        try
        {
            _draft = _document.Session.SaveDraft(CurrentDraft());
            _dirty = false;
            _savedVersion = Surface.Session.Document.Version;
            return true;
        }
        catch (BusinessException exception)
        {
            _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
            return false;
        }
    }

    private void AutoSave()
    {
        if (_dirty && (Parse(To).Count > 0 || SubjectText.Length > 0 || Surface.Session.Document.Version != _savedVersion))
        {
            SaveDraft();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !_dirty)
        {
            return;
        }

        e.Cancel = true;
        var choice = await QuestionDialog.AskAsync(this, "Änderungen speichern?", "Möchten Sie den Entwurf dieser Nachricht in „Entwürfe“ speichern?",
            new DialogChoice("Speichern", "save", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "x", IsCancel: true),
            new DialogChoice("Nicht speichern", "trash", IsSeparated: true));
        if (choice == 1 || choice < 0)
        {
            return;
        }

        if (choice == 0 && !SaveDraft())
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    /// <summary>Closes without asking (tests, the self test).</summary>
    public void CloseDiscarding()
    {
        _closeConfirmed = true;
        Close();
    }

    // ===== commands and keys =====

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var inTextField = FocusManager?.GetFocusedElement() is TextBox;
        foreach (var (gesture, command) in _shortcuts)
        {
            if (gesture.Matches(e))
            {
                if (inTextField && command.YieldsToTextField)
                {
                    return;
                }

                e.Handled = true;
                _ = OfficeWindow.RunCommandAsync(this, command);
                return;
            }
        }
    }

    private EditingSession Session => Surface.Session;

    private SelectionFormatting Format() => Session.GetFormatting();

    private void AddCommands()
    {
        void Add(string id, string label, string icon, Action run, string? shortcut = null, string? mac = null, Func<bool>? isChecked = null, string? description = null, bool yields = false) =>
            Commands.Add(new OfficeCommand(id, label, () =>
            {
                run();
                Commands.Invalidate();
            })
            { Icon = icon, Shortcut = shortcut, MacShortcut = mac, IsChecked = isChecked, Description = description, YieldsToTextField = yields });

        Commands.Add(new OfficeCommand(ComposeCommandIds.Send, "Senden", SendAsync)
        {
            Icon = "arrow-right",
            Shortcut = "Ctrl+Enter",
            Description = "Sendet die Nachricht (ohne Verbindung wartet sie im Postausgang).",
            Keywords = ["senden", "abschicken", "verschicken"],
        });
        Add(ComposeCommandIds.SaveDraft, "Speichern", "save", () => SaveDraft(), "Ctrl+S", description: "Speichert die Nachricht als Entwurf in „Entwürfe“.");
        Commands.Add(new OfficeCommand(ComposeCommandIds.Attach, "Datei anfügen", AttachAsync) { Icon = "paperclip", Description = "Fügt der Nachricht Dateien als Anlagen hinzu (auch per Ziehen und Ablegen)." });
        Commands.Add(new OfficeCommand(ComposeCommandIds.Picture, "Bilder", PictureAsync) { Icon = "image", Description = "Fügt ein Bild in den Text ein." });
        Commands.Add(new OfficeCommand(ComposeCommandIds.Link, "Link", LinkAsync) { Icon = "link", Shortcut = "Ctrl+K", Description = "Macht den markierten Text zu einem Link (nur Web- und E-Mail-Adressen)." });
        Commands.Add(new OfficeCommand(ComposeCommandIds.Signature, "Signatur", InsertSignature)
        {
            Icon = "edit",
            Description = "Fügt die Signatur des Kontos ein (Signaturen legen Sie unter Datei › Einstellungen › Signaturen an).",
            CanExecute = () => _document.Accounts.Signatures.Count > 0,
        });
        Add(ComposeCommandIds.Bold, "Fett", "bold", () => Session.ToggleBold(), "Ctrl+Shift+F", "Ctrl+B", () => Format().Bold.Is(true));
        Add(ComposeCommandIds.Italic, "Kursiv", "italic", () => Session.ToggleItalic(), "Ctrl+Shift+K", "Ctrl+I", () => Format().Italic.Is(true));
        Add(ComposeCommandIds.Underline, "Unterstrichen", "underline", () => Session.ToggleUnderline(), "Ctrl+Shift+U", "Ctrl+U", () => !Format().Underline.IsMixed && Format().Underline.Value != UnderlineStyle.None);
        Add(ComposeCommandIds.Strikethrough, "Durchgestrichen", "strikethrough", () => Session.ToggleStrikethrough(), isChecked: () => Format().Strikethrough.Is(true));
        Add(ComposeCommandIds.Bullets, "Aufzählung", "list", () => Session.ToggleList(ListKind.Bullet));
        Add(ComposeCommandIds.Numbering, "Nummerierung", "list-ordered", () => Session.ToggleList(ListKind.Numbered));
        Add(ComposeCommandIds.AlignLeft, "Linksbündig", "align-left", () => Session.SetAlignment(ParagraphAlignment.Left), isChecked: () => Format().Alignment.Is(ParagraphAlignment.Left));
        Add(ComposeCommandIds.AlignCenter, "Zentriert", "align-center", () => Session.SetAlignment(ParagraphAlignment.Center), isChecked: () => Format().Alignment.Is(ParagraphAlignment.Center));
        Add(ComposeCommandIds.AlignRight, "Rechtsbündig", "align-right", () => Session.SetAlignment(ParagraphAlignment.Right), isChecked: () => Format().Alignment.Is(ParagraphAlignment.Right));
        Add(ComposeCommandIds.ClearFormat, "Formatierung löschen", "clear-format", () => Session.ClearCharacterFormatting());
        Add(ComposeCommandIds.Undo, "Rückgängig", "undo", () => Session.Undo(), "Ctrl+Z", yields: true);
        Add(ComposeCommandIds.Redo, "Wiederholen", "redo", () => Session.Redo(), "Ctrl+Y", "Ctrl+Shift+Z", yields: true);
        Commands.Add(new OfficeCommand(ComposeCommandIds.Copy, "Kopieren", () => CopyAsync(cut: false)) { Icon = "copy", Shortcut = "Ctrl+C", YieldsToTextField = true });
        Commands.Add(new OfficeCommand(ComposeCommandIds.Cut, "Ausschneiden", () => CopyAsync(cut: true)) { Icon = "cut", Shortcut = "Ctrl+X", YieldsToTextField = true });
        Commands.Add(new OfficeCommand(ComposeCommandIds.Paste, "Einfügen", PasteAsync) { Icon = "paste", Shortcut = "Ctrl+V", YieldsToTextField = true });
        Add(ComposeCommandIds.SelectAll, "Alles markieren", "check-square", () => Session.SelectAll(), "Ctrl+A", yields: true);
        Commands.Add(new OfficeValueCommand(ComposeCommandIds.FontName, "Schriftart", OfficeValueKind.Text,
            () => Format().FontFamily is { IsMixed: false } family ? family.Value : null,
            value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Session.SetFontFamily(value.Trim());
                }
            })
        { Icon = "type", Options = WriteFonts.Families });
        Commands.Add(new OfficeValueCommand(ComposeCommandIds.FontSize, "Schriftgrad", OfficeValueKind.Number,
            () => Format().FontSize is { IsMixed: false } size ? size.Value.ToString(CultureInfo.InvariantCulture) : null,
            value =>
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size is >= 1 and <= 400)
                {
                    Session.SetFontSize(size);
                }
            })
        { Icon = "font-grow" });
        Commands.Add(new OfficeValueCommand(ComposeCommandIds.FontColor, "Schriftfarbe", OfficeValueKind.Color,
            () => Format().Color is { IsMixed: false } color && color.Value != "auto" ? "#" + color.Value : null,
            value => Session.SetTextColor(string.IsNullOrEmpty(value) ? null : value.TrimStart('#')))
        { Icon = "font-colour" });
        Commands.Add(new OfficeValueCommand(ComposeCommandIds.Highlight, "Texthervorhebungsfarbe", OfficeValueKind.Color,
            () => Format().Highlight is { IsMixed: false } highlight && highlight.Value != HighlightColor.None ? DocumentPalette.HexOf(highlight.Value) : null,
            value => Session.SetHighlight(DocumentPalette.HighlightOf(value)))
        { Icon = "highlight" });
    }

    private MatRibbonDefinition CreateRibbon()
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(
            new MatRibbonTab("message", "Nachricht").Add(
                new MatRibbonGroup("send", "Senden") { Icon = "arrow-right" }.Add(Button(ComposeCommandIds.Send, MatRibbonSize.Large), Button(ComposeCommandIds.SaveDraft)),
                new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste" }.Add(Button(ComposeCommandIds.Paste, MatRibbonSize.Large), Button(ComposeCommandIds.Cut), Button(ComposeCommandIds.Copy)),
                new MatRibbonGroup("text", "Text") { Icon = "type" }.Add(
                    Toggle(ComposeCommandIds.Bold), Toggle(ComposeCommandIds.Italic), Toggle(ComposeCommandIds.Underline),
                    Button(ComposeCommandIds.Bullets), Button(ComposeCommandIds.Numbering)),
                new MatRibbonGroup("include", "Einschließen") { Icon = "paperclip" }.Add(Button(ComposeCommandIds.Attach, MatRibbonSize.Large), Button(ComposeCommandIds.Signature, MatRibbonSize.Large)),
                new MatRibbonGroup("undo", "Rückgängig") { Icon = "undo" }.Add(Button(ComposeCommandIds.Undo), Button(ComposeCommandIds.Redo))),
            new MatRibbonTab("insert", "Einfügen").Add(
                new MatRibbonGroup("include", "Einschließen") { Icon = "paperclip" }.Add(Button(ComposeCommandIds.Attach, MatRibbonSize.Large), Button(ComposeCommandIds.Signature, MatRibbonSize.Large)),
                new MatRibbonGroup("pictures", "Illustrationen") { Icon = "image" }.Add(Button(ComposeCommandIds.Picture, MatRibbonSize.Large)),
                new MatRibbonGroup("links", "Links") { Icon = "link" }.Add(Button(ComposeCommandIds.Link, MatRibbonSize.Large))),
            new MatRibbonTab("format", "Text formatieren").Add(
                new MatRibbonGroup("font", "Schriftart") { Icon = "type" }.Add(
                    ComboBox(ComposeCommandIds.FontName, 150, isEditable: true),
                    Number(ComposeCommandIds.FontSize, 1, 400, 1, " pt", 1),
                    Toggle(ComposeCommandIds.Bold), Toggle(ComposeCommandIds.Italic), Toggle(ComposeCommandIds.Underline), Toggle(ComposeCommandIds.Strikethrough),
                    ColorPicker(ComposeCommandIds.FontColor, DocumentPalette.StandardColors),
                    ColorPicker(ComposeCommandIds.Highlight, DocumentPalette.HighlightColors, "Keine Farbe"),
                    Button(ComposeCommandIds.ClearFormat)),
                new MatRibbonGroup("paragraph", "Absatz") { Icon = "align-left" }.Add(
                    Button(ComposeCommandIds.Bullets), Button(ComposeCommandIds.Numbering),
                    Toggle(ComposeCommandIds.AlignLeft, "align"), Toggle(ComposeCommandIds.AlignCenter, "align"), Toggle(ComposeCommandIds.AlignRight, "align"))));
        return definition;
    }

    private async Task AttachAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Datei anfügen", AllowMultiple = true });
        await AttachFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async Task PictureAsync()
    {
        if (await OfficeApplication.FileDialogs.PickPictureAsync(this, SettingsService.SaveFolder) is { } path)
        {
            InsertPicture(path);
        }
    }

    private async Task LinkAsync()
    {
        var address = await TextInputDialog.AskAsync(this, "Link einfügen", "Adresse (https://… oder mailto:…)", "https://", "Einfügen");
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        try
        {
            var normalized = LinkService.Normalize(address);
            Session.SetHyperlink(normalized, Session.Selection.IsCollapsed ? normalized : null);
        }
        catch (BusinessException exception)
        {
            _message.Show(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    private void InsertSignature()
    {
        var accounts = _document.Accounts.Accounts;
        var account = FromChoice.SelectedIndex >= 0 && FromChoice.SelectedIndex < accounts.Count ? accounts[FromChoice.SelectedIndex] : null;
        var signature = _document.Accounts.Signature(account?.NewSignatureId) ?? _document.Accounts.Signatures.FirstOrDefault();
        if (signature is null || HtmlImport.Import(signature.Html, HtmlImportOptions.Mail(null, 600)) is not { } fragment)
        {
            return;
        }

        Session.Paste(fragment);
    }

    private async Task CopyAsync(bool cut)
    {
        var fragment = cut ? Session.Cut() : Session.Copy();
        if (fragment is null || Clipboard is null)
        {
            return;
        }

        _clipboardFragment = fragment;
        _clipboardText = fragment.PlainText;
        await Clipboard.SetTextAsync(_clipboardText);
    }

    /// <summary>
    /// Einfügen in ThixWrite's order: what this composer copied (with its formatting), HTML of another program (a browser, Word, a mail – sanitized by
    /// HtmlImport: no scripts, nothing fetched, pictures only when embedded), text, a picture (a screenshot).
    /// </summary>
    private async Task PasteAsync()
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (_clipboardFragment is not null && text == _clipboardText)
        {
            Session.Paste(_clipboardFragment);
            return;
        }

        if (await ClipboardHtml.TryGetAsync(clipboard) is { } html && PasteHtml(html))
        {
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            Session.PasteText(text);
            return;
        }

        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using var png = new MemoryStream();
            bitmap.Save(png);
            if (ImageData.FromBytes(png.ToArray()) is { } image)
            {
                Session.InsertImage(image);
            }
        }
    }

    /// <summary>Pastes HTML of another program into the body (false when it holds nothing to paste).</summary>
    internal bool PasteHtml(string html)
    {
        if (HtmlImport.Import(HtmlImport.FromClipboardFormat(html)) is not { } fragment)
        {
            return false;
        }

        Session.Paste(fragment);
        return true;
    }

    /// <summary>The body's context menu: the spelling suggestions for an unknown word at the caret, then the clipboard and Link.</summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var item in Spelling.MenuItems(Session.Selection.Start, () => Surface.Focus()))
            {
                menu.Items.Add(item);
            }

            foreach (var id in new[] { ComposeCommandIds.Cut, ComposeCommandIds.Copy, ComposeCommandIds.Paste, null, ComposeCommandIds.Link })
            {
                if (id is null)
                {
                    menu.Items.Add(new Separator());
                    continue;
                }

                var command = Commands[id];
                var item = new MenuItem
                {
                    Header = command.Label,
                    Icon = command.Icon is null ? null : new MatIcon { Icon = command.Icon, Size = 16 },
                    InputGesture = Ui.Gesture(command.CurrentShortcut),
                    IsEnabled = command.IsEnabled,
                };
                item.Click += async (_, _) =>
                {
                    await OfficeWindow.RunCommandAsync(this, command);
                    Surface.Focus();
                };
                menu.Items.Add(item);
            }
        };
        return menu;
    }
}

/// <summary>The ids of the compose window's commands.</summary>
public static class ComposeCommandIds
{
    public const string Send = "compose.send";
    public const string SaveDraft = "compose.save";
    public const string Attach = "compose.attach";
    public const string Picture = "compose.picture";
    public const string Link = "compose.link";
    public const string Signature = "compose.signature";
    public const string Bold = "compose.bold";
    public const string Italic = "compose.italic";
    public const string Underline = "compose.underline";
    public const string Strikethrough = "compose.strike";
    public const string Bullets = "compose.bullets";
    public const string Numbering = "compose.numbering";
    public const string AlignLeft = "compose.alignLeft";
    public const string AlignCenter = "compose.alignCenter";
    public const string AlignRight = "compose.alignRight";
    public const string ClearFormat = "compose.clearFormat";
    public const string Undo = "compose.undo";
    public const string Redo = "compose.redo";
    public const string Copy = "compose.copy";
    public const string Cut = "compose.cut";
    public const string Paste = "compose.paste";
    public const string SelectAll = "compose.selectAll";
    public const string FontName = "compose.fontName";
    public const string FontSize = "compose.fontSize";
    public const string FontColor = "compose.fontColor";
    public const string Highlight = "compose.highlight";
}
