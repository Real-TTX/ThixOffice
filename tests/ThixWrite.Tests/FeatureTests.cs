using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using ThixWrite.Pages;
using W = DocumentFormat.OpenXml.Wordprocessing;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Find/replace, the dialogs, opening Word documents with content ThixWrite keeps, and the PDF export.</summary>
public sealed class FeatureTests
{
    [AvaloniaFact]
    public async Task The_find_bar_finds_counts_replaces_and_closes_with_Escape()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Der Hund und der Hund\nHUNDE bellen");
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.F, CommandKey);
        Assert.True(editor.FindBar.IsVisible);
        Assert.False(editor.FindBar.IsReplaceVisible);
        Assert.True(editor.FindBar.SearchBox.IsFocused);

        window.KeyTextInput("hund");
        await SettleAsync();
        Assert.Equal("Treffer 1 von 3", editor.FindBar.CountText);
        Assert.Equal(new DocumentRange(new DocumentPosition(0, 4), new DocumentPosition(0, 8)), editor.Session.Selection.Range);

        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal("Treffer 2 von 3", editor.FindBar.CountText);
        editor.FindBar.WholeWord.IsChecked = true;
        editor.FindBar.MatchCase.IsChecked = true;
        await SettleAsync();
        Assert.Equal("Keine Treffer", editor.FindBar.CountText);
        editor.FindBar.MatchCase.IsChecked = false;
        await SettleAsync();
        Assert.Contains("von 2", editor.FindBar.CountText);

        // Strg+A in the search field belongs to the field, not to the document (the headless platform's text box knows only
        // Strg+A, so the field's own selection is checked where that is the system's key).
        var before = editor.Session.Selection;
        await PressAsync(window, PhysicalKey.A, CommandKey);
        Assert.Equal(before, editor.Session.Selection);
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Equal(4, editor.FindBar.SearchBox.SelectionEnd - editor.FindBar.SearchBox.SelectionStart);
        }

        if (OperatingSystem.IsMacOS())
        {
            await PressAsync(window, PhysicalKey.F, RawInputModifiers.Meta | RawInputModifiers.Alt); // ⌥⌘F (⌘H hides the app)
        }
        else
        {
            await PressAsync(window, PhysicalKey.H, RawInputModifiers.Control);
        }
        Assert.True(editor.FindBar.IsReplaceVisible);
        editor.FindBar.ReplacementBox.Text = "Katze";
        Assert.Equal(2, editor.FindBar.ReplaceAll());
        await SettleAsync();
        Assert.Equal("Der Katze und der Katze\nHUNDE bellen", TextOf(window));
        Assert.Equal("2 ersetzt", editor.FindBar.CountText);

        await PressAsync(window, PhysicalKey.Escape);
        Assert.False(editor.FindBar.IsVisible);
        Assert.True(editor.Surface.IsFocused);
        await PressAsync(window, PhysicalKey.Z, CommandKey); // Alle ersetzen is one step
        Assert.Equal("Der Hund und der Hund\nHUNDE bellen", TextOf(window));
    }

    [AvaloniaFact]
    public async Task A_Word_table_opens_as_a_table_and_a_nested_one_with_a_warning_and_both_are_kept()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "Angebot.docx");
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Angebot"))),
                new W.Table(new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Position")))))),
                new W.Paragraph(),
                new W.Table(new W.TableRow(new W.TableCell(
                    new W.Table(new W.TableRow(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("innen")))))),
                    new W.Paragraph()))),
                new W.Paragraph()));
            main.Document.Save();
        }

        var window = await NewWindowAsync();
        await TestData.App.OpenFileAsync(path, window);
        await SettleAsync();

        Assert.Equal("Angebot.docx", window.DocumentNameText);
        Assert.True(window.MessageBar.IsVisible);
        Assert.Contains("noch nicht", window.MessageBar.Text);
        Assert.Contains("bleiben beim Speichern", window.MessageBar.Text.Replace("Sie bleiben", "bleiben", StringComparison.Ordinal));
        Assert.Contains("Verschachtelte Tabelle", window.MessageBar.Text);
        window.MessageBar.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(window.MessageBar.IsVisible);

        // The simple table is a table: the caret goes down into its cell and types there.
        await PressAsync(window, PhysicalKey.Home, CommandKey);
        await PressAsync(window, PhysicalKey.ArrowDown);
        await PressAsync(window, PhysicalKey.End);
        await TypeAsync(window, " 1");
        var session = EditorOf(window).Session;
        Assert.NotNull(session.CurrentTable);
        Assert.Equal("Position 1", session.Document.ParagraphAt(session.Selection.Active.Block)!.Text);

        await PressAsync(window, PhysicalKey.End, CommandKey);
        await TypeAsync(window, "Gültig bis Ende Dezember");
        await PressAsync(window, PhysicalKey.S, CommandKey);
        using var saved = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
        var body = saved.MainDocumentPart!.Document!.Body!;
        Assert.Equal(2, body.Elements<W.Table>().Count());
        Assert.Contains("Position 1", body.Elements<W.Table>().First().InnerText);
        Assert.Contains("innen", body.Elements<W.Table>().Last().InnerText);
        Assert.Contains("Gültig bis Ende Dezember", body.InnerText);
    }

    [AvaloniaFact]
    public async Task Seite_einrichten_changes_the_pagination_in_one_undo_step()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        for (var line = 0; line < 40; line++)
        {
            editor.Session.InsertText($"Zeile {line + 1} mit etwas Text, damit die Seite sich füllt.");
            editor.Session.InsertParagraphBreak();
        }

        await SettleAsync();
        var portraitPages = editor.Layout.PageCount;

        var dialog = new PageSetupDialog(editor);
        var shown = dialog.ShowAsync(window);
        await SettleAsync();
        Assert.Equal(2.5m, dialog.MarginLeft.Value);
        dialog.Landscape.IsChecked = true;
        await SettleAsync();
        Assert.True(dialog.PaperWidth.Value > dialog.PaperHeight.Value);
        dialog.MarginTop.Value = 4;
        dialog.Confirm();
        Assert.True(await shown);
        await SettleAsync();

        var setup = editor.Document.Content.FinalSection;
        Assert.Equal(PageOrientation.Landscape, setup.Orientation);
        Assert.Equal(4 * 72 / 2.54, setup.MarginTop, 1);
        Assert.True(editor.Layout.PageCount > portraitPages);
        Assert.Equal($"Seite {editor.Layout.PageCount} von {editor.Layout.PageCount}", window.StatusBar.TextOf(WriteEditor.PageItem));
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.Landscape).IsChecked);

        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal(PageOrientation.Portrait, editor.Document.Content.FinalSection.Orientation);
        Assert.Equal(portraitPages, editor.Layout.PageCount);

        // The presets of the ribbon: Format A5, Seitenränder Schmal.
        await window.Commands[$"{WriteCommandIds.PageSize}.a5"].ExecuteAsync();
        await window.Commands[$"{WriteCommandIds.PageMargins}.narrow"].ExecuteAsync();
        await SettleAsync();
        Assert.Equal(419.55, editor.Document.Content.FinalSection.PageWidth, 1);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>($"{WriteCommandIds.PageMargins}.narrow").IsChecked);
        Assert.False(window.RibbonDefinition.Get<MatRibbonToggle>($"{WriteCommandIds.PageMargins}.normal").IsChecked);
    }

    [AvaloniaFact]
    public async Task The_zoom_dialog_sets_a_percentage_and_opens_from_the_status_bar()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);

        var dialog = new ZoomDialog(editor);
        var shown = dialog.ShowAsync(window);
        await SettleAsync();
        dialog.Presets[0].IsChecked = true; // 200 %
        await SettleAsync();
        Assert.Equal(200m, dialog.Percent.Value);
        dialog.Percent.Value = 135;
        dialog.Confirm();
        await shown;
        await SettleAsync();
        Assert.Equal(135, editor.Zoom);
        Assert.Equal(135, window.StatusBar.View.Zoom);

        editor.RequestZoomLevel();
        await SettleAsync();
        var opened = window.OwnedWindows.OfType<ZoomDialog>().Single();
        opened.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal(135, editor.Zoom);
    }

    [AvaloniaFact]
    public async Task Als_PDF_exportieren_writes_the_pages_into_a_PDF()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Bericht.pdf");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Ein Bericht für das PDF");

        await window.Commands.ExecuteAsync(WriteCommandIds.ExportPdf); // the page Exportieren
        await SettleAsync();
        var page = Assert.IsType<ExportPage>(window.Backstage.CurrentPage);
        Assert.True(await page.ExportPdfAsync());

        var bytes = File.ReadAllBytes(target);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Contains("Bericht.pdf", page.Message.Text);
        Assert.True(window.ShowsUnsavedMarker); // the export does not save the document
    }

    [AvaloniaFact]
    public async Task Speichern_unter_PDF_writes_a_copy_and_the_document_stays_a_docx()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Ein Brief");
        var docx = Path.Combine(data.DocumentsFolder, "Brief.docx");
        Assert.True(await window.SaveToAsync(docx));
        await TypeAsync(window, " mit Nachtrag");

        var pdf = Path.Combine(data.DocumentsFolder, "Brief.pdf");
        Assert.True(await window.SaveToAsync(pdf));
        await SettleAsync();

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
        Assert.Equal(docx, window.Document.FilePath);
        Assert.Equal(".docx", window.Document.FileType!.Extension);
        Assert.True(window.ShowsUnsavedMarker);
        Assert.Contains("Als PDF gespeichert", window.MessageBar.Text);
    }

    [AvaloniaFact]
    public async Task Formatting_marks_and_the_commands_of_Einfuegen_work_from_the_search()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "Text");

        window.Search.Box.Text = "formatierungszeichen";
        await SettleAsync();
        await window.Search.RunSelectedAsync();
        await SettleAsync();
        Assert.True(editor.ShowMarks);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.ShowMarks).IsChecked);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommandIds.ShowMarksView).IsChecked);

        window.Search.Box.Text = "seitenumbruch";
        await SettleAsync();
        Assert.Equal(WriteCommandIds.PageBreak, window.Search.Results[0].Id);
        await window.Search.RunSelectedAsync();
        await SettleAsync();
        Assert.Equal(2, editor.Layout.PageCount);

        window.Search.Box.Text = "zeilenabstand";
        await SettleAsync();
        await window.Search.RunSelectedAsync(); // a value command: its item in the ribbon gets the keyboard
        await SettleAsync();
        await window.Commands[WriteCommands.LineSpacingId(2)].ExecuteAsync();
        await SettleAsync();
        Assert.Equal(new LineSpacing(LineSpacingRule.Multiple, 2), editor.Session.GetFormatting().LineSpacing.Value);
        Assert.True(window.RibbonDefinition.Get<MatRibbonToggle>(WriteCommands.LineSpacingId(2)).IsChecked);
    }
}
