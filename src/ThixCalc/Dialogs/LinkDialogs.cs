using MatDesktop;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// Einfügen › Link (Strg+K): an address (http, https, mailto – checked by the business layer's LinkService) or a place in
/// this workbook ("Tabelle2!A1"), the text to show; Link entfernen for an existing one (set apart).
/// </summary>
public sealed class HyperlinkDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly TextBox _display = new();
    private readonly TextBox _address = new() { Watermark = "https://… oder Tabelle2!A1" };
    private readonly MatDesktop.MatFormField _addressField;

    private HyperlinkDialog(CalcEditor editor)
        : base("Link einfügen", 480)
    {
        _editor = editor;
        var session = editor.Session;
        var existing = session.ActiveSheet.Hyperlinks.FirstOrDefault(link => link.Range.Contains(session.ActiveCell));
        _display.Text = existing?.Display ?? (session.ActiveSheet.GetValue(session.ActiveCell) is { IsText: true } value ? value.Text : string.Empty);
        _address.Text = existing?.Target ?? existing?.Location;
        AutomationProperties.SetName(_address, "Adresse");
        AutomationProperties.SetName(_display, "Anzuzeigender Text");
        _addressField = Field("Adresse", _address);
        var body = new StackPanel { Spacing = 12, Children = { Field("Anzuzeigender Text", _display), _addressField } };
        if (existing is not null)
        {
            var remove = new Button { Content = new IconLabel { Icon = "trash", Text = "Link entfernen" } };
            remove.Click += (_, _) =>
            {
                Run(() => session.SetHyperlink(existing.Range, null));
                Close();
            };
            body.Children.Add(remove);
        }

        SetBody(body);
        Opened += (_, _) => _address.Focus();
    }

    public TextBox Address => _address;

    public TextBox Display => _display;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new HyperlinkDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var address = _address.Text?.Trim() ?? string.Empty;
        var session = _editor.Session;
        var isPlace = address.Contains('!') && !address.Contains("://", StringComparison.Ordinal);
        if (!isPlace)
        {
            try
            {
                address = LinkService.Normalize(address);
            }
            catch (ThixOffice.Core.Models.BusinessException exception)
            {
                _addressField.Error = exception.Message;
                return false;
            }
        }

        var display = string.IsNullOrWhiteSpace(_display.Text) ? address : _display.Text;
        var link = isPlace ? new Hyperlink(session.Selection, null, address, display) : new Hyperlink(session.Selection, address, null, display);
        return Run(() =>
        {
            if (session.ActiveSheet.GetValue(session.ActiveCell).IsBlank || session.ActiveSheet.GetValue(session.ActiveCell).IsText)
            {
                session.EnterInput(display);
            }

            session.SetHyperlink(session.Selection, link);
        });
    }
}

/// <summary>Einfügen › Notiz (Umschalt+F2): the note of the active cell (Excel's "Notiz"); empty removes it.</summary>
public sealed class CommentDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly TextBox _text = new() { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };

    private CommentDialog(CalcEditor editor)
        : base("Notiz", 440)
    {
        _editor = editor;
        var session = editor.Session;
        _text.Text = session.ActiveSheet.Comments.TryGetValue(session.ActiveCell, out var comment) ? comment.Text : string.Empty;
        AutomationProperties.SetName(_text, "Notiz");
        SetBody(Field($"Notiz zu {session.ActiveCell}", _text));
        Opened += (_, _) => _text.Focus();
    }

    public TextBox Text => _text;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new CommentDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var session = _editor.Session;
        var text = _text.Text?.Trim() ?? string.Empty;
        return Run(() => session.SetComment(session.ActiveCell, text.Length == 0 ? null : new CellComment(Environment.UserName, text)));
    }
}

