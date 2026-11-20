using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>Entwurf › Dokumentformatierung (Designs, Farben, Schriftarten) and Seitenhintergrund › Wasserzeichen.</summary>
public static class DesignCommands
{
    public static string DesignId(int index) => $"{WriteCommandIds.Design}.{index}";

    public static string ColorsId(int index) => $"{WriteCommandIds.ThemeColors}.{index}";

    public static string FontsId(int index) => $"{WriteCommandIds.ThemeFonts}.{index}";

    public static string WatermarkId(int index) => $"{WriteCommandIds.Watermark}.{index}";

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        var commands = window.Commands;

        commands.Add(new OfficeCommand(WriteCommandIds.Design, "Designs", () => Task.CompletedTask)
        {
            Icon = "sparkles", CanExecute = HasEditor,
            Description = "Gibt dem ganzen Dokument andere Designfarben und -schriftarten; Überschriften und Formatvorlagen folgen.",
            Keywords = ["design", "thema", "theme", "aussehen"],
        });
        for (var index = 0; index < DocumentDesign.All.Count; index++)
        {
            var design = DocumentDesign.All[index];
            commands.Add(new OfficeCommand(DesignId(index), design.Name, () => Editor()?.Session.ApplyDesign(design))
            {
                Icon = "sparkles", CanExecute = HasEditor, IsSearchable = false,
                IsChecked = () => Editor()?.Session is { } session && session.CurrentThemeColors == design.Colors && session.CurrentThemeFonts == design.Fonts,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.ThemeColors, "Farben", () => Task.CompletedTask)
        {
            Icon = "palette", CanExecute = HasEditor,
            Description = "Wählt die Designfarben des Dokuments (Akzentfarben der Überschriften, Tabellen und Formen).",
            Keywords = ["farben", "designfarben", "farbschema"],
        });
        for (var index = 0; index < ThemeColorSet.All.Count; index++)
        {
            var colors = ThemeColorSet.All[index];
            commands.Add(new OfficeCommand(ColorsId(index), colors.Name, () => Editor()?.Session.SetThemeColors(colors))
            {
                Icon = "palette", CanExecute = HasEditor, IsSearchable = false,
                IsChecked = () => Editor()?.Session.CurrentThemeColors == colors,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.ThemeFonts, "Schriftarten", () => Task.CompletedTask)
        {
            Icon = "type", CanExecute = HasEditor,
            Description = "Wählt die Designschriftarten des Dokuments: eine für Überschriften, eine für den Text.",
            Keywords = ["schriftarten", "designschriftarten", "fonts"],
        });
        for (var index = 0; index < ThemeFontSet.All.Count; index++)
        {
            var fonts = ThemeFontSet.All[index];
            commands.Add(new OfficeCommand(FontsId(index), $"{fonts.Name} ({fonts.Heading} / {fonts.Body})", () => Editor()?.Session.SetThemeFonts(fonts))
            {
                Icon = "type", CanExecute = HasEditor, IsSearchable = false,
                IsChecked = () => Editor()?.Session.CurrentThemeFonts == fonts,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.Watermark, "Wasserzeichen", () => Task.CompletedTask)
        {
            Icon = "layers", CanExecute = HasEditor,
            Description = "Legt einen Text wie „VERTRAULICH“ oder ein Bild blass hinter den Text jeder Seite.",
            Keywords = ["wasserzeichen", "vertraulich", "entwurf", "hintergrund"],
        });
        for (var index = 0; index < Watermark.Texts.Count; index++)
        {
            var text = Watermark.Texts[index];
            commands.Add(new OfficeCommand(WatermarkId(index), text, () => Editor()?.Session.SetWatermark(new Watermark { Text = text, FontFamily = Editor()!.Session.Document.Theme.MinorFont }))
            {
                Icon = "type", CanExecute = HasEditor, IsSearchable = false,
                IsChecked = () => Editor()?.Session.CurrentWatermark?.Text == text,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.WatermarkCustom, "Benutzerdefiniertes Wasserzeichen …", () => Editor() is { } editor ? WatermarkDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "edit", CanExecute = HasEditor,
            Description = "Eigener Text, Schriftart und Farbe oder ein Bild als Wasserzeichen.", Keywords = ["wasserzeichen", "bildwasserzeichen", "textwasserzeichen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.WatermarkRemove, "Wasserzeichen entfernen", () => Editor()?.Session.SetWatermark(null))
        {
            Icon = "trash", CanExecute = () => Editor()?.Session.CurrentWatermark is not null,
            Description = "Nimmt das Wasserzeichen aus allen Kopfzeilen.", Keywords = ["wasserzeichen", "entfernen"],
        });
    }
}
