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

**Phase 0 (done): the foundation.** The shared core and desktop, the logos of all three apps, CI, and MatDesktop 0.3.0's
MatRibbon, MatBackstage, MatStatusBar and MatQuickAccess in the window.
**Milestone 1a (done): ThixWrite's native editor** on `WordDocument` (docx default, txt), the filled ribbon, find/replace,
page setup, zoom, PDF export (see "ThixWrite's editor" below; gaps in docs/PLAN.md). **Next:** 1b (tables, images, headers
and footers, printing …). Do not build ribbon controls of your own.

Stack: **.NET 10** (`global.json` 10.0.100, latestMinor), C#, **Avalonia 11.3.22** with **MatDesktop 0.3.1** (a copy), xunit +
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
                           AppSettings, RecentDocument(+Item, Order), BusinessException(message, field), Printing
                           (PrinterInfo, PrintRange, PaperSize: label + DEVMODE code + CUPS name, IPrintablePages: sizes, pixel
                           bands, PDF; PrintJob)
  Documents/               OfficeDocument: the UI-free base of every document model (FilePath, FileType, Title, IsDirty, IsPristine,
                           IsRecovered, Undo/Redo, protected Read/Write(Stream), Changed)
  Commands/                OfficeCommand (Id, Label, Icon, Shortcut "Ctrl+S", MacShortcut, CurrentShortcut, Description, Keywords,
                           CanExecute, IsChecked, IsSearchable, ExecuteAsync), OfficeValueCommand : OfficeCommand (Kind Text|Number|
                           Color|Choice, Value, Options, ApplyAsync(value): font, size, colour, style), OfficeCommandRegistry (Add,
                           Find, this[id], ExecuteAsync, ApplyAsync, Search, Invalidate, StateChanged), CommonCommandIds
  Business/                SettingsService, RecentDocuments, DocumentFiles (atomic save, .bak, lock detection, recovery files),
                           DocumentService (Open, Save, SaveAs, TargetPath, recovery), AccessPolicy (LockConfiguration, DisableUpdates,
                           DisableCloud, DisableFileAssociations, DisablePrinting), PrintService (printers, PagesOf "1-3, 5",
                           Print = policy + validation, then the IPrinterSystem), InstallService (Windows self-installing exe), UninstallEntry (what
                           ThixLauncher reads), FileAssociations (ProgIDs, "Öffnen mit", Capabilities; opt-in default only for free
                           types), AutostartService (off by default), UpdateService + MacAppBundle (channel manifest, SHA-256, 6 h,
                           install on exit)
  Infrastructure/          AppInfo (Initialize(definition, appAssembly), Version/Channel/BuildNumber from the app's assembly, Pin for
                           snapshots), AppPaths, JsonFileStore, UserRegistry (IUserRegistry: WindowsUserRegistry | MemoryUserRegistry),
                           TokenStore (DPAPI / Keychain), Log, ProblemReports, FileSystemProbe (snapshots show C:\Users\Anna\…)
  Platform/                IPrinterSystem (+ SamplePrinterSystem for snapshots/tests); Windows/ ShellLink (.lnk through generated
                           COM), GdiPrinterSystem (winspool/gdi32 LibraryImport: pages as Skia pixel bands ≤ 600 dpi via
                           StretchDIBits, paper/orientation per page in the DEVMODE, copies collated here); MacOS/ CupsPrinterSystem
                           (libcups: destinations; the pages as PDF to cupsPrintFile with the media option)
  MatCloud/                COPY of the MatCloud contract (never edited here); cloud sign-in comes in a later phase
