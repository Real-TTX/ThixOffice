using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Layout › Spalten and Umbrüche, Entwurf › Seitenfarbe, Start › Groß-/Kleinschreibung (Umschalt+F3), Überprüfen › Änderungen annehmen /
/// ablehnen, and the keys German Word users know for paragraphs: Strg+1 / Strg+2 / Strg+5 (Zeilenabstand), Strg+Umschalt+N (Standard),
/// Alt+1 / Alt+2 / Alt+3 (Überschrift 1–3), Strg+M / Strg+Umschalt+M (Einzug), Strg+Umschalt+Eingabe (Spaltenumbruch).
/// </summary>
public static class LayoutCommands
{
    public static IReadOnlyList<(string Id, string Label, int Count, bool? NarrowLeft)> ColumnChoices { get; } =
    [
        ("one", "Eins", 1, null), ("two", "Zwei", 2, null), ("three", "Drei", 3, null), ("left", "Links", 2, true), ("right", "Rechts", 2, false),
    ];

    public static IReadOnlyList<(string Id, string Label, SectionStart Start)> SectionBreaks { get; } =
    [
        ("next", "Nächste Seite", SectionStart.NextPage), ("continuous", "Fortlaufend", SectionStart.Continuous),
        ("even", "Gerade Seite", SectionStart.EvenPage), ("odd", "Ungerade Seite", SectionStart.OddPage),
    ];

