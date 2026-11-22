using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Model;
using ThixOffice.Desktop;

namespace ThixMail.Editor;

/// <summary>An entry of the folder pane: a heading (Favoriten, an account), the unified inbox or a folder, with its unread count and depth in the tree.</summary>
public sealed record FolderNode(FolderNodeKind Kind, string Label, string Icon, int Unread, int Depth, long? FolderId = null, string? AccountId = null, string? State = null)
{
    public bool IsHeading => Kind is FolderNodeKind.FavoritesHeading or FolderNodeKind.Account;
}

public enum FolderNodeKind
{
    FavoritesHeading,
    Unified,

    /// <summary>The search folder "Ungelesene E-Mail": unread mail of every folder of every account (not Gelöschte Elemente, Junk-E-Mail, Entwürfe).</summary>
    SearchUnread,

    /// <summary>The search folder "Zur Nachverfolgung": flagged mail of every folder.</summary>
    SearchFlagged,
    Account,
    Folder,
}

/// <summary>A row of the folder pane: icon, name (bold with unread mail), the unread count at the right; headings in small capitals.</summary>
internal sealed class FolderRow : Grid
{
    public FolderRow(FolderNode node)
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        Margin = new Thickness(node.Depth * 14, node.IsHeading ? 8 : 0, 0, 0);
        var icon = new MatIcon { Icon = node.Icon, Size = node.IsHeading ? 14 : 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = node.Label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = node.IsHeading || node.Unread > 0 ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = node.IsHeading ? MatTokens.Type.FsSm : MatTokens.Type.FsMd,
        };
        if (node.IsHeading)
        {
            label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            icon.Use(MatIcon.ForegroundProperty, "Mat.TextMuted");
        }

        var count = new TextBlock
        {
            Text = node.Unread > 0 ? node.Unread.ToString(System.Globalization.CultureInfo.InvariantCulture) : node.State ?? "",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = MatTokens.Type.FsSm,
            Margin = new Thickness(6, 0, 4, 0),
        };
        count.Use(TextBlock.ForegroundProperty, node.Unread > 0 ? "Mat.Accent" : "Mat.Warning");
        Grid.SetColumn(label, 1);
        Grid.SetColumn(count, 2);
        Children.Add(icon);
        Children.Add(label);
        Children.Add(count);
        AutomationProperties.SetName(this, node.Unread > 0 ? $"{node.Label}, {node.Unread} ungelesen" : node.Label);
    }
}

