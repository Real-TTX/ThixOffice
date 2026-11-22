using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Pim.Calendar;

namespace ThixOffice.Pim.Desktop.Calendar;

/// <summary>Outlook's arrangements of the calendar.</summary>
public enum CalendarMode
{
    Day,
    WorkWeek,
    Week,
    Month,
}

/// <summary>
/// The calendar of an account like Outlook's: a toolbar (Heute, ‹ ›, the period), the calendars at the left (shown or hidden on this computer), and the
/// period as Tag / Arbeitswoche / Woche (a header with the days and the all-day appointments, the hours below in a scroll viewer, overlapping
/// appointments side by side) or Monat (six weeks, the appointments of each day as lines, "+n weitere"). A click selects an appointment or a time, a
/// double click opens it or asks for a new one there (<see cref="OpenRequested"/>, <see cref="NewRequested"/>) – the host's commands do the rest.
/// Appointments are drawn in their calendar's colour (<see cref="CalendarPalette"/>: user data); everything else in Mat tokens.
/// </summary>
public sealed class CalendarView : Border
{
    public const double HourHeight = 44;
    private const double TimeColumn = 66;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly Func<CalendarBook?> _book;
    private readonly string _settings;
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly StackPanel _calendarList = new() { Spacing = 2, Margin = new Thickness(10, 4, 10, 10) };
    private readonly Grid _dayArea = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly Canvas _header = new() { ClipToBounds = true };
    private readonly Canvas _hours = new() { ClipToBounds = true };
    private readonly HourLines _hourLines;
    private readonly ScrollViewer _hourScroller;
    private readonly Canvas _month = new() { ClipToBounds = true };
    private readonly Panel _body = new();
    private bool _scrolledToMorning;

    public CalendarView(Func<CalendarBook?> book, string settings = "calendar")
    {
        _book = book;
        _settings = settings;
        foreach (var key in (SettingsService.AppValue(_settings + ".hidden") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            _hidden.Add(key);
        }

        Mode = Enum.TryParse<CalendarMode>(SettingsService.AppValue(_settings + ".mode"), out var mode) ? mode : CalendarMode.WorkWeek;
        Anchor = DateTime.Today;

        TodayButton = new Button { Content = new IconLabel { Icon = "calendar", Text = "Heute" } };
        TodayButton.Click += (_, _) => GoToday();
        ToolTip.SetTip(TodayButton, "Zum heutigen Tag");
        PreviousButton = new Button { Content = new MatIcon { Icon = "chevron-left" }, Padding = new Thickness(8, 4) }.Themed("MatButton.Ghost");
        PreviousButton.Click += (_, _) => Previous();
        AutomationProperties.SetName(PreviousButton, "Zurück");
        ToolTip.SetTip(PreviousButton, "Zurück");
        NextButton = new Button { Content = new MatIcon { Icon = "chevron-right" }, Padding = new Thickness(8, 4) }.Themed("MatButton.Ghost");
        NextButton.Click += (_, _) => Next();
        AutomationProperties.SetName(NextButton, "Weiter");
        ToolTip.SetTip(NextButton, "Weiter");
        TitleText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }.Themed("MatText.SectionTitle");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(12, 10, 12, 8), Children = { TodayButton, PreviousButton, NextButton, TitleText } };

        var calendars = new DockPanel();
        var calendarsTitle = new TextBlock { Text = "Meine Kalender", Margin = new Thickness(16, 14, 12, 6) }.Themed("MatText.SectionTitle");
        DockPanel.SetDock(calendarsTitle, Dock.Top);
        calendars.Children.Add(calendarsTitle);
        calendars.Children.Add(new ScrollViewer { Content = _calendarList });
        var calendarPane = new Border { Child = calendars, Width = 220, BorderThickness = new Thickness(0, 0, 1, 0) };
        calendarPane.Use(BackgroundProperty, "Mat.NavBg");
        calendarPane.Use(BorderBrushProperty, "Mat.Border");

