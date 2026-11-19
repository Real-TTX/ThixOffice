using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
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
    private readonly Canvas _overlay = new();
    private StoryEditing? _story;
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
            Content = new Panel { Children = { _surface, _overlay } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _surface.ScrollBy = delta => _scroller.Offset = _scroller.Offset.WithY(Math.Clamp(_scroller.Offset.Y + delta, 0, Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height)));
        _scroller.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        Avalonia.Automation.AutomationProperties.SetName(_surface, "Dokument");

        FindBar = new FindBar(this) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 26, 0) };
        _view = new Grid { Children = { _scroller, FindBar } };
        _surface.ContextMenu = BuildContextMenu();
        _surface.HeaderFooterRequested += (_, area) => EditHeaderFooter(area.Page, area.Footer);
        _surface.BodyRequested += (_, _) => CloseHeaderFooter();
        Document.Writing += OnWriting;
        _surface.DropPicture = (path, position) => OfficeWindow.RunAsync(_window, "Bild einfügen", () => Task.FromResult(InsertPictureFile(path, position)));

        document.Session.StateChanged += OnStateChanged;
        _surface.Refreshed += (_, _) => ScheduleState();
        Document.ContentReplaced += OnContentReplaced;
        _surface.Scale = Zoom / 100 * DocumentSurface.PointsToDips;
    }

    public WordDocument Document { get; }

    /// <summary>The session the commands act on: the header or footer being edited, else the document's.</summary>
    public EditingSession Session => _story?.Session ?? Document.Session;

    /// <summary>The page and kind (footer?) of the header or footer being edited, else the caret's page and header.</summary>
    public (int Page, bool Footer) EditedPlace => _story is { } story ? (story.Page, story.Footer) : (CaretPage, false);

    /// <summary>True while a header or footer is edited.</summary>
    public bool IsEditingHeaderFooter => _story is not null;

    /// <summary>The surface of the header or footer being edited (tests), or null.</summary>
    internal DocumentSurface? StorySurface => _story?.Surface;

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

    /// <summary>The page of the caret (0-based): "Aktuelle Seite drucken", the status bar.</summary>
    public int CaretPage => Math.Clamp(_surface.CaretPage, 0, Math.Max(0, Layout.PageCount - 1));

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
        CloseHeaderFooter();
        Document.Session.StateChanged -= OnStateChanged;
        Document.Writing -= OnWriting;
        Document.ContentReplaced -= OnContentReplaced;
        _surface.Detach();
        Layout.Dispose();
    }

    protected override void OnZoomChanged(double zoom)
    {
        _surface.Scale = zoom / 100 * DocumentSurface.PointsToDips;
        if (_story is { } story)
        {
            story.Surface.Scale = _surface.Scale;
            Dispatcher.UIThread.Post(PositionStory, DispatcherPriority.Loaded);
        }
    }

    // ===== Headers and footers =====

    /// <summary>
    /// Opens the header (or footer) of a page for editing, as in Word: it is edited in place, the body dimmed; the tab Kopf- und
    /// Fußzeile shows. The story is the page's (one of a section before when the page takes it from there); a page without one gets
    /// a new, empty one.
    /// </summary>
    public void EditHeaderFooter(int page, bool footer)
    {
        CloseHeaderFooter();
        Layout.Update();
        page = Math.Clamp(page, 0, Layout.PageCount - 1);
        var pageLayout = Layout.Pages[page];
        var setup = pageLayout.Section.Properties;
        var kind = footer ? pageLayout.FooterKind : pageLayout.HeaderKind;
        var original = (footer ? pageLayout.Footer : pageLayout.Header) ?? Document.Session.NewHeaderFooter(footer);
        var section = Document.Session.DefiningSection(pageLayout.Section.Index, kind, footer);
        var content = Document.Content;
        var storySection = setup with
        {
            PageHeight = 5000,
            MarginTop = 0,
            MarginBottom = 0,
            TitlePage = false,
            Headers = HeaderFooterSet.Empty,
            Footers = HeaderFooterSet.Empty,
        };
        var document = new Core.Model.Document(original.Blocks, content.Styles, content.Numbering, storySection, content.Theme, content.Settings);
        var session = new EditingSession(document);
        var pages = Layout.PageCount;
        var layout = new DocumentLayout(document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver)
        {
            FieldText = field => field.Text(page + 1, pages, DateTime.Today),
        };
        session.Navigator = layout;
        var surface = new DocumentSurface(session, layout)
        {
            IsStory = true,
            MinimumHeight = Math.Max(12, footer ? setup.MarginBottom - setup.FooterDistance : setup.MarginTop - setup.HeaderDistance),
            Scale = _surface.Scale,
        };
        Avalonia.Automation.AutomationProperties.SetName(surface, footer ? "Fußzeile" : "Kopfzeile");
        _story = new StoryEditing(document, session, layout, surface, section, kind, footer, page, original);
        _overlay.Children.Add(surface);
        session.StateChanged += OnStateChanged;
        session.ContentChanged += OnStoryChanged;
        surface.Refreshed += (_, _) => ScheduleState();
        surface.KeyDown += OnStoryKey;
        _surface.DimBody = true;
        PositionStory();
        session.SetCaret(document.End);
        Dispatcher.UIThread.Post(() => surface.Focus(), DispatcherPriority.Input);
        ScheduleState();
    }

    /// <summary>Puts the edited header or footer into the document (one step) and closes it; nothing when none is open.</summary>
    public void CloseHeaderFooter()
    {
        if (_story is not { } story)
        {
            return;
        }

        CommitHeaderFooter();
        story.Session.StateChanged -= OnStateChanged;
        story.Session.ContentChanged -= OnStoryChanged;
        story.Surface.KeyDown -= OnStoryKey;
        story.Surface.Detach();
        story.Layout.Dispose();
        _overlay.Children.Remove(story.Surface);
        _story = null;
        _surface.DimBody = false;
        _surface.Focus();
        ScheduleState();
    }

    /// <summary>The text of the open header or footer into the document (before saving, when closing).</summary>
    private void CommitHeaderFooter()
    {
        if (_story is not { } story)
        {
            return;
        }

        var edited = story.Original.WithBlocks(story.Document.Blocks);
        Document.Session.AdoptDefinitions(story.Document.Styles, story.Document.Numbering);
        var current = (story.Footer ? Document.Content.Sections[Math.Min(story.Section, Document.Content.Sections.Count - 1)].Properties.Footers
            : Document.Content.Sections[Math.Min(story.Section, Document.Content.Sections.Count - 1)].Properties.Headers)[story.Kind];
        if (current is null || !current.Equals(edited))
        {
            Document.Session.SetHeaderFooter(story.Section, story.Kind, story.Footer, edited);
        }
    }

    private void OnWriting(object? sender, EventArgs e) => CommitHeaderFooter();

    private void OnStoryChanged(object? sender, EventArgs e)
    {
        _story?.Surface.InvalidateMeasure();
        Dispatcher.UIThread.Post(PositionStory, DispatcherPriority.Loaded);
    }

    private void OnStoryKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseHeaderFooter();
        }
    }

    /// <summary>The story surface over its page: the header from the header distance down, the footer up to the footer distance.</summary>
    private void PositionStory()
    {
        if (_story is not { } story || story.Page >= Layout.PageCount)
        {
            return;
        }

        var setup = Layout.Pages[story.Page].Section.Properties;
        var page = _surface.PageRect(story.Page);
        var top = story.Footer
            ? page.Y + ((setup.PageHeight - setup.FooterDistance - story.Surface.StoryHeight) * _surface.Scale)
            : page.Y + (setup.HeaderDistance * _surface.Scale);
        Canvas.SetLeft(story.Surface, page.X);
        Canvas.SetTop(story.Surface, top);
    }

    private sealed record StoryEditing(
        Core.Model.Document Document,
        EditingSession Session,
        DocumentLayout Layout,
        DocumentSurface Surface,
        int Section,
        HeaderFooterKind Kind,
        bool Footer,
        int Page,
        Story Original);

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
            return;
        }

        if (!textOnly && await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            // A picture of another program (a screenshot, a picture copied in a browser): as PNG into the document.
            using var png = new MemoryStream();
            bitmap.Save(png);
            if (ImageData.FromBytes(png.ToArray()) is { } image)
            {
                Session.InsertImage(image);
            }
        }
    }

    // ===== Pictures =====

    /// <summary>Einfügen › Bilder: the system's dialog, then the picture at the caret.</summary>
    public async Task InsertPictureFromDialogAsync()
    {
        var path = await OfficeApplication.FileDialogs.PickPictureAsync(_window, DocumentService.SuggestedFolder(Document));
        if (path is not null)
        {
            await OfficeWindow.RunAsync(_window, "Bild einfügen", () => Task.FromResult(InsertPictureFile(path)));
        }
    }

    /// <summary>Bildformat › Bild ändern: the system's dialog, then the selected picture replaced.</summary>
    public async Task ChangePictureFromDialogAsync()
    {
        var path = await OfficeApplication.FileDialogs.PickPictureAsync(_window, DocumentService.SuggestedFolder(Document));
        if (path is not null)
        {
            await OfficeWindow.RunAsync(_window, "Bild ändern", () =>
            {
                Session.ReplaceImage(ReadPicture(path));
                return Task.FromResult(true);
            });
        }
    }

    /// <summary>Inserts the picture file at the caret (or at <paramref name="at"/>: a file dropped on the page); a business error when it is none.</summary>
    public bool InsertPictureFile(string path, DocumentPosition? at = null)
    {
        var image = ReadPicture(path);
        if (at is { } position)
        {
            Session.SetCaret(position);
        }

        Session.InsertImage(image);
        return true;
    }

    private static ImageData ReadPicture(string path) =>
        ImageData.FromBytes(ImageFiles.Read(path)) ?? throw new BusinessException($"„{Path.GetFileName(path)}“ ist kein lesbares Bild.");

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
            ShowTableTabs();
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
        }, DispatcherPriority.Background);
    }

    /// <summary>Tabellenentwurf and Layout (contextual) show while the caret is in a table, Bildformat while a picture is selected, as in Word.</summary>
    private void ShowTableTabs()
    {
        if (_story is not null)
        {
            _window.RibbonDefinition.ShowContext(HeaderFooterCommands.Context);
        }
        else
        {
            _window.RibbonDefinition.HideContext(HeaderFooterCommands.Context);
        }

        if (Session.SelectedImage is not null)
        {
            _window.RibbonDefinition.ShowContext(PictureCommands.Context);
        }
        else
        {
            _window.RibbonDefinition.HideContext(PictureCommands.Context);
        }

        if (Session.CurrentTable is not null)
        {
            _window.RibbonDefinition.ShowContext(TableCommands.Context);
        }
        else
        {
            _window.RibbonDefinition.HideContext(TableCommands.Context);
        }
    }

    /// <summary>The average height of the selected table rows on the pages (Zeilen verteilen), points.</summary>
    public double AverageRowHeight()
    {
        if (Session.CurrentCells is not { } cells)
        {
            return 12;
        }

        var heights = new List<double>();
        for (var row = cells.FirstRow; row <= cells.LastRow; row++)
        {
            var (first, _) = cells.Span.BlocksOf(row, 0);
            var page = Layout.PageOfBlock(first);
            var placed = Layout.Pages[page].Cells.Where(cell => cell.TableStart == cells.Span.Start && cell.Row == row && !cell.IsRepeat).ToList();
            if (placed.Count > 0)
            {
                heights.Add(placed.Min(cell => cell.Height));
            }
        }

        return heights.Count == 0 ? 12 : heights.Average();
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
