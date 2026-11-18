# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.
The binding plan is `docs/PLAN.md` (German): architecture (section 4), phases (section 5), launcher (section 6).

## What ThixOffice is

An office suite of **separate native desktop apps** for **Windows and macOS** in one repository: **ThixWrite** (Word's place,
accent `#185ABD`), **ThixCalc** (Excel, `#107C41`) and **ThixShow** (PowerPoint, `#C43E1C`). Windows users find everything
where Word/Excel/PowerPoint have it (ribbon, "Datei" = backstage, quick access, "Was möchten Sie tun?"), and the apps look like
the other Thix apps (MatDesktop, "Apple-like"). Office formats are the default (docx/xlsx/pptx), ODF follows per app. Each
document is a window of its own, all in one process per app (like Word). UI language German (the Office apps say **"Sie"**,
like "Was möchten Sie tun?"); code, comments and identifiers English.

**Phase 0 (done): the foundation.** The shared core and desktop, the ThixWrite skeleton with a PLACEHOLDER editor ("Editor folgt",
a plain text box on `.txt`, so that new / open / save / recent / recovery / unsaved prompt / file activation are real), the
logos of all three apps, CI. **Next:** MatRibbon, MatBackstage and MatStatusBar come as MatDesktop 0.3.0 from MatDesign (a
colleague builds them) and drop into the slots below; then phase 1a, ThixWrite's native editor and docx. Do not build a ribbon
of your own and do not grow the placeholders.

Stack: **.NET 10** (`global.json` 10.0.100, latestMinor), C#, **Avalonia 11.3.22** with **MatDesktop 0.2.0** (a copy), xunit +
Avalonia.Headless. Reference apps (sibling folders): ThixCalendar (structure, setup, OTA), ThixNote (document app: file types,
update on exit), ThixClip (`--snapshot`), ThixFred (CI that publishes Windows even when macOS fails). Never use, reference or
copy anything from MatOffice.

## Map

