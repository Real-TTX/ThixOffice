using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Start in the real window: Word's keys, Sortieren, Rahmen und Schattierung.</summary>
public sealed class HomeUiTests
{
    [AvaloniaFact]
    public async Task Words_keys_set_small_caps_and_a_hanging_indent_and_Sortieren_orders_the_paragraphs()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Zeder");
        await PressAsync(window, PhysicalKey.Enter);
        await TypeAsync(window, "Ahorn");
        var editor = EditorOf(window);
        editor.Session.SelectAll();

        await PressAsync(window, OperatingSystem.IsMacOS() ? PhysicalKey.K : PhysicalKey.Q, CommandKey | RawInputModifiers.Shift);
        Assert.True(((TextRun)editor.Session.Document.ParagraphAt(0)!.Inlines[0]).Properties.SmallCaps);
        await PressAsync(window, PhysicalKey.T, CommandKey);
        Assert.True(editor.Session.Document.ParagraphAt(1)!.Properties.FirstLineIndent < 0);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.Sort);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<SortDialog>().Single();
        dialog.Confirm();
        await SettleAsync();
        Assert.Equal("Ahorn", editor.Session.Document.ParagraphAt(0)!.Text);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.PageBorders);
        await SettleAsync();
        var borders = window.OwnedWindows.OfType<BordersDialog>().Single();
        Assert.Equal(BordersDialog.PageTab, borders.Tabs.SelectedIndex);
        borders.PageSetting.SelectedIndex = 1;
        borders.Confirm();
        await SettleAsync();
        Assert.NotNull(editor.Session.Document.FinalSection.PageBorders);
    }
}
