using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Rendering;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>An entry of the slide list: a section header or a slide.</summary>
internal sealed record SlideListEntry(Rect Rect, int SlideIndex, SlideSection? Section);

/// <summary>
/// The slides as a list of thumbnails: the pane at the left of the normal view (one column, the number beside each slide) and the
/// slide sorter (a grid). Drawn by itself and virtualized (only the thumbnails in view are drawn; each is recorded once per change
/// of its slide). Sections are headers that collapse. Click selects (Strg toggles, Umschalt extends), dragging reorders, the right
/// button opens the slide menu; keys: ↑/↓ (and ←/→ in the sorter), Pos1/Ende, Entf, Eingabe (new slide), Strg+A.
/// </summary>
internal sealed class SlideListView : Control
{
    private const double SectionHeight = 28;
    private readonly ShowEditor _editor;
    private readonly HashSet<string> _collapsed = [];
    private List<SlideListEntry> _entries = [];
    private double _laidOutWidth = -1;
    private int _anchor = -1;
    private (int Index, Point Start, bool Moved)? _press;
    private int _dropIndex = -1;

    public SlideListView(ShowEditor editor, bool isSorter)
    {
        _editor = editor;
        IsSorter = isSorter;
        Focusable = true;
        ClipToBounds = true;
        Avalonia.Automation.AutomationProperties.SetName(this, isSorter ? "Foliensortierung" : "Folienminiaturen");
    }

    /// <summary>True for the slide sorter (a grid), false for the thumbnail pane.</summary>
    public bool IsSorter { get; }

    /// <summary>The width of a thumbnail in the sorter (its zoom).</summary>
    public double SorterThumbnailWidth { get; set; } = 220;

    /// <summary>The scroll viewer around the list (the visible part decides what is drawn).</summary>
    public ScrollViewer? Scroller { get; set; }

    public IReadOnlyList<SlideListEntry> Entries => _entries;

    private Presentation Presentation => _editor.Session.Presentation;

    private double Aspect
    {
        get
        {
            var size = Presentation.SlideSize;
            return size.Width == 0 ? 0.5625 : size.Height / (double)size.Width;
        }
    }