        _hourLines = new HourLines(this);
        var hourStack = new Panel { Height = 24 * HourHeight, Children = { _hourLines, _hours } };
        _hourScroller = new ScrollViewer { Content = hourStack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var headerBorder = new Border { Child = _header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Use(BorderBrushProperty, "Mat.Border");
        Grid.SetRow(_hourScroller, 1);
        _dayArea.Children.Add(headerBorder);
        _dayArea.Children.Add(_hourScroller);
        _body.Children.Add(_dayArea);
        _body.Children.Add(_month);
        _header.SizeChanged += (_, _) => LayoutHeader();
        _hours.SizeChanged += (_, _) => LayoutHours();
        _month.SizeChanged += (_, _) => LayoutMonth();
        _header.Background = CalendarPalette.HitArea;
        _hours.Background = CalendarPalette.HitArea;
        _month.Background = CalendarPalette.HitArea;
        _header.PointerPressed += OnHeaderPressed;
        _hours.PointerPressed += OnHoursPressed;
        _month.PointerPressed += OnMonthPressed;

        var main = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        main.Children.Add(toolbar);
        main.Children.Add(_body);
        main.Use(Panel.BackgroundProperty, "Mat.Surface");
        var root = new DockPanel();
        DockPanel.SetDock(calendarPane, Dock.Left);
        root.Children.Add(calendarPane);
        root.Children.Add(main);
        Child = root;
        Focusable = true;
        KeyDown += OnKeyDown;
        Refresh();
    }

    /// <summary>The selection, the period or the calendars changed (the host's commands and status bar).</summary>
    public event EventHandler? StateChanged;

    /// <summary>A double click on an appointment (or Enter): the host opens it.</summary>
    public event EventHandler<CalendarAppointment>? OpenRequested;

    /// <summary>A double click on a free time (or Enter on it): the host makes a new appointment there (all-day for the header and the month).</summary>
    public event EventHandler<(DateTime Start, bool AllDay)>? NewRequested;

    public Button TodayButton { get; }

    public Button PreviousButton { get; }

    public Button NextButton { get; }

    public TextBlock TitleText { get; }

    public CalendarMode Mode { get; private set; }

    /// <summary>The chosen day (the period shown contains it).</summary>
    public DateTime Anchor { get; private set; }

    /// <summary>The appointment chosen, or null.</summary>
    public CalendarAppointment? Selected { get; private set; }

    /// <summary>The time chosen in the grid (a new appointment starts there), or null.</summary>
    public DateTime? SelectedTime { get; private set; }

    public CalendarBook? Book => _book();

    /// <summary>The days shown (the month: its six weeks).</summary>
    public IReadOnlyList<DateTime> Days
    {
        get
        {
            var monday = Anchor.AddDays(-(((int)Anchor.DayOfWeek + 6) % 7));
            return Mode switch
            {
                CalendarMode.Day => [Anchor],
                CalendarMode.WorkWeek => [.. Enumerable.Range(0, 5).Select(i => monday.AddDays(i))],
                CalendarMode.Week => [.. Enumerable.Range(0, 7).Select(i => monday.AddDays(i))],
                _ => MonthDays(),
            };
        }
    }

    /// <summary>The appointments of the period (the shown calendars), as the view draws them.</summary>
    public IReadOnlyList<CalendarAppointment> Shown { get; private set; } = [];

    /// <summary>The calendars of the account with whether they are shown (left pane).</summary>
    public IReadOnlyList<(CalendarEntry Calendar, bool Shown)> CalendarChoices => [.. (Book?.Calendars ?? []).Select(c => (c, !_hidden.Contains(c.Key)))];

    // ===== what the commands do =====

    public void SetMode(CalendarMode mode)
    {
        Mode = mode;
        SettingsService.RememberAppValue(_settings + ".mode", mode == CalendarMode.WorkWeek ? null : mode.ToString());
        Refresh();
    }

    public void GoToday() => GoTo(DateTime.Today);

    public void GoTo(DateTime day)
    {
        Anchor = day.Date;
        SelectedTime = null;
        Refresh();
    }

    public void Next() => GoTo(Step(1));

    public void Previous() => GoTo(Step(-1));

    /// <summary>Shows or hides a calendar on this computer (ThixCalendar's own choice is not changed).</summary>
    public void SetShown(string calendarKey, bool shown)
    {
        if (shown ? !_hidden.Remove(calendarKey) : !_hidden.Add(calendarKey))
        {
            return;
        }

        SettingsService.RememberAppValue(_settings + ".hidden", _hidden.Count == 0 ? null : string.Join('|', _hidden));
        Refresh();
    }

    public void Select(CalendarAppointment? appointment)
    {
        Selected = appointment;
        if (appointment is not null)
        {
            SelectedTime = null;
        }

        Highlight();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The time a new appointment would start at: the chosen time, else 9:00 of the chosen day (8:00 tomorrow when it is past already).</summary>
    public DateTime NewStart()
    {
        if (SelectedTime is { } time)
        {
            return time;
        }

        var day = Mode == CalendarMode.Month || Days.Contains(Anchor) ? Anchor : Days[0];
        var start = day.AddHours(9);
        return start;
    }

    /// <summary>Reads the book again and lays the period out.</summary>
    public void Refresh()
    {
        var book = Book;
        var days = Days;
        Shown = book is null ? [] : book.Between(days[0], days[^1].AddDays(1)).Where(a => !_hidden.Contains(a.CalendarKey)).ToList();
        if (Selected is { } selected)
        {
            Selected = Shown.FirstOrDefault(a => a.Key == selected.Key);
        }

        TitleText.Text = PeriodTitle();
        RefreshCalendarList();
        _dayArea.IsVisible = Mode != CalendarMode.Month;
        _month.IsVisible = Mode == CalendarMode.Month;
        if (Mode == CalendarMode.Month)
        {
            LayoutMonth();
        }
        else
        {
            LayoutHeader();
            LayoutHours();
            if (!_scrolledToMorning)
            {
                _scrolledToMorning = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _hourScroller.Offset = new Vector(0, 7.5 * HourHeight), Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"23.–27. November 2026", "Donnerstag, 26. November 2026", "November 2026".</summary>
    public string PeriodTitle()
    {
        var days = Days;
        if (Mode == CalendarMode.Month)
        {
            return Anchor.ToString("MMMM yyyy", German);
        }

        if (days.Count == 1)
        {
            return days[0].ToString("dddd, d. MMMM yyyy", German);
        }

        var (first, last) = (days[0], days[^1]);
        return first.Month == last.Month
            ? $"{first.Day}.–{last.ToString("d. MMMM yyyy", German)}"
            : first.Year == last.Year ? $"{first.ToString("d. MMMM", German)} – {last.ToString("d. MMMM yyyy", German)}" : $"{first.ToString("d. MMMM yyyy", German)} – {last.ToString("d. MMMM yyyy", German)}";
    }

    private DateTime Step(int direction) => Mode switch
    {
        CalendarMode.Day => Anchor.AddDays(direction),
        CalendarMode.Month => new DateTime(Anchor.Year, Anchor.Month, 1).AddMonths(direction),
        _ => Anchor.AddDays(7 * direction),
    };

    private List<DateTime> MonthDays()
    {
        var first = new DateTime(Anchor.Year, Anchor.Month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        return [.. Enumerable.Range(0, 42).Select(i => start.AddDays(i))];
    }

    // ===== the calendars at the left =====

    private void RefreshCalendarList()
    {
        _calendarList.Children.Clear();
        string? account = null;
        foreach (var (calendar, shown) in CalendarChoices)
        {
            if (calendar.AccountName != account)
            {
                account = calendar.AccountName;
                var heading = new TextBlock { Text = account, FontSize = MatTokens.Type.FsXs, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 8, 0, 2) };
                heading.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
                _calendarList.Children.Add(heading);
            }

            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.Background = CalendarPalette.Solid(calendar.Color);
            var label = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, new TextBlock { Text = calendar.Name, VerticalAlignment = VerticalAlignment.Center } } };
            if (calendar.IsReadOnly)
            {
                var icon = new MatIcon { Icon = "lock", Size = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                icon.Use(MatIcon.ForegroundProperty, "Mat.TextMuted");
                ToolTip.SetTip(icon, "Wird über die ThixCloud abgeglichen – ändern in ThixCalendar");
                label.Children.Add(icon);
            }

            var box = new CheckBox { Content = label, IsChecked = shown };
            AutomationProperties.SetName(box, calendar.Name);
            var key = calendar.Key;
            box.IsCheckedChanged += (_, _) => SetShown(key, box.IsChecked == true);
            _calendarList.Children.Add(box);
        }

        if (_calendarList.Children.Count == 0)
        {
            var empty = new TextBlock { Text = "Noch kein Kalender – der erste Termin legt „Kalender“ an.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 0, 0) };
            empty.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            _calendarList.Children.Add(empty);
        }
    }

    // ===== day, work week, week =====

    private double DayWidth(double width) => Math.Max(20, (width - TimeColumn) / Math.Max(1, Days.Count));

    private void LayoutHeader()
    {
        _header.Children.Clear();
        var width = _header.Bounds.Width > 0 ? _header.Bounds.Width : 800;
        var days = Days;
        var dayWidth = DayWidth(width);
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var today = day == DateTime.Today;
            var name = new TextBlock { Text = day.ToString("ddd d.", German), FontWeight = today ? FontWeight.Bold : FontWeight.Normal, Width = dayWidth - 8 };
            name.Use(TextBlock.ForegroundProperty, today ? "Mat.Accent" : "Mat.TextMuted");
            Canvas.SetLeft(name, TimeColumn + (i * dayWidth) + 6);
            Canvas.SetTop(name, 6);
            _header.Children.Add(name);
        }

        // all-day appointments in lanes across the days they cover
        var lanes = new List<DateTime>();
        var allDay = Shown.Where(a => a.IsAllDay || a.EndDay - a.FirstDay >= TimeSpan.FromDays(1) && !SameDay(a)).OrderBy(a => a.FirstDay).ThenByDescending(a => a.EndDay - a.FirstDay).ToList();
        foreach (var appointment in allDay)
        {
            var firstIndex = Math.Max(0, (int)(appointment.FirstDay - days[0]).TotalDays);
            var lastIndex = Math.Min(days.Count - 1, (int)(appointment.EndDay - days[0]).TotalDays - 1);
            if (lastIndex < firstIndex)
            {
                lastIndex = firstIndex;
            }

            var lane = lanes.FindIndex(free => free <= days[firstIndex]);
            if (lane < 0)
            {
                lane = lanes.Count;
                lanes.Add(DateTime.MinValue);
            }

            lanes[lane] = days[lastIndex].AddDays(1);
            var block = Block(appointment, appointment.Title, compact: true);
            block.Width = ((lastIndex - firstIndex + 1) * dayWidth) - 4;
            block.Height = 20;
            Canvas.SetLeft(block, TimeColumn + (firstIndex * dayWidth) + 2);
            Canvas.SetTop(block, 30 + (lane * 22));
            _header.Children.Add(block);
        }

        _header.Height = 32 + (Math.Max(1, lanes.Count) * 22);
        var allDayLabel = new TextBlock { Text = "ganztägig", FontSize = MatTokens.Type.FsXs, Width = TimeColumn - 6, TextAlignment = TextAlignment.Right };
        allDayLabel.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Canvas.SetLeft(allDayLabel, 0);
        Canvas.SetTop(allDayLabel, 32);
        _header.Children.Add(allDayLabel);
        Highlight();
    }

    private static bool SameDay(CalendarAppointment appointment) => appointment.EndDay - appointment.FirstDay <= TimeSpan.FromDays(1);

    private void LayoutHours()
    {
        _hours.Children.Clear();
        var width = _hours.Bounds.Width > 0 ? _hours.Bounds.Width : 800;
        var days = Days;
        var dayWidth = DayWidth(width);
        for (var hour = 0; hour < 24; hour++)
        {
            var label = new TextBlock { Text = $"{hour:00}:00", FontSize = MatTokens.Type.FsXs, Width = TimeColumn - 10, TextAlignment = TextAlignment.Right };
            label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, (hour * HourHeight) + 2);
            _hours.Children.Add(label);
        }

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var timed = Shown.Where(a => !a.IsAllDay && SameDay(a) && a.FirstDay == day).OrderBy(a => a.Start).ThenByDescending(a => a.End).ToList();
            foreach (var (appointment, column, columns) in Columns(timed))
            {
                var start = appointment.StartLocal;
                var end = appointment.EndLocal > start ? appointment.EndLocal : start.AddMinutes(30);
                var top = (start - day).TotalHours * HourHeight;
                var bottom = Math.Min(24 * HourHeight, (end - day).TotalHours * HourHeight);
                var columnWidth = (dayWidth - 6) / columns;
                var text = $"{appointment.Title}" + (appointment.Location.Length > 0 ? $"\n{appointment.Location}" : "");
                var block = Block(appointment, text, compact: bottom - top < 30);
                block.Width = Math.Max(10, columnWidth - 2);
                block.Height = Math.Max(16, bottom - top - 2);
                Canvas.SetLeft(block, TimeColumn + (i * dayWidth) + 2 + (column * columnWidth));
                Canvas.SetTop(block, top + 1);
                _hours.Children.Add(block);
            }
        }

        _hourLines.InvalidateVisual();
        Highlight();
    }

    /// <summary>Overlapping appointments of a day side by side: each gets a column of its cluster and the cluster's number of columns.</summary>
    internal static IEnumerable<(CalendarAppointment Appointment, int Column, int Columns)> Columns(IReadOnlyList<CalendarAppointment> timed)
    {
        var cluster = new List<(CalendarAppointment Appointment, int Column)>();
        var clusterEnd = DateTimeOffset.MinValue;
        var columnEnds = new List<DateTimeOffset>();
        foreach (var appointment in timed)
        {
            var end = appointment.End > appointment.Start ? appointment.End : appointment.Start.AddMinutes(30);
            if (cluster.Count > 0 && appointment.Start >= clusterEnd)
            {
                foreach (var (placed, column) in cluster)
                {
                    yield return (placed, column, columnEnds.Count);
                }

                cluster.Clear();
                columnEnds.Clear();
            }

            var free = columnEnds.FindIndex(columnEnd => columnEnd <= appointment.Start);
            if (free < 0)
            {
                free = columnEnds.Count;
                columnEnds.Add(end);
            }
            else
            {
                columnEnds[free] = end;
            }

            cluster.Add((appointment, free));
            clusterEnd = cluster.Count == 1 ? end : (end > clusterEnd ? end : clusterEnd);
        }

        foreach (var (placed, column) in cluster)
        {
            yield return (placed, column, columnEnds.Count);
        }
    }

    // ===== month =====

    private void LayoutMonth()
    {
        _month.Children.Clear();
        var width = _month.Bounds.Width > 0 ? _month.Bounds.Width : 800;
        var height = _month.Bounds.Height > 0 ? _month.Bounds.Height : 600;
        var days = Days;
        var cellWidth = width / 7;
        var nameHeight = 24.0;
        var cellHeight = (height - nameHeight) / 6;
        for (var column = 0; column < 7; column++)
        {
            var name = new TextBlock { Text = days[column].ToString("dddd", German), Width = cellWidth - 8 };
            name.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            Canvas.SetLeft(name, (column * cellWidth) + 6);
            Canvas.SetTop(name, 4);
            _month.Children.Add(name);
        }

        var lines = Math.Max(1, (int)((cellHeight - 24) / 19));
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var (x, y) = ((index % 7) * cellWidth, nameHeight + ((index / 7) * cellHeight));
            var cell = new Border { Width = cellWidth, Height = cellHeight, BorderThickness = new Thickness(0, 1, index % 7 == 6 ? 0 : 1, 0) };
            cell.Use(BorderBrushProperty, "Mat.Border");
            if (day.Month != Anchor.Month)
            {
                cell.Use(BackgroundProperty, "Mat.Bg");
            }

            Canvas.SetLeft(cell, x);
            Canvas.SetTop(cell, y);
            _month.Children.Add(cell);
            var number = new TextBlock { Text = day.Day == 1 ? day.ToString("d. MMM", German) : day.Day.ToString(German), FontWeight = day == DateTime.Today ? FontWeight.Bold : FontWeight.Normal };
            number.Use(TextBlock.ForegroundProperty, day == DateTime.Today ? "Mat.Accent" : day.Month == Anchor.Month ? "Mat.Text" : "Mat.TextMuted");
            Canvas.SetLeft(number, x + 6);
            Canvas.SetTop(number, y + 3);
            _month.Children.Add(number);

            var ofDay = Shown.Where(a => a.Touches(day)).ToList();
            for (var i = 0; i < Math.Min(lines, ofDay.Count); i++)
            {
                var appointment = ofDay[i];
                if (i == lines - 1 && ofDay.Count > lines)
                {
                    var more = new TextBlock { Text = $"+{ofDay.Count - lines + 1} weitere", FontSize = MatTokens.Type.FsXs };
                    more.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
                    Canvas.SetLeft(more, x + 6);
                    Canvas.SetTop(more, y + 24 + (i * 19));
                    _month.Children.Add(more);
                    break;
                }

                var text = appointment.IsAllDay ? appointment.Title : $"{appointment.StartLocal:HH:mm} {appointment.Title}";
                var block = Block(appointment, text, compact: true);
                block.Width = cellWidth - 6;
                block.Height = 18;
                Canvas.SetLeft(block, x + 3);
                Canvas.SetTop(block, y + 22 + (i * 19));
                _month.Children.Add(block);
            }
        }

        Highlight();
    }

    // ===== appointments =====

    /// <summary>The block of an appointment: a bar in its calendar's colour, a tint of it behind its text; read-only ones with a lock.</summary>
    private Border Block(CalendarAppointment appointment, string text, bool compact)
    {
        var calendar = Book?.Calendar(appointment.CalendarKey);
        var label = new TextBlock
        {
            Text = text,
            FontSize = MatTokens.Type.FsSm,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.WrapWithOverflow,
            Margin = new Thickness(6, compact ? 1 : 3, 4, 1),
        };
        label.Use(TextBlock.ForegroundProperty, "Mat.Text");
        var bar = new Border { Width = 3 };
        bar.Background = CalendarPalette.Solid(calendar?.Color);
        if (bar.Background is null)
        {
            bar.Use(BackgroundProperty, "Mat.Accent");
        }

        var content = new DockPanel { Children = { bar, label } };
        DockPanel.SetDock(bar, Dock.Left);
        var block = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Tag = appointment,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        block.Background = CalendarPalette.Tint(calendar?.Color, appointment.Status == MatCloud.Contract.Calendar.CalendarEventStatus.Tentative);
        if (block.Background is null)
        {
            block.Use(BackgroundProperty, "Mat.AccentSoft");
        }

        var when = appointment.IsAllDay ? "ganztägig" : $"{appointment.StartLocal:HH:mm}–{appointment.EndLocal:HH:mm}";
        AutomationProperties.SetName(block, $"{appointment.Title}, {when}");
        ToolTip.SetTip(block, $"{appointment.Title}\n{when}" + (appointment.Location.Length > 0 ? $"\n{appointment.Location}" : "")
            + (appointment.IsReadOnly ? "\nWird über die ThixCloud abgeglichen – ändern in ThixCalendar" : ""));
        block.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Select(appointment);
            Focus();
            if (e.ClickCount == 2)
            {
                OpenRequested?.Invoke(this, appointment);
            }
        };
        return block;
    }

