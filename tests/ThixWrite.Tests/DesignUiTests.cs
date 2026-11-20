using Avalonia.Headless.XUnit;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Entwurf in the real window: Designs, Farben, Schriftarten, Wasserzeichen.</summary>
public sealed class DesignUiTests
{
    [AvaloniaFact]
    public async Task A_design_from_the_ribbon_changes_the_theme_and_a_watermark_goes_into_the_header()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Bericht");
        var editor = EditorOf(window);

        var facet = DocumentDesign.All.ToList().FindIndex(design => design.Name == "Facette");
        await window.Commands.ExecuteAsync(DesignCommands.DesignId(facet));
        await SettleAsync();
        Assert.Equal("Trebuchet MS", editor.Session.Document.Theme.MajorFont);
        Assert.True(window.Commands[DesignCommands.DesignId(facet)]!.IsChecked!());

        await window.Commands.ExecuteAsync(DesignCommands.WatermarkId(1));
        await SettleAsync();
        Assert.Equal(Watermark.Texts[1], editor.Session.CurrentWatermark?.Text);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.WatermarkCustom);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<WatermarkDialog>().Single();
        Assert.True(dialog.TextChoice.IsChecked);
        dialog.WatermarkText.Text = "INTERN";
        dialog.SemiTransparent.IsChecked = false;
        dialog.Confirm();
        await SettleAsync();
        Assert.Equal("INTERN", editor.Session.CurrentWatermark?.Text);
        Assert.False(editor.Session.CurrentWatermark!.SemiTransparent);

        await window.Commands.ExecuteAsync(WriteCommandIds.WatermarkRemove);
        await SettleAsync();
        Assert.Null(editor.Session.CurrentWatermark);
    }
}
