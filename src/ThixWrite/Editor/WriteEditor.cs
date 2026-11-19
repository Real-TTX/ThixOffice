using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// ThixWrite's editor of one <see cref="WordDocument"/> in a window: the pages (<see cref="DocumentSurface"/>) in a
/// ScrollViewer on the workspace, the find bar above them (<see cref="FindBar"/>), the zoom (Strg + mouse wheel, Seitenbreite,
/// Ganze Seite, the zoom dialog), the clipboard (ThixWrite's rich fragment and plain text), the status bar texts (Seite X von
/// Y, Wörter) and the ribbon state (on every change of the session, coalesced to once per dispatcher round).
/// </summary>
public sealed class WriteEditor : OfficeEditor
{
    public const string PageItem = "page";
    public const string WordsItem = "words";

    private static readonly DataFormat<byte[]> FragmentFormat = DataFormat.CreateBytesApplicationFormat("ThixWrite.Fragment");

    private readonly OfficeWindow _window;
    private readonly DocumentSurface _surface;
    private readonly ScrollViewer _scroller;
    private readonly Grid _view;
    private bool _statePending;
    private long _countedVersion = -1;
    private DocumentStatistics _statistics = new(0, 0, 0, 0);

    public WriteEditor(OfficeWindow window, WordDocument document)
    {
        _window = window;
        Document = document;
        Layout = new DocumentLayout(document.Content, WriteFonts.Measurer, LayoutOptions.Default, document.Session.Resolver);
        document.Session.Navigator = Layout;
        _surface = new DocumentSurface(document.Session, Layout);
        _scroller = new ScrollViewer
        {
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _surface.ScrollBy = delta => _scroller.Offset = _scroller.Offset.WithY(Math.Clamp(_scroller.Offset.Y + delta, 0, Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height)));
        _scroller.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        Avalonia.Automation.AutomationProperties.SetName(_surface, "Dokument");

        FindBar = new FindBar(this) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 26, 0) };
        _view = new Grid { Children = { _scroller, FindBar } };
        _surface.ContextMenu = BuildContextMenu();

        document.Session.StateChanged += OnStateChanged;
        _surface.Refreshed += (_, _) => ScheduleState();
        Document.ContentReplaced += OnContentReplaced;
        _surface.Scale = Zoom / 100 * DocumentSurface.PointsToDips;
    }

    public WordDocument Document { get; }

    public EditingSession Session => Document.Session;

    public DocumentLayout Layout { get; }

    /// <summary>The pages control (tests drive it with keys and the pointer).</summary>
    internal DocumentSurface Surface => _surface;

    internal ScrollViewer Scroller => _scroller;

    /// <summary>Suchen / Ersetzen.</summary>
    public FindBar FindBar { get; }

    public override Control View => _view;

    /// <summary>Whether the formatting marks show (¶ ·).</summary>
    public bool ShowMarks
    {
        get => _surface.Pictures.ShowMarks;
        set
        {
            _surface.Pictures.ShowMarks = value;
            _surface.InvalidateVisual();
            ScheduleState();
        }
    }

    /// <summary>The colour the Schriftfarbe button applies (the last one chosen; null = Automatisch).</summary>
    public string? FontColor { get; set; } = DocumentPalette.DefaultFontColor;

    /// <summary>The colour the Hervorhebung button applies (the last one chosen; null = none).</summary>
    public string? HighlightColor { get; set; } = DocumentPalette.HighlightColors[0];

    public override string? OpeningWarning
    {
        get
        {
            var notes = Document.Content.UnsupportedContent;
            if (notes.Count == 0)
            {
                return null;
            }

            var details = string.Join(Environment.NewLine, notes.Select(note => "• " + note.Message));
            return $"Dieses Dokument enthält Inhalte, die ThixWrite noch nicht anzeigt oder bearbeitet. Sie bleiben beim Speichern als Word-Dokument erhalten:{Environment.NewLine}{details}";
        }
    }

    public override void FocusDocument() => _surface.Focus();

    public override void RequestZoomLevel() => _ = ZoomDialog.ShowAsync(_window, this);

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        statusBar.SetItem(PageItem, $"Seite {_surface.CaretPage + 1} von {Layout.PageCount}");
        if (_countedVersion != Document.Content.Version)
        {
            _statistics = Session.GetStatistics();
            _countedVersion = Document.Content.Version;
        }

        var words = _statistics.Words;
        if (!Session.Selection.IsCollapsed)
        {
            var selected = Session.GetSelectionStatistics().Words;
            statusBar.SetItem(WordsItem, $"{selected} von {words} {(words == 1 ? "Wort" : "Wörtern")}");
            return;
        }

        statusBar.SetItem(WordsItem, words == 1 ? "1 Wort" : $"{words} Wörter");
    }

    public override void Detach()
    {
        Session.StateChanged -= OnStateChanged;
        Document.ContentReplaced -= OnContentReplaced;
        _surface.Detach();
        Layout.Dispose();
    }

    protected override void OnZoomChanged(double zoom) => _surface.Scale = zoom / 100 * DocumentSurface.PointsToDips;

    // ===== Zoom =====

    /// <summary>"Seitenbreite": the widest page fills the width of the window.</summary>
    public void ZoomToPageWidth()
    {
        var width = _scroller.Viewport.Width > 0 ? _scroller.Viewport.Width : _view.Bounds.Width;
        var page = Layout.Pages.Max(candidate => candidate.Width);
        if (width > 0)
        {
            Zoom = Math.Floor((width - (2 * DocumentSurface.SideMargin)) / (page * DocumentSurface.PointsToDips) * 100);
        }
    }

    /// <summary>"Ganze Seite": the page of the caret fits into the window.</summary>
    public void ZoomToWholePage()
    {
        var viewport = _scroller.Viewport.Width > 0 ? _scroller.Viewport : _view.Bounds.Size;
        var page = Layout.Pages[Math.Clamp(_surface.CaretPage, 0, Layout.PageCount - 1)];
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var byWidth = (viewport.Width - (2 * DocumentSurface.SideMargin)) / (page.Width * DocumentSurface.PointsToDips);
        var byHeight = (viewport.Height - (2 * DocumentSurface.TopMargin)) / (page.Height * DocumentSurface.PointsToDips);
        Zoom = Math.Floor(Math.Min(byWidth, byHeight) * 100);
        Dispatcher.UIThread.Post(() =>
        {
            var top = _surface.PageRect(_surface.CaretPage).Top - DocumentSurface.TopMargin;
            _scroller.Offset = _scroller.Offset.WithY(Math.Max(0, top));
        }, DispatcherPriority.Background);
    }

    /// <summary>Strg + mouse wheel zooms in steps of 10 %.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(modifier) || e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        Zoom = e.Delta.Y > 0 ? Math.Floor((Zoom / 10) + 1) * 10 : Math.Ceiling((Zoom / 10) - 1) * 10;
    }

    // ===== Clipboard =====

    /// <summary>Kopieren: the selection as ThixWrite's fragment and as plain text.</summary>
    public async Task CopyAsync()
    {
        if (Session.Copy() is { } fragment)
        {
            await PutOnClipboardAsync(fragment);
        }
    }

    /// <summary>Ausschneiden.</summary>
    public async Task CutAsync()
    {
        if (Session.Cut() is { } fragment)
        {
            await PutOnClipboardAsync(fragment);
        }
    }

    /// <summary>Einfügen: ThixWrite's own fragment with its formatting, else the text; <paramref name="textOnly"/>: "Nur Text einfügen".</summary>
    public async Task PasteAsync(bool textOnly = false)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        if (!textOnly && await clipboard.TryGetValueAsync(FragmentFormat) is { Length: > 0 } bytes &&
            ClipboardFormats.Import(new Dictionary<string, byte[]> { [ClipboardFormats.Fragment] = bytes }) is { } fragment)
        {
            Session.Paste(fragment);
            return;
        }

        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            Session.PasteText(text);
        }
    }

    private async Task PutOnClipboardAsync(DocumentFragment fragment)
    {
        if (Clipboard is not { } clipboard)
        {
            return;
        }

        var formats = ClipboardFormats.Export(fragment);
        var item = new DataTransferItem();
        item.Set(FragmentFormat, formats[ClipboardFormats.Fragment]);
        item.SetText(fragment.PlainText);
        var transfer = new DataTransfer();
        transfer.Add(item);
        await clipboard.SetDataAsync(transfer);
    }

    private IClipboard? Clipboard => TopLevel.GetTopLevel(_surface)?.Clipboard ?? _window.Clipboard;

    // ===== State =====

    private void OnStateChanged(object? sender, EventArgs e) => ScheduleState();

    /// <summary>The ribbon and the status bar follow the session, once per dispatcher round however many changes came.</summary>
    private void ScheduleState()
    {
        if (_statePending)
        {
            return;
        }

        _statePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _statePending = false;
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
        }, DispatcherPriority.Background);
    }

    private void OnContentReplaced(object? sender, EventArgs e) => _surface.ScheduleRefresh();

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var id in new[] { WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Paste, WriteCommandIds.PasteText })
        {
            if (_window.Commands.Find(id) is not { } command)
            {
                continue;
            }

            var item = new MenuItem
            {
                Header = command.Label,
                Icon = command.Icon is null ? null : new MatDesktop.MatIcon { Icon = command.Icon, Size = 16 },
                InputGesture = Ui.Gesture(command.CurrentShortcut),
            };
            item.Click += async (_, _) =>
            {
                await OfficeWindow.RunCommandAsync(_window, command);
                FocusDocument();
            };
            menu.Items.Add(item);
        }

        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                var command = _window.Commands.All.FirstOrDefault(candidate => candidate.Label == (string?)item.Header);
                item.IsEnabled = command?.IsEnabled ?? true;
            }
        };
        return menu;
    }
}