src/ThixOffice.Desktop     shared Avalonia UI
  MatDesktop/              COPY of MatDesign's desktop toolkit 0.3.1 (never edited here)
  OfficeApplication        base Application of every app: Definition, CreateDocument, CreateEditor, AddCommands, CreateRibbon(window),
                           CreateBackstageItems(window), SelfTestChecks, SnapshotScenes + ShowSnapshotScene/EndSnapshotScene,
                           FillSampleDocument; start-up (self test, Windows setup modes, single instance forwarding files, recovery,
                           crash reports, theme), NewDocumentWindow, ShowWindow, OpenFilesAsync/OpenFileAsync, HandleActivation,
                           ExitApplication / OnExit (installs a downloaded update); the last window ends the app on Windows, not on
                           macOS (LastWindowEndsApp; ⌘Q ends it, the Dock icon opens a new document); FileDialogs (swappable)
  OfficeProgram            Main helper: Run<TApp>(args, definition); BuildAvaloniaApp<TApp>()
  OfficeSelfTest, OfficeSnapshot, OfficeSetup (MatSetupWindow), UpdateCoordinator, UiWatchdog, Ui (helpers)
  Shell/                   OfficeWindow (MatWindow per document: title bar with logo, word mark, MatQuickAccess, document name +
                           unsaved dot, CommandSearchBox; MatRibbon; message bar; editor; status bar; backstage over it all;
                           keyboard; unsaved prompt; recovery timer), OfficeRibbonBinding + OfficeRibbonItems (see below),
                           OfficeBackstage (MatBackstage with the pages by id; BackstageItem/BackstageContent), OfficeStatusBar
                           (MatStatusBar: texts by id + zoom), OfficeEditor (the app's part; Zoom 10–500 %, OpeningWarning, RequestZoomLevel), CommandSearchBox,
                           IFileDialogs + SystemFileDialogs
  Pages/                   NewPage, OpenPage (Durchsuchen + Zuletzt verwendet: search, sort, pin, show, remove), SaveAsPage,
                           SettingsPage (Allgemein | Speichern | Updates | Erweitert), AboutPage
  Dialogs/                 QuestionDialog (+ SaveChangesDialog: Speichern, Abbrechen, gap, Nicht speichern), ProblemReportDialog
  Controls/                ActionBar, IconLabel, InfoBar, FormErrors (from the reference apps)
src/ThixOffice.Text        (no UI, no native code) ITextMeasurer, FontSpec/FontMetrics, ShapedText, FixedWidthTextMeasurer (tests),
                           Drawing/ display lists (GlyphRunItem, RectangleItem, LineItem in points), Unicode/ LineBreaker (UAX 14),
                           TextBoundaries (graphemes, words)
src/ThixOffice.Text.Skia   SkiaTextMeasurer (SkiaSharp typefaces + HarfBuzz shaping, font fallback), SkiaPageRenderer (display list
                           → any SKCanvas), PdfExport (SKDocument.CreatePdf); SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1 = Avalonia's
src/ThixWrite.Core         (no UI) ThixWriteApp.Definition (.docx Supported + default, .txt Supported, .odt/.rtf Planned);
                           Model/ (Document of immutable blocks, styles, lists, sections, preserved content), Editing/ (EditingSession:
                           selection, every command as a reversible transaction, undo, clipboard fragments, find/replace,
                           GetFormatting), Layout/ (DocumentLayout: incremental line breaking + pagination, hit testing, caret and
                           selection geometry, display lists), Formats/ (docx with the Open XML SDK, plain text),
                           Documents/WordDocument (OfficeDocument: Content + Session; LoadedFormat / SaveFormat), BlankDocument +
                           DocumentFonts (new documents: Aptos → Calibri → Helvetica Neue → Arial, whatever the machine has)
src/ThixWrite              App (OfficeApplication: WordDocument, WriteEditor, commands, ribbon, backstage page, self test, snapshot
                           scenes), Program, WriteSelfTest, Editor/ (WriteEditor, DocumentSurface + CaretLayer + WriteInputClient,
                           PagePictures + PageDrawOperation + FormattingMarks, FindBar, WriteCommands, WriteRibbon + WriteCommandIds,
                           DocumentPalette, WriteFonts, SampleDocument), Dialogs/ (WriteDialog, PageSetupDialog, ZoomDialog),
                           Pages/PrintPage (Datei › Drucken, Strg+P: printer, copies, pages, paper; preview with
                           paging; prints PrintablePages on a worker thread), Pages/ExportPage (Datei › Exportieren: PDF), Assets (rendered by tools/render-icons.js)
tests/ThixOffice.Core.Tests     definition, version, settings, policies, recent list, atomic save, recovery, install / file types /
                                autostart in an in-memory registry (+ the whole setup on Windows), updates, commands, MatCloud copy
tests/ThixOffice.Desktop.Tests  headless with a small TestOfficeApp: shell, search, backstage, keyboard, unsaved prompt, ribbon
                                binding, quick access, zoom,
                                settings + lock, no literal colours, MatDesktop copy
tests/ThixWrite.Core.Tests      model, editing engine (random undo/redo symmetry), layout (fixed-width measurer, 300 pages),
                                docx/txt round trips and fixtures, Unicode rules, Skia measurer and PDF
tests/ThixWrite.Tests           the real ThixWrite headless, driven by real key / text / pointer events: typing, caret movement,
                                selection, formatting shortcuts, ribbon state, lists, mouse, clipboard, IME client, virtualization,
                                zoom, find bar, page setup / zoom dialogs, unsupported-content warning, docx / txt through the
                                window flow, PDF export, recent list, prompts, recovery, snapshots, no literal colours
branding/<id>/             logo-icon, logo-small, logo-document(-small), logo-full SVGs + png/ + .ico; branding/launcher/<id>.png
tools/render-icons.js      renders every app's logos (installed Edge, playwright-core)
scripts/package-macos.sh   <Name>.app per app (Info.plist with CFBundleGetInfoString "<Name> <version>", document types from
                           scripts/macos-document-types/<id>.plist, icns, ad-hoc signature), zip + dmg
.github/workflows/         _desktop.yml (reusable: version → windows → macos → publish), thixwrite.yml (caller)
```

## The ribbon, the backstage, the status bar

- **Ribbon = MatDesktop's `MatRibbonDefinition`, bound to the commands by id.** `OfficeApplication.CreateRibbon(window)` returns a
  NEW definition per window (its items carry the window's state). Item id = command id; build items with `OfficeRibbonItems`
  (`Button(id, size)`, `Toggle(id, groupName, size)`, `ComboBox(id, width, isEditable, options)`, `Number(id, min, max, …)`,
  `ColorPicker(id, palette)`, `Gallery(id, entries)`) or with Mat items whose id is a command id; do not give a bound item a
  callback of its own. `OfficeRibbonBinding.Bind(definition, commands, ribbon)` (the window does it after the quick access put its items into
  `definition.QuickAccess`): fills label /
  icon / shortcut (`Ui.ShortcutText` of `CurrentShortcut`) / tooltip (`Description`) from the command; LEAVES OUT items whose
  command the window does not have and that have no callback, then empty rows, stacks, groups and tabs (`MissingCommandIds`);
  routes `MatRibbonDefinition.Invoked` to `OfficeWindow.RunCommandAsync` (value items – combo, number, colour, gallery – to
  `OfficeValueCommand.ApplyAsync(value)`, numbers invariant); on `OfficeCommandRegistry.StateChanged` and after each use it pushes
  `IsEnabled`, toggle `IsChecked` (= `Checked`) and the values into every item of the id (the definition's
  `SetEnabled`/`SetChecked`); `Reveal(id)` puts the keyboard into an item (the
  search does that for a value command). Contextual tabs: the app calls `RibbonDefinition.ShowContext/HideContext`.
  The definition stays in the app project (not in `<App>.Core`): `MatRibbonModel.cs` is System-only but lives in
  ThixOffice.Desktop's MatDesktop copy and uses `MatText`; Core cannot reference it without a second copy of MatDesktop types.
- **Quick access**: `MatQuickAccess` with three items bound like the ribbon's (Speichern, Rückgängig, Wiederholen; icons save /
  undo / redo).
- **Keys**: the window handles every command shortcut first (tunnel handler; `MatRibbon.HandlesCollapseKey = false`, so Strg+F1
  is only the ToggleRibbon command's; `Ctrl` = ⌘ on
  macOS, `MacShortcut` where the Mac differs: Wiederholen ⇧⌘Z, Speichern unter ⇧⌘S, Menüband ⌥⌘R); the ribbon handles only
  Alt / F10, the key tips and its arrow keys (not while the backstage is open). Undo/redo in a ribbon field stay the field's.
- **Backstage**: `OfficeBackstage` (MatBackstage below the title bar): `Open(pageId)`, `Close()`, `IsOpen`, `CurrentPageId`,
  `CurrentPage`, `OpenChanged`; pages are made anew each time they show. Page ids `OfficeWindow.NewPageId` … `AboutPageId`; Info
  has no entry, the version at the foot opens it (`MatBackstage.ShowPage`). `Open(pageId, byKeyboard)` shows the focus ring only
  when the keyboard opened it (`MatRibbonFileEventArgs.ByKeyboard`; commands open it through `OfficeWindow.ShowBackstage`, which
  knows whether a shortcut ran them).
- **Status bar**: `OfficeStatusBar` (MatStatusBar): `SetItem(id, text, side)`, `RemoveItem`, `TextOf`; its zoom and
  `OfficeEditor.Zoom` (10–500 %, `OnZoomChanged`, `ZoomChanged`) follow each other; commands `view.zoomIn/zoomOut/zoom100`.
- The ribbon's collapsed state is remembered (`AppSettings.IsRibbonCollapsed`, `MatRibbon.CollapsedChanged`).

## ThixWrite's editor

- **Per window:** `WordDocument` (model `Content` + `EditingSession`) → `WriteEditor` (an `OfficeEditor`) makes a
  `DocumentLayout` (shared `WriteFonts.Measurer`, sets `Session.Navigator`) and a `DocumentSurface` in a ScrollViewer; opening a
  file into the window makes a new editor. Everything that changes the document goes through the session; the view only listens.
- **Drawing:** the surface's size is all pages stacked (points × zoom × 96/72, gaps from `LayoutOptions.PageGap`); `Render`
  draws only the pages in the effective viewport: paper (white `DocumentPalette.Paper`, `Mat.Border` + a shadow from
  `Mat.Overlay`) through Avalonia, then ONE custom draw operation (`PageDrawOperation`, Skia lease) that replays each page's
  `SKPicture` (`PagePictures`: recorded from `DocumentLayout.GetDisplayList` with `SkiaPageRenderer`, + `FormattingMarks` when
  ¶ is on; invalidated from `DocumentLayout.Changed.FirstChangedPage`), then the selection (`Mat.Selection`). Vector pictures
  stay crisp at any zoom/DPI. The caret and the IME composition are a separate layer (`CaretLayer`): blinking never redraws pages.
- **Refresh:** session `ContentChanged`/`SelectionChanged` → `ScheduleRefresh` (once, Normal priority): layout update, caret,
  keep the caret in view (`BringIntoView`), notify the IME client. `StateChanged` → `WriteEditor.ScheduleState` (once per round,
  Background priority): `Commands.Invalidate()` (ribbon sync through `GetFormatting()`, cached by the session) and the status bar.
- **Input:** `DocumentSurface.HandleKey` (movement with Shift; Strg/⌥ = word, ⌘←/→ = line, Strg/⌘+Pos1/Ende = document;
  Backspace/Entf, Enter, Umschalt+Enter, Tab in lists), `OnTextInput` (control characters dropped), `WriteInputClient`
  (surrounding text = the caret's paragraph, cursor rectangle, preedit shown by the caret layer), pointer (click, Shift+click,
  drag with auto-scroll, double = word, triple = paragraph, right click keeps a selection, context menu). The window handles
  every command shortcut first (tunnel); commands with `YieldsToTextField` leave their keys to a focused TextBox.
- **Clipboard:** `DataFormat.CreateBytesApplicationFormat("ThixWrite.Fragment")` (the core's docx fragment) + text.
- **Colours:** chrome only Mat tokens; document colours (paper, ink, marks, font/highlight palettes) only in
  `Editor/DocumentPalette.cs` – the one file the colour rules test exempts.

### Adding a command to ThixWrite

1. An id in `WriteCommandIds` (`write.<name>`; value commands note their value format).
2. Register it in `WriteCommands.Add` (or `App.AddCommands`): `OfficeCommand` (or `OfficeValueCommand` for a font, size, colour,
   style, number) with German label, sprite icon, `Shortcut`/`MacShortcut`, `Description`, `Keywords`, `CanExecute`,
   `IsChecked` reading `Format()` (the session's cached formatting; mixed = unchecked/null). Act on `Editor()` at run time.
   A shortcut that a text field also uses (copy, select all) gets `YieldsToTextField = true`.
3. Place it in `WriteRibbon` with `OfficeRibbonItems` (or a Mat item whose id is the command id). Menu entries are commands of
   their own (`IsSearchable = false`). Ids must be unique in the definition (`Problems()`): a second place needs an alias command.
4. A test through real input in tests/ThixWrite.Tests; check the snapshots.

## Build / run / test

- `dotnet build ThixOffice.slnx` – expected: 0 warnings, 0 errors. `dotnet test ThixOffice.slnx` – all green.
- Run: `dotnet run --project src/ThixWrite`; with `THIXWRITE_DATA_DIR=<folder>` an instance of its own (settings, recent list,
  recovery in that folder) that never meets the installed one. A development build never runs the setup.
- **Snapshots:** `dotnet run --project src/ThixWrite -- --snapshot <folder>` draws every window headless in light and dark
  (`thixwrite-main`, `-main-ribbon-collapsed`, `-backstage-{new,open,saveas,settings,settings-save,settings-updates,about}`,
  `-dialog-save-changes`, `-setup-{welcome,options,update,uninstall}` on Windows, and the app's scenes
  (`OfficeApplication.SnapshotScenes`; ThixWrite: `-main-layout`, `-main-view`, `-main-find`, `-dialog-page-setup`,
  `-backstage-print`, `-backstage-export`)): the sample report (`SampleDocument`), version 0.1.0, ANNA-PC, C:\Users\Anna\Dokumente, a sample recent
  list – no real path shows. `THIXWRITE_SCREENSHOTS=<folder> dotnet test tests/ThixWrite.Tests` does the same from the tests.
  LOOK at the pictures after a change of the UI; `docs/images` has the ones of the README.
- The Windows publish (win-x64) is a **trimmed** single file (`TrimMode=partial`, own assemblies + ThixOffice.Text(.Skia) rooted,
  ~43 MB; Avalonia, SkiaSharp, HarfBuzz and the Open XML SDK are trimmed as libraries that declare themselves trimmable – keep
  ThixWrite's own code free of reflection: `XmlFragments.Parse<TParent>`, no `Activator`). `ThixWrite.exe --self-test <report>`
  checks the published program (settings, documents, recovery, manifest, contract, DPAPI, COM, document icon, every window, and
  the app's `SelfTestChecks`: ThixWrite types into a real editor, formats, saves docx, reopens and compares, keeps a table, draws a
  page with Skia, exports a PDF, prints through GDI into "Microsoft Print to PDF" when the machine has it); CI runs it.
- Icons: `cd tools && npm install && node render-icons.js` (`--preview <png>` draws every app at 16–256 px on light and dark – look).
- **MatDesktop** (`src/ThixOffice.Desktop/MatDesktop/`) is a COPY from MatDesign (0.3.1 from branch `feature/ribbon`,
  `src/MatDesktop`, version in `matdesktop.version`): never edit it here; copy again with `node tools/matdesktop-copy.js
  <path>/ThixOffice/src/ThixOffice.Desktop` in MatDesign. `RulesTests.The_copy_of_MatDesktop_is_unchanged` holds it. Icons the
  sprite lacks go into MatDesign's sprite first.
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
