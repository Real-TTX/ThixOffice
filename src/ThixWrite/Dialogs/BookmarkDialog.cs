using Avalonia.Automation;
using Avalonia.Controls;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Editing;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Einfügen › Lesezeichen ("Textmarke"): the name and the bookmarks of the document; OK adds a bookmark of that name around the selection
/// (one of that name moves here), "Gehe zu" puts the caret on the chosen one, "Löschen" removes it. Links can point to bookmarks.
/// </summary>
public sealed class BookmarkDialog : WriteDialog
{
    private readonly WriteEditor _editor;
    private readonly MatFormField _nameField;

    public BookmarkDialog(WriteEditor editor)
        : base("Textmarke", 440)
    {
        _editor = editor;
        NameBox = new TextBox { Watermark = "Name der Textmarke" };
        AutomationProperties.SetName(NameBox, "Textmarkenname");
        List = new ListBox { Height = 160 };
        AutomationProperties.SetName(List, "Textmarken");
        List.SelectionChanged += (_, _) =>
        {
            if (List.SelectedItem is string name)
            {
                NameBox.Text = name;
            }
        };
        var go = new Button { Content = new IconLabel { Icon = "arrow-right", Text = "Gehe zu" } };
        var delete = new Button { Content = new IconLabel { Icon = "trash", Text = "Löschen" } };
        ActionBar.SetSeparated(delete, true);
        go.Click += (_, _) =>
        {
            if (List.SelectedItem is string name)
            {
                _editor.Session.GoToBookmark(name);
            }
        };
        delete.Click += (_, _) =>
        {
            if (List.SelectedItem is string name)
            {
                _editor.Session.DeleteBookmark(name);
                Fill();
            }
        };
        _nameField = Field("Textmarkenname", NameBox);
        SetBody(new StackPanel { Spacing = 10, Children = { _nameField, List, new ActionBar { Children = { go, delete } } } });
        Fill();
        Opened += (_, _) => NameBox.Focus();
    }

    public TextBox NameBox { get; }

    public ListBox List { get; }

    /// <summary>The error at the name (tests).</summary>
    public string? NameError => _nameField.Error;

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new BookmarkDialog(editor).ShowAsync(owner);

    private void Fill() => List.ItemsSource = _editor.Session.Bookmarks().Select(bookmark => bookmark.Name).ToList();

    protected override bool Apply()
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (EditingSession.BookmarkNameProblem(name) is { } problem)
        {
            _nameField.Error = problem;
            return false;
        }

        _editor.Session.AddBookmark(name);
        return true;
    }
}
