using System.Globalization;
using Avalonia.Controls;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// The commands of pictures: Einfügen › Bilder, and the contextual tab Bildformat (shown while a picture is selected):
/// Alternativtext, Bild ändern, Größe zurücksetzen, the alignment of its paragraph, Höhe and Breite (cm, the aspect ratio kept).
/// </summary>
public static class PictureCommands
{
    /// <summary>The context of the contextual tab (shown while one picture is selected).</summary>
    public const string Context = "picture";

    private const double PointsPerCm = 72 / 2.54;

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        bool Selected() => Editor()?.Session.SelectedImage is not null;
        ImageInline? Picture() => Editor()?.Session.SelectedImage?.Image;

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.InsertPicture, "Bilder", () => Editor()?.InsertPictureFromDialogAsync() ?? Task.CompletedTask)
        {
            Icon = "image", CanExecute = HasEditor,
            Description = "Fügt ein Bild aus einer Datei ein (PNG, JPEG, GIF, BMP).", Keywords = ["bild", "foto", "grafik", "einfügen", "png", "jpeg"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PictureAltText, "Alternativtext", () => Editor() is { } editor ? AltTextDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "text-box", CanExecute = Selected,
            Description = "Beschreibt das Bild für Menschen, die es nicht sehen (Bildschirmleser).", Keywords = ["barrierefreiheit", "beschreibung", "alt"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PictureChange, "Bild ändern", () => Editor()?.ChangePictureFromDialogAsync() ?? Task.CompletedTask)
        {
            Icon = "refresh", CanExecute = Selected,
            Description = "Ersetzt das Bild durch ein anderes; die Breite bleibt.", Keywords = ["ersetzen", "austauschen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PictureResetSize, "Größe zurücksetzen", () => Editor()?.Session.ResetImageSize())
        {
            Icon = "restore", CanExecute = Selected,
            Description = "Zeigt das Bild wieder in seiner eigenen Größe (höchstens so breit wie der Text).",
        });

        foreach (var (id, label, icon, alignment) in new[]
                 {
                     (WriteCommandIds.PictureAlignLeft, "Links", "align-left", ParagraphAlignment.Left),
                     (WriteCommandIds.PictureAlignCenter, "Zentriert", "align-center", ParagraphAlignment.Center),
                     (WriteCommandIds.PictureAlignRight, "Rechts", "align-right", ParagraphAlignment.Right),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Editor()?.Session.SetAlignment(alignment))
            {
                Icon = icon, CanExecute = Selected, IsSearchable = false,
                IsChecked = () => Editor()?.Session.GetFormatting().Alignment is { IsMixed: false } current && current.Value == alignment,
            });
        }

        commands.Add(new OfficeValueCommand(WriteCommandIds.PictureHeight, "Höhe", OfficeValueKind.Number,
            () => Picture() is { } picture ? Centimetres(picture.Height) : null,
            value => Resize(Editor(), value, byWidth: false))
        {
            Icon = "", CanExecute = Selected, IsSearchable = false,
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.PictureWidth, "Breite", OfficeValueKind.Number,
            () => Picture() is { } picture ? Centimetres(picture.Width) : null,
            value => Resize(Editor(), value, byWidth: true))
        {
            Icon = "", CanExecute = Selected, IsSearchable = false,
        });
    }

    private static string Centimetres(double points) => Math.Round(points / PointsPerCm, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>A new height or width in cm; the other follows (the aspect ratio stays, as with Word's "Seitenverhältnis sperren").</summary>
    private static void Resize(WriteEditor? editor, string? value, bool byWidth)
    {
        if (editor?.Session.SelectedImage is not { } selected
            || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) || centimetres <= 0)
        {
            return;
        }

        var image = selected.Image;
        var points = centimetres * PointsPerCm;
        var ratio = image.Height / image.Width;
        editor.Session.SetImageSize(byWidth ? points : points / ratio, byWidth ? points * ratio : points);
    }
}