```
ThixOffice.slnx, Directory.Build.props (versioning, Major/Minor PER APP), global.json
src/ThixOffice.Core        (no UI) shared by every app; every service is parameterized by the app's OfficeAppDefinition
  Models/                  OfficeAppDefinition (id, name, accent, description, DocumentTypes, UntitledBaseName, BlankDocumentLabel;
                           derived: Suffix, ExecutableName, SetupFileName, UninstallKey, PolicyKey, MacBundleId info.schmoldt.<id>,
                           ManifestFileName <id>-update.json, MacPackageFileName, DataDirectoryVariable <APP>_DATA_DIR,
                           ScreenshotsVariable, DocumentIconFileName, SupportedTypes, DefaultType, SaveType, SupportedTypeOf),
                           DocumentType (Extension, ProgId, Description, Support Supported|Planned, IsDefault, MimeType, UTI),
                           AppSettings, RecentDocument(+Item, Order), BusinessException(message, field)
  Documents/               OfficeDocument: the UI-free base of every document model (FilePath, FileType, Title, IsDirty, IsPristine,
                           IsRecovered, Undo/Redo, protected Read/Write(Stream), Changed)
  Commands/                OfficeCommand (Id, Label, Icon, Shortcut "Ctrl+S", Description, Keywords, CanExecute, IsChecked,
                           IsSearchable, ExecuteAsync), OfficeCommandRegistry (Add, Find, this[id], ExecuteAsync, Search, Invalidate,
                           StateChanged), CommonCommandIds, RibbonLayout / RibbonTab / RibbonGroup / RibbonItem (+ RibbonItemSize)
  Business/                SettingsService, RecentDocuments, DocumentFiles (atomic save, .bak, lock detection, recovery files),
                           DocumentService (Open, Save, SaveAs, TargetPath, recovery), AccessPolicy (LockConfiguration, DisableUpdates,
                           DisableCloud, DisableFileAssociations), InstallService (Windows self-installing exe), UninstallEntry (what
                           ThixLauncher reads), FileAssociations (ProgIDs, "Öffnen mit", Capabilities; opt-in default only for free
                           types), AutostartService (off by default), UpdateService + MacAppBundle (channel manifest, SHA-256, 6 h,
                           install on exit)
  Infrastructure/          AppInfo (Initialize(definition, appAssembly), Version/Channel/BuildNumber from the app's assembly, Pin for
                           snapshots), AppPaths, JsonFileStore, UserRegistry (IUserRegistry: WindowsUserRegistry | MemoryUserRegistry),
                           TokenStore (DPAPI / Keychain), Log, ProblemReports, FileSystemProbe (snapshots show C:\Users\Anna\…)
  Platform/Windows/        ShellLink (.lnk through generated COM)
  MatCloud/                COPY of the MatCloud contract (never edited here); cloud sign-in comes in a later phase
src/ThixOffice.Desktop     shared Avalonia UI
  MatDesktop/              COPY of MatDesign's desktop toolkit 0.2.0 (never edited here)
  OfficeApplication        base Application of every app: Definition, CreateDocument, CreateEditor, AddCommands, Ribbon,
                           FillSampleDocument; start-up (self test, Windows setup modes, single instance forwarding files, recovery,
                           crash reports, theme), NewDocumentWindow, ShowWindow, OpenFilesAsync/OpenFileAsync, HandleActivation,
                           ExitApplication (installs a downloaded update), InstallUpdateNowAsync; FileDialogs (swappable)
  OfficeProgram            Main helper: Run<TApp>(args, definition); BuildAvaloniaApp<TApp>()
  OfficeSelfTest, OfficeSnapshot, OfficeSetup (MatSetupWindow), UpdateCoordinator, UiWatchdog, Ui (helpers)
  Shell/                   OfficeWindow (MatWindow per document: title bar with logo, word mark, quick access, document name + unsaved
                           dot, CommandSearchBox; ribbon slot; message bar; editor; status bar slot; backstage over it all; keyboard;
                           unsaved prompt; recovery timer), OfficeEditor (the app's part), ShellParts.cs (IOfficeRibbon,
                           IOfficeBackstage + BackstageItem/BackstageContent, IOfficeStatusBar, OfficeShellParts factories),
                           TemporaryCommandBar / TemporaryBackstage / TemporaryStatusBar (PLACEHOLDERS), CommandSearchBox,
                           IFileDialogs + SystemFileDialogs
  Pages/                   NewPage, OpenPage (Durchsuchen + Zuletzt verwendet: search, sort, pin, show, remove), SaveAsPage,
                           SettingsPage (Allgemein | Speichern | Updates | Erweitert), AboutPage
  Dialogs/                 QuestionDialog (+ SaveChangesDialog: Speichern, Abbrechen, gap, Nicht speichern), ProblemReportDialog
  Controls/                ActionBar, IconLabel, InfoBar, FormErrors (from the reference apps)
src/ThixWrite.Core         ThixWriteApp.Definition; Documents/TextDocument (PHASE 0 ONLY: plain text, typing undo)
src/ThixWrite              App (OfficeApplication), Program, Editor/PlaceholderEditor (PHASE 0 ONLY), Editor/WriteRibbon (layout +
                           "Datum und Uhrzeit"), Assets (rendered by tools/render-icons.js)
tests/ThixOffice.Core.Tests     definition, version, settings, policies, recent list, atomic save, recovery, install / file types /
                                autostart in an in-memory registry (+ the whole setup on Windows), updates, commands, MatCloud copy
tests/ThixOffice.Desktop.Tests  headless with a small TestOfficeApp: shell, search, backstage, keyboard, unsaved prompt, slots,
                                settings + lock, no literal colours, MatDesktop copy
tests/ThixWrite.Tests           the real ThixWrite headless: new/type/save, open/replace/bring to front, recent list, prompts,
                                recovery, second start, refusal of docx, commands, snapshots, no literal colours
branding/<id>/             logo-icon, logo-small, logo-document(-small), logo-full SVGs + png/ + .ico; branding/launcher/<id>.png
tools/render-icons.js      renders every app's logos (installed Edge, playwright-core)
scripts/package-macos.sh   <Name>.app per app (Info.plist with CFBundleGetInfoString "<Name> <version>", document types from
                           scripts/macos-document-types/<id>.plist, icns, ad-hoc signature), zip + dmg
.github/workflows/         _desktop.yml (reusable: version → windows → macos → publish), thixwrite.yml (caller)
```

## The slots for MatRibbon, MatBackstage, MatStatusBar (MatDesktop 0.3.0)

Every document window takes its three parts from `OfficeShellParts.CreateRibbon / CreateBackstage / CreateStatusBar` (today the
`Temporary*` placeholders). When the copy of MatDesktop 0.3.0 is in, write one small adapter per part and set the factory once
(in `OfficeApplication.Initialize`); windows, pages, commands and tests stay.

