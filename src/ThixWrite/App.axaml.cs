using Avalonia.Markup.Xaml;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core;
using ThixWrite.Core.Documents;
using ThixWrite.Editor;

namespace ThixWrite;

/// <summary>
/// ThixWrite on ThixOffice.Desktop: the definition (ThixWriteApp), the document (phase 0: TextDocument), the editor
/// (phase 0: <see cref="PlaceholderEditor"/>), ThixWrite's commands and its ribbon. Start-up, single instance, setup,
/// updates, the window, the backstage and the settings are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public override OfficeAppDefinition Definition => ThixWriteApp.Definition;

    public override RibbonLayout Ribbon => WriteRibbon.Layout;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override OfficeDocument CreateDocument(string untitledName) => new TextDocument(untitledName);

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document) => new PlaceholderEditor((TextDocument)document);

    public override void AddCommands(OfficeWindow window) => WriteRibbon.AddCommands(window);

    protected override void FillSampleDocument(OfficeDocument document) =>
        ((TextDocument)document).Load(PlaceholderEditor.SampleText);
}
