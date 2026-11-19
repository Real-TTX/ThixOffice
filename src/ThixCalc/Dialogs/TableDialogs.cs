using Avalonia.Automation;
using Avalonia.Controls;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// Tabelle erstellen (Strg+T): "Wo befinden sich die Daten für die Tabelle?" – the proposed range (the current region) – and
/// "Tabelle hat Überschriften" (guessed: a first row of texts over values).
/// </summary>
public sealed class CreateTableDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly string? _style;
    private readonly MatFormField _rangeField;

    private CreateTableDialog(CalcEditor editor, string? style)
        : base("Tabelle erstellen", 420)
    {
        _editor = editor;
        _style = style;
        var session = editor.Session;
        var range = session.ProposedTableRange();
        RangeBox = new TextBox { Text = "=" + ChartReferences.Of(session.ActiveSheet, range), MinWidth = 260 };
        AutomationProperties.SetName(RangeBox, "Bereich der Tabelle");
        HeaderBox = new CheckBox { Content = "Tabelle hat Überschriften", IsChecked = LooksLikeHeaders(session.ActiveSheet, range) };
        _rangeField = Field("Wo befinden sich die Daten für die Tabelle?", RangeBox);
        SetBody(new StackPanel { Spacing = 12, Children = { _rangeField, HeaderBox } });
    }

    public TextBox RangeBox { get; }

    public CheckBox HeaderBox { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, string? style) =>
        editor.Editing.Commit() ? Create(editor, style).ShowAsync(owner) : Task.FromResult(false);

    public static CreateTableDialog Create(CalcEditor editor, string? style = null) => new(editor, style);

    /// <summary>Excel's guess: every cell of the first row is text and the second row has something else.</summary>
    private static bool LooksLikeHeaders(Worksheet sheet, CellRange range)
    {
        if (range.RowCount < 2)
        {
            return false;
        }

        var first = Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetValue(range.FirstRow, column)).ToList();
        var second = Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetValue(range.FirstRow + 1, column)).ToList();
        return first.All(value => value.Kind == CellValueKind.Text) && (second.Any(value => value.Kind != CellValueKind.Text) || range.RowCount > 2);
    }

    protected override bool Apply()
    {
        _rangeField.Error = null;
        try
        {
            var range = ParseRange(_editor.Session, RangeBox.Text);
            _editor.Session.CreateTable(range, HeaderBox.IsChecked == true, _style);
            return true;
        }
        catch (BusinessException exception)
        {
            _rangeField.Error = exception.Message;
            return false;
        }
    }

    /// <summary>A range typed into a table dialog: "A1:C9", "$A$1:$C$9" or "=Tabelle1!$A$1:$C$9" on the active sheet.</summary>
    internal static CellRange ParseRange(EditingSession session, string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().TrimStart('=');
        var bang = trimmed.LastIndexOf('!');
        if (bang > 0)
        {
            var sheetName = trimmed[..bang].Trim('\'');
            if (!string.Equals(sheetName, session.ActiveSheet.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException("Eine Tabelle entsteht auf dem aktiven Blatt.", "range");
            }

            trimmed = trimmed[(bang + 1)..];
        }

        return CellRange.TryParse(trimmed.Replace("$", string.Empty, StringComparison.Ordinal)) is { } range
            ? range
            : throw new BusinessException("Geben Sie einen Bereich ein, z. B. =$A$1:$C$10.", "range");
    }
}

/// <summary>Tabellengröße ändern: the new range of the table (its header row stays where it is).</summary>
public sealed class ResizeTableDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly TableDefinition _table;
    private readonly MatFormField _rangeField;

    private ResizeTableDialog(CalcEditor editor, TableDefinition table)
        : base("Tabellengröße ändern", 420)
    {
        _editor = editor;
        _table = table;
        RangeBox = new TextBox { Text = "=" + ChartReferences.Of(editor.Session.ActiveSheet, table.Range), MinWidth = 260 };
        AutomationProperties.SetName(RangeBox, "Neuer Datenbereich");
        _rangeField = Field("Wählen Sie den neuen Datenbereich für die Tabelle aus", RangeBox);
        SetBody(new StackPanel { Spacing = 8, Children = { _rangeField, new TextBlock { Text = "Die Überschriften müssen in derselben Zeile bleiben.", TextWrapping = Avalonia.Media.TextWrapping.Wrap } } });
    }

    public TextBox RangeBox { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, TableDefinition table) => Create(editor, table).ShowAsync(owner);

    public static ResizeTableDialog Create(CalcEditor editor, TableDefinition table) => new(editor, table);

    protected override bool Apply()
    {
        _rangeField.Error = null;
        try
        {
            _editor.Session.ResizeTable(_table.Name, CreateTableDialog.ParseRange(_editor.Session, RangeBox.Text));
            return true;
        }
        catch (BusinessException exception)
        {
            _rangeField.Error = exception.Message;
            return false;
        }
    }
}
