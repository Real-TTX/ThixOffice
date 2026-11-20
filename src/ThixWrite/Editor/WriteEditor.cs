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
using ThixWrite.Core.Formats.Html;
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
    private (ThixWrite.Core.Model.CharacterProperties Characters, ThixWrite.Core.Model.ParagraphProperties Paragraph)? _painted;
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
        RulerView = new Ruler(this) { IsVisible = false };
        _scroller.ScrollChanged += (_, _) => RulerView.InvalidateVisual();
        Navigation = new NavigationPane(this) { IsVisible = false };
        CommentsView = new CommentsPane(this) { IsVisible = Core.Model.Comments.All(document.Content).Count > 0 };
        StylesView = new StylesPane(this, NewStyleAsync, ModifyStyleAsync) { IsVisible = false };
        Grid.SetRowSpan(StylesView, 2);
        Grid.SetColumn(StylesView, 3);
        Grid.SetRow(_scroller, 1);
        Grid.SetRow(FindBar, 1);
        Grid.SetColumn(RulerView, 1);
        Grid.SetColumn(_scroller, 1);
        Grid.SetColumn(FindBar, 1);
        Grid.SetRowSpan(Navigation, 2);
        Grid.SetRowSpan(CommentsView, 2);
        Grid.SetColumn(CommentsView, 2);
        _view = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Children = { Navigation, RulerView, _scroller, FindBar, CommentsView, StylesView },
        };
        _surface.Shading = CommentShading;
        Spelling = new SpellingSupport(this, _surface.InvalidateVisual);
        _surface.Misspelled = pages => Spelling.Misspelled(pages);
        _surface.ContextMenu = BuildContextMenu();
        _surface.HeaderFooterRequested += (_, area) => EditHeaderFooter(area.Page, area.Footer);
        _surface.LinkClicked += (_, link) => _ = LinkCommands.Follow(_window, this, link);
        _surface.BodyRequested += (_, _) => CloseHeaderFooter();
        _surface.NoteRequested += OnNoteRequested;
        _surface.ShapeTextRequested += (_, position) => EditShapeText(position);
        _surface.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            if (IsEditingNote || IsEditingShapeText)
            {
                CloseHeaderFooter(); // a click on the text leaves the note (the click itself goes on)
            }
        }, RoutingStrategies.Tunnel);
        _surface.SelectedByPointer += (_, _) => Paint();
        Document.Writing += OnWriting;
        _surface.DropPicture = (path, position) => OfficeWindow.RunAsync(_window, "Bild einfügen", () => Task.FromResult(InsertPictureFile(path, position)));

        document.Session.StateChanged += OnStateChanged;
        document.Session.RevisionAuthor = ThixOffice.Core.Business.SettingsService.AuthorName;
        _surface.Refreshed += (_, _) => ScheduleState();
        Document.ContentReplaced += OnContentReplaced;
        _surface.Scale = Zoom / 100 * DocumentSurface.PointsToDips;
    }

    public WordDocument Document { get; }

    /// <summary>The session the commands act on: the header or footer being edited, else the document's.</summary>
    public EditingSession Session => _story?.Session ?? Document.Session;

    /// <summary>The page and kind (footer?) of the header or footer being edited, else the caret's page and header.</summary>
    public (int Page, bool Footer) EditedPlace => _story is { } story ? (story.Page, story.Footer) : (CaretPage, false);

    /// <summary>True while Format übertragen waits for the selection that gets the format.</summary>
    public bool IsPainting => _painted is not null;

    /// <summary>Format übertragen: takes the format at the selection (characters, and the paragraph's); the next selection gets it.</summary>
    public void ToggleFormatPainter()
    {
        if (_painted is not null)
        {
            _painted = null;
            ScheduleState();
            return;
        }

        var session = Session;
        var start = session.Selection.Start;
        if (session.Document.ParagraphAt(start.Block) is not { } paragraph)
        {
            return;
        }

        var characters = paragraph.Length == 0 ? paragraph.MarkProperties
            : paragraph.InlineAt(Math.Min(start.Offset, paragraph.Length - 1))?.Properties ?? paragraph.MarkProperties;
        _painted = (characters with { UnknownXml = null }, paragraph.Properties with { Numbering = null, Tabs = paragraph.Properties.Tabs });
        ScheduleState();
    }

    /// <summary>The selection made by the pointer while Format übertragen waits: it gets the format, the painter stops.</summary>
    private void Paint()
    {
        if (_painted is not { } painted)
        {
            return;
        }

        _painted = null;
        var session = Session;
        var range = session.Selection.Range;
        var wholeParagraphs = range.Start.Offset == 0 && (range.End.Block > range.Start.Block || range.End.Offset >= session.Document.Blocks[range.End.Block].Length);
        session.FormatCharacters("Format übertragen", (_, props) => painted.Characters with { UnknownXml = props.UnknownXml });
        if (wholeParagraphs && !range.IsEmpty)
        {
            session.FormatParagraphs("Format übertragen", props => painted.Paragraph with { Numbering = props.Numbering, UnknownXml = props.UnknownXml });
        }

        ScheduleState();
    }

    /// <summary>True while a header or footer is edited.</summary>
    public bool IsEditingHeaderFooter => _story is { Note: null };

    /// <summary>The surface of the header or footer being edited (tests), or null.</summary>
    internal DocumentSurface? StorySurface => _story?.Surface;

    public DocumentLayout Layout { get; }

    /// <summary>The pages control (tests drive it with keys and the pointer).</summary>
    internal DocumentSurface Surface => _surface;

    internal ScrollViewer Scroller => _scroller;

    /// <summary>Ansicht › Lineal (hidden at first, as in Word).</summary>
    public Ruler RulerView { get; }

    /// <summary>The spelling check of this editor (red wavy lines, the context menu's suggestions, Rechtschreibung).</summary>
    internal SpellingSupport Spelling { get; }

    /// <summary>
    /// The spelling entries of the context menu for the unknown word at <paramref name="position"/>: its suggestions (a click puts it
    /// in), Alle ignorieren, Zum Wörterbuch hinzufügen; empty when the word is known.
    /// </summary>
    internal IReadOnlyList<Control> SpellingMenuItems(DocumentPosition position)
    {
        if (Spelling.ErrorAt(position) is not { } error || Spelling.Checker is not { } checker)
        {
            return [];
        }

        var items = new List<Control>();
        var suggestions = checker.Suggest(error.Word).Take(5).ToList();
        foreach (var suggestion in suggestions)
        {
            var item = new MenuItem { Header = suggestion, FontWeight = Avalonia.Media.FontWeight.SemiBold };
            item.Click += (_, _) =>
            {
                Document.Session.ReplaceWord(error.Range, suggestion);
                FocusDocument();
            };
            items.Add(item);
        }

        if (suggestions.Count == 0)
        {
            items.Add(new MenuItem { Header = "(Keine Vorschläge)", IsEnabled = false });
        }

        var ignore = new MenuItem { Header = "Alle ignorieren", Icon = new MatDesktop.MatIcon { Icon = "eye-off", Size = 16 } };
        ignore.Click += (_, _) => ThixOffice.Core.Spelling.SpellingService.Ignore(error.Word);
        var add = new MenuItem { Header = "Zum Wörterbuch hinzufügen", Icon = new MatDesktop.MatIcon { Icon = "book", Size = 16 } };
        add.Click += (_, _) => ThixOffice.Core.Spelling.SpellingService.AddToDictionary(checker, error.Word);
        items.Add(ignore);
        items.Add(add);
        items.Add(new Separator());
        return items;
    }

    /// <summary>Überprüfen › Kommentare anzeigen: the comments at the right (shown at first when the document has comments).</summary>
    internal CommentsPane CommentsView { get; }

    public bool ShowComments
    {
        get => CommentsView.IsVisible;
        set
        {
            CommentsView.IsVisible = value;
            CommentsView.Refresh();
            _surface.InvalidateVisual();
            ScheduleState();
        }
    }

    /// <summary>Neuer Kommentar: on the selection (or the word at the caret) by the author of the settings; the card opens for its text.</summary>
    public void AddComment()
    {
        CloseHeaderFooter();
        Document.Session.AddComment(ThixOffice.Core.Business.SettingsService.AuthorName, ThixOffice.Core.Business.SettingsService.AuthorInitials);
        var id = Document.Session.CommentAtCaret?.Comment.Id ?? Document.Session.Comments().LastOrDefault()?.Comment.Id;
        CommentsView.IsVisible = true;
        if (id is not null)
        {
            CommentsView.FocusComment(id);
        }

        ScheduleState();
    }

    private (long Version, Selection Selection, IReadOnlyList<(DocumentRange, bool)> Ranges)? _shading;

    /// <summary>The text of the comments to shade while the pane shows them (cached per document version and selection).</summary>
    private IReadOnlyList<(DocumentRange Range, bool Active)> CommentShading()
    {
        if (!CommentsView.IsVisible)
        {
            return [];
        }

        var session = Document.Session;
        if (_shading is { } cached && cached.Version == Document.Content.Version && cached.Selection == session.Selection)
        {
            return cached.Ranges;
        }

        var active = session.CommentAtCaret;
        var ranges = session.Comments().Where(placed => !placed.Range.IsEmpty)
            .Select(placed => (placed.Range, active is not null && placed.Reference == active.Reference)).ToList();
        _shading = (Document.Content.Version, session.Selection, ranges);
        return ranges;
    }

    /// <summary>The "Formatvorlagen" pane at the right (Alt+Strg+Umschalt+S; hidden at first).</summary>
    internal StylesPane StylesView { get; }

    public bool ShowStyles
    {
        get => StylesView.IsVisible;
        set
        {
            StylesView.IsVisible = value;
            StylesView.Refresh();
            ScheduleState();
        }
    }

    /// <summary>"Neue Formatvorlage …" (based on the style of the caret's paragraph).</summary>
    public async Task NewStyleAsync()
    {
        await Dialogs.StyleDialog.CreateAsync(_window, this);
        RefreshStyleGallery();
    }

    /// <summary>"Formatvorlage ändern …" for the style of the caret's paragraph.</summary>
    public async Task ModifyStyleAsync()
    {
        var style = Session.CurrentParagraphStyle ?? BuiltInStyles.Definition(BuiltInStyles.Normal)!;
        await Dialogs.StyleDialog.ModifyAsync(_window, this, style);
        RefreshStyleGallery();
    }

    /// <summary>"Auswahl als neue Schnellformatvorlage …": asks for the name, makes the style from the selection's format.</summary>
    public async Task StyleFromSelectionAsync()
    {
        var name = NextStyleName();
        string? problem = null;
        while (true)
        {
            var answer = await ThixOffice.Desktop.Dialogs.TextInputDialog.AskAsync(_window, "Neue Formatvorlage aus Formatierung erstellen", "Name", name, "OK", problem);
            if (answer is null)
            {
                return;
            }

            name = answer.Trim();
            problem = Session.StyleNameProblem(name);
            if (problem is null)
            {
                break;
            }
        }

        Session.StyleFromSelection(name);
        RefreshStyleGallery();
    }

    private string NextStyleName()
    {
        for (var number = 1; ; number++)
        {
            var name = $"Formatvorlage{number}";
            if (Session.StyleNameProblem(name) is null)
            {
                return name;
            }
        }
    }

    /// <summary>The gallery of the ribbon shows the document's quick styles as they are now.</summary>
    internal void RefreshStyleGallery()
    {
        if (_window.RibbonDefinition.Find(WriteCommandIds.Styles) is MatDesktop.MatRibbonGallery gallery)
        {
            var entries = WriteRibbon.StyleEntriesOf(Session);
            gallery.Entries.Clear();
            gallery.Entries.AddRange(entries);
        }

        ScheduleState();
    }

    /// <summary>Ansicht › Navigationsbereich: the headings at the left (hidden at first).</summary>
    internal NavigationPane Navigation { get; }

    public bool ShowNavigation
    {
        get => Navigation.IsVisible;
        set
        {
            Navigation.IsVisible = value;
            Navigation.Refresh();
            ScheduleState();
        }
    }

    public bool ShowRuler
    {
        get => RulerView.IsVisible;
        set
        {
            RulerView.IsVisible = value;
            ScheduleState();
        }
    }

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

    /// <summary>The pages as laid out ("Speichern unter" › PDF, "Als PDF speichern").</summary>
    public override PdfSource Pdf => new(() => [.. Layout.GetDisplayLists()], WriteFonts.Measurer);

    public override string? OpeningWarning
    {
        get
        {
            var notes = Document.Content.UnsupportedContent;
            if (notes.Count == 0)
            {
                return null;
            }

            if (Document.LoadedFormat == WordFileFormat.Odt)
            {
                var lost = string.Join(Environment.NewLine, notes.Select(note => $"• {note.Count} × {UnsupportedContent.PlainLabel(note.Kind)}"));
                return $"Dieses OpenDocument-Dokument enthält Inhalte, die ThixWrite nicht anzeigt. Beim Speichern sind sie nicht mehr enthalten:{Environment.NewLine}{lost}";
            }

            var details = string.Join(Environment.NewLine, notes.Select(note => "• " + note.Message));
            return $"Dieses Dokument enthält Inhalte, die ThixWrite noch nicht anzeigt oder bearbeitet. Sie bleiben beim Speichern als Word-Dokument erhalten:{Environment.NewLine}{details}";
        }
    }

    private bool _odtConfirmed;

    /// <summary>
    /// Saving as .odt: content kept from a Word file (fields, drawings, content controls …) has no place there – the user is asked
    /// once before it is left out. The header or footer being edited goes into the document first.
    /// </summary>
    public override async Task<bool> ConfirmSaveAsync(string path)
    {
        CommitHeaderFooter();
        if (!string.Equals(Path.GetExtension(path), ".odt", StringComparison.OrdinalIgnoreCase) || _odtConfirmed || Document.LoadedFormat == WordFileFormat.Odt)
        {
            return true;
        }

        var preserved = Document.Content.Blocks.OfType<OpaqueBlock>().Count()
            + Document.Content.Blocks.OfType<Core.Model.Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<OpaqueInline>().Count(inline => inline.Kind != PreservedKind.Bookmark && !inline.IsMarker);
        if (preserved == 0)
        {
            return true;
        }

        var answer = await ThixOffice.Desktop.Dialogs.QuestionDialog.AskAsync(_window, "Als OpenDocument speichern",
            "Dieses Dokument enthält Inhalte aus Word, die das OpenDocument-Format in ThixWrite nicht aufnehmen kann (z. B. Formen, Inhaltssteuerelemente, Formeln). Sie sind in der .odt-Datei nicht enthalten. Möchten Sie trotzdem als OpenDocument speichern?",
            new ThixOffice.Desktop.Dialogs.DialogChoice("Als OpenDocument speichern", "save", "MatButton.Primary", IsDefault: true),
            new ThixOffice.Desktop.Dialogs.DialogChoice("Abbrechen", "x", IsCancel: true));
        _odtConfirmed = answer == 0;
        return _odtConfirmed;
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
        Spelling.Dispose();
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
        if (story.Shape is { } shape)
        {
            if (!edited.Equals(story.Original))
            {
                Document.Session.SetShapeText(shape, edited);
            }

            return;
        }

        if (story.Note is { } reference)
        {
            Document.Session.SetNoteContent(reference, edited);
            return;
        }

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
        if (story.Shape is not null)
        {
            Canvas.SetLeft(story.Surface, page.X + (story.ShapeOrigin.X * _surface.Scale));
            Canvas.SetTop(story.Surface, page.Y + (story.ShapeOrigin.Y * _surface.Scale));
            return;
        }

        var top = story.Note is not null ? page.Y + (story.NoteTop * _surface.Scale)
            : story.Footer
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
        Story Original)
    {
        /// <summary>The reference of the footnote or endnote being edited (null: a header or footer).</summary>
        public DocumentPosition? Note { get; init; }

        /// <summary>The top of the note's text on its page (points).</summary>
        public double NoteTop { get; init; }

        /// <summary>The shape whose text is edited (null: a header, footer or note).</summary>
        public DocumentPosition? Shape { get; init; }

        /// <summary>The top left of the shape's text on its page (points).</summary>
        public (double X, double Y) ShapeOrigin { get; init; }
    }

    // ===== Shapes and text boxes =====

    /// <summary>
    /// Einfügen › Formen / Textfeld: the next drag on a page draws the shape (a click puts it there in Word's size); it floats in front of the
    /// text, anchored to the paragraph there, and is selected. Esc cancels.
    /// </summary>
    public void BeginDrawShape(string preset, bool textBox)
    {
        CloseHeaderFooter();
        _surface.DrawRequest = (page, rect) =>
        {
            InsertDrawnShape(page, rect, preset, textBox);
            return Task.CompletedTask;
        };
        _surface.Focus();
    }

    /// <summary>True while the next drag draws a shape (tests).</summary>
    internal bool IsDrawingShape => _surface.DrawRequest is not null;

    private void InsertDrawnShape(int page, Rect rect, string preset, bool textBox)
    {
        var (width, height) = rect.Width < 4 || rect.Height < 4 ? (textBox ? 144.0 : 72.0, 72.0) : (rect.Width, rect.Height);
        var hit = Layout.HitTest(page, rect.X, rect.Y);
        var block = hit.Position.Block;
        var (left, top) = Layout.ParagraphOrigin(block, page);
        var anchor = new DrawingAnchor(DrawingFrame.Column, Math.Round(rect.X - left, 2), null, DrawingFrame.Paragraph, Math.Round(rect.Y - top, 2), null, DrawingWrap.None, false,
            9, 0, 9, 0, 251659264 + Document.Content.Blocks.Count);
        var shape = textBox ? EditingSession.DefaultTextBox() : EditingSession.DefaultShape(preset);
        Document.Session.InsertShape(new ShapeInline(shape, width, height, CharacterProperties.Empty) { Anchor = anchor, IsTextBox = textBox }, new DocumentPosition(block, 0));
        if (textBox)
        {
            EditShapeText(Document.Session.SelectedObject!.Value.Position);
        }

        ScheduleState();
    }

    /// <summary>The text of a shape opened in place (like a note): typing goes there; a click outside, Esc or saving puts it into the shape.</summary>
    public void EditShapeText(DocumentPosition position)
    {
        CloseHeaderFooter();
        Layout.Update();
        var inline = Document.Content.ParagraphAt(position.Block)?.InlineAt(position.Offset);
        var shape = inline switch
        {
            ShapeInline own => own,
            OpaqueInline opaque => EditingSession.ToShape(opaque),
            _ => null,
        };
        if (shape is null || Layout.GetObjectRect(position) is not { } rect)
        {
            return;
        }

        var (insetLeft, insetTop, insetRight, insetBottom) = shape.Shape.Insets;
        var width = Math.Max(12, rect.Width - insetLeft - insetRight);
        var original = shape.Shape.Text ?? new Story([Paragraph.Empty(new ParagraphProperties { Alignment = ParagraphAlignment.Center, SpacingAfter = 0 },
            new CharacterProperties { Color = TextColor.FromHex("FFFFFF") })]);
        var content = Document.Content;
        var storySection = Layout.Pages[rect.Page].Section.Properties with
        {
            PageWidth = width,
            PageHeight = 5000,
            MarginLeft = 0,
            MarginRight = 0,
            MarginTop = 0,
            MarginBottom = 0,
            Gutter = 0,
            ColumnCount = 1,
            TitlePage = false,
            Headers = HeaderFooterSet.Empty,
            Footers = HeaderFooterSet.Empty,
        };
        var document = new Core.Model.Document(original.Blocks, content.Styles, content.Numbering, storySection, content.Theme, content.Settings);
        var session = new EditingSession(document);
        var layout = new DocumentLayout(document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver);
        session.Navigator = layout;
        var surface = new DocumentSurface(session, layout) { IsStory = true, MinimumHeight = Math.Max(12, rect.Height - insetTop - insetBottom), Scale = _surface.Scale };
        Avalonia.Automation.AutomationProperties.SetName(surface, shape.IsTextBox ? "Textfeld" : "Text in der Form");
        _story = new StoryEditing(document, session, layout, surface, -1, HeaderFooterKind.Default, false, rect.Page, original)
        {
            Shape = position,
            ShapeOrigin = (rect.X + insetLeft, rect.Y + insetTop),
        };
        _overlay.Children.Add(surface);
        session.StateChanged += OnStateChanged;
        session.ContentChanged += OnStoryChanged;
        surface.Refreshed += (_, _) => ScheduleState();
        surface.KeyDown += OnStoryKey;
        PositionStory();
        session.SetCaret(document.End);
        Dispatcher.UIThread.Post(() => surface.Focus(), DispatcherPriority.Input);
        ScheduleState();
    }

    /// <summary>True while the text of a shape is edited.</summary>
    public bool IsEditingShapeText => _story?.Shape is not null;

    // ===== Footnotes and endnotes =====

    /// <summary>True while the text of a footnote or endnote is edited.</summary>
    public bool IsEditingNote => _story?.Note is not null;

    /// <summary>The reference of the note being edited, or null.</summary>
    public DocumentPosition? EditedNote => _story?.Note;

    /// <summary>Referenzen › Fußnote / Endnote einfügen: the reference at the caret, then its text opened for typing.</summary>
    public void InsertNote(NoteKind kind)
    {
        CloseHeaderFooter();
        var reference = Document.Session.InsertNote(kind);
        EditNote(reference);
    }

    /// <summary>
    /// Opens the text of the note referenced at <paramref name="reference"/> in place, at the foot of its page (or after the text for an
    /// endnote): typing goes there, the reference keeps its place; a click on the text, Esc or saving puts it into the document as one step.
    /// <paramref name="at"/>: where the user clicked (page points), the caret goes there; else to the end.
    /// </summary>
    public void EditNote(DocumentPosition reference, Point? at = null)
    {
        CloseHeaderFooter();
        Layout.Update();
        if (Document.Session.NoteAt(reference) is not { } note || Layout.FindNote(reference) is not { } found)
        {
            return;
        }

        var (page, placed) = found;
        var setup = Layout.Pages[page].Section.Properties;
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
        var document = new Core.Model.Document(note.Content.Blocks, content.Styles, content.Numbering, storySection, content.Theme, content.Settings);
        var session = new EditingSession(document);
        var layout = new DocumentLayout(document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver) { NoteMarkText = placed.Label };
        session.Navigator = layout;
        var height = Layout.Pages.SelectMany(each => each.Footnotes.Concat(each.Endnotes)).Where(each => each.Reference == reference)
            .Sum(each => each.Lines.Sum(line => line.Line.Height));
        var surface = new DocumentSurface(session, layout) { IsStory = true, MinimumHeight = Math.Max(12, height), Scale = _surface.Scale };
        Avalonia.Automation.AutomationProperties.SetName(surface, NoteNumbering.Label(note.Kind));
        _story = new StoryEditing(document, session, layout, surface, -1, HeaderFooterKind.Default, false, page, note.Content)
        {
            Note = reference,
            NoteTop = placed.Lines[0].Top,
        };
        _overlay.Children.Add(surface);
        session.StateChanged += OnStateChanged;
        session.ContentChanged += OnStoryChanged;
        surface.Refreshed += (_, _) => ScheduleState();
        surface.KeyDown += OnStoryKey;
        PositionStory();
        var caret = at is { } point ? layout.HitTest(0, point.X, point.Y - placed.Lines[0].Top).Position : document.End;
        session.SetCaret(caret);
        Dispatcher.UIThread.Post(() =>
        {
            surface.Focus();
            surface.BringIntoView();
        }, DispatcherPriority.Input);
        ScheduleState();
    }

    /// <summary>A click on the text of a note at the foot of a page (or after the text) opens it there.</summary>
    private void OnNoteRequested(object? sender, (PlacedNote Note, Point At) request)
    {
        if (request.Note.IsContinuation)
        {
            EditNote(request.Note.Reference);
            return;
        }

        EditNote(request.Note.Reference, request.At);
    }

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

        if (!textOnly && await TryGetHtmlAsync(clipboard) is { } html && PasteHtml(html))
        {
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

    /// <summary>
    /// Pastes HTML of another program (a browser, Word, a mail): its formatting, lists, tables, links and embedded pictures, sanitized
    /// (<see cref="HtmlImport"/>). False when it holds nothing to paste.
    /// </summary>
    public bool PasteHtml(string html)
    {
        if (HtmlImport.Import(HtmlImport.FromClipboardFormat(html)) is not { } fragment)
        {
            return false;
        }

        Session.Paste(fragment);
        return true;
    }

    /// <summary>The HTML on the clipboard (Windows "HTML Format", macOS public.html), or null.</summary>
    private static async Task<string?> TryGetHtmlAsync(IClipboard clipboard)
    {
        var format = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "HTML Format");
        try
        {
            return await clipboard.TryGetValueAsync(format) is { Length: > 0 } bytes ? System.Text.Encoding.UTF8.GetString(bytes).TrimEnd(' ') : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
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
    internal void ScheduleState()
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
            RulerView.InvalidateVisual();
            Navigation.Refresh();
            CommentsView.Refresh();
            StylesView.Refresh();
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
        }, DispatcherPriority.Background);
    }

    /// <summary>Tabellenentwurf and Layout (contextual) show while the caret is in a table, Bildformat while a picture is selected, as in Word.</summary>
    private void ShowTableTabs()
    {
        if (IsEditingHeaderFooter)
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

        if (Session.SelectedShape is not null)
        {
            _window.RibbonDefinition.ShowContext(ShapeCommands.Context);
        }
        else
        {
            _window.RibbonDefinition.HideContext(ShapeCommands.Context);
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
        var menu = OfficeCommandMenu.Create(_window,
            [
                WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Paste, WriteCommandIds.PasteText, null,
                WriteCommandIds.FontDialog, WriteCommandIds.ParagraphDialog, null,
                WriteCommandIds.Hyperlink, WriteCommandIds.HyperlinkOpen, WriteCommandIds.HyperlinkRemove, null,
                WriteCommandIds.NewComment,
            ],
            [WriteCommandIds.HyperlinkOpen, WriteCommandIds.HyperlinkRemove]);
        var spelling = new List<Control>();
        menu.Opening += (_, _) =>
        {
            // An unknown word under the right click: its suggestions first, as in Word.
            foreach (var item in spelling)
            {
                menu.Items.Remove(item);
            }

            spelling.Clear();
            spelling.AddRange(SpellingMenuItems(Document.Session.Selection.Start));
            for (var index = 0; index < spelling.Count; index++)
            {
                menu.Items.Insert(index, spelling[index]);
            }
        };
        return menu;
    }
}
