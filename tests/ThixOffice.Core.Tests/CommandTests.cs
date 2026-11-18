using ThixOffice.Core.Commands;

namespace ThixOffice.Core.Tests;

/// <summary>The UI-free command registry the search box, the ribbon and the keyboard share.</summary>
public sealed class CommandTests
{
    private static OfficeCommandRegistry Registry(Func<bool>? canUndo = null)
    {
        var registry = new OfficeCommandRegistry();
        registry.Add(new OfficeCommand("file.save", "Speichern", () => { }) { Keywords = ["sichern"], Shortcut = "Ctrl+S" });
        registry.Add(new OfficeCommand("file.saveAs", "Speichern unter", () => { }) { Description = "Unter neuem Namen ablegen." });
        registry.Add(new OfficeCommand("format.size", "Schriftgröße", () => { }));
        registry.Add(new OfficeCommand("edit.undo", "Rückgängig", () => { }) { CanExecute = canUndo });
        registry.Add(new OfficeCommand("file.backstage", "Datei", () => { }) { IsSearchable = false });
        return registry;
    }

    [Fact]
    public void The_search_finds_by_label_keyword_and_description_best_first()
    {
        var registry = Registry();

        Assert.Equal(["file.save", "file.saveAs"], registry.Search("speich").Select(command => command.Id));
        Assert.Equal(["file.saveAs"], registry.Search("unter").Select(command => command.Id));
        Assert.Equal(["file.save"], registry.Search("sichern").Select(command => command.Id));
        Assert.Equal(["file.saveAs"], registry.Search("namen").Select(command => command.Id));
        Assert.Equal(["file.saveAs"], registry.Search("speichern unter").Select(command => command.Id));
        Assert.Empty(registry.Search("datei")); // not searchable
        Assert.Empty(registry.Search("   "));
    }

    [Fact]
    public void Umlauts_and_case_do_not_matter()
    {
        var registry = Registry();
        Assert.Equal("format.size", registry.Search("SCHRIFTGROESSE").Single().Id);
        Assert.Equal("format.size", registry.Search("größe").Single().Id);
        Assert.Equal("edit.undo", registry.Search("ruckgangig").Count == 0 ? registry.Search("rueckgaengig").Single().Id : "edit.undo");
    }

    [Fact]
    public async Task A_disabled_command_does_not_run()
    {
        var canUndo = false;
        var ran = 0;
        var registry = new OfficeCommandRegistry();
        registry.Add(new OfficeCommand("edit.undo", "Rückgängig", () => ran++) { CanExecute = () => canUndo });

        Assert.False(await registry.ExecuteAsync("edit.undo"));
        canUndo = true;
        Assert.True(await registry.ExecuteAsync("edit.undo"));
        Assert.Equal(1, ran);
        Assert.False(await registry.ExecuteAsync("gibt.es.nicht"));
    }

    [Fact]
    public void An_id_exists_once_and_views_hear_about_changes()
    {
        var registry = Registry();
        var changes = 0;
        registry.StateChanged += (_, _) => changes++;

        Assert.Throws<InvalidOperationException>(() => registry.Add(new OfficeCommand("file.save", "Noch einmal", () => { })));
        registry.Invalidate();
        Assert.Equal(1, changes);
        Assert.Throws<KeyNotFoundException>(() => registry["gibt.es.nicht"]);
    }

    [Fact]
    public void A_checkable_command_says_whether_it_is_on()
    {
        var on = false;
        var command = new OfficeCommand("view.ruler", "Lineal", () => on = !on) { IsChecked = () => on };
        Assert.True(command.IsCheckable);
        Assert.False(command.Checked);
        _ = command.ExecuteAsync();
        Assert.True(command.Checked);
        Assert.False(new OfficeCommand("a", "A", () => { }).IsCheckable);
    }

    [Fact]
    public void The_ribbon_layout_names_its_commands_once()
    {
        var layout = new RibbonLayout(
        [
            new RibbonTab("home", "Start", [new RibbonGroup("doc", "Dokument", [new RibbonItem("file.save", RibbonItemSize.Large), new RibbonItem("edit.undo")])]),
            new RibbonTab("view", "Ansicht", [new RibbonGroup("x", "X", [new RibbonItem("file.save")])]),
        ]);

        Assert.Equal(["file.save", "edit.undo"], layout.CommandIds);
    }
}
