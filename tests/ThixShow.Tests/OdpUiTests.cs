using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using ThixOffice.Charts;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Documents;
using ThixShow.Core.Formats.Odp;
using ThixShow.Core.Model;
using ThixShow.Editor;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>OpenDocument Presentation through the window: Speichern unter .odp, edited, Strg+S, opened again; the questions and warnings.</summary>
public sealed class OdpUiTests
{
    [AvaloniaFact]
    public async Task A_presentation_saved_as_odp_is_edited_saved_and_opens_again_as_odp()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Vortrag.odp");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeTitleAsync(window, "Quartalszahlen");

        Assert.True(await window.SaveAsAsync()); // nothing ODF cannot take: no question
        await SettleAsync();
        using (var file = File.OpenRead(target))
        {
            Assert.True(OdpReader.IsOdp(file));
        }

        var editor = EditorOf(window);
        Assert.Equal(ShowFileFormat.Odp, editor.Document.SaveFormat);

        // Edited and saved with Strg+S: still OpenDocument.
        await TypeTitleAsync(window, " 2027");
        Assert.True(window.Document.IsDirty);
        Assert.True(await window.SaveAsync());
        Assert.False(window.Document.IsDirty);
        using (var file = File.OpenRead(target))
        {
            Assert.True(OdpReader.IsOdp(file));
        }

