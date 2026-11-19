using Avalonia.Headless.XUnit;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Formats.Odt;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>OpenDocument Text through the window: Speichern unter .odt, opened again, saved as .odt by Strg+S.</summary>
public sealed class OdtUiTests
{
    [AvaloniaFact]
    public async Task A_document_saved_as_odt_opens_again_and_keeps_its_format()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Brief.odt");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        await TypeAsync(window, "Sehr geehrte Damen und Herren,\nvielen Dank.");
        Assert.True(await window.SaveAsAsync());
        await SettleAsync();
        Assert.True(OdtReader.IsOdt(File.OpenRead(target)));
        var editor = EditorOf(window);
        Assert.Equal(WordFileFormat.Odt, editor.Document.SaveFormat);

        window.Close(); // saved: no question
        await SettleAsync();
        var second = await TestData.App.OpenFileAsync(target, null);
        await SettleAsync();
        var reopened = EditorOf(second!);
        Assert.Equal(WordFileFormat.Odt, reopened.Document.LoadedFormat);
        Assert.Equal("Sehr geehrte Damen und Herren,\nvielen Dank.", TextOf(second!));
        Assert.Null(reopened.OpeningWarning);
    }
}
