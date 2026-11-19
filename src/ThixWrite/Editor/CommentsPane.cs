using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The comments at the right of the pages (Word's "Kommentare"-Bereich): a card per comment in document order – author, date, its
/// text in a field (typed into directly; saved as one step when the field is left), its replies under it – the card of the comment
/// at the caret marked. A click on a card selects the text the comment is about; the trash button deletes the comment.
/// </summary>
internal sealed class CommentsPane : Border
{
    private readonly WriteEditor _editor;
    private readonly StackPanel _cards = new() { Spacing = 10, Margin = new Thickness(12, 0, 12, 12) };
    private readonly TextBlock _empty;
    private readonly Dictionary<string, (Border Card, TextBox? Field)> _byId = new(StringComparer.Ordinal);
    private long _shownVersion = -1;
    private string? _activeId;

    public CommentsPane(WriteEditor editor)
    {
        _editor = editor;
        Width = 280;
        BorderThickness = new Thickness(1, 0, 0, 0);
        this.Use(BorderBrushProperty, "Mat.Border").Use(BackgroundProperty, "Mat.Surface");
        var title = new TextBlock { Text = "Kommentare", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(14, 12, 14, 10) };
        _empty = new TextBlock
        {
            Text = "Noch keine Kommentare. Markieren Sie Text und wählen Sie „Neuer Kommentar“ (Strg+Alt+K).",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(14, 0, 14, 0),
        }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(_empty);
        panel.Children.Add(new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Child = panel;
    }

    /// <summary>The comments shown (tests).</summary>
    public IReadOnlyList<PlacedComment> Shown { get; private set; } = [];

    /// <summary>The text field of a comment's card (tests type into it), or null.</summary>
    public TextBox? FieldOf(string id) => _byId.TryGetValue(id, out var card) ? card.Field : null;

    /// <summary>Shows the comments as they are now: the cards are made anew when the document changed, the marked one follows the caret.</summary>
    public void Refresh()
    {
        if (!IsVisible)
        {
            return;
        }

        var document = _editor.Document;
        if (_shownVersion != document.Content.Version && !IsTyping)
        {
            _shownVersion = document.Content.Version;
            Rebuild();
        }

        var active = _editor.Document.Session.CommentAtCaret?.Comment.Id;
        if (active == _activeId)
        {
            return;
        }

        _activeId = active;
        foreach (var (id, (card, _)) in _byId)
        {
            card.Use(BorderBrushProperty, id == active ? "Mat.Accent" : "Mat.Border");
            card.BorderThickness = new Thickness(id == active ? 2 : 1);
        }
    }

    /// <summary>Puts the keyboard into the text field of a comment (a new one: the user types its text).</summary>
    public void FocusComment(string id)
    {
        Refresh();
        if (FieldOf(id) is { } field)
        {
            // After the card is in the tree and laid out (and after the command's own focus handling).
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                field.Focus();
                field.CaretIndex = field.Text?.Length ?? 0;
                field.BringIntoView();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private bool IsTyping => _byId.Values.Any(card => card.Field?.IsFocused == true);

    private void Rebuild()
    {
        Shown = _editor.Document.Session.Comments();
        _byId.Clear();
        _cards.Children.Clear();
        _activeId = null;
        var replies = Shown.Where(placed => placed.Comment.IsReply).ToLookup(placed => placed.Comment.ParentParaId!, StringComparer.OrdinalIgnoreCase);
        var parents = Shown.Where(placed => placed.Comment.ParaId is not null).Select(placed => placed.Comment.ParaId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var placed in Shown)
        {
            if (placed.Comment.IsReply && parents.Contains(placed.Comment.ParentParaId!))
            {
                continue; // shown under the comment it answers
            }

            _cards.Children.Add(Card(placed, placed.Comment.ParaId is { } paraId ? [.. replies[paraId]] : []));
        }

        _empty.IsVisible = Shown.Count == 0;
    }

    private Border Card(PlacedComment placed, IReadOnlyList<PlacedComment> replies)
    {
        var comment = placed.Comment;
        var delete = new Button { Content = new MatIcon { Icon = "trash", Size = 14 }, Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Top }.Themed("MatButton.Ghost");
        ToolTip.SetTip(delete, "Kommentar löschen");
        Avalonia.Automation.AutomationProperties.SetName(delete, "Kommentar löschen");
        delete.Click += (_, _) => Delete(comment.Id);
        var header = new DockPanel();
        DockPanel.SetDock(delete, Dock.Right);
        header.Children.Add(delete);
        header.Children.Add(Byline(comment));

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        TextBox? field = null;
        if (comment.IsReply)
        {
            body.Children.Add(new TextBlock { Text = comment.Text, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            field = new TextBox
            {
                Text = comment.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Watermark = "Kommentar eingeben …",
                MinHeight = 32,
            };
            Avalonia.Automation.AutomationProperties.SetName(field, $"Kommentar von {comment.Author}");
            field.LostFocus += (_, _) => Commit(comment.Id, field.Text ?? "");
            field.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _editor.FocusDocument();
                }
            };
            body.Children.Add(field);
        }

        foreach (var reply in replies)
        {
            var answer = new StackPanel { Spacing = 2, Margin = new Thickness(12, 4, 0, 0) };
            answer.Children.Add(Byline(reply.Comment));
            answer.Children.Add(new TextBlock { Text = reply.Comment.Text, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(answer);
        }

        if (comment.Done)
        {
            body.Children.Add(new TextBlock { Text = "Erledigt", FontSize = 11 }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"));
        }

        var card = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = body, Tag = comment.Id }
            .Use(BorderBrushProperty, "Mat.Border").Use(BackgroundProperty, "Mat.Bg");
        card.PointerPressed += (_, _) => SelectText(comment.Id);
        _byId[comment.Id] = (card, field);
        return card;
    }

    private static Control Byline(Comment comment)
    {
        var line = new StackPanel { Spacing = 0 };
        line.Children.Add(new TextBlock { Text = comment.Author.Length > 0 ? comment.Author : "Unbekannt", FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (comment.Date is { } date)
        {
            line.Children.Add(new TextBlock { Text = Ui.FormatDateTime(new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local))), FontSize = 11 }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"));
        }

        return line;
    }

    private PlacedComment? Find(string id) => _editor.Document.Session.Comments().FirstOrDefault(placed => placed.Comment.Id == id);

    private void Commit(string id, string text)
    {
        if (Find(id) is { } placed)
        {
            _editor.Document.Session.SetCommentText(placed.Reference, text);
        }

        Refresh();
    }

    private void SelectText(string id)
    {
        if (Find(id) is { } placed)
        {
            _editor.CloseHeaderFooter();
            _editor.Document.Session.Select(placed.Range.Start, placed.Range.End);
        }
    }

    private void Delete(string id)
    {
        if (Find(id) is { } placed)
        {
            _editor.Document.Session.DeleteComment(placed.Reference);
            _editor.FocusDocument();
        }
    }
}