/// <summary>
/// A row of the message list: the date group's heading, or a message – a bar at the left while unread, the sender (bold while unread), the time, the
/// subject with the number of messages of a conversation and its categories, the preview (comfortable density), icons for attachments and the flag,
/// and – while the pointer is over it – quick actions: delete, flag, read/unread. The header comes from the editor's cache (read when the row shows).
/// </summary>
internal sealed class MessageRow : Grid
{
    private readonly MailEditor _editor;
    private readonly Border _unread = new() { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4, 8, 4) };
    private readonly TextBlock _sender = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _date = new() { FontSize = MatTokens.Type.FsXs, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _subject = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = MatTokens.Type.FsSm };
    private readonly TextBlock _count = new() { FontSize = MatTokens.Type.FsXs, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _preview = new() { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = MatTokens.Type.FsXs };
    private readonly MatIcon _attachment = new() { Icon = "paperclip", Size = 13, Margin = new Thickness(4, 0, 0, 0) };
    private readonly MatIcon _flag = new() { Icon = "pin", Size = 13, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _group = new() { FontWeight = FontWeight.SemiBold, FontSize = MatTokens.Type.FsSm, Margin = new Thickness(4, 10, 0, 4) };
    private readonly StackPanel _quick = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
    private readonly Grid _message = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    private ListRow? _row;

    public MessageRow(MailEditor editor)
    {
        _editor = editor;
        _date.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _preview.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _count.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _group.Use(TextBlock.ForegroundProperty, "Mat.Accent");
        _unread.Use(Border.BackgroundProperty, "Mat.Accent");
        _flag.Use(MatIcon.ForegroundProperty, "Mat.Danger");
        _attachment.Use(MatIcon.ForegroundProperty, "Mat.TextMuted");

        var top = new DockPanel();
        DockPanel.SetDock(_date, Dock.Right);
        top.Children.Add(_date);
        top.Children.Add(_sender);
        var middle = new DockPanel();
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _attachment, _flag } };
        DockPanel.SetDock(icons, Dock.Right);
        middle.Children.Add(icons);
        var subjectLine = new StackPanel { Orientation = Orientation.Horizontal, Children = { _subject, _count, _chips } };
        middle.Children.Add(subjectLine);
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { top, middle, _preview } };
        Grid.SetColumn(texts, 1);
        _message.Children.Add(_unread);
        _message.Children.Add(texts);

        foreach (var (icon, name, command) in new[] { ("trash", "Löschen", MailCommandIds.Delete), ("pin", "Zur Nachverfolgung", MailCommandIds.Flag), ("mail", "Gelesen/Ungelesen", MailCommandIds.ToggleRead) })
        {
            var button = new Button { Content = new MatIcon { Icon = icon, Size = 13 }, Padding = new Thickness(5), MinHeight = 0, Focusable = false, IsTabStop = false }.Themed("MatButton.Ghost");
            AutomationProperties.SetName(button, name);
            ToolTip.SetTip(button, name);
            button.Click += async (_, e) =>
            {
                e.Handled = true;
                if (_row is { IsMessage: true } row)
                {
                    await _editor.QuickActionAsync(row.Id, command);
                }
            };
            _quick.Children.Add(button);
        }

        var quickBack = new Border { Child = _quick, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        quickBack.Use(Border.BackgroundProperty, "Mat.Surface");
        Children.Add(_group);
        Children.Add(_message);
        Children.Add(quickBack);
        PointerEntered += (_, _) => _quick.IsVisible = _row?.IsMessage == true;
        PointerExited += (_, _) => _quick.IsVisible = false;
        DataContextChanged += (_, _) => Show(DataContext as ListRow);
    }

    public void Show(ListRow? row)
    {
        _row = row;
        _quick.IsVisible = false;
        if (row is null)
        {
            return;
        }

        var isGroup = row.Kind == RowKind.Group;
        _group.IsVisible = isGroup;
        _message.IsVisible = !isGroup;
        if (isGroup)
        {
            _group.Text = row.Group;
            AutomationProperties.SetName(this, row.Group);
            MinHeight = 0;
            return;
        }

        var header = _editor.HeaderOf(row.Id);
        var compact = _editor.IsCompact;
        Margin = new Thickness(row.Kind == RowKind.Child ? 22 : 0, 0, 0, 0);
        MinHeight = compact ? 38 : 58;
        if (header is null)
        {
            _sender.Text = "…";
            _subject.Text = "";
            _preview.Text = "";
            return;
        }

        var unread = !header.IsRead;
        _unread.Opacity = unread ? 1 : 0;
        _sender.FontWeight = unread ? FontWeight.SemiBold : FontWeight.Normal;
        _sender.Text = _editor.IsSentFolder(header.FolderId) ? "An: " + string.Join("; ", header.To.Select(a => a.Display)) : header.From.Display;
        _subject.Text = header.Subject.Length > 0 ? header.Subject : "(Kein Betreff)";
        _subject.FontWeight = unread ? FontWeight.SemiBold : FontWeight.Normal;
        _subject.Use(TextBlock.ForegroundProperty, unread ? "Mat.Accent" : "Mat.Text");
        _count.Text = row.Count > 1 ? $"({row.Count}){(row.IsExpanded ? " ▾" : " ▸")}" : "";
        _date.Text = MessageList.ShortDate(header.Date, DateTimeOffset.Now);
        _preview.Text = header.Preview;
        _preview.IsVisible = !compact;
        _attachment.IsVisible = header.HasAttachments;
        _flag.IsVisible = header.IsFlagged;
        _chips.Children.Clear();
        foreach (var category in header.Categories.Take(3))
        {
            var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0), Child = new TextBlock { Text = category, FontSize = MatTokens.Type.FsXs } };
            chip.Use(Border.BackgroundProperty, "Mat.AccentSoft");
            ((TextBlock)chip.Child).Use(TextBlock.ForegroundProperty, CategoryTokens.Of(category));
            _chips.Children.Add(chip);
        }

        AutomationProperties.SetName(this, $"{(unread ? "Ungelesen, " : "")}{header.From.Display}, {_subject.Text}, {_date.Text}");
    }
}

/// <summary>The colour of a category: one of the Mat accent tokens, chosen by the name (the same name always gets the same colour).</summary>
internal static class CategoryTokens
{
    private static readonly string[] Tokens = ["Mat.Accent", "Mat.Danger", "Mat.Warning", "Mat.SuccessText", "Mat.AccentText", "Mat.DangerText", "Mat.WarningText", "Mat.TextMuted"];

    public static string Of(string name)
    {
        var hash = 0;
        foreach (var character in name.ToLowerInvariant())
        {
            hash = (hash * 31) + character;
        }

        return Tokens[Math.Abs(hash % Tokens.Length)];
    }
}
