using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Documents;
using ThixShow.Core.Editing;
using ThixShow.Core.Formats.Odp;
using ThixShow.Core.Model;
using ThixShow.Show;

namespace ThixShow.Editor;

/// <summary>The views of PowerPoint's status bar.</summary>
public enum ShowView
{
    Normal,
    Sorter,
    Reading,
}

/// <summary>Which part of the window has the keyboard (Entf, Kopieren and Strg+A act there).</summary>
public enum EditorArea
{
    Slide,
    Slides,
    Notes,
}

/// <summary>
/// ThixShow's editor of one <see cref="ShowDocument"/> in a window, laid out like PowerPoint's normal view: the thumbnail pane at
/// the left (<see cref="SlideListView"/>), the slide in the middle on its pasteboard (<see cref="SlideCanvas"/>, fitted to the
/// window until a zoom is chosen), the notes below (<see cref="NotesPane"/>), both with splitters; the slide sorter and the reading
/// view take the whole area. The status bar: "Folie X von Y", Notizen, the four views, the zoom and "An Fenster anpassen".
/// Everything that changes the presentation goes through the session; the views follow its events (ribbon and status once per
/// dispatcher round).
/// </summary>
public sealed class ShowEditor : OfficeEditor
{
    public const string SlideItem = "slide";

    private readonly OfficeWindow _window;
    private readonly Grid _view;
    private readonly Grid _normal;
    private readonly ScrollViewer _canvasScroller;
    private readonly ScrollViewer _paneScroller;
    private readonly ScrollViewer _sorterScroller;
    private readonly GridSplitter _notesSplitter;
    private readonly Dictionary<ShowView, Button> _viewButtons = [];
    private readonly Button _notesButton;
    private bool _statePending;
    private bool _fitting;
    private uint? _shownSlide;