- `IOfficeRibbon`: `View`, `SelectedTabId`, `IsCollapsed`, `event FileTabInvoked` (opens the backstage), `event CollapsedChanged`,
  `Bind(RibbonLayout layout, OfficeCommandRegistry commands)`. The layout is data (`OfficeApplication.Ribbon`); every item names a
  command id; the ribbon runs `OfficeWindow.RunCommandAsync(source, command)` and follows `OfficeCommandRegistry.StateChanged`
  for `IsEnabled` / `Checked`.
- `IOfficeBackstage`: `View`, `IsOpen`, `CurrentPageId`, `CurrentPage`, `event OpenChanged`, `Configure(BackstageContent)`,
  `Open(pageId)`, `Close()`. `BackstageContent(Items, Settings, About, Version, Channel)`; `BackstageItem(Id, Label, Icon,
  CreatePage | Run, Shortcut)`. Page ids: `OfficeWindow.NewPageId` … `AboutPageId`; About opens from the version at the foot.
- `IOfficeStatusBar`: `View`, `SetItem(id, text, StatusBarSide)`, `RemoveItem(id)`, `TextOf(id)`; the editor writes it
  (`OfficeEditor.UpdateStatus`).

## Build / run / test

- `dotnet build ThixOffice.slnx` – expected: 0 warnings, 0 errors. `dotnet test ThixOffice.slnx` – all green.
- Run: `dotnet run --project src/ThixWrite`; with `THIXWRITE_DATA_DIR=<folder>` an instance of its own (settings, recent list,
  recovery in that folder) that never meets the installed one. A development build never runs the setup.
- **Snapshots:** `dotnet run --project src/ThixWrite -- --snapshot <folder>` draws every window headless in light and dark
  (`thixwrite-main`, `-main-ribbon-collapsed`, `-backstage-{new,open,saveas,settings,settings-save,settings-updates,about}`,
  `-dialog-save-changes`, `-setup-{welcome,options,update,uninstall}` on Windows): version 0.1.0, ANNA-PC, C:\Users\Anna\Dokumente,
  a sample recent list – no real path shows. `THIXWRITE_SCREENSHOTS=<folder> dotnet test tests/ThixWrite.Tests` does the same
  from the tests. LOOK at the pictures after a change of the UI; `docs/images` has the ones of the README.
- The Windows publish (win-x64) is a **trimmed** single file (`TrimMode=partial`, own assemblies rooted, ~35 MB).
  `ThixWrite.exe --self-test <report>` checks the published program (settings, documents, recovery, manifest, contract, DPAPI,
  COM, document icon, every window); CI runs it.
- Icons: `cd tools && npm install && node render-icons.js` (`--preview <png>` draws every app at 16–256 px on light and dark – look).
- **MatDesktop** (`src/ThixOffice.Desktop/MatDesktop/`) is a COPY from MatDesign (branch `feature/desktop-and-services`,
  `src/MatDesktop`, version in `matdesktop.version`): never edit it here; copy again with `node tools/matdesktop-copy.js
  <path>/ThixOffice/src/ThixOffice.Desktop` in MatDesign. `RulesTests.The_copy_of_MatDesktop_is_unchanged` holds it. Icons the
  sprite lacks go into MatDesign's sprite first (today: undo/redo are `restore` and `restore` mirrored).
- **MatCloud contract** (`src/ThixOffice.Core/MatCloud/`) is a COPY: `node ../MatCloud/tools/contract-copy.js src/ThixOffice.Core`;
  `CopyTests` holds it. ThixCloud sign-in (app ids write/calc/show) comes in a later phase.

## Rules

- **Microsoft C# coding conventions**, file-scoped namespaces, `_camelCase` fields, human-readable names, early returns, no
  needless nesting.
- **Business layer = static classes** in ThixOffice.Core. UI and models never touch files, the registry or HTTP: pages call
  `DocumentService`, `RecentDocuments`, `SettingsService`, `FileAssociations` …; file dialogs only return paths. Validation **and
  permission checks** (AccessPolicy) live in the business layer. Errors are `BusinessException(message, field)`; the UI shows them
  at the `MatFormField` whose `controls:FormErrors.Field` is that field, or in a message.
