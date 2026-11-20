using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Überprüfen › Änderungen nachverfolgen in the real window.</summary>
public sealed class RevisionUiTests
{
    [AvaloniaFact]
    public async Task Strg_Umschalt_E_tracks_typing_the_markup_menu_switches_the_view_and_Annehmen_takes_the_change()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Alt");
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.E, CommandKey | RawInputModifiers.Shift);
        Assert.True(editor.Session.TrackChanges);
        Assert.True(window.Commands[WriteCommandIds.TrackChanges].IsChecked?.Invoke());
        await TypeAsync(window, " neu");
        Assert.Contains(editor.Session.Document.ParagraphAt(0)!.Inlines, inline => inline is TextRun { Text: " neu", Revision.Kind: RevisionKind.Insertion });

        await window.Commands.ExecuteAsync($"{WriteCommandIds.Markup}.all");
        Assert.Equal(RevisionView.AllMarkup, editor.Layout.RevisionView);

        await PressAsync(window, PhysicalKey.Home);
        await window.Commands.ExecuteAsync(WriteCommandIds.NextRevision);
        await window.Commands.ExecuteAsync(WriteCommandIds.AcceptRevision);
        await SettleAsync();
        Assert.False(editor.Session.HasRevisions);
        Assert.Equal("Alt neu", editor.Session.Document.ParagraphAt(0)!.Text);
    }
}
