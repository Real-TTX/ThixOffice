using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop;

/// <summary>
/// The application object every app of the suite derives from (ThixWrite's <c>App</c>): the app names its definition,
/// its document model, its editor, its commands and its ribbon; everything else happens here, the same for every app:
///  - Windows: the published exe is its own setup ("--update [--restart]", "--uninstall [--quiet]", "--quiet");
///  - "--self-test &lt;report&gt;" checks the published program, "--snapshot &lt;folder&gt;" draws its windows (OfficeProgram);
///  - one instance per user and data folder (MatSingleInstance, DESK-05): a second start – a double click on a document –
///    hands its files to the running one, which opens each in a window of its own (or the window that already has it);
///    &lt;APP&gt;_DATA_DIR makes a development run an instance of its own;
///  - a document per window; on Windows the last window closed ends the app, on macOS the app stays until ⌘Q (the Mac
///    convention; a click on the Dock icon opens a new document); a downloaded update is installed when the app ends
///    (never under the user's hands, like ThixNote); after a crash the recovery files come back as unsaved documents;
///  - crashes and hangs are reported (ProblemReports, UiWatchdog) and shown at the next start.
/// </summary>
public abstract class OfficeApplication : Application
{
    private readonly List<OfficeWindow> _windows = [];
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MatSingleInstance? _instance;
    private Bitmap? _logo;
    private WindowIcon? _icon;
    private bool _isExiting;
    private bool _hasExited;
    private bool _updateStarted;

    public static OfficeApplication Instance => (OfficeApplication)Current!;

    /// <summary>The system's file dialogs; tests put in their own answers.</summary>
    public static IFileDialogs FileDialogs { get; set; } = new SystemFileDialogs();

    /// <summary>The app: id, name, accent, document types.</summary>
    public abstract OfficeAppDefinition Definition { get; }

    /// <summary>
    /// The ribbon of <paramref name="window"/>: a NEW definition per window (its items carry the window's state), made of
    /// items whose ids are command ids (<see cref="OfficeRibbonItems"/>). <see cref="OfficeRibbonBinding"/> fills in labels,
    /// icons and shortcuts from the commands, runs them, follows their state and leaves out items whose command the window
    /// does not have (yet). Called after <see cref="AddCommands"/>.
    /// </summary>
    public virtual MatRibbonDefinition CreateRibbon(OfficeWindow window) => new();

    /// <summary>The control of a <see cref="MatRibbonCustom"/> place of the app's ribbon (a picker the ribbon does not know), or null.</summary>
    public virtual Control? CreateRibbonContent(OfficeWindow window, MatRibbonCustom item) => null;

    /// <summary>The assembly with the app's Assets (logo-256.png, &lt;id&gt;.ico): the app itself.</summary>
    protected virtual string AssetsAssembly => Definition.Name;

    /// <summary>The logo (title bar, about page, setup): avares://&lt;App&gt;/Assets/logo-256.png.</summary>
    public Bitmap Logo => _logo ??= new Bitmap(AssetLoader.Open(new Uri($"avares://{AssetsAssembly}/Assets/logo-256.png")));