    /// <summary>The slides or their order changed: lay out again.</summary>
    public void Refresh()
    {
        _laidOutWidth = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Collapses or opens a section.</summary>
    public void ToggleSection(string sectionId)
    {
        if (!_collapsed.Remove(sectionId))
        {
            _collapsed.Add(sectionId);
        }

        Refresh();
    }

    public bool IsCollapsed(string sectionId) => _collapsed.Contains(sectionId);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(Bounds.Width, 200) : availableSize.Width;
        Layout(width);
        var height = _entries.Count == 0 ? 0 : _entries.Max(entry => entry.Rect.Bottom) + 12;
        return new Size(width, height);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Math.Abs(e.NewSize.Width - _laidOutWidth) > 0.5)
        {
            Layout(e.NewSize.Width);
            InvalidateMeasure();
        }
    }

    /// <summary>Places section headers and thumbnails for <paramref name="width"/>.</summary>
    private void Layout(double width)
    {
        _laidOutWidth = width;
        var entries = new List<SlideListEntry>();
        var presentation = Presentation;
        var sectionOf = new Dictionary<uint, SlideSection>();
        foreach (var section in presentation.Sections)
        {
            foreach (var id in section.SlideIds)
            {
                sectionOf[id] = section;
            }
        }

        var numberWidth = IsSorter ? 0 : 30;
        var gap = IsSorter ? 18 : 10;
        var thumbWidth = IsSorter ? SorterThumbnailWidth : Math.Max(40, width - numberWidth - 18);
        var thumbHeight = thumbWidth * Aspect;
        var columns = IsSorter ? Math.Max(1, (int)((width - gap) / (thumbWidth + gap))) : 1;
        var y = 8.0;
        var column = 0;
        SlideSection? current = null;
        for (var index = 0; index < presentation.Slides.Count; index++)
        {
            var slide = presentation.Slides[index];
            var section = sectionOf.GetValueOrDefault(slide.Id);
            if (section is not null && !ReferenceEquals(section, current))
            {
                current = section;
                if (column > 0)
                {
                    y += thumbHeight + gap + (IsSorter ? 20 : 0);
                    column = 0;
                }

                entries.Add(new SlideListEntry(new Rect(4, y, width - 8, SectionHeight), -1, section));
                y += SectionHeight + 4;
            }

            if (section is not null && _collapsed.Contains(section.Id))
            {
                continue;
            }

            var x = IsSorter ? gap + (column * (thumbWidth + gap)) : numberWidth;
            entries.Add(new SlideListEntry(new Rect(x, y, thumbWidth, thumbHeight), index, section));
            column++;
            if (column >= columns)
            {
                column = 0;
                y += thumbHeight + gap + (IsSorter ? 20 : 0);
            }
        }

        _entries = entries;
    }

    // ===== Drawing =====

    public override void Render(DrawingContext context)
    {
        if (Math.Abs(Bounds.Width - _laidOutWidth) > 0.5)
        {
            Layout(Bounds.Width);
        }

        context.FillRectangle(Ui.Brush(IsSorter ? "Mat.Bg" : "Mat.Surface"), new Rect(Bounds.Size));
        var visible = Scroller is { } scroller ? new Rect(scroller.Offset.X, scroller.Offset.Y, scroller.Viewport.Width, scroller.Viewport.Height).Inflate(40) : new Rect(Bounds.Size);
        var session = _editor.Session;
        var presentation = Presentation;
        var selected = session.Selection.Slides.ToHashSet();
        var current = session.Selection.CurrentSlide;
        var pictures = new List<(Rect Rect, SKPicture Picture)>();
        var text = Ui.Brush("Mat.Text");
        var muted = Ui.Brush("Mat.TextMuted");
        var accent = Ui.Brush("Mat.Accent");
        var border = new Pen(Ui.Brush("Mat.Border"), 1);
        foreach (var entry in _entries)
        {
            if (!entry.Rect.Intersects(visible))
            {
                continue;
            }

            if (entry.SlideIndex < 0 && entry.Section is { } section)
            {
                DrawSection(context, entry, section, text, muted);
                continue;
            }

            var slide = presentation.Slides[entry.SlideIndex];
            pictures.Add((entry.Rect, _editor.Pictures.Thumbnail(presentation, slide)));
            context.DrawRectangle(null, border, entry.Rect.Inflate(0.5));
            var isCurrent = slide.Id == current;
            if (selected.Contains(slide.Id) || isCurrent)
            {
                context.DrawRectangle(null, new Pen(accent, isCurrent ? 3 : 2), entry.Rect.Inflate(isCurrent ? 3 : 2.5), 3, 3);
            }

            var number = (entry.SlideIndex + 1).ToString(CultureInfo.CurrentCulture);
            var label = new FormattedText(number, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, slide.Hidden ? muted : text);
            if (IsSorter)
            {
                context.DrawText(label, new Point(entry.Rect.Right - label.Width, entry.Rect.Bottom + 4));
            }
            else
            {
                context.DrawText(label, new Point(entry.Rect.X - 8 - label.Width, entry.Rect.Y));
            }

            if (slide.Hidden)
            {
                // A hidden slide: its number crossed out and the thumbnail faded (PowerPoint's look).
                var numberX = IsSorter ? entry.Rect.Right - label.Width : entry.Rect.X - 8 - label.Width;
                var numberY = IsSorter ? entry.Rect.Bottom + 4 : entry.Rect.Y;
                context.DrawLine(new Pen(muted, 1), new Point(numberX - 1, numberY + label.Height - 2), new Point(numberX + label.Width + 1, numberY + 2));
            }

            if (slide.Transition is not null || slide.Timing is not null)
            {
                // A star below the number: the slide has a transition or animations.
                var star = new FormattedText("★", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, muted);
                context.DrawText(star, IsSorter ? new Point(entry.Rect.X, entry.Rect.Bottom + 4) : new Point(entry.Rect.X - 8 - star.Width, entry.Rect.Y + 18));
            }
        }

        var hidden = presentation.Slides.Select((slide, index) => (slide, index)).Where(pair => pair.slide.Hidden).Select(pair => pair.index).ToHashSet();
        var faded = pictures.Select(picture => _entries.First(entry => entry.Rect == picture.Rect)).Where(entry => hidden.Contains(entry.SlideIndex)).Select(entry => entry.Rect).ToList();
        var slideWidth = Units.EmuToPoints(presentation.SlideSize.Width);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            foreach (var (rect, picture) in pictures)
            {
                canvas.Save();
                canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
                canvas.DrawColor(ShowPalette.EmptySlide);
                canvas.Translate((float)rect.X, (float)rect.Y);
                canvas.Scale((float)(rect.Width / slideWidth));
                canvas.DrawPicture(picture);
                canvas.Restore();
            }
        }));

        var veil = Ui.Brush("Mat.Surface");
        foreach (var rect in faded)
        {
            using (context.PushOpacity(0.55))
            {
                context.FillRectangle(veil, rect);
            }
        }

        if (_dropIndex >= 0)
        {
            DrawDropMarker(context, accent);
        }
    }

    private void DrawSection(DrawingContext context, SlideListEntry entry, SlideSection section, IBrush text, IBrush muted)
    {
        var collapsed = _collapsed.Contains(section.Id);
        var chevron = new FormattedText(collapsed ? "▸" : "▾", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, muted);
        context.DrawText(chevron, new Point(entry.Rect.X + 4, entry.Rect.Y + 6));
        var count = section.SlideIds.Count;
        var label = collapsed ? $"{section.Name} ({count})" : section.Name;
        var name = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, text)
        {
            MaxTextWidth = Math.Max(10, entry.Rect.Width - 24),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(name, new Point(entry.Rect.X + 20, entry.Rect.Y + 6));
    }

    /// <summary>Where the slides being dragged would go: a line between two thumbnails.</summary>
    private void DrawDropMarker(DrawingContext context, IBrush accent)
    {
        var slides = _entries.Where(entry => entry.SlideIndex >= 0).ToList();
        if (slides.Count == 0)
        {
            return;
        }

        var pen = new Pen(accent, 2);
        var before = slides.FirstOrDefault(entry => entry.SlideIndex >= _dropIndex);
        if (before is null)
        {
            var last = slides[^1].Rect;
            if (IsSorter)
            {
                context.DrawLine(pen, new Point(last.Right + 8, last.Top), new Point(last.Right + 8, last.Bottom));
            }
            else
            {
                context.DrawLine(pen, new Point(last.Left, last.Bottom + 5), new Point(last.Right, last.Bottom + 5));
            }

            return;
        }

        var rect = before.Rect;
        if (IsSorter)
        {
            context.DrawLine(pen, new Point(rect.Left - 9, rect.Top), new Point(rect.Left - 9, rect.Bottom));
        }
        else
        {
            context.DrawLine(pen, new Point(rect.Left, rect.Top - 5), new Point(rect.Right, rect.Top - 5));
        }
    }

    /// <summary>The thumbnail rectangle of a slide (to scroll it into view), or null when its section is collapsed.</summary>
    public Rect? RectOf(int slideIndex) => _entries.FirstOrDefault(entry => entry.SlideIndex == slideIndex)?.Rect;

    /// <summary>Scrolls the current slide into view.</summary>
    public void BringCurrentIntoView()
    {
        var index = _editor.CurrentIndex;
        if (index >= 0 && RectOf(index) is { } rect)
        {
            this.BringIntoView(rect.Inflate(8));
        }
    }

    // ===== Input =====

    private SlideListEntry? EntryAt(Point point) => _entries.FirstOrDefault(entry => entry.Rect.Inflate(IsSorter ? 6 : 4).Contains(point) || (!IsSorter && entry.SlideIndex >= 0 && point.Y >= entry.Rect.Top - 4 && point.Y <= entry.Rect.Bottom + 4));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        e.Handled = true;
        if (EntryAt(point.Position) is not { } entry)
        {
            return;
        }

        var session = _editor.Session;
        if (entry.SlideIndex < 0 && entry.Section is { } section)
        {
            if (point.Position.X < entry.Rect.X + 20 || e.ClickCount == 2)
            {
                ToggleSection(section.Id);
            }
            else
            {
                session.SelectSlides(section.SlideIds);
            }

            return;
        }

        var slide = Presentation.Slides[entry.SlideIndex];
        var toggle = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (point.Properties.IsRightButtonPressed)
        {
            if (!session.Selection.Slides.Contains(slide.Id))
            {
                session.GoToSlide(slide.Id);
            }

            ContextMenu = ShowContextMenus.ForSlides(_editor.Window);
            return;
        }

        if (e.ClickCount == 2 && IsSorter)
        {
            session.GoToSlide(slide.Id);
            _editor.SetView(ShowView.Normal);
            return;
        }

        if (toggle)
        {
            var ids = session.Selection.Slides.ToList();
            if (!ids.Remove(slide.Id))
            {
                ids.Add(slide.Id);
            }

            session.SelectSlides(ids.Count == 0 ? [slide.Id] : ids);
            _anchor = entry.SlideIndex;
            return;
        }

        if (extend && _anchor >= 0)
        {
            var (from, to) = (Math.Min(_anchor, entry.SlideIndex), Math.Max(_anchor, entry.SlideIndex));
            session.SelectSlides(Presentation.Slides.Skip(from).Take(to - from + 1).Select(item => item.Id));
            return;
        }

        if (!session.Selection.Slides.Contains(slide.Id) || session.Selection.Slides.Count == 1)
        {
            session.GoToSlide(slide.Id);
        }

        _anchor = entry.SlideIndex;
        _press = (entry.SlideIndex, point.Position, false);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_press is not { } press)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!press.Moved && Math.Abs(position.X - press.Start.X) + Math.Abs(position.Y - press.Start.Y) < 6)
        {
            return;
        }

        _press = press with { Moved = true };
        _dropIndex = DropIndexAt(position);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is not { } press)
        {
            return;
        }

        _press = null;
        e.Pointer.Capture(null);
        var drop = _dropIndex;
        _dropIndex = -1;
        InvalidateVisual();
        var session = _editor.Session;
        if (!press.Moved)
        {
            // A click on a slide of a multiple selection makes it the only one (after a possible drag did not happen).
            var slide = Presentation.Slides[press.Index];
            if (session.Selection.Slides.Count > 1)
            {
                session.GoToSlide(slide.Id);
            }

            return;
        }

        if (drop >= 0)
        {
            var ids = session.Selection.Slides.ToList();
            var before = Presentation.Slides.Take(drop).Count(slide => ids.Contains(slide.Id));
            _ = _editor.RunAsync("Folien verschieben", () => session.MoveSlides(ids, drop - before));
        }
    }

    /// <summary>The index a drop at a point inserts before (0 … slide count).</summary>
    private int DropIndexAt(Point point)
    {
        var slides = _entries.Where(entry => entry.SlideIndex >= 0).ToList();
        foreach (var entry in slides)
        {
            var rect = entry.Rect;
            var before = IsSorter
                ? point.Y < rect.Bottom + 10 && point.Y >= rect.Top - 10 && point.X < rect.Center.X
                : point.Y < rect.Center.Y;
            if (before || (IsSorter && point.Y < rect.Top - 10))
            {
                return entry.SlideIndex;
            }
        }

        return Presentation.Slides.Count;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var session = _editor.Session;
        var index = _editor.CurrentIndex;
        var count = Presentation.Slides.Count;
        var columns = IsSorter ? ColumnsInRow() : 1;
        int? target = e.Key switch
        {
            Key.Up => index - columns,
            Key.Down => index + columns,
            Key.Left when IsSorter => index - 1,
            Key.Right when IsSorter => index + 1,
            Key.PageUp => index - (columns * 4),
            Key.PageDown => index + (columns * 4),
            Key.Home => 0,
            Key.End => count - 1,
            _ => null,
        };
        if (target is { } next && count > 0)
        {
            next = Math.Clamp(next, 0, count - 1);
            var id = Presentation.Slides[next].Id;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var anchor = _anchor < 0 ? index : _anchor;
                var (from, to) = (Math.Min(anchor, next), Math.Max(anchor, next));
                session.SelectSlides(Presentation.Slides.Skip(from).Take(to - from + 1).Select(slide => slide.Id));
                session.GoToSlide(id);
                session.SelectSlides(Presentation.Slides.Skip(from).Take(to - from + 1).Select(slide => slide.Id));
            }
            else
            {
                _anchor = next;
                session.GoToSlide(id);
            }

            BringCurrentIntoView();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                _ = _editor.DeleteSlidesAsync();
                e.Handled = true;
                break;
            case Key.Enter:
                _ = _editor.RunAsync("Neue Folie", () => session.AddSlide());
                e.Handled = true;
                break;
        }
    }

    private int ColumnsInRow()
    {
        var slides = _entries.Where(entry => entry.SlideIndex >= 0).ToList();
        if (slides.Count == 0)
        {
            return 1;
        }

        var top = slides[0].Rect.Top;
        return Math.Max(1, slides.Count(entry => Math.Abs(entry.Rect.Top - top) < 0.5));
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _editor.FocusedArea = EditorArea.Slides;
    }
}
