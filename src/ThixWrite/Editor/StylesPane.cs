using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// "Formatvorlagen" (Alt+Strg+Umschalt+S): the paragraph (¶) and character (a) styles of the document and Word's quick styles, by name,
/// with a search above; a click formats the selection with the style; below: Neue Formatvorlage, Formatvorlage ändern. The style of
/// the caret's paragraph is the selected one. Follows the document once per dispatcher round while it is shown.
/// </summary>
internal sealed class StylesPane : Border
{
    private readonly WriteEditor _editor;
    private readonly ListBox _list;
    private readonly TextBox _search;
    private (long Version, string? Current, string Filter) _shown = (-1, null, "");
    private bool _filling;

    public StylesPane(WriteEditor editor, Func<Task> create, Func<Task> modify)
    {
        _editor = editor;
        Width = 250;
        BorderThickness = new Thickness(1, 0, 0, 0);
        this.Use(BorderBrushProperty, "Mat.Border").Use(BackgroundProperty, "Mat.Surface");
        var title = new TextBlock { Text = "Formatvorlagen", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(14, 12, 14, 8) };
        _search = new TextBox { Watermark = "Formatvorlage suchen", Margin = new Thickness(12, 0, 12, 8) };
        Avalonia.Automation.AutomationProperties.SetName(_search, "Formatvorlage suchen");
        _search.TextChanged += (_, _) => Refresh();
        _list = new ListBox { BorderThickness = default }.Use(BackgroundProperty, "Mat.Surface");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        Avalonia.Automation.AutomationProperties.SetName(_list, "Formatvorlagen");
        _list.SelectionChanged += (_, _) => ApplySelected();
        var add = new Button { Content = new IconLabel { Icon = "plus", Text = "Neu" } };
        var change = new Button { Content = new IconLabel { Icon = "edit", Text = "Ändern" } };
        ToolTip.SetTip(add, "Neue Formatvorlage …");
        ToolTip.SetTip(change, "Formatvorlage ändern …");
        add.Click += async (_, _) => await create();
        change.Click += async (_, _) => await modify();
        var actions = new ActionBar { Margin = new Thickness(12, 8), Children = { add, change } };
        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(title);
        panel.Children.Add(_search);
        panel.Children.Add(actions);
        panel.Children.Add(_list);
        Child = panel;
    }

    /// <summary>The styles shown (tests).</summary>
    public IReadOnlyList<Style> Shown { get; private set; } = [];

    /// <summary>The list (tests click it).</summary>
    public ListBox List => _list;

    public void Refresh()
    {
        if (!IsVisible)
        {
            return;
        }

        var session = _editor.Session;
        var current = session.CurrentParagraphStyle?.Id;
        var filter = _search.Text?.Trim() ?? "";
        var key = (_editor.Document.Content.Version, current, filter);
        if (_shown == key)
        {
            return;
        }

        _shown = key;
        Shown = [.. session.PaneStyles().Where(style => filter.Length == 0 || BuiltInStyles.DisplayName(style).Contains(filter, StringComparison.CurrentCultureIgnoreCase))];
        _filling = true;
        try
        {
            var items = Shown.Select(style => new ListBoxItem
            {
                Content = new DockPanel
                {
                    Children =
                    {
                        Marker(style),
                        new TextBlock { Text = BuiltInStyles.DisplayName(style), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                Padding = new Thickness(14, 5, 10, 5),
                Tag = style,
            }).ToList();
            _list.ItemsSource = items;
            _list.SelectedItem = items.FirstOrDefault(item => ((Style)item.Tag!).Id == current);
        }
        finally
        {
            _filling = false;
        }
    }

    private static TextBlock Marker(Style style)
    {
        var marker = new TextBlock { Text = style.Type == StyleType.Character ? "a" : "¶", Width = 18, VerticalAlignment = VerticalAlignment.Center };
        marker.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        DockPanel.SetDock(marker, Dock.Right);
        return marker;
    }

    private void ApplySelected()
    {
        if (_filling || _list.SelectedItem is not ListBoxItem { Tag: Style style })
        {
            return;
        }

        _editor.Session.ApplyStyle(style.Id);
        _editor.FocusDocument();
    }
}
