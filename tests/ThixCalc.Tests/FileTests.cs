using System.Text;
using Avalonia.Headless.XUnit;
using ThixCalc.Core.Documents;
using ThixCalc.Dialogs;
using ThixOffice.Desktop.Dialogs;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Open and save flows: CSV (Textimport when ambiguous, the warning on saving), a workbook with macros.</summary>
public sealed class FileTests
{
    [AvaloniaFact]
    public async Task An_ambiguous_csv_asks_and_reads_again_with_the_choice()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "Liste.csv");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("Name,Wert;Ort\nMüller,1;Köln\nSchmidt,2;Bonn\n"));
        await NewWindowAsync();
        var window = await TestData.App.OpenFileAsync(path);
        Assert.NotNull(window);
        await SettleAsync();
        var document = (CalcDocument)window!.Document;
        Assert.True(document.CsvDetection?.IsAmbiguous);
        var dialog = Assert.Single(window.OwnedWindows.OfType<CsvImportDialog>());
        dialog.Delimiter.SelectedIndex = 1; // Komma
        dialog.Confirm();
        await SettleAsync();
        Assert.Equal("Wert;Ort", document.Workbook.Sheets[0].GetValue(0, 1).Text);
        Assert.Equal("Müller", document.Workbook.Sheets[0].GetValue(1, 0).Text);
        Assert.False(document.IsDirty);
    }

    [AvaloniaFact]
    public async Task Saving_formulas_as_csv_asks_first()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "=1+1");
        var save = window.SaveToAsync(Path.Combine(data.DocumentsFolder, "Werte.csv"));
        await SettleAsync();
        var question = Assert.Single(window.OwnedWindows.OfType<QuestionDialog>());
        Assert.Contains("nur das aktive Blatt", question.Message, StringComparison.Ordinal);
        question.Buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.True(await save);
        Assert.Equal("2", File.ReadAllText(Path.Combine(data.DocumentsFolder, "Werte.csv")).Trim('﻿', '\r', '\n'));
    }
}