    /// <summary>The chosen appointment framed in the accent.</summary>
    private void Highlight()
    {
        foreach (var canvas in new[] { _header, _hours, _month })
        {
            foreach (var block in canvas.Children.OfType<Border>().Where(b => b.Tag is CalendarAppointment))
            {
                var selected = Selected is { } chosen && ((CalendarAppointment)block.Tag!).Key == chosen.Key;
                block.BorderThickness = new Thickness(selected ? 2 : 0);
                block.Use(BorderBrushProperty, "Mat.Accent");
            }
        }

        _hourLines.InvalidateVisual();
    }

    // ===== pointer and keys =====

    private void OnHoursPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(_hours);
        var dayWidth = DayWidth(_hours.Bounds.Width);
        var index = (int)((point.X - TimeColumn) / dayWidth);
        if (point.X < TimeColumn || index < 0 || index >= Days.Count)
        {
            return;
        }

        var minutes = Math.Clamp((int)(point.Y / HourHeight * 60) / 30 * 30, 0, (24 * 60) - 30);
        ChooseTime(Days[index].AddMinutes(minutes), allDay: false, e.ClickCount == 2);
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(_header);
        var index = (int)((point.X - TimeColumn) / DayWidth(_header.Bounds.Width));
        if (point.X >= TimeColumn && index >= 0 && index < Days.Count)
        {
            ChooseTime(Days[index], allDay: true, e.ClickCount == 2);
        }
    }

    private void OnMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(_month);
        var column = (int)(point.X / (_month.Bounds.Width / 7));
        var row = (int)((point.Y - 24) / ((_month.Bounds.Height - 24) / 6));
        if (column is >= 0 and < 7 && row is >= 0 and < 6)
        {
            ChooseTime(Days[(row * 7) + column], allDay: true, e.ClickCount == 2);
        }
    }

    private void ChooseTime(DateTime time, bool allDay, bool open)
    {
        Selected = null;
        SelectedTime = time;
        Anchor = time.Date;
        Highlight();
        Focus();
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (open)
        {
            NewRequested?.Invoke(this, (time, allDay));
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Selected is { } appointment:
                e.Handled = true;
                OpenRequested?.Invoke(this, appointment);
                break;
            case Key.Enter when SelectedTime is { } time:
                e.Handled = true;
                NewRequested?.Invoke(this, (time, Mode == CalendarMode.Month));
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                MoveDay(-1);
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                MoveDay(1);
                break;
        }
    }

    private void MoveDay(int days)
    {
        var target = Anchor.AddDays(days);
        Selected = null;
        SelectedTime = null;
        if (Days.Contains(target))
        {
            Anchor = target;
            StateChanged?.Invoke(this, EventArgs.Empty);
            Highlight();
        }
        else
        {
            GoTo(target);
        }
    }

    /// <summary>The hours: lines every hour (dotted every half), the chosen time, the line of now.</summary>
    private sealed class HourLines(CalendarView view) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var width = Bounds.Width;
            var days = view.Days;
            var dayWidth = view.DayWidth(width);
            var line = new Pen(Ui.Brush("Mat.Border"), 1);
            var half = new Pen(Ui.Brush("Mat.Border"), 1) { DashStyle = DashStyle.Dot };
            for (var hour = 0; hour < 24; hour++)
            {
                var y = Math.Round(hour * HourHeight) + 0.5;
                context.DrawLine(line, new Point(TimeColumn, y), new Point(width, y));
                context.DrawLine(half, new Point(TimeColumn, y + (HourHeight / 2)), new Point(width, y + (HourHeight / 2)));
            }

            for (var i = 0; i <= days.Count; i++)
            {
                var x = Math.Round(TimeColumn + (i * dayWidth)) + 0.5;
                context.DrawLine(line, new Point(x, 0), new Point(x, Bounds.Height));
            }

            if (view.SelectedTime is { } chosen && IndexOf(days, chosen.Date) is >= 0 and var column)
            {
                var top = chosen.TimeOfDay.TotalHours * HourHeight;
                context.FillRectangle(Ui.Brush("Mat.AccentSoft"), new Rect(TimeColumn + (column * dayWidth) + 1, top + 1, dayWidth - 2, (HourHeight / 2) - 1));
            }

            var today = IndexOf(days, DateTime.Today);
            if (today >= 0)
            {
                var y = DateTime.Now.TimeOfDay.TotalHours * HourHeight;
                context.DrawLine(new Pen(Ui.Brush("Mat.Danger"), 2), new Point(TimeColumn + (today * dayWidth), y), new Point(TimeColumn + ((today + 1) * dayWidth), y));
            }
        }

        private static int IndexOf(IReadOnlyList<DateTime> days, DateTime day)
        {
            for (var i = 0; i < days.Count; i++)
            {
                if (days[i] == day)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
