using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixShow.Core;
using ThixShow.Core.Documents;
using ThixShow.Editor;
using ThixShow.Pages;
using ThixShow.Show;

namespace ThixShow;

/// <summary>
/// ThixShow on ThixOffice.Desktop: the definition (ThixShowApp), the document (<see cref="ShowDocument"/>: pptx, pptm), the editor
/// (<see cref="ShowEditor"/>), ThixShow's commands and ribbon, its backstage pages, self test and snapshot scenes. Start-up, single
/// instance, setup, updates, the window, the backstage and the settings are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public const string DesignScene = "main-design";
    public const string ShapesScene = "main-shapes";
    public const string TransitionsScene = "main-transitions";
    public const string AnimationsScene = "main-animations";
    public const string SorterScene = "main-sorter";
    public const string PresenterScene = "presenter";
    public const string PresenterSlidesScene = "presenter-slides";
    public const string ShowControlsScene = "show-controls";
    public const string ScreensScene = "main-screens";
    public const string PrintScene = "backstage-print";
    public const string ContentScene = "main-content";
    public const string ExportScene = "backstage-export";

    public override OfficeAppDefinition Definition => ThixShowApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [ContentScene, DesignScene, ShapesScene, TransitionsScene, AnimationsScene, SorterScene, PresenterScene, PresenterSlidesScene, ShowControlsScene, ScreensScene, PrintScene, ExportScene];

    public override string SampleDocumentName => "Jahresrückblick 2026";

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => ShowRibbon.Create(window);

    public override Control? CreateRibbonContent(OfficeWindow window, MatRibbonCustom item) => ShowGalleries.Create(window, item.Id);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override OfficeDocument CreateDocument(string untitledName) => new ShowDocument(untitledName);

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document) => new ShowEditor(window, (ShowDocument)document);

    public override void AddCommands(OfficeWindow window)
    {
        ShowCommands.Add(window);
        ShowCommands.AddAliases(window);
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [ShowPrintPage.Item(window), ShowExportPage.Item(window)];

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [("ThixShow slides, shapes, pptx, show", () => ShowSelfTest.RunAsync(this))];

    /// <summary>The sample presentation, on its second slide (the list with its levels).</summary>
    protected override void FillSampleDocument(OfficeDocument document)
    {
        var show = (ShowDocument)document;
        show.Load(SampleDeck.Create());
    }

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (ShowEditor)window.Editor;
        var session = editor.Session;
        switch (name)
        {
            case ContentScene:
                // The chart slide, the chart selected (its frame and handles).
                session.GoToSlide(session.Presentation.Slides[2].Id);
                var chart = session.CurrentSlide!.Shapes.OfType<Core.Model.GraphicFrame>().FirstOrDefault();
                if (chart is not null)
                {
                    session.SelectShapes([chart.Id]);
                }

                break;
            case DesignScene:
                window.Ribbon.SelectTab("design");
                break;
            case ShapesScene:
                session.GoToSlide(session.Presentation.Slides[3].Id);
                Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendantsOf<ThixOffice.Desktop.Controls.RibbonFlyoutButton>().FirstOrDefault(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Formen")?.Open();
                break;
            case TransitionsScene:
                session.GoToSlide(session.Presentation.Slides[1].Id);
                window.Ribbon.SelectTab("transitions");
                break;
            case AnimationsScene:
                session.GoToSlide(session.Presentation.Slides[1].Id);
                var body = session.CurrentSlide!.Shapes.OfType<Core.Model.AutoShape>().FirstOrDefault(shape => shape.Placeholder is { IsTitle: false });
                if (body is not null)
                {
                    session.SelectShapes([body.Id]);
                }

                window.Ribbon.SelectTab("animations");
                editor.ToggleAnimationPane();
                break;
            case SorterScene:
                editor.SetView(ShowView.Sorter);
                break;
            case PresenterScene or PresenterSlidesScene:
                var controller = new ShowController(session.Presentation, new Core.Show.SlideShowOptions(StartSlide: 1));
                controller.Start();
                controller.GoTo(1);
                var presenter = new PresenterWindow(controller, () => new DateTime(2026, 11, 19, 14, 32, 0));
                presenter.SwapDisplays = () => { };
                presenter.Show();
                if (name == PresenterSlidesScene)
                {
                    presenter.ShowNavigator();
                }

                Dispatcher.UIThread.RunJobs();
                return presenter;
            case ShowControlsScene:
                // The show window with its control bar (the mouse just moved), on the slide with the picture.
                var running = new ShowController(session.Presentation, new Core.Show.SlideShowOptions(StartSlide: 0, UseTimings: false));
                running.Start();
                running.GoTo(Math.Min(3, session.Presentation.SlideCount - 1));
                var showWindow = SlideShowWindow.ForSnapshot(running);
                showWindow.Show();
                Dispatcher.UIThread.RunJobs();
                return showWindow;
            case ScreensScene:
                // Bildschirmpräsentation › Bildschirme with a notebook and a projector: the list of Bildschirm opened.
                ShowScreens.ForTests = () =>
                [
                    new ShowScreen(0, new Avalonia.PixelRect(0, 0, 2560, 1440), IsPrimary: true),
                    new ShowScreen(1, new Avalonia.PixelRect(2560, 0, 1920, 1080), IsPrimary: false),
                ];
                window.Ribbon.SelectTab("slideShow");
                Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendantsOf<ThixOffice.Desktop.Controls.RibbonFlyoutButton>().FirstOrDefault(button => button.Label.StartsWith("Bildschirm", StringComparison.Ordinal))?.Open();
                break;
            case PrintScene:
                window.ShowBackstage(ShowPrintPage.PageId);
                break;
            case ExportScene:
                window.ShowBackstage(ShowExportPage.PageId);
                break;
        }

        Dispatcher.UIThread.RunJobs();
        return window;
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        var editor = (ShowEditor)window.Editor;
        switch (name)
        {
            case PresenterScene or PresenterSlidesScene or ShowControlsScene when shown is Window presenter:
                presenter.Close();
                break;
            case ScreensScene:
                ShowScreens.ForTests = null;
                break;
            case PrintScene or ExportScene:
                window.Backstage.Close();
                break;
            case SorterScene:
                editor.SetView(ShowView.Normal);
                break;
            case AnimationsScene:
                editor.ToggleAnimationPane();
                break;
        }

        window.Ribbon.SelectTab("home");
        editor.Session.SelectShapes([]);
        editor.Session.GoToSlide(editor.Session.Presentation.Slides[0].Id);
    }
}

/// <summary>Visual tree helpers of the snapshot scenes.</summary>
internal static class SceneExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual visual) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(visual).OfType<T>();
}
