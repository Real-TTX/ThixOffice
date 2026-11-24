using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
///  1. the title bar (MatTitleBar, DESK-03): logo and word mark "Thix" + the app's suffix in its accent, the quick access
///     (MatQuickAccess: Speichern, Rückgängig, Wiederholen), the document's name with a dot while it has unsaved changes, in
///     the middle the command search "Was möchten Sie tun?", the caption buttons;
///  2. the ribbon (MatRibbon, drawn from the app's <see cref="OfficeApplication.CreateRibbon"/>, bound to the commands by
///     <see cref="OfficeRibbonBinding"/>);
///  3. a message bar (a recovered document, a problem);
///  4. the editor of the app (<see cref="OfficeEditor"/>);
///  5. the status bar (<see cref="OfficeStatusBar"/>: the editor's texts and the zoom).
/// "Datei" opens the backstage (<see cref="OfficeBackstage"/>) over 2–5. Every action is a command of <see cref="Commands"/>,
/// so the search, the ribbon, the quick access and the keyboard do the same. The window handles the shortcuts of the
/// commands (Strg+S …, ⌘S … on macOS, before any control); the ribbon handles Alt / F10 and its key tips. Closing asks
/// about unsaved changes.
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
    private readonly MatIcon _protectedMarker = new() { Icon = "lock", Size = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
    private readonly DispatcherTimer _recoveryTimer = new();
    private readonly DispatcherTimer _uploadRetryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly CloudIndicator _cloudIndicator = new();
    private readonly List<(KeyGesture Gesture, OfficeCommand Command)> _shortcuts = [];
    private OfficeEditor _editor;
    private Task<bool>? _upload;
    private DriveSaveResult? _conflict;
    private bool _driveMessageShown;
    private bool _closeConfirmed;
    private bool _isAsking;
    private bool _keepRecovery;
    private bool _runningShortcut;

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
        Search = new CommandSearchBox(Commands) { Run = RunFromSearchAsync };
        Search.Finished += (_, _) => FocusDocument();
        Backstage = new OfficeBackstage();
        Backstage.OpenChanged += (_, open) => OnBackstageOpenChanged(open);
        StatusBar = new OfficeStatusBar();

        RegisterCommonCommands();
        app.AddCommands(this);
        CollectShortcuts();
        _editor = app.CreateEditor(this, document);
        StatusBar.View.ZoomChanged += (_, zoom) => _editor.Zoom = zoom;
        StatusBar.View.ZoomLevelRequested += (_, _) => _editor.RequestZoomLevel(); // the editor's zoom dialog

        // The ribbon: the app's definition (made for this window: its items carry this window's state), bound to the commands.
        // Strg+F1 / ⌥⌘R is the ToggleRibbon command's (the window's shortcuts); the ribbon keeps Alt, F10 and the key tips.
        Ribbon = new MatRibbon { IsCollapsed = settings.IsRibbonCollapsed, HandlesCollapseKey = false };
        Ribbon.FileRequested += (_, e) => Backstage.Open(byKeyboard: e.ByKeyboard);
        Ribbon.CollapsedChanged += (_, collapsed) =>
        {
            SettingsService.Remember(current => current with { IsRibbonCollapsed = collapsed });
            Commands.Invalidate();
        };
        RibbonDefinition = app.CreateRibbon(this);
        Ribbon.CustomContent = item => app.CreateRibbonContent(this, item);
        QuickAccess = new MatQuickAccess(RibbonDefinition);
        // a workspace has nothing to save: its quick access is undo and redo
        foreach (var id in IsWorkspace ? new[] { CommonCommandIds.Undo, CommonCommandIds.Redo } : [CommonCommandIds.Save, CommonCommandIds.Undo, CommonCommandIds.Redo])
        {
            QuickAccess.Add(OfficeRibbonItems.Button(id)); // items of their own, sharing the id (and the state) with the ribbon's
        }

        RibbonBinding = OfficeRibbonBinding.Bind(RibbonDefinition, Commands, Ribbon);
        _editor.RibbonBound(this);

        TitleBar = new MatTitleBar
        {
            AppName = definition.Name,
            Logo = app.Logo,
            WordMark = Ui.WordMark(definition.Suffix),
            BrandWidth = 196,
            Center = BuildTitleCenter(),
        };

        _unsavedMarker.Use(Shape.FillProperty, "Mat.Accent");
        ToolTip.SetTip(_unsavedMarker, "Ungespeicherte Änderungen");
        _protectedMarker.Use(MatIcon.ForegroundProperty, "Mat.TextMuted");
        ToolTip.SetTip(_protectedMarker, "Mit Kennwort geschützt");
        AutomationProperties.SetName(_protectedMarker, "Mit Kennwort geschützt");

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        var editorArea = new Border { Child = _editorHost, ClipToBounds = true }.Use(Border.BackgroundProperty, "Mat.Bg");
        Grid.SetRow(Ribbon, 1);
        Grid.SetRow(_messageBar, 2);
        Grid.SetRow(editorArea, 3);
        Grid.SetRow(StatusBar.View, 4);
        Grid.SetRow(Backstage.View, 1);
        Grid.SetRowSpan(Backstage.View, 4);
        layout.Children.Add(TitleBar);
        layout.Children.Add(Ribbon);
        layout.Children.Add(_messageBar);
        layout.Children.Add(editorArea);
        layout.Children.Add(StatusBar.View);
        layout.Children.Add(Backstage.View);
        Content = layout;

        Backstage.Configure(BuildBackstage());

        AttachDocument(document);

        // Registered before the ribbon is attached (it listens on the window too): the commands' shortcuts – Strg+F1 among
        // them – are handled here first and marked handled, so the ribbon never acts on the same key a second time.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Opened += (_, _) => FocusDocument();
        _recoveryTimer.Tick += (_, _) => WriteRecovery();
        ConfigureRecoveryTimer(settings);
        SettingsService.Changed += OnSettingsChanged;
        CloudService.StatusChanged += OnCloudStatusChanged;
        _cloudIndicator.Click += async (_, _) => await OnCloudIndicatorClickAsync();
        _uploadRetryTimer.Tick += async (_, _) => await RetryUploadQuietlyAsync();
        Closed += (_, _) =>
        {
            SettingsService.Changed -= OnSettingsChanged;
            CloudService.StatusChanged -= OnCloudStatusChanged;
            _recoveryTimer.Stop();
            _uploadRetryTimer.Stop();
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

    /// <summary>The ribbon (MatDesktop): "Datei" and the app's tabs.</summary>
    public MatRibbon Ribbon { get; }

    /// <summary>This window's ribbon definition (the app's, made for this window); the items carry the window's state.</summary>
    public MatRibbonDefinition RibbonDefinition { get; }

    /// <summary>How the ribbon and the quick access are bound to <see cref="Commands"/>.</summary>
    public OfficeRibbonBinding RibbonBinding { get; }

    public OfficeBackstage Backstage { get; }

    public OfficeStatusBar StatusBar { get; }

    /// <summary>The quick access of the title bar: Speichern, Rückgängig, Wiederholen.</summary>
    public MatQuickAccess QuickAccess { get; }

    /// <summary>The buttons of the quick access, in their order.</summary>
    public IReadOnlyList<Button> QuickAccessButtons => [.. QuickAccess.Children.OfType<Button>()];

    /// <summary>The document's name in the title bar.</summary>
    public string DocumentNameText => _documentName.Text ?? "";

    /// <summary>True while the dot of unsaved changes shows.</summary>
    public bool ShowsUnsavedMarker => _unsavedMarker.IsVisible;

    /// <summary>True while the title bar shows the lock of a document protected with a password.</summary>
    public bool ShowsProtectedMarker => _protectedMarker.IsVisible;

    /// <summary>The message under the ribbon (a recovered document, a problem), for tests.</summary>
    public InfoBar MessageBar => _messageBar;

    /// <summary>The state of ThixCloud and of a ThixDrive document at the right of the title bar.</summary>
    public CloudIndicator CloudIndicator => _cloudIndicator;

    /// <summary>The upload that runs or ran last (tests, closing waits for it).</summary>
    public Task<bool>? Upload => _upload;

    /// <summary>
    /// Runs a command from any control of a document window (the ribbon, the search, a page) and shows a refusal of the
    /// business layer in a message; false when it could not run.
    /// </summary>
    public static Task<bool> RunCommandAsync(Visual source, OfficeCommand command) => RunAsync(source, command.Label, command.ExecuteAsync);

    /// <summary>Runs <paramref name="action"/> (a command, a value command with a value) and shows a refusal of the business layer under <paramref name="label"/>.</summary>
    public static async Task<bool> RunAsync(Visual source, string label, Func<Task<bool>> action)
    {
        try
        {
            return await action();
        }
        catch (BusinessException exception)
        {
            if (TopLevel.GetTopLevel(source) is Window window)
            {
                await QuestionDialog.ShowMessageAsync(window, label, exception.Message);
            }

            return false;
        }
    }

    // ===== The flows =====

    /// <summary>"Speichern": into the document's file, or "Speichern unter" when it has none. True when it is saved.</summary>
    public async Task<bool> SaveAsync()
    {
        if (Document.FilePath is { } current && (!await ConfirmPasswordLossAsync(current) || !await _editor.ConfirmSaveAsync(current)))
        {
            return false;
        }

        if (Document.IsInDrive)
        {
            return await SaveToDriveAsync(() => DriveService.SaveAsync(Document));
        }

        if (Document.FilePath is null)
        {
            return await SaveAsAsync();
        }

        return await GuardAsync("Speichern", () => DocumentService.Save(Document));
    }

    /// <summary>
    /// "Speichern unter" › ThixDrive: the document as <paramref name="fileName"/> into a folder of the Drive (null: the top). A taken name
    /// asks "Ersetzen?"; false when it was not saved (the person chose another name). Refusals (a bad name, offline) are thrown for
    /// the page to show at its field.
    /// </summary>
    public async Task<bool> SaveIntoDriveAsync(long? folderId, string fileName)
    {
        if (DocumentService.ExportTypeOf(fileName) is not null)
        {
            return await ExportIntoDriveAsync(folderId, fileName);
        }

        if (!await ConfirmPasswordLossAsync(DriveService.TargetName(fileName)) || !await _editor.ConfirmSaveAsync(fileName))
        {
            return false;
        }

        var result = await DriveService.SaveAsAsync(Document, folderId, fileName);
        if (result.Outcome == DriveSaveOutcome.NameTaken)
        {
            if (!await DriveDialogs.AskReplaceAsync(this, DriveService.TargetName(fileName)))
            {
                return false;
            }

            result = await DriveService.SaveAsAsync(Document, folderId, fileName, result.Current);
        }

        if (result.Outcome == DriveSaveOutcome.ChangedMeanwhile)
        {
            throw new BusinessException($"{result.Message} Bitte versuchen Sie es noch einmal.");
        }

        ShowDriveMessage(null);
        return result.IsUploaded;
    }

    /// <summary>Uploads a ThixDrive document that was saved but not uploaded (the title bar's "Nicht hochgeladen", the timer, closing).</summary>
    public Task<bool> RetryUploadAsync() =>
        Document.IsInDrive ? SaveToDriveAsync(() => DriveService.UploadAsync(Document)) : Task.FromResult(true);

    /// <summary>The question after a conflict (also from the title bar's "Konflikt"): Als Kopie speichern, Später entscheiden, Überschreiben.</summary>
    public async Task<bool> ResolveConflictAsync()
    {
        if (_conflict is not { } conflict || Document.Drive is not { } drive)
        {
            return true;
        }

        switch (await DriveDialogs.AskConflictAsync(this, drive.Name, conflict.Message))
        {
            case DriveConflictChoice.SaveCopy:
                return await SaveToDriveAsync(() => DriveService.SaveCopyAsync(Document));
            case DriveConflictChoice.Overwrite:
                return await SaveToDriveAsync(() => DriveService.OverwriteAsync(Document, conflict.Current));
            default:
                ShowDriveMessage($"„{drive.Name}“ ist auf diesem Computer gespeichert. Die Fassung in ThixDrive wurde inzwischen geändert – „Konflikt“ oben rechts fragt erneut.");
                return true;
        }
    }

    /// <summary>
    /// Runs a save into ThixDrive (one at a time) and follows its outcome: uploaded – done; not uploaded – a message, the retry
    /// timer; changed in between – the conflict question. True when the document is saved (here at least).
    /// </summary>
    private async Task<bool> SaveToDriveAsync(Func<Task<DriveSaveResult>> save)
    {
        if (_upload is { IsCompleted: false } running)
        {
            await running;
        }

        _conflict = null;
        var task = RunDriveSaveAsync(save);
        _upload = task;
        var saved = await task;
        return _conflict is not null && Document.DriveState == DriveSyncState.Conflict ? await ResolveConflictAsync() : saved;
    }

    private async Task<bool> RunDriveSaveAsync(Func<Task<DriveSaveResult>> save)
    {
        DriveSaveResult result;
        try
        {
            result = await save();
        }
        catch (BusinessException exception)
        {
            RefreshDocumentState();
            await QuestionDialog.ShowMessageAsync(this, "Hochladen nicht möglich", exception.Message);
            UpdateRetryTimer();
            return !Document.IsDirty;
        }

        RefreshDocumentState();
        switch (result.Outcome)
        {
            case DriveSaveOutcome.ChangedMeanwhile:
                _conflict = result; // asked once this upload is over (the answer starts the next one)
                UpdateRetryTimer();
                return true;
            case DriveSaveOutcome.NotUploaded:
                ShowDriveMessage(result.Message, InfoSeverity.Warning);
                UpdateRetryTimer();
                return true;
            default:
                _conflict = null;
                ShowDriveMessage(null);
                UpdateRetryTimer();
                return true;
        }
    }

    /// <summary>The timer tries a waiting upload again every 30 seconds while signed in (never a conflict: the person decides that).</summary>
    private async Task RetryUploadQuietlyAsync()
    {
        if (Document.DriveState != DriveSyncState.NotUploaded || _upload is { IsCompleted: false } || !CloudService.IsConnected)
        {
            return;
        }

        var task = QuietUploadAsync();
        _upload = task;
        await task;
    }

    private async Task<bool> QuietUploadAsync()
    {
        try
        {
            var result = await DriveService.UploadAsync(Document);
            if (result.Outcome == DriveSaveOutcome.ChangedMeanwhile)
            {
                _conflict = result;
            }
            else if (result.IsUploaded)
            {
                ShowDriveMessage(null);
            }
        }
        catch (BusinessException exception)
        {
            ShowDriveMessage(exception.Message, InfoSeverity.Warning);
        }

        UpdateRetryTimer();
        RefreshDocumentState();
        return Document.DriveState == DriveSyncState.Uploaded;
    }

    private void UpdateRetryTimer()
    {
        if (Document.DriveState == DriveSyncState.NotUploaded)
        {
            _uploadRetryTimer.Start();
        }
        else
        {
            _uploadRetryTimer.Stop();
        }
    }

    /// <summary>The message bar for the upload (null hides it again – but only a message the upload put there).</summary>
    private void ShowDriveMessage(string? text, InfoSeverity severity = InfoSeverity.Info)
    {
        if (text is null)
        {
            if (_driveMessageShown)
            {
                ShowMessage(null);
                _driveMessageShown = false;
            }

            return;
        }

        ShowMessage(text, severity, isClosable: true);
        _driveMessageShown = true;
    }

    private async Task OnCloudIndicatorClickAsync()
    {
        switch (_cloudIndicator.Kind)
        {
            case CloudIndicatorKind.NotUploaded:
                await RetryUploadAsync();
                break;
            case CloudIndicatorKind.Conflict:
                await ResolveConflictAsync();
                break;
            default:
                ShowSettings(SettingsPage.CloudTab);
                break;
        }
    }

    private void OnCloudStatusChanged(object? sender, CloudStatus status) => Ui.Post(RefreshDocumentState);

    /// <summary>"Speichern unter": the system's dialog, then the save. True when it is saved.</summary>
    public async Task<bool> SaveAsAsync()
    {
        var target = await OfficeApplication.FileDialogs.PickSaveTargetAsync(this, DocumentService.SuggestedFileName(Document), DocumentService.SuggestedFolder(Document));
        return target is not null && await SaveToAsync(target);
    }

    /// <summary>
    /// Saves the document as <paramref name="path"/> (the Speichern-unter page, tests). True when it is saved. A path of an export type
    /// (PDF) writes a copy instead: the document keeps its file, type and unsaved state (<see cref="ExportToAsync"/>).
    /// </summary>
    public async Task<bool> SaveToAsync(string path)
    {
        if (DocumentService.ExportTypeOf(path) is not null)
        {
            return await ExportToAsync(path);
        }

        return await ConfirmPasswordLossAsync(path) && await _editor.ConfirmSaveAsync(path) && await GuardAsync("Speichern unter", () => DocumentService.SaveAs(Document, path));
    }

    /// <summary>
    /// A document protected with a password saved as a type that cannot carry one (.odt, .txt, .csv …) asks first: "Ohne Kennwort
    /// speichern" (the save leaves the document unprotected) or Abbrechen. True when the save may go on.
    /// </summary>
    private async Task<bool> ConfirmPasswordLossAsync(string path)
    {
        if (!DocumentService.LosesPassword(Document, path))
        {
            return true;
        }

        return await QuestionDialog.AskAsync(this, "Kennwortschutz", DocumentService.PasswordLossQuestion(path),
            new DialogChoice("Ohne Kennwort speichern", "save", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "x", IsCancel: true)) == 0;
    }

    /// <summary>True when the document's type can carry a password (a new document is saved in the app's own type).</summary>
    public bool CanProtectDocument => !IsWorkspace && (Document.FileType ?? AppInfo.Definition.SaveType).CanEncrypt;

    /// <summary>
    /// "Mit Kennwort verschlüsseln": the dialog sets, changes or removes the document's password; it applies from the next save on
    /// (the document shows unsaved changes, like Word). True when it was changed.
    /// </summary>
    public async Task<bool> EncryptAsync()
    {
        if (!CanProtectDocument)
        {
            return false;
        }

        if (!await new EncryptDocumentDialog(Document).ShowAsync(this))
        {
            return false;
        }

        ShowMessage(Document.HasPassword
            ? $"„{Document.Title}“ wird beim nächsten Speichern mit dem Kennwort verschlüsselt."
            : $"Der Kennwortschutz von „{Document.Title}“ wird beim nächsten Speichern entfernt.", InfoSeverity.Info, isClosable: true);
        return true;
    }

    /// <summary>"Als PDF speichern": the system's dialog for a PDF, then the copy (the document stays as it is). True when written.</summary>
    public async Task<bool> SaveAsPdfAsync()
    {
        var name = System.IO.Path.ChangeExtension(DocumentService.SuggestedFileName(Document), DocumentType.Pdf.Extension);
        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(this, name, DocumentService.SuggestedFolder(Document), "PDF-Dokument", DocumentType.Pdf.Extension);
        return target is not null && await ExportToAsync(target);
    }

    /// <summary>
    /// Writes the document's pages as a PDF to <paramref name="path"/> (the extension is added when missing) and says so in the message bar
    /// with "Öffnen". The document keeps its own file, type and unsaved state – like Word's "PDF (*.pdf)" in "Speichern unter".
    /// </summary>
    public async Task<bool> ExportToAsync(string path)
    {
        if (_editor.Pdf is not { } pdf)
        {
            await QuestionDialog.ShowMessageAsync(this, "Als PDF speichern", $"{AppInfo.ProductName} kann dieses Dokument nicht als PDF speichern.");
            return false;
        }

        string? written = null;
        if (!await GuardAsync("Als PDF speichern", () => written = DocumentService.Export(path, DocumentType.Pdf.Extension, stream => pdf.Write(stream, Document.Title))))
        {
            return false;
        }

        var file = written!;
        ShowMessage($"Als PDF gespeichert: {file}", InfoSeverity.Success, isClosable: true, "Öffnen", () => Ui.OpenExternal(file));
        return true;
    }

    /// <summary>"Speichern unter" › ThixDrive as PDF: a copy into a folder of the Drive (a taken name asks "Ersetzen?"); the document stays as it is.</summary>
    private async Task<bool> ExportIntoDriveAsync(long? folderId, string fileName)
    {
        if (_editor.Pdf is not { } pdf)
        {
            throw new BusinessException($"{AppInfo.ProductName} kann dieses Dokument nicht als PDF speichern.");
        }

        void Write(Stream stream) => pdf.Write(stream, Document.Title);
        var result = await DriveService.ExportAsync(folderId, fileName, Write);
        if (result.Outcome == DriveSaveOutcome.NameTaken)
        {
            if (!await DriveDialogs.AskReplaceAsync(this, DriveService.TargetName(fileName)))
            {
                return false;
            }

            result = await DriveService.ExportAsync(folderId, fileName, Write, result.Current);
        }

        if (result.Outcome == DriveSaveOutcome.ChangedMeanwhile)
        {
            throw new BusinessException($"{result.Message} Bitte versuchen Sie es noch einmal.");
        }

        ShowMessage($"Als PDF gespeichert: {result.Message}", InfoSeverity.Success, isClosable: true);
        return result.IsUploaded;
    }

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
        var zoom = _editor.Zoom;
        _editor.Detach();
        _editor.ZoomChanged -= OnEditorZoomChanged;
        Document.Changed -= OnDocumentChanged;
        DocumentService.DiscardRecovery(Document);
        Document = document;
        _editor = _app.CreateEditor(this, document);
        _editor.Zoom = zoom;
        AttachDocument(document);
        _editor.RibbonBound(this);
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

    /// <summary>
    /// Opens the backstage on a page (null: the first; Einstellungen › Updates from a notification …); opened by a shortcut,
    /// the entry shows the focus ring.
    /// </summary>
    public void ShowBackstage(string? pageId) => Backstage.Open(pageId, byKeyboard: _runningShortcut);

    /// <summary>Opens Einstellungen on a tab (SettingsPage.GeneralTab …).</summary>
    public void ShowSettings(string tab)
    {
        ShowBackstage(SettingsPageId);
        (Backstage.CurrentPage as SettingsPage)?.ShowTab(tab);
    }

    /// <summary>Shows a message under the ribbon (null hides it); <paramref name="actionLabel"/> + <paramref name="action"/> add a button ("Öffnen").</summary>
    public void ShowMessage(string? text, InfoSeverity severity = InfoSeverity.Info, bool isClosable = false, string? actionLabel = null, Action? action = null)
    {
        if (text is null)
        {
            _messageBar.Hide();
            return;
        }

        _messageBar.Show(text, severity, isClosable, actionLabel, action);
    }

    // ===== Building =====

    private Grid BuildTitleCenter()
    {
        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 16, 0),
            Children = { _documentName, _protectedMarker, _unsavedMarker },
        };
        var search = Search;
        search.VerticalAlignment = VerticalAlignment.Center;
        search.HorizontalAlignment = HorizontalAlignment.Center;
        var center = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(search, 2);
        Grid.SetColumn(_cloudIndicator, 3);
        center.Children.Add(QuickAccess);
        center.Children.Add(name);
        center.Children.Add(search);
        center.Children.Add(_cloudIndicator);
        return center;
    }

    /// <summary>A window of a workspace app (ThixContacts): no document flows, no recovery, a backstage of the app's pages, Einstellungen and Info.</summary>
    public static bool IsWorkspace => AppInfo.Definition.IsWorkspace;

    private void RegisterCommonCommands()
    {
        if (!IsWorkspace)
        {
            RegisterDocumentCommands();
        }

        RegisterWindowCommands();
    }

    /// <summary>Neu, Öffnen, Speichern, Speichern unter, Als PDF speichern, Schließen: the flows of a document app.</summary>
    private void RegisterDocumentCommands()
    {
        Commands.Add(new OfficeCommand(CommonCommandIds.New, "Neu", () => _app.NewDocumentWindow())
        {
            Icon = "file-new",
            Shortcut = "Ctrl+N",
            Description = "Erstellt ein neues, leeres Dokument in einem eigenen Fenster.",
            Keywords = ["leer", "erstellen", "anlegen", "dokument"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Open, "Öffnen", () => ShowBackstage(OpenPageId))
        {
            Icon = "folder-open",
            Shortcut = "Ctrl+O",
            Description = "Öffnet ein Dokument – zuletzt verwendet, aus ThixDrive oder auf diesem Computer.",
            Keywords = ["laden", "datei", "zuletzt", "cloud", "thixdrive"],
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
            Icon = "save-as",
            Shortcut = "F12",
            MacShortcut = "Ctrl+Shift+S",
            Description = "Speichert das Dokument unter einem neuen Namen oder an einem anderen Ort.",
            Keywords = ["kopie", "umbenennen", "exportieren", "sichern"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.SaveAsPdf, "Als PDF speichern", SaveAsPdfAsync)
        {
            Icon = "export",
            Description = "Speichert eine Kopie als PDF – das Dokument selbst bleibt, wie es ist.",
            Keywords = ["pdf", "exportieren", "kopie", "drucken", "versenden", "adobe"],
            CanExecute = () => _editor.Pdf is not null && AppInfo.Definition.ExportTypes.Contains(DocumentType.Pdf),
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Encrypt, "Mit Kennwort verschlüsseln", EncryptAsync)
        {
            Icon = "lock",
            Description = "Schützt das Dokument mit einem Kennwort – ohne das Kennwort lässt es sich nicht öffnen. Möglich in den Office-Formaten (.docx, .xlsx, .pptx).",
            Keywords = ["kennwort", "passwort", "verschlüsseln", "schützen", "dokument schützen", "entschlüsseln", "kennwort entfernen"],
            CanExecute = () => CanProtectDocument,
            IsChecked = () => Document.HasPassword,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Close, "Schließen", CloseDocumentAsync)
        {
            Icon = "x",
            Shortcut = "Ctrl+W",
            Description = "Schließt dieses Dokument und sein Fenster.",
            Keywords = ["beenden", "fenster"],
        });
    }

    /// <summary>The commands every window has: undo, the backstage, the ribbon, zoom, settings, info, updates.</summary>
    private void RegisterWindowCommands()
    {
        Commands.Add(new OfficeCommand(CommonCommandIds.Undo, "Rückgängig", () => Document.Undo())
        {
            Icon = "undo",
            Shortcut = "Ctrl+Z",
            CanExecute = () => Document.CanUndo,
            Keywords = ["zurück", "widerrufen"],
            YieldsToTextField = true,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Redo, "Wiederholen", () => Document.Redo())
        {
            Icon = "redo",
            Shortcut = "Ctrl+Y",
            MacShortcut = "Ctrl+Shift+Z",
            CanExecute = () => Document.CanRedo,
            Keywords = ["wiederherstellen"],
            YieldsToTextField = true,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Backstage, "Datei", () => ShowBackstage(null))
        {
            Icon = "file",
            IsSearchable = false,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.ToggleRibbon, "Menüband reduzieren", () => Ribbon.ToggleCollapsed())
        {
            Icon = "chevron-up",
            Shortcut = "Ctrl+F1",
            MacShortcut = "Ctrl+Alt+R", // ⌥⌘R, MatRibbon's key on macOS
            Description = "Zeigt vom Menüband nur die Registerkarten – oder wieder alles.",
            Keywords = ["menüband", "ausblenden", "einblenden", "ribbon", "platz"],
            IsChecked = () => Ribbon.IsCollapsed,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.ZoomIn, "Vergrößern", () => _editor.Zoom = Math.Floor((_editor.Zoom / 10) + 1) * 10)
        {
            Icon = "zoom-in",
            Description = "Zeigt das Dokument größer an.",
            Keywords = ["zoom", "größer", "lupe"],
            CanExecute = () => _editor.Zoom < OfficeEditor.MaxZoom,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.ZoomOut, "Verkleinern", () => _editor.Zoom = Math.Ceiling((_editor.Zoom / 10) - 1) * 10)
        {
            Icon = "zoom-out",
            Description = "Zeigt das Dokument kleiner an.",
            Keywords = ["zoom", "kleiner", "lupe"],
            CanExecute = () => _editor.Zoom > OfficeEditor.MinZoom,
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Zoom100, "100 %", () => _editor.Zoom = 100)
        {
            Icon = "search",
            Description = "Zeigt das Dokument in Originalgröße.",
            Keywords = ["zoom", "originalgröße", "hundert"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.Settings, "Einstellungen", () => ShowBackstage(SettingsPageId))
        {
            Icon = "settings",
            Description = "Erscheinungsbild, Speichern, Updates.",
            Keywords = ["optionen", "erscheinungsbild", "dunkel", "hell", "speicherort", "autostart"],
        });
        Commands.Add(new OfficeCommand(CommonCommandIds.About, "Info", () => ShowBackstage(AboutPageId))
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

    private BackstageContent BuildBackstage() => IsWorkspace
        ? new(
            [.. _app.CreateBackstageItems(this)],
            new BackstageItem(SettingsPageId, "Einstellungen", "settings", () => new SettingsPage(this)),
            new BackstageItem(AboutPageId, "Info", "info", () => new AboutPage(this)),
            AppInfo.Version,
            AppInfo.Channel)
        : new(
        [
            new BackstageItem(NewPageId, "Neu", "file-new", () => new NewPage(this), Shortcut: ShortcutOf(CommonCommandIds.New)),
            new BackstageItem(OpenPageId, "Öffnen", "folder-open", () => new OpenPage(this), Shortcut: ShortcutOf(CommonCommandIds.Open)),
            new BackstageItem(SaveItemId, "Speichern", "save", Run: async () => await SaveAsync(), Shortcut: ShortcutOf(CommonCommandIds.Save)),
            new BackstageItem(SaveAsPageId, "Speichern unter", "save-as", () => new SaveAsPage(this), Shortcut: ShortcutOf(CommonCommandIds.SaveAs)),
            .. _app.CreateBackstageItems(this),
            new BackstageItem(CloseItemId, "Schließen", "x", Run: CloseDocumentAsync, Shortcut: ShortcutOf(CommonCommandIds.Close)),
        ],
        new BackstageItem(SettingsPageId, "Einstellungen", "settings", () => new SettingsPage(this)),
        new BackstageItem(AboutPageId, "Info", "info", () => new AboutPage(this)),
        AppInfo.Version,
        AppInfo.Channel);

    private string? ShortcutOf(string commandId) => Commands.Find(commandId)?.CurrentShortcut;

    private void CollectShortcuts()
    {
        _shortcuts.Clear();
        foreach (var command in Commands.All)
        {
            if (Ui.Gesture(command.CurrentShortcut) is { } gesture)
            {
                _shortcuts.Add((gesture, command));
            }
        }
    }

    /// <summary>The search chose a command: a value command shows its field in the ribbon (the user types the size there); any other runs.</summary>
    private async Task<bool> RunFromSearchAsync(OfficeCommand command)
    {
        if (command is OfficeValueCommand && RibbonBinding.Reveal(command.Id))
        {
            return true;
        }

        FocusDocument();
        return await RunCommandAsync(this, command);
    }

    // ===== Document =====

    private void AttachDocument(OfficeDocument document)
    {
        _editorHost.Content = _editor.View;
        _editor.ZoomChanged += OnEditorZoomChanged;
        StatusBar.View.Zoom = _editor.Zoom;
        document.Changed += OnDocumentChanged;
        if (document.IsRecovered)
        {
            ShowMessage("Dieses Dokument wurde nach einem unerwarteten Ende wiederhergestellt. Speichern Sie es, um es zu behalten.", InfoSeverity.Warning);
        }
        else if (_editor.OpeningWarning is { } warning)
        {
            ShowMessage(warning, InfoSeverity.Warning, isClosable: true);
        }
        else if (document.DriveState == DriveSyncState.NotUploaded)
        {
            ShowDriveMessage("Dieses Dokument hat Änderungen, die noch nicht in ThixDrive sind. Sie werden hochgeladen, sobald die ThixCloud erreichbar ist.", InfoSeverity.Warning);
            Dispatcher.UIThread.Post(() => _ = RetryUploadQuietlyAsync(), DispatcherPriority.Background);
        }
        else
        {
            ShowMessage(null);
        }

        UpdateRetryTimer();
        RefreshDocumentState();
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(RefreshDocumentState);

    private void RefreshDocumentState()
    {
        _documentName.Text = Document.Title;
        _unsavedMarker.IsVisible = Document.IsDirty;
        _protectedMarker.IsVisible = Document.HasPassword;
        ToolTip.SetTip(_documentName, Document.LocationText);
        Title = $"{(Document.IsDirty ? "● " : "")}{Document.Title} – {AppInfo.ProductName}";
        _cloudIndicator.Refresh(Document, CloudService.Status, AppInfo.Definition.HasCloud && !AccessPolicy.IsCloudDisabled);
        Commands.Invalidate();
        _editor.UpdateStatus(StatusBar);
    }

    private void OnEditorZoomChanged(object? sender, double zoom)
    {
        StatusBar.View.Zoom = zoom;
        Commands.Invalidate();
    }

    private void OnBackstageOpenChanged(bool open)
    {
        // The backstage covers the ribbon: Alt and the key tips are not the ribbon's while it is open.
        Ribbon.HandlesWindowKeys = !open;
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

        // A text field (Schriftgrad in the ribbon, the find bar) undoes, copies and selects its own text; the other shortcuts
        // (Strg+S) still apply there.
        var inTextField = FocusManager?.GetFocusedElement() is TextBox or ITextField { IsEditingText: true };
        // Several commands may share a key when only one of them applies at a time (ThixMail's Strg+N: a new mail, appointment or contact by the
        // view): the first enabled one runs, else the first.
        var matches = _shortcuts.Where(shortcut => shortcut.Gesture.Matches(e)).Select(shortcut => shortcut.Command).ToList();
        if (matches.Count > 0)
        {
            var command = matches.Count == 1 ? matches[0] : matches.FirstOrDefault(candidate => candidate.IsEnabled) ?? matches[0];
            if (inTextField && (command.YieldsToTextField || !command.IsEnabled))
            {
                return; // a text field keeps the keys a command cannot use now (⌥← moves by words where Zurück of the calendar does not apply)
            }

            e.Handled = true;
            _runningShortcut = true;
            try
            {
                await RunCommandAsync(this, command);
            }
            finally
            {
                _runningShortcut = false;
            }
        }
    }

    // ===== Closing =====

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || (!Document.IsDirty && !HasUploadToFinish))
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
            var close = true;
            if (Document.IsDirty)
            {
                close = await SaveChangesDialog.AskAsync(this, Document.Title) switch
                {
                    SaveChoice.Save => await SaveAsync(),
                    SaveChoice.DontSave => true,
                    _ => false,
                };
            }

            if (close && HasUploadToFinish)
            {
                close = await FinishUploadAsync();
            }

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

    /// <summary>A ThixDrive document whose save is being uploaded or waits (saved here, not in the Drive yet).</summary>
    private bool HasUploadToFinish => Document.IsInDrive && !Document.IsDirty &&
                                      (_upload is { IsCompleted: false } || Document.DriveState is DriveSyncState.NotUploaded or DriveSyncState.Conflict);

    /// <summary>
    /// Closing a ThixDrive document: a running upload is waited for; one that waits (offline, conflict) is asked about – Erneut
    /// versuchen, Abbrechen, Trotzdem schließen (the working copy keeps the changes; they go up when the document is opened again).
    /// </summary>
    private async Task<bool> FinishUploadAsync()
    {
        if (_upload is { IsCompleted: false } running)
        {
            ShowMessage("Das Dokument wird noch in ThixDrive hochgeladen – das Fenster schließt danach.", InfoSeverity.Info);
            await running;
        }

        while (Document.DriveState is DriveSyncState.NotUploaded or DriveSyncState.Conflict)
        {
            var isConflict = Document.DriveState == DriveSyncState.Conflict;
            var message = isConflict ? "Die Fassung in ThixDrive wurde inzwischen geändert." : "";
            switch (await DriveDialogs.AskPendingUploadAsync(this, Document.Drive!.Name, message))
            {
                case PendingUploadChoice.Retry when isConflict:
                    await ResolveConflictAsync();
                    break;
                case PendingUploadChoice.Retry:
                    await RetryUploadAsync();
                    break;
                case PendingUploadChoice.CloseAnyway:
                    return true;
                default:
                    return false;
            }
        }

        return true;
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
        if (IsWorkspace)
        {
            return; // the app keeps its data itself, there is no document to recover
        }

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
