using System.Globalization;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// The dialogs Schriftart … (Strg+D) and Absatz … (the launchers of their ribbon groups), Format übertragen, Einfügen › Symbol
/// (the frequent symbols and "Weitere Symbole …").
/// </summary>
public static class TextCommands
{
    /// <summary>The symbols of Einfügen › Symbol's menu (Word's most used).</summary>
    public static IReadOnlyList<string> FrequentSymbols { get; } =
        ["€", "£", "¥", "©", "®", "™", "±", "≠", "≤", "≥", "÷", "×", "∞", "µ", "α", "β", "π", "Ω", "°", "§", "¶", "•", "–", "—", "…", "„", "“", "‚", "‘", "→", "←", "✓"];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.FontDialog, "Schriftart …", () => Editor() is { } editor ? FontDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "type", Shortcut = "Ctrl+D", CanExecute = HasEditor,
            Description = "Schriftart, Schriftschnitt, Größe, Unterstreichung und Effekte in einem Dialog.", Keywords = ["schrift", "font", "effekte", "dialog"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ParagraphDialog, "Absatz …", () => Editor() is { } editor ? ParagraphDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "align-left", CanExecute = HasEditor,
            Description = "Einzüge, Abstände, Zeilenabstand, Umbruch und Tabstopps der Absätze.", Keywords = ["absatz", "einzug", "abstand", "zeilenabstand", "tabstopp", "tabulator"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.TabsDialog, "Tabstopps …", () => Editor() is { } editor ? ParagraphDialog.ShowAsync(window, editor, ParagraphDialog.TabsTab) : Task.CompletedTask)
        {
            Icon = "indent", CanExecute = HasEditor,
            Description = "Legt die Tabstopps der Absätze fest: Position, Ausrichtung, Füllzeichen.", Keywords = ["tabstopp", "tabulator", "tab"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.FormatPainter, "Format übertragen", () => Editor()?.ToggleFormatPainter())
        {
            Icon = "format-painter", Shortcut = "Ctrl+Shift+C", CanExecute = HasEditor, IsChecked = () => Editor()?.IsPainting == true,
            Description = "Übernimmt das Format der Markierung; die nächste Markierung bekommt es.", Keywords = ["pinsel", "format kopieren", "übertragen"],
        });

        commands.Add(new OfficeCommand(WriteCommandIds.Ruler, "Lineal", () =>
        {
            if (Editor() is { } editor)
            {
                editor.ShowRuler = !editor.ShowRuler;
            }
        })
        {
            Icon = "ruler", CanExecute = HasEditor, IsChecked = () => Editor()?.ShowRuler == true,
            Description = "Zeigt das Lineal mit Seitenrändern, Einzügen und Tabstopps; ziehen ändert sie.", Keywords = ["lineal", "einzug", "tabstopp", "ränder"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Symbol, "Symbol", () => Editor() is { } editor ? SymbolDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "symbol", CanExecute = HasEditor,
            Description = "Fügt ein Zeichen ein, das die Tastatur nicht hat (€, ©, →, Ω …).", Keywords = ["symbol", "sonderzeichen", "zeichen", "euro", "copyright"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.SymbolMore, "Weitere Symbole …", () => Editor() is { } editor ? SymbolDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "symbol", CanExecute = HasEditor, IsSearchable = false,
        });
        foreach (var symbol in FrequentSymbols)
        {
            commands.Add(new OfficeCommand(SymbolId(symbol), symbol, () => Editor()?.Session.InsertText(symbol))
            {
                CanExecute = HasEditor, IsSearchable = false,
            });
        }
    }

    /// <summary>The id of a symbol of the menu: write.symbol.u20ac.</summary>
    public static string SymbolId(string symbol) => $"{WriteCommandIds.Symbol}.u{char.ConvertToUtf32(symbol, 0).ToString("x4", CultureInfo.InvariantCulture)}";
}