    public ShowEditor(OfficeWindow window, ShowDocument document)
    {
        _window = window;
        Document = document;
        Canvas = new SlideCanvas(this);
        Thumbnails = new SlideListView(this, isSorter: false);
        Sorter = new SlideListView(this, isSorter: true);
        Notes = new NotesPane(this);
        FindBar = new ShowFindBar(this) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 26, 0), IsVisible = false };

        _canvasScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Panel { Children = { Canvas, Canvas.Overlay } },
        };
        _canvasScroller.Use(ScrollViewer.BackgroundProperty, "Mat.Bg");
        _canvasScroller.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new ViewportObserver(this));
        _canvasScroller.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        _paneScroller = new ScrollViewer { Content = Thumbnails, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Thumbnails.Scroller = _paneScroller;
        _paneScroller.ScrollChanged += (_, _) => Thumbnails.InvalidateVisual();
        _paneScroller.Use(ScrollViewer.BackgroundProperty, "Mat.Surface");

        _sorterScroller = new ScrollViewer { Content = Sorter, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false };
        Sorter.Scroller = _sorterScroller;
        _sorterScroller.ScrollChanged += (_, _) => Sorter.InvalidateVisual();

        // The normal view: pane | splitter | (slide / splitter / notes).
        var center = new Grid { RowDefinitions = new RowDefinitions("*,Auto,110"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        AnimationPane = new AnimationPane(this) { IsVisible = false };
        Grid.SetColumn(AnimationPane, 1);
        Grid.SetRowSpan(AnimationPane, 3);
        center.Children.Add(AnimationPane);
        _notesSplitter = new GridSplitter { Height = 5, ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
        _notesSplitter.Use(GridSplitter.BackgroundProperty, "Mat.Border");
        Grid.SetRow(_canvasScroller, 0);
        Grid.SetRow(FindBar, 0);
        Grid.SetRow(_notesSplitter, 1);
        Grid.SetRow(Notes, 2);
        center.Children.Add(_canvasScroller);
        center.Children.Add(FindBar);
        center.Children.Add(_notesSplitter);
        center.Children.Add(Notes);
        NotesRow = center.RowDefinitions[2];

        var paneSplitter = new GridSplitter { Width = 5, ResizeDirection = GridResizeDirection.Columns };
        paneSplitter.Use(GridSplitter.BackgroundProperty, "Mat.Border");
        _normal = new Grid { ColumnDefinitions = new ColumnDefinitions("190,Auto,*") };
        _normal.ColumnDefinitions[0].MinWidth = 90;
        _normal.ColumnDefinitions[0].MaxWidth = 420;
        Grid.SetColumn(paneSplitter, 1);
        Grid.SetColumn(center, 2);
        _normal.Children.Add(_paneScroller);
        _normal.Children.Add(paneSplitter);
        _normal.Children.Add(center);

        Reading = new ReadingView(this) { IsVisible = false };
        _view = new Grid { Children = { _normal, _sorterScroller, Reading } };
        _view.Use(Panel.BackgroundProperty, "Mat.Bg");
        _view.ActualThemeVariantChanged += (_, _) => RefreshAll();

        _notesButton = StatusToggle("Notizen", "comment", "Notizen ein- oder ausblenden", () => SetNotesVisible(!NotesVisible));
        SetActive(_notesButton, true);
        var statusRight = window.StatusBar.View.Right;
        statusRight.Add(_notesButton);
        foreach (var (view, icon, label) in new[] { (ShowView.Normal, "sidebar", "Normal"), (ShowView.Sorter, "grid", "Foliensortierung"), (ShowView.Reading, "book", "Leseansicht") })
        {
            var button = StatusToggle(null, icon, label, () => SetView(view));
            _viewButtons[view] = button;
            statusRight.Add(button);
        }

        var showButton = StatusToggle(null, "present", "Bildschirmpräsentation (Umschalt+F5)", () => _ = SlideShowWindow.StartAsync(_window, this, fromCurrent: true));
        statusRight.Add(showButton);
        statusRight.Add(StatusToggle(null, "zoom-in", "An Fenster anpassen", FitToWindow));

        Attach();
        document.ContentReplaced += OnContentReplaced;
        document.Writing += (_, _) => Notes.Commit();
        FitToWindow();
        UpdateViewButtons();
    }

    public ShowDocument Document { get; }

    public EditingSession Session => Document.Session;

    public override Control View => _view;

    internal OfficeWindow Window => _window;

    internal SlideCanvas Canvas { get; }

    internal SlideListView Thumbnails { get; }

    internal SlideListView Sorter { get; }

    internal NotesPane Notes { get; }

    internal ReadingView Reading { get; }

    internal ShowFindBar FindBar { get; }

    internal RowDefinition NotesRow { get; }

    internal SlidePictures Pictures { get; } = new();

    /// <summary>The view shown (Normal, Foliensortierung, Leseansicht).</summary>
    public ShowView CurrentView { get; private set; } = Editor.ShowView.Normal;

    /// <summary>Which part has the keyboard.</summary>
    public EditorArea FocusedArea { get; set; } = EditorArea.Slide;

    /// <summary>True while the zoom follows the window (until a zoom is chosen).</summary>
    public bool FitsWindow { get; private set; } = true;

    /// <summary>Snap to the grid while moving (Ansicht › Am Raster ausrichten).</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>Smart guides while moving (Ansicht › Intelligente Führungslinien; on like PowerPoint).</summary>
    public bool SmartGuides { get; set; } = true;

    public bool NotesVisible { get; private set; } = true;

    /// <summary>The colour Schriftfarbe's button applies (the last one chosen; null = Automatisch).</summary>
    public string? FontColor { get; set; } = ShowPalette.DefaultFontColor;

    /// <summary>The colour of Texthervorhebungsfarbe's button.</summary>
    public string? HighlightColor { get; set; } = ShowPalette.StandardColors[3];

    /// <summary>The colour Fülleffekt's button applies (null = Keine Füllung).</summary>
    public string? FillColor { get; set; } = ShowPalette.DefaultFillColor;

    /// <summary>The colour Formkontur's button applies (null = Keine Kontur).</summary>
    public string? OutlineColor { get; set; } = ShowPalette.DefaultOutlineColor;

    /// <summary>The colour of Hintergrund formatieren's button.</summary>
    public string? BackgroundColor { get; set; } = ShowPalette.StandardColors[6];

    /// <summary>The direction new effects of Animationen get (Effektoptionen).</summary>
    public AnimationDirection AnimationDirection { get; set; } = AnimationDirection.Bottom;

    /// <summary>The Animationsbereich at the right of the slide.</summary>
    internal AnimationPane AnimationPane { get; private set; } = null!;

    public bool AnimationPaneVisible => AnimationPane.IsVisible;

    /// <summary>Kopieren, Ausschneiden, Einfügen.</summary>
    internal ShowClipboard Clipboard => _clipboard ??= new ShowClipboard(this);

    private ShowClipboard? _clipboard;

    /// <summary>The index of the current slide (-1 without slides).</summary>
    public int CurrentIndex => Session.Selection.CurrentSlide is { } id ? Session.Presentation.IndexOf(id) : -1;

    /// <summary>The ids of the selected slides (the current one when none is marked).</summary>
    public IReadOnlyList<uint> SelectedSlideIds
    {
        get
        {
            var ids = Session.Selection.Slides.ToList();
            if (ids.Count == 0 && Session.Selection.CurrentSlide is { } current)
            {
                ids.Add(current);
            }

            return ids;
        }
    }

    /// <summary>The slides, one page each ("Speichern unter" › PDF, "Als PDF speichern").</summary>
    public override PdfSource Pdf => new(() => ThixShow.Pages.ShowPages.DisplayLists(this, 0), ThixOffice.Desktop.Rendering.OfficeFonts.Skia);

    public override string? OpeningWarning
    {
        get
        {
            if (Document.LoadedFormat == ShowFileFormat.Odp)
            {
                var lost = Document.UnsupportedContent.Select(content => "• " + content.PlainMessage).ToList();
                return lost.Count == 0
                    ? null
                    : $"Diese OpenDocument-Präsentation enthält Inhalte, die ThixShow nicht anzeigt. Beim Speichern sind sie nicht mehr enthalten:{Environment.NewLine}{string.Join(Environment.NewLine, lost)}";
            }

            var notes = Document.UnsupportedContent.Select(content => "• " + content.Message).ToList();
            var macros = Document.HasMacros ? "Diese Präsentation enthält Makros. Makros werden nicht ausgeführt, bleiben aber erhalten." : null;
            if (notes.Count == 0)
            {
                return macros;
            }

            var text = $"Diese Präsentation enthält Inhalte, die ThixShow noch nicht bearbeitet. Sie bleiben beim Speichern als PowerPoint-Präsentation erhalten:{Environment.NewLine}{string.Join(Environment.NewLine, notes)}";
            return macros is null ? text : macros + Environment.NewLine + text;
        }
    }

    public override void FocusDocument()
    {
        switch (CurrentView)
        {
            case Editor.ShowView.Sorter:
                Sorter.Focus();
                break;
            case Editor.ShowView.Reading:
                Reading.Focus();
                break;
            default:
                Canvas.Focus();
                break;
        }
    }

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        var count = Session.Presentation.SlideCount;
        var index = CurrentIndex;
        statusBar.SetItem(SlideItem, index < 0 ? $"{count.ToString(CultureInfo.CurrentCulture)} Folien" : $"Folie {(index + 1).ToString(CultureInfo.CurrentCulture)} von {count.ToString(CultureInfo.CurrentCulture)}");
    }

    public override void Detach()
    {
        Notes.Commit();
        Session.PresentationChanged -= OnPresentationChanged;
        Session.SelectionChanged -= OnSelectionChanged;
        Session.StateChanged -= OnStateChanged;
        Document.ContentReplaced -= OnContentReplaced;
        Reading.Stop();
    }

    /// <summary>What the user agreed to leave out of an .odp file (asked again when more would be lost, like ThixCalc's .ods).</summary>
    private IReadOnlyList<string> _odpConfirmed = [];

    /// <summary>
    /// Before a save: the notes being typed go in; a presentation with macros saved as .pptx is warned that the macros stay behind;
    /// saving as .odp names what the OpenDocument format cannot take (charts, SmartArt, effects …) and asks – again only when
    /// something new would be lost (a chart added to a presentation opened from .odp, say).
    /// </summary>
    public override async Task<bool> ConfirmSaveAsync(string path)
    {
        Notes.Commit();
        if (Path.GetExtension(path).Equals(".odp", StringComparison.OrdinalIgnoreCase))
        {
            // What an opened .odp already lacked (comments, fonts … – named by the opening warning) is no news when it is saved again.
            var losses = OdpWriter.Losses(Session.Presentation)
                .Where(loss => Document.LoadedFormat != ShowFileFormat.Odp || Session.Presentation.UnsupportedContent.All(content => content.Kind != loss.Kind))
                .Select(loss => loss.PlainMessage).Distinct().ToList();
            if (!losses.Except(_odpConfirmed).Any())
            {
                return true;
            }

            var list = string.Join(Environment.NewLine, losses.Select(loss => "• " + loss));
            var answer = await QuestionDialog.AskAsync(_window, "Als OpenDocument speichern",
                $"Diese Präsentation enthält Inhalte, die das OpenDocument-Format in ThixShow nicht aufnehmen kann. Sie sind in der .odp-Datei nicht enthalten:{Environment.NewLine}{list}{Environment.NewLine}Möchten Sie trotzdem als OpenDocument speichern?",
                new DialogChoice("Als OpenDocument speichern", "save", "MatButton.Primary", IsDefault: true),
                new DialogChoice("Abbrechen", "x", IsCancel: true));
            if (answer != 0)
            {
                return false;
            }

            _odpConfirmed = losses;
            return true;
        }

        if (Path.GetExtension(path).Equals(".pptx", StringComparison.OrdinalIgnoreCase) && Document.HasMacros)
        {
            var save = await QuestionDialog.AskAsync(_window, "Makros gehen verloren",
                "Diese Präsentation enthält Makros. Eine .pptx-Datei kann sie nicht speichern – wählen Sie „PowerPoint-Präsentation mit Makros (.pptm)“, um sie zu behalten.",
                new DialogChoice("Ohne Makros speichern", "save", "MatButton.Primary"),
                new DialogChoice("Abbrechen", "x", IsDefault: true, IsCancel: true));
            return save == 0;
        }

        return true;
    }

    /// <summary>Runs a session command; a refusal of the core (BusinessException) is shown as a message.</summary>
    public Task<bool> RunAsync(string label, Action action) => OfficeWindow.RunAsync(_window, label, () =>
    {
        action();
        return Task.FromResult(true);
    });

    // ===== Views and zoom =====

    /// <summary>Normal, Foliensortierung or Leseansicht (Bildschirmpräsentation is a window of its own).</summary>
    public void SetView(ShowView view)
    {
        Notes.Commit();
        Session.EndTextEditing();
        CurrentView = view;
        _normal.IsVisible = view == Editor.ShowView.Normal;
        _sorterScroller.IsVisible = view == Editor.ShowView.Sorter;
        Reading.IsVisible = view == Editor.ShowView.Reading;
        if (view == Editor.ShowView.Reading)
        {
            Reading.Start(Math.Max(0, CurrentIndex));
        }
        else
        {
            Reading.Stop();
        }

        if (view == Editor.ShowView.Sorter)
        {
            Session.SelectSlides(SelectedSlideIds);
            Sorter.Refresh();
            Dispatcher.UIThread.Post(Sorter.BringCurrentIntoView, DispatcherPriority.Background);
        }

        UpdateViewButtons();
        FocusDocument();
        Dispatcher.UIThread.Post(FocusDocument, DispatcherPriority.Input);
        ScheduleState();
    }

    /// <summary>The reading view ended (Esc or past the last slide): back to the normal view on the slide it showed.</summary>
    internal void EndReading(int slideIndex)
    {
        if (slideIndex >= 0 && slideIndex < Session.Presentation.SlideCount)
        {
            Session.GoToSlide(Session.Presentation.Slides[slideIndex].Id);
        }

        SetView(Editor.ShowView.Normal);
    }

    private void UpdateViewButtons()
    {
        foreach (var (view, button) in _viewButtons)
        {
            SetActive(button, view == CurrentView);
        }
    }

    /// <summary>Ansicht › Notizen and the status bar's Notizen: the notes pane below the slide.</summary>
    public void SetNotesVisible(bool visible)
    {
        NotesVisible = visible;
        Notes.IsVisible = visible;
        _notesSplitter.IsVisible = visible;
        NotesRow.Height = visible ? new GridLength(Math.Max(60, NotesRow.ActualHeight > 0 ? NotesRow.ActualHeight : 110)) : new GridLength(0);
        SetActive(_notesButton, visible);
        ScheduleState();
    }

    /// <summary>Animationen › Animationsbereich: shows or hides the list of the slide's effects.</summary>
    public void ToggleAnimationPane()
    {
        AnimationPane.IsVisible = !AnimationPane.IsVisible;
        AnimationPane.Load();
        ScheduleState();
    }

    /// <summary>Vorschau (Übergänge, Animationen): the current slide plays on the slide area once, then the editor shows it again.</summary>
    public void Preview()
    {
        if (CurrentIndex < 0 || CurrentView != Editor.ShowView.Normal)
        {
            return;
        }

        Notes.Commit();
        Session.EndTextEditing();
        Canvas.PlayPreview(CurrentIndex);
    }

    /// <summary>"An Fenster anpassen": the zoom that shows the whole slide, kept while the window changes.</summary>
    public void FitToWindow()
    {
        FitsWindow = true;
        ApplyFit();
    }

    private void ApplyFit()
    {
        var viewport = _canvasScroller.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var size = Session.Presentation.SlideSize;
        var (width, height) = (Units.EmuToPoints(size.Width) * 96 / 72, Units.EmuToPoints(size.Height) * 96 / 72);
        var zoom = Math.Floor(Math.Min((viewport.Width - (2 * SlideCanvas.PasteboardMargin)) / width, (viewport.Height - (2 * SlideCanvas.PasteboardMargin)) / height) * 100);
        _fitting = true;
        Zoom = Math.Max(MinZoom, zoom);
        _fitting = false;
    }

    protected override void OnZoomChanged(double zoom)
    {
        if (!_fitting)
        {
            FitsWindow = false;
        }

        Canvas.Refresh();
        ScheduleState();
    }

    public override void RequestZoomLevel() => _ = Dialogs.ZoomDialog.ShowAsync(_window, this);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(modifier) || e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        Zoom = e.Delta.Y > 0 ? Math.Floor(Zoom / 10 + 1) * 10 : Math.Ceiling(Zoom / 10 - 1) * 10;
    }

    private sealed class ViewportObserver(ShowEditor editor) : IObserver<Size>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Size value)
        {
            editor.Canvas.MinWidth = value.Width;
            editor.Canvas.MinHeight = value.Height;
            if (editor.FitsWindow)
            {
                editor.ApplyFit();
            }
        }
    }

    private static Button StatusToggle(string? text, string icon, string tooltip, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new MatIcon { Icon = icon, Size = 14 } } };
        if (text is not null)
        {
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        }

        var button = new Button { Content = content, Margin = new Thickness(1, 0) }.Themed("MatButton.Status");
        Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>The active view and the shown notes are marked in the status bar like a pressed button.</summary>
    private static void SetActive(Button button, bool active)
    {
        if (active)
        {
            button.Use(Button.BackgroundProperty, "Mat.Selection");
        }
        else
        {
            button.ClearValue(Button.BackgroundProperty);
        }

        button.Tag = active;
    }

    /// <summary>Suchen / Ersetzen: the bar at the top right of the slide.</summary>
    public void OpenFind(bool replace)
    {
        if (CurrentView != Editor.ShowView.Normal)
        {
            SetView(Editor.ShowView.Normal);
        }

        Notes.Commit();
        FindBar.Open(replace);
    }

    // ===== Slides =====

    /// <summary>Folie löschen: the selected slides (the presentation keeps at least none – PowerPoint allows an empty one).</summary>
    public Task DeleteSlidesAsync()
    {
        var ids = SelectedSlideIds;
        return ids.Count == 0 ? Task.CompletedTask : RunAsync(ids.Count == 1 ? "Folie löschen" : "Folien löschen", () => Session.DeleteSlides(ids));
    }

    /// <summary>Pictures from files: each read in the business layer, inserted centred (or where they were dropped), one undo step.</summary>
    public async Task InsertPicturesAsync(IReadOnlyList<string> paths, (double X, double Y)? at = null)
    {
        if (paths.Count == 0)
        {
            return;
        }

        await OfficeWindow.RunAsync(_window, "Bilder einfügen", () =>
        {
            var images = paths.Select(path => ImageData.From(ImageFiles.Read(path))).ToList();
            Session.Group(images.Count == 1 ? "Bild einfügen" : "Bilder einfügen", () =>
            {
                foreach (var image in images)
                {
                    var id = Session.InsertPicture(image);
                    if (at is { } point && Session.CurrentSlide is { } slide && slide.FindShape(id)?.Transform is { } transform)
                    {
                        var dx = Units.PointsToEmu(point.X) - (transform.X + (transform.Width / 2));
                        var dy = Units.PointsToEmu(point.Y) - (transform.Y + (transform.Height / 2));
                        Session.MoveShapes(dx, dy, [id]);
                    }
                }
            });
            return Task.FromResult(true);
        });
        Canvas.Focus();
    }

    // ===== Following the session =====

    private void Attach()
    {
        Session.PresentationChanged += OnPresentationChanged;
        Session.SelectionChanged += OnSelectionChanged;
        Session.StateChanged += OnStateChanged;
        if (Session.Selection.CurrentSlide is null && Session.Presentation.SlideCount > 0)
        {
            Session.GoToSlide(Session.Presentation.Slides[0].Id);
        }

        Pictures.Clear();
        RefreshAll();
    }

    private void RefreshAll()
    {
        Pictures.Clear();
        Thumbnails.Refresh();
        Sorter.Refresh();
        Canvas.Refresh();
        Notes.Load();
        ScheduleState();
    }

    private void OnContentReplaced(object? sender, EventArgs e)
    {
        Notes.Reset();
        Attach();
        FitToWindow();
    }

    private void OnPresentationChanged(object? sender, PresentationChangedEventArgs e)
    {
        Pictures.OnPresentationChanged(e);
        if (e.DesignChanged || e.SlideListChanged)
        {
            Thumbnails.Refresh();
            Sorter.Refresh();
            if (FitsWindow)
            {
                ApplyFit();
            }
        }
        else
        {
            Thumbnails.InvalidateVisual();
            Sorter.InvalidateVisual();
        }

        Canvas.Refresh();
        Notes.Load();
        AnimationPane.Load();
        ScheduleState();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (Session.Selection.CurrentSlide != _shownSlide)
        {
            _shownSlide = Session.Selection.CurrentSlide;
            Notes.Load();
            Canvas.Refresh();
            Thumbnails.BringCurrentIntoView();
        }

        Thumbnails.InvalidateVisual();
        Sorter.InvalidateVisual();
        AnimationPane.Load();
        Canvas.Overlay.ResetBlink();
        Canvas.InputClient.NotifyChanged();
        ScheduleState();
    }

    private void OnStateChanged(object? sender, EventArgs e) => ScheduleState();

    /// <summary>The ribbon (incl. contextual tabs) and the status bar follow the session, once per dispatcher round.</summary>
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
            ShowContextMenus.UpdateContextTabs(_window, this);
            _window.Commands.Invalidate();
            UpdateStatus(_window.StatusBar);
        }, DispatcherPriority.Background);
    }
}
