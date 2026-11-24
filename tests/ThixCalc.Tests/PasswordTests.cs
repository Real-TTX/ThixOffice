using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Documents;
using ThixOffice.Core.Business;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>An .xlsx protected with "Mit Kennwort verschlüsseln" in the real ThixCalc: opened with its password, saved encrypted again.</summary>
public sealed class PasswordTests
{
    private const string Password = "Geheim-2026";

    [AvaloniaFact]
    public async Task A_protected_xlsx_opens_with_its_password_and_stays_protected()
    {
        using var data = new TestData();
        var plain = Path.Combine(data.DocumentsFolder, "Muster.xlsx");
        DocumentService.SaveAs(TestData.App.CreateSampleDocument(plain), plain);
        var path = Path.Combine(data.DocumentsFolder, "Geschützt.xlsx");
        using (var output = File.Create(path))
        {
            OfficeEncryption.Encrypt(File.ReadAllBytes(plain), Password, output);
        }

        var expected = DocumentService.Open(plain, name => new CalcDocument(name)).Workbook;
        var window = await NewWindowAsync();
        var opening = TestData.App.OpenFileAsync(path, window);
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<DocumentPasswordDialog>());
        dialog.PasswordBox.Text = Password;
        dialog.Confirm();
        for (var round = 0; round < 200 && !opening.IsCompleted; round++)
        {
            await SettleAsync();
        }

        var opened = Assert.IsType<OfficeWindow>(await opening);
        var workbook = ((CalcDocument)opened.Document).Workbook;
        Assert.Equal(expected.Sheets.Select(sheet => sheet.Name), workbook.Sheets.Select(sheet => sheet.Name));
        Assert.Equal(expected.Sheets[0].GetValue(0, 0).Text, workbook.Sheets[0].GetValue(0, 0).Text);
        Assert.True(opened.Document.HasPassword);

        var cell = EditorOf(opened).Session.ActiveCell;
        var sheetName = EditorOf(opened).Session.ActiveSheet.Name;
        await EnterAsync(opened, "Vertraulich");
        await PressAsync(opened, PhysicalKey.S, CommandKey);
        Assert.False(opened.Document.IsDirty);
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(file));
        }

        var again = DocumentService.Open(path, name => new CalcDocument(name), Password);
        Assert.Equal("Vertraulich", again.Workbook.Sheets.Single(sheet => sheet.Name == sheetName).GetValue(cell).Text);
    }
}