/// <summary>
/// Formeln › Namens-Manager (Strg+F3): the defined names with what they refer to; toolbar above (Neu, Bearbeiten), actions
/// below left, Löschen set apart (house rules for lists). Neu / Bearbeiten ask for the name and the reference.
/// </summary>
public sealed class NameManagerDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly ListBox _names = new() { Height = 220 };

    private NameManagerDialog(CalcEditor editor)
        : base("Namens-Manager", 560)
    {
        _editor = editor;
        AutomationProperties.SetName(_names, "Namen");
        var add = new Button { Content = new IconLabel { Icon = "plus", Text = "Neu …" } }.Themed("MatButton.Primary");
        add.Click += async (_, _) => await EditAsync(null);
        var edit = new Button { Content = new IconLabel { Icon = "edit", Text = "Bearbeiten …" } };
        edit.Click += async (_, _) => await EditAsync(Selected());
        var delete = new Button { Content = new IconLabel { Icon = "trash", Text = "Löschen" } }.Themed("MatButton.Danger");
        ActionBar.SetSeparated(delete, true);
        delete.Click += async (_, _) =>
        {
            if (Selected() is { } name && await QuestionDialog.ConfirmAsync(this, "Namen löschen", $"Möchten Sie den Namen „{name.Name}“ löschen?", "Löschen", isDestructive: true))
            {
                Run(() => _editor.Session.DeleteName(name.Name, ScopeOf(name)));
                Refresh();
            }
        };
        SetBody(new StackPanel { Spacing = 10, Children = { _names, new ActionBar { Children = { add, edit, delete } } } });
        OkButton.Content = new IconLabel { Icon = "check", Text = "Schließen" };
        CancelButton.IsVisible = false;
        Refresh();
    }

    public ListBox Names => _names;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new NameManagerDialog(editor).ShowAsync(owner);

    /// <summary>Formeln › Namen definieren: the name for the selection, asked once.</summary>
    public static async Task DefineAsync(Window owner, CalcEditor editor)
    {
        var session = editor.Session;
        var reference = "=" + Core.Formulas.FormulaTokens.QuoteSheet(session.ActiveSheet.Name) + "!" + Absolute(session.Selection);
        string? error = null;
        while (await TextInputDialog.AskAsync(owner, "Neuer Name", $"Name für {reference[1..]}", string.Empty, "Definieren", error) is { } name)
        {
            try
            {
                session.DefineName(name.Trim(), reference);
                return;
            }
            catch (ThixOffice.Core.Models.BusinessException exception)
            {
                error = exception.Message;
            }
        }
    }

    private static string Absolute(CellRange range) => range.IsSingleCell
        ? $"${A1.ColumnName(range.FirstColumn)}${range.FirstRow + 1}"
        : $"${A1.ColumnName(range.FirstColumn)}${range.FirstRow + 1}:${A1.ColumnName(range.LastColumn)}${range.LastRow + 1}";

    private DefinedName? Selected() => _names.SelectedItem is Entry entry ? entry.Name : null;

    private Worksheet? ScopeOf(DefinedName name) => name.SheetId is { } id ? _editor.Session.Workbook.GetSheetById(id) : null;

    private void Refresh()
    {
        var session = _editor.Session;
        _names.ItemsSource = session.Workbook.Names
            .Where(name => !name.IsBuiltIn && !name.Hidden)
            .OrderBy(name => name.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new Entry(name, session.NameFormulaText(name)))
            .ToList();
    }

    private async Task EditAsync(DefinedName? existing)
    {
        var session = _editor.Session;
        var name = await TextInputDialog.AskAsync(this, existing is null ? "Neuer Name" : "Name bearbeiten", "Name", existing?.Name ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var current = existing is null ? "=" + Core.Formulas.FormulaTokens.QuoteSheet(session.ActiveSheet.Name) + "!" + Absolute(session.Selection) : session.NameFormulaText(existing);
        var refersTo = await TextInputDialog.AskAsync(this, "Bezieht sich auf", "Bezieht sich auf", current);
        if (string.IsNullOrWhiteSpace(refersTo))
        {
            return;
        }

        Run(() =>
        {
            if (existing is not null && !string.Equals(existing.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                session.DeleteName(existing.Name, ScopeOf(existing));
            }

            session.DefineName(name.Trim(), refersTo.Trim(), existing is null ? null : ScopeOf(existing));
        });
        Refresh();
    }

    protected override bool Apply() => true;

    private sealed record Entry(DefinedName Name, string RefersTo)
    {
        public override string ToString() => $"{Name.Name}    {RefersTo}";
    }
}
