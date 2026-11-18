using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// One document in one window (like Word: every document its own window, all in one process). From top to bottom:
///  1. the title bar (MatTitleBar, DESK-03): logo and word mark "Thix" + the app's suffix in its accent, the quick-access
///     buttons (Speichern, Rückgängig, Wiederholen), the document's name with a dot while it has unsaved changes, in the
///     middle the command search "Was möchten Sie tun?", the caption buttons;
///  2. the ribbon slot (<see cref="IOfficeRibbon"/>; today <see cref="TemporaryCommandBar"/>, next MatRibbon);
///  3. a message bar (a recovered document, a problem);
///  4. the editor of the app (<see cref="OfficeEditor"/>);
///  5. the status bar slot (<see cref="IOfficeStatusBar"/>).
/// "Datei" opens the backstage (<see cref="IOfficeBackstage"/>) over 2–5. Every action is a command of <see cref="Commands"/>,
/// so the search, the ribbon, the quick-access buttons and the keyboard do the same. Closing asks about unsaved changes.
/// </summary>
public sealed class OfficeWindow : MatWindow
{
    public const string NewPageId = "new";
    public const string OpenPageId = "open";
    public const string SaveItemId = "save";
    public const string SaveAsPageId = "saveAs";
    public const string CloseItemId = "close";
    public const string SettingsPageId = "settings";
    public const string AboutPageId = "about";

    private readonly OfficeApplication _app;
    private readonly ContentControl _editorHost = new();
    private readonly InfoBar _messageBar = new() { Margin = new Thickness(16, 10, 16, 0) };
    private readonly TextBlock _documentName = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
    private readonly Ellipse _unsavedMarker = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
    private readonly DispatcherTimer _recoveryTimer = new();
    private readonly List<(KeyGesture Gesture, OfficeCommand Command)> _shortcuts = [];
    private OfficeEditor _editor;
    private bool _closeConfirmed;
    private bool _isAsking;
    private bool _keepRecovery;