    /// <summary>The icon of every window (task bar, Alt+Tab): avares://&lt;App&gt;/Assets/&lt;id&gt;.ico.</summary>
    public WindowIcon AppIcon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri($"avares://{AssetsAssembly}/Assets/{Definition.Id}.ico")));

    /// <summary>The document windows that are open, in the order they were opened.</summary>
    public IReadOnlyList<OfficeWindow> DocumentWindows => _windows;

    /// <summary>The self test closes its windows and ends by itself: the last window closed must not end the app then.</summary>
    internal bool KeepsRunningWithoutWindows { get; set; }

    /// <summary>
    /// Whether closing the last document window ends the app: on Windows yes (like Word); on macOS no – the app stays in
    /// the Dock and the menu bar, a click on its icon opens a new document, ⌘Q ends it (the Mac convention).
    /// </summary>
    public static bool LastWindowEndsApp(bool isMacOS) => !isMacOS;

    /// <summary>The program file; shortcuts and file types point to it.</summary>
    public string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Definition.Name);

    /// <summary>A new, empty document of the app named <paramref name="untitledName"/>.</summary>
    public abstract OfficeDocument CreateDocument(string untitledName);

    /// <summary>The editor of <paramref name="document"/> in <paramref name="window"/>.</summary>
    public abstract OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document);

    /// <summary>
    /// The document the snapshots show: made by the app, filled with its sample content (<see cref="FillSampleDocument"/>),
    /// as if it were saved at <paramref name="path"/> (no file is written).
    /// </summary>
    public OfficeDocument CreateSampleDocument(string path)
    {
        var document = CreateDocument(DocumentService.NextUntitledName());
        FillSampleDocument(document);
        document.MarkSaved(path, Definition.SaveType);
        return document;
    }

    /// <summary>Puts the app's sample content into a new document (snapshots, tests).</summary>
    protected virtual void FillSampleDocument(OfficeDocument document)
    {
    }

    /// <summary>The app adds its own commands to a new window (the shell's are there already).</summary>
    public virtual void AddCommands(OfficeWindow window)
    {
    }

    /// <summary>The app's own backstage pages of <paramref name="window"/> (Drucken/Exportieren), shown after "Speichern unter".</summary>
    public virtual IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [];

    /// <summary>
    /// The app's own checks of the published program (<c>--self-test</c>): its document model, its formats, its editor in a
    /// real window. Each returns a note for the report or throws.
    /// </summary>
    public virtual IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [];

    /// <summary>
    /// The app's own pictures of <c>--snapshot</c> ("main-find" …), drawn after the main window with the sample document:
    /// <see cref="ShowSnapshotScene"/> prepares one and returns the top level to draw, <see cref="EndSnapshotScene"/> undoes it.
    /// </summary>
    public virtual IReadOnlyList<string> SnapshotScenes => [];

    /// <summary>Prepares the snapshot scene <paramref name="name"/> in <paramref name="window"/>; returns what to draw (the window or a dialog).</summary>
    public virtual TopLevel ShowSnapshotScene(OfficeWindow window, string name) => window;

    /// <summary>Undoes the snapshot scene <paramref name="name"/> (closes a dialog, the find bar, the backstage).</summary>
    public virtual void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
    }

    public override void Initialize()
    {
        if (!AppInfo.IsInitialized)
        {
            AppInfo.Initialize(Definition, GetType().Assembly);
        }

        // The apps speak German, the texts of MatDesktop (title bar, setup) too.
        MatText.Language = "de";
        MatTheme.Install(this, new MatThemeOptions(Definition.Accent) { Icon = AppIcon });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _desktop = desktop;
        var args = desktop.Args ?? [];

        var selfTest = Array.IndexOf(args, OfficeSelfTest.Argument);
        if (selfTest >= 0)
        {
            var report = selfTest + 1 < args.Length ? args[selfTest + 1] : Path.Combine(AppPaths.DataDirectory, "self-test.txt");
            Dispatcher.UIThread.Post(async () => EndProcess(await OfficeSelfTest.RunAsync(this, report)));
            return;
        }

        if (OperatingSystem.IsWindows() && RunSetupIfRequested(args))
        {
            return;
        }

        var files = DocumentFilesIn(args);
        if (!TakeSingleInstance(files))
        {
            EndProcess(0);
            return;
        }

        Start(files);
    }

    // ===== Documents and windows =====

    /// <summary>"Neu": a new, empty document in a window of its own.</summary>
    public OfficeWindow NewDocumentWindow() => ShowWindow(CreateDocument(DocumentService.NextUntitledName()));

    /// <summary>Shows <paramref name="document"/> in a new window.</summary>
    public OfficeWindow ShowWindow(OfficeDocument document)
    {
        var window = new OfficeWindow(this, document);
        _windows.Add(window);
        window.Closed += (_, _) => OnWindowClosed(window);
        if (_desktop is not null && _desktop.MainWindow is null)
        {
            _desktop.MainWindow = window;
        }

        window.Show();
        window.Activate();
        return window;
    }

    /// <summary>
    /// Opens files: a document that is open already comes to the front; otherwise it goes into <paramref name="into"/> when
    /// that window holds an untouched new document (like Word's "Dokument1"), else into a window of its own. Problems are
    /// shown, one message per file.
    /// </summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths, OfficeWindow? into = null)
    {
        foreach (var path in paths)
        {
            await OpenFileAsync(path, into);
            into = null;
        }
    }

    /// <summary>Opens one file (see <see cref="OpenFilesAsync"/>); returns its window, or null when it could not be opened.</summary>
    public async Task<OfficeWindow?> OpenFileAsync(string path, OfficeWindow? into = null)
    {
        var open = WindowOf(path);
        if (open is not null)
        {
            open.BringToFront();
            return open;
        }

        try
        {
            return Place(DocumentService.Open(path, CreateDocument), into);
        }
        catch (BusinessException exception)
        {
            await ShowOpenProblemAsync(into, exception);
            return null;
        }
    }

    /// <summary>
    /// Opens a file of ThixDrive (downloaded into its working copy): a window that has it already comes to the front; otherwise like
    /// <see cref="OpenFileAsync"/>. Null when it could not be opened (the reason was shown).
    /// </summary>
    public async Task<OfficeWindow?> OpenFromDriveAsync(long itemId, OfficeWindow? into = null)
    {
        if (WindowOfDriveItem(itemId) is { } open)
        {
            open.BringToFront();
            return open;
        }

        try
        {
            return Place(await DriveService.OpenAsync(itemId, CreateDocument), into);
        }
        catch (BusinessException exception)
        {
            await ShowOpenProblemAsync(into, exception);
            return null;
        }
    }

    /// <summary>
    /// Opens an entry of "Zuletzt verwendet": a file of this computer, or a ThixDrive document – from the Drive, or from its working
    /// copy when the cloud cannot be reached.
    /// </summary>
    public async Task<OfficeWindow?> OpenRecentAsync(RecentDocument entry, OfficeWindow? into = null)
    {
        if (entry.Drive is not { } drive)
        {
            return await OpenFileAsync(entry.Path, into);
        }

        if ((WindowOfDriveItem(drive.ItemId) ?? WindowOf(entry.Path)) is { } open)
        {
            open.BringToFront();
            return open;
        }

        try
        {
            return Place(await DriveService.OpenRecentAsync(entry, CreateDocument), into);
        }
        catch (BusinessException exception)
        {
            await ShowOpenProblemAsync(into, exception);
            return null;
        }
    }

    /// <summary>The window that has this ThixDrive item open, or null.</summary>
    public OfficeWindow? WindowOfDriveItem(long itemId) =>
        _windows.FirstOrDefault(window => window.Document.Drive is { } drive && drive.ItemId == itemId && CloudService.IsSignedInTo(drive.ServerUrl, drive.UserName));

    /// <summary>An opened document goes into <paramref name="into"/> when that window holds an untouched new document, else into a window of its own.</summary>
    private OfficeWindow Place(OfficeDocument document, OfficeWindow? into)
    {
        var target = into ?? _windows.LastOrDefault(window => window.IsActive) ?? _windows.LastOrDefault();
        if (target is not null && target.Document.IsPristine)
        {
            target.ReplaceDocument(document);
            target.BringToFront();
            return target;
        }

        return ShowWindow(document);
    }

    private async Task ShowOpenProblemAsync(OfficeWindow? into, BusinessException exception)
    {
        var owner = into ?? _windows.LastOrDefault();
        if (owner is not null)
        {
            await QuestionDialog.ShowMessageAsync(owner, "Öffnen nicht möglich", exception.Message);
        }
        else
        {
            MatNotification.Show("Öffnen nicht möglich", exception.Message, isWarning: true);
        }
    }

    /// <summary>The window that has this file open, or null.</summary>
    public OfficeWindow? WindowOf(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return _windows.FirstOrDefault(window => window.Document.FilePath is { } open &&
                                                 string.Equals(open, fullPath, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A second start (or Finder) handed over files: open them, or bring a window to the front when there are none.</summary>
    public void HandleActivation(IReadOnlyList<string> files)
    {
        if (_isExiting)
        {
            return;
        }

        if (files.Count > 0)
        {
            _ = OpenFilesAsync(files);
            return;
        }

        if (_windows.LastOrDefault() is { } window)
        {
            window.BringToFront();
        }
        else
        {
            NewDocumentWindow();
        }
    }

    /// <summary>The setting "Erscheinungsbild": Wie das System, Hell, Dunkel (DESK-02).</summary>
    public static void ApplyTheme(ThemeChoice theme) => MatTheme.SetAppearance(theme switch
    {
        ThemeChoice.Light => MatAppearance.Light,
        ThemeChoice.Dark => MatAppearance.Dark,
        _ => MatAppearance.System,
    });

    /// <summary>
    /// Ends the app once its last window has closed: a downloaded update is installed when automatic updates are on – the
    /// app is closed anyway, nobody is interrupted.
    /// </summary>
    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        if (_desktop is null)
        {
            OnExit();
            return;
        }

        _desktop.Shutdown(0); // raises Exit: OnExit
        OnExit();
    }

    /// <summary>
    /// The app ends – after its last window on Windows, by ⌘Q on macOS or by <see cref="ExitApplication"/>: a downloaded
    /// update is installed when automatic updates are on (unless "Jetzt installieren" started it already).
    /// </summary>
    private void OnExit()
    {
        if (_hasExited)
        {
            return;
        }

        _hasExited = true;
        _isExiting = true;
        UiWatchdog.Stop();
        var update = UpdateService.Downloaded;
        if (!_updateStarted && update is not null && SettingsService.Get().InstallUpdatesAutomatically && UpdateService.CanUpdate)
        {
            try
            {
                // Off the UI thread: its continuations must not wait for the thread that waits here.
                Task.Run(() => UpdateService.StartInstallerAsync(update, restart: false)).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is IOException or BusinessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Warning($"The waiting update could not be started: {exception.Message}");
            }
        }

        _instance?.Dispose();
        Log.Info($"{Definition.Name} ended");
    }

    /// <summary>"Jetzt installieren": every window is closed (each asks about its changes), then the update starts and opens the app again.</summary>
    public async Task<bool> InstallUpdateNowAsync(DownloadedUpdate update)
    {
        foreach (var window in _windows.ToList())
        {
            window.Close();
            await Task.Delay(50);
            while (window.IsVisible && window.OwnedWindows.Count > 0)
            {
                await Task.Delay(100);
            }

            if (_windows.Contains(window))
            {
                return false; // the user kept a document open (Abbrechen)
            }
        }

        await UpdateService.StartInstallerAsync(update, restart: true);
        _updateStarted = true;
        _isExiting = false;
        ExitApplication();
        return true;
    }

    // ===== Start =====

    /// <summary>
    /// Ends the process through the main loop. Shutting down straight from OnFrameworkInitializationCompleted (update, quiet
    /// uninstall, second instance) throws "Dispatcher shut down" because the loop has not started yet.
    /// </summary>
    private void EndProcess(int exitCode) => Dispatcher.UIThread.Post(() => _desktop?.Shutdown(exitCode));

    private void Start(IReadOnlyList<string> files)
    {
        // The app decides when it ends (OnWindowClosed: Windows after the last window, macOS by ⌘Q); either way OnExit runs.
        if (_desktop is not null)
        {
            _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _desktop.Exit += (_, _) => OnExit();
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        UiWatchdog.Start();
        Log.Info($"{Definition.Name} {AppInfo.Version} starting on {UpdateService.PlatformKey}");
        UpdateService.RemoveOldPackages();

        ApplyTheme(SettingsService.Get().Theme);
        SettingsService.Changed += (_, settings) => Ui.Post(() => ApplyTheme(settings.Theme));

        var recovered = RecoverDocuments();
        if (files.Count > 0)
        {
            _ = OpenFilesAsync(files);
        }
        else if (recovered == 0)
        {
            NewDocumentWindow();
        }

        // Crashed or hung last time: say so, with the report.
        Dispatcher.UIThread.Post(() => ProblemReportDialog.ShowUnseen(), DispatcherPriority.Background);

        // macOS: Finder hands opened documents over as an activation; opening the app again shows a window.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, activation) =>
            {
                if (activation is FileActivatedEventArgs opened)
                {
                    var paths = opened.Files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
                    Ui.Post(() => HandleActivation(paths));
                }
                else if (activation.Kind == ActivationKind.Reopen)
                {
                    Ui.Post(() => HandleActivation([]));
                }
            };
        }

        UpdateCoordinator.Start();
        _ = Task.Run(ConnectCloudAtStartAsync);
    }

    /// <summary>
    /// ThixCloud at the start, in the background and silent: the sign-in ThixLauncher left is redeemed (when the app is not signed in),
    /// then the account is asked once (the state: online, offline, signed out by the cloud, switched off).
    /// </summary>
    private static async Task ConnectCloudAtStartAsync()
    {
        if (!AppInfo.Definition.HasCloud)
        {
            return;
        }

        try
        {
            await CloudService.ConnectFromLauncherAsync();
            await CloudService.RefreshAccountAsync();
        }
        catch (BusinessException exception)
        {
            Log.Warning($"ThixCloud at the start: {exception.Message}");
        }
    }

    /// <summary>What a crash left behind comes back as unsaved documents, each in its window; returns how many.</summary>
    internal int RecoverDocuments()
    {
        var count = 0;
        foreach (var info in DocumentService.RecoveredDocuments())
        {
            try
            {
                ShowWindow(DocumentService.Recover(info, CreateDocument));
                count++;
            }
            catch (Exception exception) when (exception is BusinessException or IOException or InvalidDataException or FormatException)
            {
                Log.Warning($"A recovery file could not be opened: {exception.Message}");
                DocumentService.DiscardRecovery(info);
            }
        }

        return count;
    }

    private void OnWindowClosed(OfficeWindow window)
    {
        _windows.Remove(window);
        if (_desktop is not null && ReferenceEquals(_desktop.MainWindow, window))
        {
            _desktop.MainWindow = _windows.FirstOrDefault();
        }

        if (_windows.Count == 0 && _desktop is not null && !KeepsRunningWithoutWindows && LastWindowEndsApp(OperatingSystem.IsMacOS()))
        {
            ExitApplication();
        }
    }

    /// <summary>
    /// Windows: the published exe is its own setup (InstallService):
    ///  - "--uninstall" (from "Installierte Apps"): the uninstall assistant, or silently with "--quiet";
    ///  - "--update" (the update, ThixLauncher): installs itself silently; with "--restart" it starts the app again;
    ///  - "--quiet": installs itself silently with the default choices;
    ///  - started anywhere else than the program folder without a document: the setup assistant.
    /// A development build (not single-file) always runs as the application.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private bool RunSetupIfRequested(string[] args)
    {
        if (args.Contains(InstallService.UninstallArgument))
        {
            if (args.Contains(InstallService.QuietArgument))
            {
                if (InstallService.Uninstall(deleteUserData: false, Log.Info))
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
                return true;
            }

            OfficeSetup.Run(this, isUninstall: true, removeProgramFolder =>
            {
                if (removeProgramFolder)
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
            });
            return true;
        }

        var isUpdate = args.Contains(InstallService.UpdateArgument);
        if (isUpdate || (args.Contains(InstallService.QuietArgument) && !InstallService.IsInstalledExecutable(ExecutablePath)))
        {
            var exitCode = 0;
            try
            {
                InstallService.Install(ExecutablePath, isUpdate && InstallService.IsInstalled ? null : new InstallOptions(DesktopShortcut: false), Log.Info);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
            {
                Log.Error("Silent installation failed", exception);
                Console.Error.WriteLine(exception.Message);
                exitCode = 1;
            }

            if (args.Contains(InstallService.RestartArgument) && File.Exists(InstallService.InstalledExecutable))
            {
                InstallService.StartInstalled();
            }

            EndProcess(exitCode);
            return true;
        }

        if (OfficeProgram.IsSingleFile &&!InstallService.IsInstalledExecutable(ExecutablePath) && DocumentFilesIn(args).Count == 0)
        {
            OfficeSetup.Run(this, isUninstall: false, _ => EndProcess(0));
            return true;
        }

        return false;
    }

    /// <summary>The documents named on the command line (a double click, "Öffnen mit"), as full paths of existing files.</summary>
    internal static IReadOnlyList<string> DocumentFilesIn(IEnumerable<string> args) =>
        args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            .Select(argument =>
            {
                try
                {
                    return Path.GetFullPath(argument);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return null;
                }
            })
            .OfType<string>()
            .Where(File.Exists)
            .ToList();

    /// <summary>
    /// One instance per user and data folder (a named mutex and a pipe; on macOS a lock file and a socket): a second start
    /// hands its files to the first one and ends. On Windows the setup may also ask the running instance to end.
    /// </summary>
    private bool TakeSingleInstance(IReadOnlyList<string> files)
    {
        _instance = new MatSingleInstance(Definition.Name, AppPaths.HasOverride ? AppPaths.DataDirectory : null);
        _instance.SecondStart += (_, args) => Ui.Post(() => HandleActivation(DocumentFilesIn(args)));
        if (!_instance.TryClaim([.. files]))
        {
            Log.Info($"{Definition.Name} runs already: the files were handed to it");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            ListenForExitSignal();
        }

        return true;
    }

    /// <summary>The setup asks a running instance to end (update, uninstall): unsaved changes go into recovery files, then it ends.</summary>
    [SupportedOSPlatform("windows")]
    private void ListenForExitSignal()
    {
        if (AppPaths.HasOverride)
        {
            return; // a development run with its own data folder is none of the setup's business
        }

        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstallService.ExitSignalName);
        var listener = new Thread(() =>
        {
            exitSignal.WaitOne();
            Ui.Post(() =>
            {
                Log.Info("Setup asked the app to end");
                foreach (var window in _windows.ToList())
                {
                    window.CloseKeepingRecovery();
                }

                ExitApplication();
            });
        })
        {
            IsBackground = true,
            Name = "Exit signal listener",
        };
        listener.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled error", e.Exception);
        e.Handled = true;
        var text = e.Exception is BusinessException ? e.Exception.Message : $"{e.Exception.Message} – Details im Protokoll ({AppPaths.LogDirectory}).";
        MatNotification.Show(e.Exception is BusinessException ? Definition.Name : "Unerwarteter Fehler", text, isWarning: true);
    }
}
