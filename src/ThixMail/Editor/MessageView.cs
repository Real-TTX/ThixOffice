using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Pim.Desktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Editor;

namespace ThixMail.Editor;

/// <summary>
/// One message shown (the reading pane, a message window of its own, the preview of printing): the header – subject, the sender with avatar, the date,
/// the recipients (collapsible) –, the bar "Bilder laden" while remote pictures are blocked, the attachments (open with a warning for programs, save,
/// save all), a note for a calendar invitation, and the body: the mail's HTML made safe and laid out with ThixWrite's engine as a read-only story
/// (<see cref="DocumentSurface"/> with <c>IsReadOnly</c>) on white paper, as wide as the pane. Links open only after their real target is shown
/// (the host decides). Colours from Mat tokens; the paper is ThixWrite's document colour.
/// </summary>
public sealed class MessageView : UserControl
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly TextBlock _subject = new() { TextWrapping = TextWrapping.Wrap, FontSize = MatTokens.Type.FsXl, FontWeight = FontWeight.SemiBold };
    private readonly PersonAvatar _avatar = new(40);
    private readonly TextBlock _from = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly SelectableTextBlock _fromAddress = new() { FontSize = MatTokens.Type.FsSm };
    private readonly TextBlock _date = new() { FontSize = MatTokens.Type.FsSm, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly SelectableTextBlock _recipients = new() { FontSize = MatTokens.Type.FsSm, TextWrapping = TextWrapping.Wrap, MaxLines = 1 };
    private readonly Button _moreRecipients = new() { Content = "Mehr", Padding = new Thickness(6, 0), MinHeight = 0 };
    private readonly Border _imagesBar = new() { IsVisible = false, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8), Margin = new Thickness(0, 10, 0, 0), BorderThickness = new Thickness(1) };
    private readonly TextBlock _imagesText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = MatTokens.Type.FsSm };
    private readonly InfoBar _note = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly WrapPanel _attachments = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Border _paper = new() { Padding = new Thickness(20, 16), Margin = new Thickness(0, 12, 0, 0) };
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 10 };
    private readonly Grid _root = new();
    private readonly StackPanel _content = new() { Margin = new Thickness(20, 16, 20, 20) };
    private ReadMail? _mail;
    private MessageHeader? _header;
    private IReadOnlyDictionary<string, byte[]>? _remote;
    private DocumentSurface? _surface;
    private double _laidOutWidth;
    private bool _relayoutQueued;
    private bool _recipientsOpen;

    public MessageView()
    {
        _fromAddress.Use(SelectableTextBlock.ForegroundProperty, "Mat.TextMuted");
        _date.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _recipients.Use(SelectableTextBlock.ForegroundProperty, "Mat.TextMuted");
        _moreRecipients.Themed("MatButton.Ghost");
        _moreRecipients.Click += (_, _) =>
        {
            _recipientsOpen = !_recipientsOpen;
            _recipients.MaxLines = _recipientsOpen ? 0 : 1;
            _moreRecipients.Content = _recipientsOpen ? "Weniger" : "Mehr";
        };

        var names = new StackPanel { Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _from, _fromAddress } };
        var senderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 4) };
        Grid.SetColumn(names, 1);
        Grid.SetColumn(_date, 2);
        senderRow.Children.Add(_avatar);
        senderRow.Children.Add(names);
        senderRow.Children.Add(_date);
        var recipientsRow = new DockPanel { Margin = new Thickness(52, 0, 0, 0) };
        DockPanel.SetDock(_moreRecipients, Dock.Right);
        recipientsRow.Children.Add(_moreRecipients);
        recipientsRow.Children.Add(_recipients);

        // remote pictures blocked: "Bilder laden" / "Immer für diesen Absender"
        _imagesBar.Use(Border.BackgroundProperty, "Mat.WarningBg");
        _imagesBar.Use(Border.BorderBrushProperty, "Mat.Border");
        LoadImagesButton = new Button { Content = new IconLabel { Icon = "image", Text = "Bilder laden" }, Padding = new Thickness(10, 3) };
        TrustSenderButton = new Button { Content = "Immer für diesen Absender", Padding = new Thickness(10, 3) }.Themed("MatButton.Ghost");
        LoadImagesButton.Click += (_, _) => LoadImagesRequested?.Invoke(this, false);
        TrustSenderButton.Click += (_, _) => LoadImagesRequested?.Invoke(this, true);
        var imagesRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var imagesActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { LoadImagesButton, TrustSenderButton } };
        var shield = new MatIcon { Icon = "shield", Size = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_imagesText, 1);
        Grid.SetColumn(imagesActions, 2);
        imagesRow.Children.Add(shield);
        imagesRow.Children.Add(_imagesText);
        imagesRow.Children.Add(imagesActions);
        _imagesBar.Child = imagesRow;

        _paper.Background = new SolidColorBrush(DocumentPalette.Paper);
        _paper.CornerRadius = new CornerRadius(6);
        _paper.Use(Border.BorderBrushProperty, "Mat.Border");
        _paper.BorderThickness = new Thickness(1);

        _content.Children.Add(_subject);
        _content.Children.Add(senderRow);
        _content.Children.Add(recipientsRow);
        _content.Children.Add(_imagesBar);
        _content.Children.Add(_note);
        _content.Children.Add(_attachments);
        _content.Children.Add(_paper);
        _scroller = new ScrollViewer { Content = _content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _scroller.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                QueueRelayout();
            }
        };

        var emptyText = new TextBlock { Text = "Wählen Sie ein Element aus, um es zu lesen.", HorizontalAlignment = HorizontalAlignment.Center };
        emptyText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _empty.Children.Add(new MatIcon { Icon = "mail", Size = 40, HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(emptyText);
        _root.Children.Add(_scroller);
        _root.Children.Add(_empty);
        Content = _root;
        ShowNothing();
    }

    /// <summary>"Bilder laden" (false) or "Immer für diesen Absender" (true) was clicked.</summary>
    public event EventHandler<bool>? LoadImagesRequested;

    /// <summary>A link of the body was clicked: its address (the host shows the target and opens only web and mail links).</summary>
    public event EventHandler<string>? LinkRequested;

    /// <summary>An attachment is to be opened (true) or saved (false).</summary>
    public event EventHandler<(MailAttachment Attachment, bool Open)>? AttachmentRequested;

    /// <summary>"Alle speichern".</summary>
    public event EventHandler? SaveAllRequested;

    public Button LoadImagesButton { get; }

    public Button TrustSenderButton { get; }

    /// <summary>The message shown (null: none).</summary>
    public ReadMail? Mail => _mail;

    public MessageHeader? Header => _header;

    /// <summary>The body's surface (tests: its document, its pictures).</summary>
    internal DocumentSurface? Surface => _surface;

    /// <summary>The body's view (blocked pictures, links), from the last layout.</summary>
    public MailView? View { get; private set; }

    public bool ShowsImagesBar => _imagesBar.IsVisible;

    public IReadOnlyList<Button> AttachmentButtons => [.. _attachments.Children.OfType<Button>()];

    public string SubjectText => _subject.Text ?? "";

    public string RecipientsText => _recipients.Text ?? "";

    /// <summary>Nothing selected.</summary>
    public void ShowNothing(string? text = null)
    {
        _mail = null;
        _header = null;
        _scroller.IsVisible = false;
        _empty.IsVisible = true;
        if (text is not null && _empty.Children[1] is TextBlock label)
        {
            label.Text = text;
        }
    }

    /// <summary>A message whose body is still being fetched (or cannot be fetched offline): the header from the store.</summary>
    public void ShowHeader(MessageHeader header, string state)
    {
        _header = header;
        _mail = null;
        _scroller.IsVisible = true;
        _empty.IsVisible = false;
        FillHeader(header.Subject, header.From, header.To, header.Cc, header.Date);
        _imagesBar.IsVisible = false;
        _attachments.Children.Clear();
        _note.Show(state, InfoSeverity.Info);
        SetBody(null);
    }

    /// <summary>Shows a message; <paramref name="remote"/>: remote pictures already loaded (null: blocked).</summary>
    public void Show(ReadMail mail, MessageHeader? header, IReadOnlyDictionary<string, byte[]>? remote)
    {
        _mail = mail;
        _header = header;
        _remote = remote;
        _scroller.IsVisible = true;
        _empty.IsVisible = false;
        _recipientsOpen = false;
        _recipients.MaxLines = 1;
        _moreRecipients.Content = "Mehr";
        FillHeader(mail.Subject, mail.From, mail.To, mail.Cc, mail.Date == DateTimeOffset.MinValue ? header?.Date ?? DateTimeOffset.Now : mail.Date);
        FillAttachments(mail);
        if (mail.Calendar is { } calendar)
        {
            _note.Show($"Diese Nachricht enthält eine Kalendereinladung ({(calendar.Method.Length > 0 ? calendar.Method : "Termin")}). Zusagen und Absagen folgen mit dem Kalender von ThixMail; bis dahin können Sie die Datei „.ics“ öffnen.", InfoSeverity.Info);
        }
        else if (mail.Problems.Count > 0)
        {
            _note.Show(string.Join(" ", mail.Problems), InfoSeverity.Warning);
        }
        else
        {
            _note.Hide();
        }

        _laidOutWidth = 0;
        Relayout();
        _scroller.Offset = default;
    }

    private void FillHeader(string subject, MailAddress from, IReadOnlyList<MailAddress> to, IReadOnlyList<MailAddress> cc, DateTimeOffset date)
    {
        _subject.Text = subject.Length > 0 ? subject : "(Kein Betreff)";
        _from.Text = from.Display;
        _fromAddress.Text = from.Name.Length > 0 ? from.Address : "";
        _avatar.ShowName(from.Display);
        _date.Text = date.ToLocalTime().ToString("ddd dd.MM.yyyy HH:mm", German);
        var line = "An: " + string.Join("; ", to.Select(a => a.Display));
        if (cc.Count > 0)
        {
            line += "   Cc: " + string.Join("; ", cc.Select(a => a.Display));
        }

        _recipients.Text = line;
        _moreRecipients.IsVisible = to.Count + cc.Count > 2;
        AutomationProperties.SetName(_subject, _subject.Text);
    }

    private void FillAttachments(ReadMail mail)
    {
        _attachments.Children.Clear();
        foreach (var attachment in mail.Attachments)
        {
            var button = new Button
            {
                Content = new IconLabel { Icon = AttachmentSafety.IsExecutable(attachment.FileName) ? "alert" : "paperclip", Text = $"{attachment.FileName} ({Size(attachment.Size)})" },
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 4),
            };
            AutomationProperties.SetName(button, attachment.FileName);
            ToolTip.SetTip(button, AttachmentSafety.IsExecutable(attachment.FileName)
                ? "Diese Datei kann ein Programm starten. Öffnen nur, wenn Sie dem Absender vertrauen."
                : "Klicken: öffnen · Rechtsklick: speichern");
            button.Click += (_, _) => AttachmentRequested?.Invoke(this, (attachment, true));
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Öffnen" };
            open.Click += (_, _) => AttachmentRequested?.Invoke(this, (attachment, true));
            var save = new MenuItem { Header = "Speichern unter …" };
            save.Click += (_, _) => AttachmentRequested?.Invoke(this, (attachment, false));
            menu.Items.Add(open);
            menu.Items.Add(save);
            button.ContextMenu = menu;
            _attachments.Children.Add(button);
        }

        if (mail.Attachments.Count > 1)
        {
            var all = new Button { Content = new IconLabel { Icon = "download", Text = "Alle speichern" }, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 4) }.Themed("MatButton.Ghost");
            all.Click += (_, _) => SaveAllRequested?.Invoke(this, EventArgs.Empty);
            _attachments.Children.Add(all);
        }

        _attachments.IsVisible = mail.Attachments.Count > 0;
    }

    /// <summary>"1,2 MB", "340 KB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{(bytes / 1024.0 / 1024.0).ToString("0.0", German)} MB",
        >= 1024 => $"{(bytes / 1024.0).ToString("0", German)} KB",
        _ => $"{bytes} Byte",
    };

    private void QueueRelayout()
    {
        if (_relayoutQueued)
        {
            return;
        }

        _relayoutQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _relayoutQueued = false;
            Relayout();
        }, DispatcherPriority.Background);
    }

    /// <summary>The body laid out again for the pane's width (a new message, a resize, pictures loaded).</summary>
    private void Relayout()
    {
        if (_mail is not { } mail)
        {
            return;
        }

        var available = _scroller.Bounds.Width > 0 ? _scroller.Bounds.Width : Bounds.Width > 0 ? Bounds.Width : 640;
        var widthDip = Math.Max(160, available - _content.Margin.Left - _content.Margin.Right - _paper.Padding.Left - _paper.Padding.Right - 2);
        if (Math.Abs(widthDip - _laidOutWidth) < 4 && _surface is not null)
        {
            return;
        }

        _laidOutWidth = widthDip;
        var points = widthDip / DocumentSurface.PointsToDips;
        var view = MailHtml.ToView(mail, points, _remote, WriteFonts.Default);
        View = view;
        _imagesText.Text = view.BlockedImages.Count == 1
            ? "Ein Bild aus dem Internet wurde blockiert – der Absender soll nicht erfahren, dass Sie die Nachricht lesen."
            : $"{view.BlockedImages.Count} Bilder aus dem Internet wurden blockiert – der Absender soll nicht erfahren, dass Sie die Nachricht lesen.";
        _imagesBar.IsVisible = view.HasBlockedImages;
        var session = new EditingSession(view.Document);
        var layout = new DocumentLayout(view.Document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver);
        session.Navigator = layout;
        layout.Update();
        var surface = new DocumentSurface(session, layout) { IsStory = true, IsReadOnly = true, MinimumHeight = 24 };
        AutomationProperties.SetName(surface, "Nachrichtentext");
        surface.LinkClicked += (_, link) =>
        {
            if (link.Uri is { } uri)
            {
                LinkRequested?.Invoke(this, uri);
            }
        };
        SetBody(surface);
    }

    private void SetBody(DocumentSurface? surface)
    {
        _surface = surface;
        _paper.Child = surface;
        _paper.IsVisible = surface is not null;
    }

    /// <summary>The text selected in the body (Kopieren), or the whole text when nothing is.</summary>
    public string? SelectedText()
    {
        if (_surface is not { } surface)
        {
            return null;
        }

        var selection = surface.Session.Selection;
        return selection.IsCollapsed ? null : DocumentFragment.Copy(surface.Session.Document, selection.Range).PlainText;
    }
}
