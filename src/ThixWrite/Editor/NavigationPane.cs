using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Ansicht › Navigationsbereich: the headings of the document as a list at the left of the pages (indented by level, like Word's
/// "Überschriften"); a click puts the caret on the heading. Follows the document once per dispatcher round while it is shown.
/// </summary>
internal sealed class NavigationPane : Border
{
    private readonly WriteEditor _editor;
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private long _shownVersion = -1;
    private bool _filling;

    public NavigationPane(WriteEditor editor)
    {
        _editor = editor;
        Width = 240;
        BorderThickness = new Thickness(0, 0, 1, 0);
        this.Use(BorderBrushProperty, "Mat.Border").Use(BackgroundProperty, "Mat.Surface");
        var title = new TextBlock { Text = "Navigation", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(14, 12, 14, 8) };
        var caption = new TextBlock { Text = "Überschriften", FontSize = 12, Margin = new Thickness(14, 0, 14, 6) }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _empty = new TextBlock
        {
            Text = "Dieses Dokument hat keine Überschriften. Formatieren Sie Text mit „Überschrift 1“ oder „Überschrift 2“, damit er hier erscheint.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 4, 14, 0),
            FontSize = 12,
        }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _list = new ListBox { BorderThickness = default }.Use(BackgroundProperty, "Mat.Surface");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        Avalonia.Automation.AutomationProperties.SetName(_list, "Überschriften");
        _list.SelectionChanged += (_, _) => GoToSelected();
        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(caption);
        panel.Children.Add(_empty);
        panel.Children.Add(_list);
        Child = panel;
    }

    /// <summary>The headings shown (tests).</summary>
    public IReadOnlyList<TocHeading> Headings { get; private set; } = [];

    /// <summary>The list (tests click it).</summary>
    public ListBox List => _list;

    /// <summary>Shows the headings as they are now (cheap when the document did not change).</summary>
    public void Refresh()
    {
        var document = _editor.Document;
        if (!IsVisible || _shownVersion == document.Content.Version)
        {
            return;
        }

        _shownVersion = document.Content.Version;
        Headings = document.Session.Headings(levels: 9);
        _filling = true;
        try
        {
            _list.ItemsSource = Headings.Select(heading => new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = heading.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = heading.Level == 1 ? FontWeight.SemiBold : FontWeight.Normal,
                },
                Padding = new Thickness(14 + ((heading.Level - 1) * 14), 5, 10, 5),
                Tag = heading,
            }).ToList();
        }
        finally
        {
            _filling = false;
        }

        _empty.IsVisible = Headings.Count == 0;
    }

    private void GoToSelected()
    {
        if (_filling || _list.SelectedItem is not ListBoxItem { Tag: TocHeading heading })
        {
            return;
        }

        _editor.CloseHeaderFooter();
        if (heading.Block < _editor.Document.Content.Blocks.Count)
        {
            _editor.Document.Session.SetCaret(new DocumentPosition(heading.Block, 0));
        }

        _editor.FocusDocument();
    }
}