- **Documents are never lost:** save atomically (temporary file, then replace), recovery files while unsaved, ask before closing
  (Speichern | Abbrechen | gap | Nicht speichern), never open a format the app cannot read (planned types are not registered and
  are refused), never take a file type from another program silently (FileAssociations: "Öffnen mit"; default only opt-in and only
  for free types; the system's "Standard-Apps" decide). Updates install when the app closes.
- **UI rules:** lists get a toolbar above (search, filter, sort); list actions below, left-aligned, positive → negative,
  delete-like actions with a gap (`controls:ActionBar.Separated`); buttons in one row; icons where they help; tabs for areas of
  one item; hide what does not apply; tiles keep their distance; About opens from the version bottom left.
  **Colours only from MatDesktop** (`{DynamicResource Mat.*}` in XAML, `control.Use(property, "Mat.TextMuted")` in code); the one
  literal is the app's accent (`ThixWriteApp.Accent`); the rules tests fail on any other. Icons only from the sprite
  (`<mat:MatIcon Icon="save"/>`); icon-only buttons get `AutomationProperties.Name` and a tooltip with the shortcut (`Ui.Tooltip`).
- Shared controls have ONE code base (ThixOffice.Desktop); several instances on one page must work (no static state in controls).
- Platforms: one code base, differences behind `OperatingSystem.IsWindows()/IsMacOS()`. macOS cannot be checked from the Windows
  machine: CI builds and tests it.
- Edit text files with the editor tools or Node / `[IO.File]` with UTF-8: Windows PowerShell 5.1 `Get-Content`/`Set-Content`
  double-encode umlauts.

## Versioning, branches, CI

- `Directory.Build.props` holds Major/Minor **per app** (`<ThixWriteMajor>`, `<ThixWriteMinor>` …; one element per line, CI and
  package-macos.sh read them with sed). A project belongs to the app its name starts with; the shared projects are "ThixOffice"
  and read the version of the app that starts them (`AppInfo.Initialize`). CI sets `Channel`, `BuildNumber` (the run number of the
  app's own workflow) and `BuildDate`: release `0.1.42-20261118` (main), nightly `nightly-43-20261118` (dev), local `local-20261118`.
  AssemblyMetadata: `ThixOfficeAppId`, `ThixOfficeChannel`, `ThixOfficeBuildNumber`, `ThixOfficeRepository` (Real-TTX/ThixOffice).
- Branches: `main` = release, `dev` = nightly. **Commits are authored by Matthias Schmoldt only – no co-author trailers, no other
  names, no "generated with" lines.** Small logical commits; never push to main without the owner; no manual releases.
- `.github/workflows/_desktop.yml` (reusable, `workflow_call`: app-name, app-id, project, tests, build-number): version → Windows
  (test, publish win-x64, self test, `<App>Setup.exe`) → macOS (test, publish osx-arm64 + osx-x64, package-macos.sh) → publish
  (manifest `<id>-update.json`; rolling releases `release-channel` / `nightly-channel` SHARED by all apps – only the app's own
  files are uploaded with `--clobber`, the release keeps its title; one concurrency group `thixoffice-publish` for every app;
  versioned release `<id>-v<version>` on main). Windows publishes even when macOS fails.
- Launcher (docs/PLAN.md section 6): per app id, manifest, `<App>Setup.exe`, `<App>-macOS-<arch>.zip/.dmg`, bundle id; the uninstall
  entry has DisplayVersion / InstallLocation / DisplayIcon / UninstallString / QuietUninstallString; macOS `CFBundleGetInfoString`
  is "<App> <version>"; `branding/launcher/<id>.png` is the catalog logo.

## Adding an app to the suite (ThixCalc, ThixShow)

1. `src/ThixCalc.Core`: `ThixCalcApp.Definition` (id `thixcalc`, name `ThixCalc`, accent, description, document types – formats
   without a reader are `Planned`; `UntitledBaseName` "Mappe", `BlankDocumentLabel` "Leere Arbeitsmappe") and the document model
   (derive from `OfficeDocument`).
2. `src/ThixCalc`: copy ThixWrite's csproj / app.manifest / App.axaml / Program.cs; `App : OfficeApplication` with
   `CreateDocument`, `CreateEditor` (an `OfficeEditor`), `AddCommands`, `Ribbon`; `Program.Main => OfficeProgram.Run<App>(…)`.
3. Major/Minor already exist in `Directory.Build.props` (`ThixCalcMajor/Minor`); add the projects to `ThixOffice.slnx`, a test
   project `tests/ThixCalc.Tests`, and `InternalsVisibleTo` entries in ThixOffice.Core / ThixOffice.Desktop if the tests need them.
4. Logos exist (`branding/thixcalc`); `node tools/render-icons.js` fills `src/ThixCalc/Assets` once the project exists.
5. `scripts/macos-document-types/thixcalc.plist`; `.github/workflows/thixcalc.yml` = a copy of thixwrite.yml with its paths,
   app name/id, project and tests.
6. Launcher: catalog entry, logo `branding/launcher/thixcalc.png` (on a branch of ThixLauncher, see PLAN section 6).
