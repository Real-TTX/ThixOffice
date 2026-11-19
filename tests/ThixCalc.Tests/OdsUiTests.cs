using Avalonia.Headless.XUnit;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Formats;
using ThixCalc.Core.Formats.Ods;
using ThixCalc.Core.Model;
using ThixOffice.Desktop.Dialogs;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>OpenDocument Spreadsheet through the window: Speichern unter .ods, Strg+S keeps the format, opened again; the question before losing content.</summary>
public sealed class OdsUiTests
{
    [AvaloniaFact]
    public async Task A_workbook_saved_as_ods_opens_again_and_keeps_its_format()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Budget.ods");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await EnterAsync(window, "12,5");
        await EnterAsync(window, "7");
        await EnterAsync(window, "=SUMME(A1:A2)");
        Assert.True(await window.SaveAsAsync());
        await SettleAsync();
        Assert.Empty(window.OwnedWindows.OfType<QuestionDialog>());
        using (var stream = File.OpenRead(target))
        {
            Assert.True(OdsReader.IsOds(stream));
        }

        var document = (CalcDocument)window.Document;
        Assert.Equal(WorkbookFileFormat.Ods, document.SaveFormat);

        // An edit, then Strg+S writes .ods again.
        await EnterAsync(window, "Ende");
        Assert.True(document.IsDirty);
        await PressAsync(window, Avalonia.Input.PhysicalKey.S, CommandKey);
        await SettleAsync();
        Assert.False(document.IsDirty);

        window.Close();
        await SettleAsync();
        var second = await TestData.App.OpenFileAsync(target, null);
        await SettleAsync();
        var reopened = (CalcDocument)second!.Document;
        Assert.Equal(WorkbookFileFormat.Ods, reopened.LoadedFormat);
        var sheet = reopened.Workbook.Sheets[0];
        Assert.Equal(12.5, sheet["A1"].Number);
        Assert.Equal(19.5, sheet["A3"].Number);
        Assert.Equal("SUM(A1:A2)", ThixCalc.Core.Formulas.FormulaFormatter.Format(sheet.GetFormula(2, 0)!, reopened.Workbook, new CellAddress(2, 0)));
        Assert.Equal("Ende", sheet["A4"].Text);
        Assert.Null(EditorOf(second).OpeningWarning);
    }

    [AvaloniaFact]
    public async Task Saving_what_ods_cannot_hold_asks_first()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "5");
        var sheet = EditorOf(window).Session.ActiveSheet;
        sheet.ConditionalFormats.Add(new ConditionalFormat([CellRange.Parse("A1:A5")], [
            new ConditionalFormatRule { Type = ConditionalFormatType.DataBar, Thresholds = [new(ConditionalValueType.Min), new(ConditionalValueType.Max)], Colors = [CalcColor.FromRgb(99, 142, 198)] },
        ]));
        var path = Path.Combine(data.DocumentsFolder, "Balken.ods");

        var cancelled = window.SaveToAsync(path);
        await SettleAsync();
        var question = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.Contains("Datenbalken", question.Message, StringComparison.Ordinal);
        question.Buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.False(await cancelled);
        Assert.False(File.Exists(path));

        var saved = window.SaveToAsync(path);
        await SettleAsync();
        question = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        question.Buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(await saved);
        Assert.True(File.Exists(path));

        // Asked once: the next save of the same content does not ask again.
        var again = window.SaveToAsync(path);
        await SettleAsync();
        Assert.Empty(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.True(await again);
    }

    [AvaloniaFact]
    public async Task An_ods_with_content_ThixCalc_does_not_take_over_says_so_when_it_opens()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "Mit Makro.ods");
        var workbook = ThixCalc.Core.Model.Workbook.Create();
        workbook.Sheets[0].SetValue(0, 0, 1);
        using (var stream = new MemoryStream())
        {
            OdsWriter.Write(workbook, stream);
            File.WriteAllBytes(path, WithMacro(stream.ToArray()));
        }

        await NewWindowAsync();
        var window = await TestData.App.OpenFileAsync(path, null);
        await SettleAsync();
        var warning = EditorOf(window!).OpeningWarning;
        Assert.NotNull(warning);
        Assert.Contains("OpenDocument", warning, StringComparison.Ordinal);
        Assert.Contains("Makros", warning, StringComparison.Ordinal);
    }

    private static byte[] WithMacro(byte[] package)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("Basic/Standard/Module1.xml").Open();
            entry.Write("<module/>"u8);
        }

        return buffer.ToArray();
    }
}