    public static IReadOnlyList<(string Id, string Label, CaseChange Change)> CaseChoices { get; } =
    [
        ("sentence", "Satzanfang groß.", CaseChange.Sentence), ("lower", "kleinbuchstaben", CaseChange.Lower), ("upper", "GROSSBUCHSTABEN", CaseChange.Upper),
        ("title", "Ersten Buchstaben Im Wort Groß", CaseChange.Title), ("toggle", "gROSS-/kLEINSCHREIBUNG UMKEHREN", CaseChange.Toggle),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        EditingSession? Session() => Editor()?.Session;
        bool HasEditor() => Editor() is not null;
        bool InBody() => Editor() is { IsEditingHeaderFooter: false, IsEditingNote: false };
        void With(Action<EditingSession> action)
        {
            if (Session() is { } session)
            {
                action(session);
            }
        }

        var commands = window.Commands;

        // Layout › Spalten
        commands.Add(new OfficeCommand(WriteCommandIds.Columns, "Spalten", () => { })
        {
            Icon = "columns", CanExecute = InBody,
            Description = "Teilt den Text des Abschnitts in Spalten – wie in einer Zeitung.", Keywords = ["spalten", "zweispaltig", "dreispaltig", "zeitung", "kolumnen"],
        });
        foreach (var (id, label, count, narrowLeft) in ColumnChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.Columns}.{id}", label, () => With(session =>
            {
                if (narrowLeft is { } left)
                {
                    session.SetUnequalColumns(left);
                }
                else
                {
                    session.SetColumns(count);
                }
            }))
            {
                Icon = "columns", CanExecute = InBody, IsSearchable = false,
                IsChecked = () => Session() is { } session && session.Document.SectionOf(session.Selection.Start.Block).Properties is var section
                                  && (narrowLeft is null ? section.ColumnCount == count && section.Columns.Count == 0 : section.Columns.Count == 2 && (section.Columns[0].Width < section.Columns[1].Width) == narrowLeft),
            });
        }

        // Layout › Umbrüche
        commands.Add(new OfficeCommand(WriteCommandIds.Breaks, "Umbrüche", () => { })
        {
            Icon = "page-break", CanExecute = InBody,
            Description = "Fügt einen Seiten-, Spalten- oder Abschnittsumbruch ein.", Keywords = ["umbruch", "abschnitt", "abschnittsumbruch", "spaltenumbruch", "seitenumbruch"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ColumnBreak, "Spaltenumbruch", () => With(session => session.InsertColumnBreak()))
        {
            Icon = "page-break", Shortcut = "Ctrl+Shift+Enter", CanExecute = InBody,
            Description = "Der Text nach der Einfügemarke beginnt in der nächsten Spalte.", Keywords = ["spalte", "umbruch", "spaltenumbruch"],
        });
        foreach (var (id, label, start) in SectionBreaks)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.SectionBreak}.{id}", $"Abschnittsumbruch: {label}", () => With(session => session.InsertSectionBreak(start)))
            {
                Icon = "page-break", CanExecute = InBody,
                Description = $"Beginnt einen neuen Abschnitt ({label}) – mit eigener Seiteneinrichtung, eigenen Spalten, Kopf- und Fußzeilen.",
                Keywords = ["abschnitt", "abschnittsumbruch", label.ToLowerInvariant()],
            });
        }

        // Entwurf › Seitenfarbe
        commands.Add(new OfficeValueCommand(WriteCommandIds.PageColor, "Seitenfarbe", OfficeValueKind.Color,
            () => Session()?.Document.PageColor is { } color ? "#" + color : null,
            value => With(session => session.SetPageColor(string.IsNullOrEmpty(value) ? null : value.TrimStart('#'))))
        {
            Icon = "palette", CanExecute = HasEditor,
            Description = "Färbt den Hintergrund der Seiten.", Keywords = ["seitenfarbe", "hintergrund", "farbe", "seite"],
        });

        // Start › Groß-/Kleinschreibung
        commands.Add(new OfficeCommand(WriteCommandIds.ChangeCase, "Groß-/Kleinschreibung", () => With(session => session.CycleCase()))
        {
            Icon = "change-case", Shortcut = "Shift+F3", CanExecute = HasEditor,
            Description = "Schreibt die Markierung groß, klein oder mit großen Anfangsbuchstaben (Umschalt+F3 wechselt).",
            Keywords = ["großschreibung", "kleinschreibung", "großbuchstaben", "kleinbuchstaben", "groß klein"],
        });
        foreach (var (id, label, change) in CaseChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.ChangeCase}.{id}", label, () => With(session => session.ChangeCase(change)))
            {
                CanExecute = HasEditor, IsSearchable = false,
            });
        }

        // Überprüfen › Änderungen
        commands.Add(new OfficeCommand(WriteCommandIds.AcceptAllRevisions, "Alle Änderungen annehmen", () => With(session => session.AcceptAllRevisions()))
        {
            Icon = "check-circle", CanExecute = () => InBody() && Session()?.HasRevisions == true,
            Description = "Übernimmt alle nachverfolgten Änderungen in den Text: Eingefügtes bleibt, Gelöschtes verschwindet.",
            Keywords = ["änderungen", "annehmen", "überarbeitung", "nachverfolgen", "akzeptieren"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.RejectAllRevisions, "Alle Änderungen ablehnen", () => With(session => session.RejectAllRevisions()))
        {
            Icon = "x-circle", CanExecute = () => InBody() && Session()?.HasRevisions == true,
            Description = "Verwirft alle nachverfolgten Änderungen: Eingefügtes verschwindet, Gelöschtes kommt zurück.",
            Keywords = ["änderungen", "ablehnen", "überarbeitung", "nachverfolgen", "verwerfen"],
        });

        // The keys of German Word (not offered by the search: the commands they run are).
        void Key(string id, string label, string shortcut, string? macShortcut, Action<EditingSession> action) =>
            commands.Add(new OfficeCommand(id, label, () => With(action)) { Shortcut = shortcut, MacShortcut = macShortcut, CanExecute = HasEditor, IsSearchable = false });

        Key(WriteCommandIds.KeyLineSpacing1, "Einfacher Zeilenabstand", "Ctrl+D1", null, session => session.SetLineSpacing(LineSpacing.Single));
        Key(WriteCommandIds.KeyLineSpacing2, "Doppelter Zeilenabstand", "Ctrl+D2", null, session => session.SetLineSpacing(new LineSpacing(LineSpacingRule.Multiple, 2)));
        Key(WriteCommandIds.KeyLineSpacing15, "Zeilenabstand 1,5", "Ctrl+D5", null, session => session.SetLineSpacing(new LineSpacing(LineSpacingRule.Multiple, 1.5)));
        Key(WriteCommandIds.KeyNormal, "Standard", "Ctrl+Shift+N", null, session => session.ApplyStyle(StyleId(session, "Normal", BuiltInStyles.Normal)));
        Key(WriteCommandIds.KeyHeading1, "Überschrift 1", "Alt+D1", "Ctrl+Alt+D1", session => session.ApplyStyle(StyleId(session, "heading 1", BuiltInStyles.Heading1)));
        Key(WriteCommandIds.KeyHeading2, "Überschrift 2", "Alt+D2", "Ctrl+Alt+D2", session => session.ApplyStyle(StyleId(session, "heading 2", "Heading2")));
        Key(WriteCommandIds.KeyHeading3, "Überschrift 3", "Alt+D3", "Ctrl+Alt+D3", session => session.ApplyStyle(StyleId(session, "heading 3", "Heading3")));
        Key(WriteCommandIds.KeyIndent, "Einzug vergrößern", "Ctrl+M", "Ctrl+Alt+M", session => session.IncreaseIndent());
        Key(WriteCommandIds.KeyOutdent, "Einzug verkleinern", "Ctrl+Shift+M", "Ctrl+Alt+Shift+M", session => session.DecreaseIndent());
    }

    /// <summary>The id of a built-in style in the document (German Word names "heading 1" "berschrift1"), else ThixWrite's id.</summary>
    private static string StyleId(EditingSession session, string name, string fallback) =>
        session.Document.Styles.Styles.FirstOrDefault(style => style.Type == StyleType.Paragraph && string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? fallback;
}