        window.Close(); // saved: no question
        await SettleAsync();
        var second = await TestData.App.OpenFileAsync(target, null);
        await SettleAsync();
        var reopened = EditorOf(second!);
        Assert.Equal(ShowFileFormat.Odp, reopened.Document.LoadedFormat);
        Assert.Equal(ShowFileFormat.Odp, reopened.Document.SaveFormat);
        Assert.Equal("Quartalszahlen 2027", SlideTitles.TitleOf(reopened.Session.Presentation, reopened.Session.Presentation.Slides[0]));
        Assert.Null(reopened.OpeningWarning);
    }

    [AvaloniaFact]
    public async Task Saving_a_presentation_with_a_chart_as_odp_asks_first()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await window.Commands[ShowCommandIds.NewSlide].ExecuteAsync();
        await SettleAsync();
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [new SeriesSpec(ChartText.Of("Werte"), ChartDataRef.LiteralText(["A", "B"]), ChartDataRef.Literal([1, 2]))]);
        editor.Session.InsertChart(chart);
        await SettleAsync();

        var target = Path.Combine(data.DocumentsFolder, "Mit Diagramm.odp");
        var asking = editor.ConfirmSaveAsync(target);
        await SettleAsync();
        var question = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.Contains("1 × Diagramm", question.Message, StringComparison.Ordinal);
        Assert.Contains("OpenDocument", question.Title, StringComparison.Ordinal);
        question.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        Assert.False(await asking);
        Assert.False(File.Exists(target));

        // Saved anyway: the chart is not in the file, the rest is.
        data.Dialogs.SaveTargets.Enqueue(target);
        var saving = window.SaveAsAsync();
        await SettleAsync();
        var again = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        again.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Als OpenDocument speichern
        Assert.True(await saving);
        var read = OdpReader.Read(File.OpenRead(target));
        Assert.Equal(2, read.SlideCount);
        Assert.DoesNotContain(ShapeTree.All(read.Slides[1].Shapes), shape => shape is GraphicFrame);

        // Asked once per window: a pptx stays a pptx without a question.
        Assert.True(await editor.ConfirmSaveAsync(target));
        Assert.True(await editor.ConfirmSaveAsync(Path.Combine(data.DocumentsFolder, "Mit Diagramm.pptx")));
    }

    [AvaloniaFact]
    public async Task The_sample_saved_as_odp_keeps_its_slides_pictures_tables_notes_and_transitions()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var sample = EditorOf(window).Session.Presentation;
        var target = Path.Combine(data.DocumentsFolder, "Rückblick.odp");
        data.Dialogs.SaveTargets.Enqueue(target);
        var saving = window.SaveAsAsync();
        await SettleAsync();
        Assert.Single(window.OwnedWindows.OfType<QuestionDialog>()).Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // the chart
        Assert.True(await saving);

        var read = OdpReader.Read(File.OpenRead(target));
        Assert.Equal(sample.SlideCount, read.SlideCount);
        for (var index = 0; index < sample.SlideCount; index++)
        {
            Assert.Equal(SlideTitles.TitleOf(sample, sample.Slides[index]), SlideTitles.TitleOf(read, read.Slides[index]));
            Assert.Equal(sample.Slides[index].NotesText, read.Slides[index].NotesText);
            Assert.Equal(sample.Slides[index].Transition?.Kind, read.Slides[index].Transition?.Kind);
            Assert.Equal(
                ShapeTree.All(sample.Slides[index].Shapes).Count(shape => shape is PictureShape || shape is GraphicFrame { Table: not null }),
                ShapeTree.All(read.Slides[index].Shapes).Count(shape => shape is PictureShape || shape is GraphicFrame { Table: not null }));
        }

        Assert.Single(read.Slides[1].Timing!.MainSequence);
    }

    [AvaloniaFact]
    public async Task An_odp_with_content_ThixShow_does_not_show_warns_that_it_is_left_out()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "Fremd.odp");
        File.WriteAllBytes(path, OdpWithChart());
        var window = await TestData.App.OpenFileAsync(path, null);
        await SettleAsync();
        var editor = EditorOf(window!);
        Assert.Equal(ShowFileFormat.Odp, editor.Document.LoadedFormat);
        Assert.Contains("OpenDocument-Präsentation", editor.OpeningWarning, StringComparison.Ordinal);
        Assert.Contains("1 × Diagramm", editor.OpeningWarning, StringComparison.Ordinal);
        Assert.Equal("Aus LibreOffice", SlideTitles.TitleOf(editor.Session.Presentation, editor.Session.Presentation.Slides[0]));
    }

    [AvaloniaFact]
    public async Task A_chart_added_to_a_presentation_opened_from_odp_is_asked_about_before_saving()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Vortrag.odp");
        data.Dialogs.SaveTargets.Enqueue(target);
        var first = await NewWindowAsync();
        await TypeTitleAsync(first, "Plan");
        Assert.True(await first.SaveAsAsync());
        first.Close();
        await SettleAsync();

        var window = await TestData.App.OpenFileAsync(target, null);
        await SettleAsync();
        var editor = EditorOf(window!);
        Assert.Equal(ShowFileFormat.Odp, editor.Document.LoadedFormat);
        Assert.True(await editor.ConfirmSaveAsync(target)); // nothing new: no question

        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [new SeriesSpec(ChartText.Of("Werte"), ChartDataRef.LiteralText(["A", "B"]), ChartDataRef.Literal([1, 2]))]);
        editor.Session.InsertChart(chart);
        await SettleAsync();
        var saving = window!.SaveAsync(); // Strg+S into the same .odp
        await SettleAsync();
        var question = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.Contains("1 × Diagramm", question.Message, StringComparison.Ordinal);
        question.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        Assert.False(await saving);
        Assert.True(window.Document.IsDirty);
    }

    private static async Task TypeTitleAsync(OfficeWindow window, string text)
    {
        var editor = EditorOf(window);
        var session = editor.Session;
        session.GoToSlide(session.Presentation.Slides[0].Id);
        var title = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        editor.Canvas.EditTextOf(title.Id);
        editor.Canvas.Focus();
        await TypeAsync(window, text);
        session.EndTextEditing();
        await SettleAsync();
    }

    private static byte[] OdpWithChart()
    {
        const string content = """
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0" xmlns:xlink="http://www.w3.org/1999/xlink" office:version="1.3">
              <office:body><office:presentation>
                <draw:page draw:name="page1">
                  <draw:frame svg:x="2cm" svg:y="1cm" svg:width="20cm" svg:height="2cm" presentation:class="title"><draw:text-box><text:p>Aus LibreOffice</text:p></draw:text-box></draw:frame>
                  <draw:frame svg:x="2cm" svg:y="4cm" svg:width="10cm" svg:height="6cm"><draw:object xlink:href="./Object 1" draw:notify-on-update-of-ranges="A1:B2"/></draw:frame>
                </draw:page>
              </office:presentation></office:body>
            </office:document-content>
            """;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var stream = zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())
            {
                stream.Write(Encoding.ASCII.GetBytes(OdpWriter.MimeType));
            }

            using (var stream = zip.CreateEntry("content.xml").Open())
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }
}