    public OfficeWindow(OfficeApplication app, OfficeDocument document)
    {
        _app = app;
        Document = document;
        var definition = AppInfo.Definition;
        var settings = SettingsService.Get();
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseOwnTitleBar();

        Commands = new OfficeCommandRegistry();
        Search = new CommandSearchBox(Commands);
        Search.Finished += (_, _) => FocusDocument();
        TitleBar = new MatTitleBar
        {
            AppName = definition.Name,
            Logo = app.Logo,
            WordMark = Ui.WordMark(definition.Suffix),
            BrandWidth = 196,
            Center = BuildTitleCenter(),
        };

        Ribbon = OfficeShellParts.CreateRibbon();
        Ribbon.IsCollapsed = settings.IsRibbonCollapsed;
        Ribbon.FileTabInvoked += (_, _) => Backstage!.Open();
        Ribbon.CollapsedChanged += (_, collapsed) => SettingsService.Remember(current => current with { IsRibbonCollapsed = collapsed });
        StatusBar = OfficeShellParts.CreateStatusBar();
        Backstage = OfficeShellParts.CreateBackstage();
        Backstage.OpenChanged += (_, open) => OnBackstageOpenChanged(open);

        _unsavedMarker.Use(Shape.FillProperty, "Mat.Accent");
        ToolTip.SetTip(_unsavedMarker, "Ungespeicherte Änderungen");

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        var editorArea = new Border { Child = _editorHost, ClipToBounds = true }.Use(Border.BackgroundProperty, "Mat.Bg");
        Grid.SetRow(Ribbon.View, 1);
        Grid.SetRow(_messageBar, 2);
        Grid.SetRow(editorArea, 3);
        Grid.SetRow(StatusBar.View, 4);
        Grid.SetRow(Backstage.View, 1);
        Grid.SetRowSpan(Backstage.View, 4);
        layout.Children.Add(TitleBar);
        layout.Children.Add(Ribbon.View);
        layout.Children.Add(_messageBar);
        layout.Children.Add(editorArea);
        layout.Children.Add(StatusBar.View);
        layout.Children.Add(Backstage.View);
        Content = layout;

        RegisterCommonCommands();
        app.AddCommands(this);
        Ribbon.Bind(app.Ribbon, Commands);
        Backstage.Configure(BuildBackstage());
        CollectShortcuts();

        _editor = app.CreateEditor(this, document);
        AttachDocument(document);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Opened += (_, _) => FocusDocument();
        _recoveryTimer.Tick += (_, _) => WriteRecovery();
        ConfigureRecoveryTimer(settings);
        SettingsService.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            SettingsService.Changed -= OnSettingsChanged;
            _recoveryTimer.Stop();
            _editor.Detach();
            Document.Changed -= OnDocumentChanged;
        };
    }

    /// <summary>The document of this window (it changes when a file is opened into an untouched new document).</summary>
    public OfficeDocument Document { get; private set; }

    /// <summary>The app's editor of <see cref="Document"/>.</summary>
    public OfficeEditor Editor => _editor;

    /// <summary>Every command of this window: the shell's (<see cref="CommonCommandIds"/>) and the app's.</summary>
    public OfficeCommandRegistry Commands { get; }

    public MatTitleBar TitleBar { get; }

    public CommandSearchBox Search { get; }

    public IOfficeRibbon Ribbon { get; }

    public IOfficeBackstage Backstage { get; }

    public IOfficeStatusBar StatusBar { get; }

    /// <summary>The buttons of the title bar: Speichern, Rückgängig, Wiederholen.</summary>
    public IReadOnlyList<Button> QuickAccessButtons { get; private set; } = [];

    /// <summary>The document's name in the title bar.</summary>
    public string DocumentNameText => _documentName.Text ?? "";

    /// <summary>True while the dot of unsaved changes shows.</summary>
    public bool ShowsUnsavedMarker => _unsavedMarker.IsVisible;

    /// <summary>The message under the ribbon (a recovered document, a problem), for tests.</summary>
    public InfoBar MessageBar => _messageBar;

    /// <summary>
    /// Runs a command from any control of a document window (the ribbon, the search, a page) and shows a refusal of the
    /// business layer in a message; false when it could not run.
    /// </summary>
    public static async Task<bool> RunCommandAsync(Visual source, OfficeCommand command)
    {
        try
        {
            return await command.ExecuteAsync();
        }
        catch (BusinessException exception)
        {
            if (TopLevel.GetTopLevel(source) is Window window)
            {
                await QuestionDialog.ShowMessageAsync(window, command.Label, exception.Message);
            }

            return false;
        }
    }

    // ===== The flows =====

    /// <summary>"Speichern": into the document's file, or "Speichern unter" when it has none. True when it is saved.</summary>
    public async Task<bool> SaveAsync()
    {
        if (Document.FilePath is null)
        {
            return await SaveAsAsync();
        }

        return await GuardAsync("Speichern", () => DocumentService.Save(Document));
    }

    /// <summary>"Speichern unter": the system's dialog, then the save. True when it is saved.</summary>
    public async Task<bool> SaveAsAsync()
    {
        var target = await OfficeApplication.FileDialogs.PickSaveTargetAsync(this, DocumentService.SuggestedFileName(Document), DocumentService.SuggestedFolder(Document));
        return target is not null && await SaveToAsync(target);
    }

    /// <summary>Saves the document as <paramref name="path"/> (the Speichern-unter page, tests). True when it is saved.</summary>
    public Task<bool> SaveToAsync(string path) => GuardAsync("Speichern unter", () =>
    {
        DocumentService.SaveAs(Document, path);
    });

    /// <summary>"Öffnen" → "Durchsuchen": the system's dialog, then every chosen file (each in its window).</summary>
    public async Task OpenWithDialogAsync()
    {
        var files = await OfficeApplication.FileDialogs.PickFilesToOpenAsync(this, DocumentService.SuggestedFolder(Document));
        if (files.Count > 0)
        {
            Backstage.Close();
            await _app.OpenFilesAsync(files, this);
        }
    }

    /// <summary>Puts another document into this window (a file opened while this window held an untouched new document).</summary>
    public void ReplaceDocument(OfficeDocument document)
    {
        _editor.Detach();
        Document.Changed -= OnDocumentChanged;
        DocumentService.DiscardRecovery(Document);
        Document = document;
        _editor = _app.CreateEditor(this, document);
        AttachDocument(document);
        FocusDocument();
    }

    /// <summary>Closes the window; unsaved changes are asked about first (the Closing handler).</summary>
    public Task CloseDocumentAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ends the window without asking (the setup asks the app to end): unsaved changes are kept in the recovery file, the
    /// next start brings them back.
    /// </summary>
    public void CloseKeepingRecovery()
    {
        if (Document.IsDirty)
        {
            WriteRecovery();
        }

        _closeConfirmed = true;
        _keepRecovery = true;
        Close();
    }


    /// <summary>Gives the keyboard to the document.</summary>
    public void FocusDocument() => Dispatcher.UIThread.Post(() => _editor.FocusDocument(), DispatcherPriority.Input);

    /// <summary>Opens the backstage on a page (Einstellungen › Updates from a notification …).</summary>
    public void ShowBackstage(string pageId) => Backstage.Open(pageId);

    /// <summary>Opens Einstellungen on a tab (SettingsPage.GeneralTab …).</summary>
    public void ShowSettings(string tab)
    {
        Backstage.Open(SettingsPageId);
        (Backstage.CurrentPage as SettingsPage)?.ShowTab(tab);
    }

    /// <summary>Shows a message under the ribbon (null hides it).</summary>
    public void ShowMessage(string? text, InfoSeverity severity = InfoSeverity.Info)
    {
        if (text is null)
        {
            _messageBar.Hide();
            return;
        }

        _messageBar.Show(text, severity);
    }

    // ===== Building =====

    private Grid BuildTitleCenter()
    {
        var save = QuickAccess("save", "Speichern", "Ctrl+S", CommonCommandIds.Save);
        var undo = QuickAccess("restore", "Rückgängig", "Ctrl+Z", CommonCommandIds.Undo);
        var redo = QuickAccess("restore", "Wiederholen", "Ctrl+Y", CommonCommandIds.Redo, mirrored: true);
        QuickAccessButtons = [save, undo, redo];

        var quickAccess = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { save, undo, redo } };
        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 16, 0),
            Children = { _documentName, _unsavedMarker },
        };
        var search = Search;
        search.VerticalAlignment = VerticalAlignment.Center;
        search.HorizontalAlignment = HorizontalAlignment.Center;
        var center = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(search, 2);
        center.Children.Add(quickAccess);
        center.Children.Add(name);
        center.Children.Add(search);
        return center;
    }

    private Button QuickAccess(string icon, string label, string shortcut, string commandId, bool mirrored = false)
    {
        var button = new Button
        {
            Content = mirrored ? Ui.MirroredIcon(icon) : new MatIcon { Icon = icon, Size = 16 },
            Theme = Ui.Theme("MatButton.Icon"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, Ui.Tooltip(label, shortcut));
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) =>
        {
            if (Commands.Find(commandId) is { } command)
            {
                await RunCommandAsync(this, command);
            }
        };
        return button;
    }

    private void RegisterCommonCommands()
    {
        Commands.Add(new OfficeCommand(CommonCommandIds.New, "Neu", () => _app.NewDocumentWindow())
        {
            Icon = "file",
            Shortcut = "Ctrl+N",
            Description = "Erstellt ein neues, leeres Dokument in einem eigenen Fenster.",
            Keywords = ["leer", "erstellen", "anlegen", "dokument"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Open, "Öffnen", () => Backstage.Open(OpenPageId))
        {
            Icon = "folder-open",
            Shortcut = "Ctrl+O",
            Description = "Öffnet ein Dokument – zuletzt verwendet oder auf diesem Computer.",
            Keywords = ["laden", "datei", "zuletzt"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Save, "Speichern", SaveAsync)
        {
            Icon = "save",
            Shortcut = "Ctrl+S",
            Description = "Speichert das Dokument unter seinem Namen.",
            Keywords = ["sichern", "ablegen"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.SaveAs, "Speichern unter", SaveAsAsync)
        {
            Icon = "folder",
            Shortcut = "F12",
            Description = "Speichert das Dokument unter einem neuen Namen oder an einem anderen Ort.",
            Keywords = ["kopie", "umbenennen", "exportieren", "sichern"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Close, "Schließen", CloseDocumentAsync)
        {
            Icon = "x",
            Shortcut = "Ctrl+W",
            Description = "Schließt dieses Dokument und sein Fenster.",
            Keywords = ["beenden", "fenster"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Undo, "Rückgängig", () => Document.Undo())
        {
            Icon = "restore",
            Shortcut = "Ctrl+Z",
            CanExecute = () => Document.CanUndo,
            Keywords = ["zurück", "widerrufen"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Redo, "Wiederholen", () => Document.Redo())
        {
            Icon = "restore",
            Shortcut = "Ctrl+Y",
            CanExecute = () => Document.CanRedo,
            Keywords = ["wiederherstellen"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Backstage, "Datei", () => Backstage.Open())
        {
            Icon = "file",
            IsSearchable = false,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.ToggleRibbon, "Menüband reduzieren", ToggleRibbon)
        {
            Icon = "chevron-up",
            Shortcut = "Ctrl+F1",
            Description = "Zeigt vom Menüband nur die Registerkarten – oder wieder alles.",
            Keywords = ["menüband", "ausblenden", "einblenden", "ribbon", "platz"],
            IsChecked = () => Ribbon.IsCollapsed,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Settings, "Einstellungen", () => Backstage.Open(SettingsPageId))
        {
            Icon = "settings",
            Description = "Erscheinungsbild, Speichern, Updates.",
            Keywords = ["optionen", "erscheinungsbild", "dunkel", "hell", "speicherort", "autostart"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.About, "Info", () => Backstage.Open(AboutPageId))
        {
            Icon = "info",
            Description = $"Version und Kanal von {AppInfo.ProductName}.",
            Keywords = ["version", "über", "hilfe"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.CheckForUpdates, "Nach Updates suchen", () => ShowSettings(SettingsPage.UpdatesTab))
        {
            Icon = "download",
            Keywords = ["update", "aktualisieren", "version"],
        });
    }

    private BackstageContent BuildBackstage() => new(
        [
            new BackstageItem(NewPageId, "Neu", "file", () => new NewPage(this), Shortcut: "Ctrl+N"),
            new BackstageItem(OpenPageId, "Öffnen", "folder-open", () => new OpenPage(this), Shortcut: "Ctrl+O"),
            new BackstageItem(SaveItemId, "Speichern", "save", Run: async () => await SaveAsync(), Shortcut: "Ctrl+S"),
            new BackstageItem(SaveAsPageId, "Speichern unter", "folder", () => new SaveAsPage(this), Shortcut: "F12"),
            new BackstageItem(CloseItemId, "Schließen", "x", Run: CloseDocumentAsync, Shortcut: "Ctrl+W"),
        ],
        new BackstageItem(SettingsPageId, "Einstellungen", "settings", () => new SettingsPage(this)),
        new BackstageItem(AboutPageId, "Info", "info", () => new AboutPage(this)),
        AppInfo.Version,
        AppInfo.Channel);

    private void CollectShortcuts()
    {
        _shortcuts.Clear();
        foreach (var command in Commands.All)
        {
            if (Ui.Gesture(command.Shortcut) is { } gesture)
            {
                _shortcuts.Add((gesture, command));
            }
        }
    }

    // ===== Document =====

    private void AttachDocument(OfficeDocument document)
    {
        _editorHost.Content = _editor.View;
        document.Changed += OnDocumentChanged;
        if (document.IsRecovered)
        {
            ShowMessage("Dieses Dokument wurde nach einem unerwarteten Ende wiederhergestellt. Speichern Sie es, um es zu behalten.", InfoSeverity.Warning);
        }
        else
        {
            ShowMessage(null);
        }

        RefreshDocumentState();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(RefreshDocumentState);

    private void RefreshDocumentState()
    {
        _documentName.Text = Document.Title;
        _unsavedMarker.IsVisible = Document.IsDirty;
        ToolTip.SetTip(_documentName, Document.FilePath ?? "Noch nicht gespeichert");
        Title = $"{(Document.IsDirty ? "● " : "")}{Document.Title} – {AppInfo.ProductName}";
        Commands.Invalidate();
        _editor.UpdateStatus(StatusBar);
        foreach (var (button, id) in QuickAccessButtons.Zip([CommonCommandIds.Save, CommonCommandIds.Undo, CommonCommandIds.Redo]))
        {
            button.IsEnabled = Commands.Find(id)?.IsEnabled ?? false;
        }
    }

    private void ToggleRibbon()
    {
        Ribbon.IsCollapsed = !Ribbon.IsCollapsed;
        SettingsService.Remember(current => current with { IsRibbonCollapsed = Ribbon.IsCollapsed });
        Commands.Invalidate();
    }

    private void OnBackstageOpenChanged(bool open)
    {
        if (!open)
        {
            FocusDocument();
        }
    }

    // ===== Keyboard =====

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Backstage.IsOpen)
        {
            e.Handled = true;
            Backstage.Close();
            return;
        }

        if (e.Key == Key.Q && e.KeyModifiers == KeyModifiers.Alt)
        {
            e.Handled = true;
            Backstage.Close();
            Search.FocusSearch();
            return;
        }

        if (Search.Box.IsFocused)
        {
            return; // the search box keeps its own keys (Strg+Z there undoes the typing)
        }

        foreach (var (gesture, command) in _shortcuts)
        {
            if (gesture.Matches(e))
            {
                e.Handled = true;
                await RunCommandAsync(this, command);
                return;
            }
        }
    }

    // ===== Closing =====

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !Document.IsDirty)
        {
            Finish();
            return;
        }

        e.Cancel = true;
        if (_isAsking)
        {
            return;
        }

        _isAsking = true;
        try
        {
            Backstage.Close();
            var choice = await SaveChangesDialog.AskAsync(this, Document.Title);
            var close = choice switch
            {
                SaveChoice.Save => await SaveAsync(),
                SaveChoice.DontSave => true,
                _ => false,
            };
            if (close)
            {
                _closeConfirmed = true;
                Close();
            }
        }
        finally
        {
            _isAsking = false;
        }
    }

    /// <summary>The window really closes: remember its size, drop the recovery file (unless the setup asked to keep it).</summary>
    private void Finish()
    {
        if (!_keepRecovery)
        {
            DocumentService.DiscardRecovery(Document);
        }

        if (WindowState == WindowState.Normal && Bounds.Width > 0)
        {
            SettingsService.Remember(current => current with { WindowWidth = Bounds.Width, WindowHeight = Bounds.Height });
        }
    }

    private async Task<bool> GuardAsync(string action, Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (BusinessException exception)
        {
            await QuestionDialog.ShowMessageAsync(this, $"{action} nicht möglich", exception.Message);
            return false;
        }
    }


    // ===== Recovery =====

    private void OnSettingsChanged(object? sender, AppSettings settings) => Ui.Post(() => ConfigureRecoveryTimer(settings));

    private void ConfigureRecoveryTimer(AppSettings settings)
    {
        _recoveryTimer.Stop();
        if (settings.AutoRecoverMinutes > 0)
        {
            _recoveryTimer.Interval = TimeSpan.FromMinutes(settings.AutoRecoverMinutes);
            _recoveryTimer.Start();
        }
    }

    /// <summary>Writes the recovery file of unsaved changes (the timer; before the setup ends the app).</summary>
    internal void WriteRecovery()
    {
        try
        {
            DocumentService.WriteRecovery(Document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BusinessException)
        {
            Log.Warning($"The recovery file could not be written: {exception.Message}");
        }
    }
}
