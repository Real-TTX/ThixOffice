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
page setup, zoom, PDF export. **Milestone 1b (done):** printing, tables, pictures, headers/footers/page numbers, links, ruler and
tab stops, the dialogs Schriftart/Absatz, format painter, symbols, HTML paste (see the sections below; gaps in docs/PLAN.md).
**Phase 2, ThixOffice side (done):** ThixCloud sign-in and documents in ThixDrive for every app (section "ThixCloud and ThixDrive").
**Phase 3, first release of ThixCalc (done):** the grid, cell and formula editing, sheet tabs, the German Excel ribbon and dialogs,
find/replace, clipboard, page setup, printing and PDF, CSV/xlsx/xlsm flows (section "ThixCalc's editor"; gaps in docs/PLAN.md).
**Next:** ThixCalc charts; ThixWrite 1c (table of contents, footnotes, comments, spelling, odt). Do not build ribbon controls of your own.

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
                           ManifestFileName <id>-update.json, MacPackageFileName, DataDirectoryVariable <APP>_DATA_DIR, CloudAppId
                           (write/calc/show; null = no cloud) + HasCloud,
                           ScreenshotsVariable, DocumentIconFileName, SupportedTypes, DefaultType, SaveType, SupportedTypeOf),
                           DocumentType (Extension, ProgId, Description, Support Supported|Planned, IsDefault, MimeType, UTI),
                           AppSettings (+ CloudSettings: address, account, device, protected token), DriveOrigin (+ DriveSyncState),
                           RecentDocument (+ Drive: RecentDriveItem, Item, Order), BusinessException(message, field), Printing
                           (PrinterInfo, PrintRange, PaperSize: label + DEVMODE code + CUPS name, IPrintablePages: sizes, pixel
                           bands, PDF; PrintJob)
  Documents/               OfficeDocument: the UI-free base of every document model (FilePath, FileType, Title, IsDirty, IsPristine,
                           IsRecovered, Undo/Redo, protected Read/Write(Stream), Changed; Drive + DriveState + LocationText for a ThixDrive
                           document whose FilePath is its local working copy)
  Commands/                OfficeCommand (Id, Label, Icon, Shortcut "Ctrl+S", MacShortcut, CurrentShortcut, Description, Keywords,
                           CanExecute, IsChecked, IsSearchable, ExecuteAsync), OfficeValueCommand : OfficeCommand (Kind Text|Number|
                           Color|Choice, Value, Options, ApplyAsync(value): font, size, colour, style), OfficeCommandRegistry (Add,
                           Find, this[id], ExecuteAsync, ApplyAsync, Search, Invalidate, StateChanged), CommonCommandIds
  Business/                SettingsService, RecentDocuments, DocumentFiles (atomic save, .bak, lock detection, recovery files),
                           DocumentService (Open, Save, SaveAs, TargetPath, recovery), AccessPolicy (LockConfiguration, DisableUpdates,
                           DisableCloud, DisableFileAssociations, DisablePrinting), PrintService (printers, PagesOf "1-3, 5",
                           Print = policy + validation, then the IPrinterSystem), InstallService (Windows self-installing exe), UninstallEntry (what
                           ThixLauncher reads), CloudService + DriveService (ThixCloud / ThixDrive, see below), FileAssociations (ProgIDs, "Öffnen mit", Capabilities; opt-in default only for free
                           types), AutostartService (off by default), UpdateService + MacAppBundle (channel manifest, SHA-256, 6 h,
                           install on exit)
  Infrastructure/          AppInfo (Initialize(definition, appAssembly), Version/Channel/BuildNumber from the app's assembly, Pin for
                           snapshots), AppPaths, JsonFileStore, UserRegistry (IUserRegistry: WindowsUserRegistry | MemoryUserRegistry),
                           TokenStore (DPAPI / Keychain), Log, ProblemReports, FileSystemProbe (snapshots show C:\Users\Anna\…),
                           AppPaths.DriveDirectory (the working copies of ThixDrive documents)
  Platform/                IPrinterSystem (+ SamplePrinterSystem for snapshots/tests); SampleCloud (a ThixCloud in memory: sign-in,
                           Drive, faults – snapshots, self test, tests); Windows/ ShellLink (.lnk through generated
                           COM), GdiPrinterSystem (winspool/gdi32 LibraryImport: pages as Skia pixel bands ≤ 600 dpi via
                           StretchDIBits, paper/orientation per page in the DEVMODE, copies collated here); MacOS/ CupsPrinterSystem
                           (libcups: destinations; the pages as PDF to cupsPrintFile with the media option)
  MatCloud/                COPY of the MatCloud contract 1.4.0 (never edited here): CloudContract, MatCloudClient(.Drive), CloudHandoff
src/ThixOffice.Desktop     shared Avalonia UI
  MatDesktop/              COPY of MatDesign's desktop toolkit 0.3.1 (never edited here)
  OfficeApplication        base Application of every app: Definition, CreateDocument, CreateEditor, AddCommands, CreateRibbon(window),
                           CreateBackstageItems(window), SelfTestChecks, SnapshotScenes + ShowSnapshotScene/EndSnapshotScene,
                           FillSampleDocument; start-up (self test, Windows setup modes, single instance forwarding files, recovery,
                           crash reports, theme), NewDocumentWindow, ShowWindow, OpenFilesAsync/OpenFileAsync, HandleActivation,
                           ExitApplication / OnExit (installs a downloaded update); the last window ends the app on Windows, not on
                           macOS (LastWindowEndsApp; ⌘Q ends it, the Dock icon opens a new document); FileDialogs (swappable)
  Rendering/               SkiaDrawOperation (the Skia lease of every editor surface; device-pixel mode for the grid), PrintablePages
                           (display lists → print bands / PDF on the app's paper colour)
  OfficeProgram            Main helper: Run<TApp>(args, definition); BuildAvaloniaApp<TApp>()
  OfficeSelfTest, OfficeSnapshot, OfficeSetup (MatSetupWindow), UpdateCoordinator, UiWatchdog, Ui (helpers)
  Shell/                   OfficeWindow (MatWindow per document: title bar with logo, word mark, MatQuickAccess, document name +
                           unsaved dot, CommandSearchBox; MatRibbon; message bar; editor; status bar; backstage over it all;
                           keyboard; unsaved prompt; recovery timer), OfficeRibbonBinding + OfficeRibbonItems (see below),
                           OfficeBackstage (MatBackstage with the pages by id; BackstageItem/BackstageContent), OfficeStatusBar
                           (MatStatusBar: texts by id + zoom), OfficeEditor (the app's part; Zoom 10–500 %, OpeningWarning, RequestZoomLevel), CommandSearchBox,
                           IFileDialogs + SystemFileDialogs
                           OfficeCommandMenu (context menus made of commands), OfficeEditor.ConfirmSaveAsync (the editor may warn before a
                           save: CSV, macros)
  Pages/                   OfficePrintPage (Datei › Drucken: IPrintSource gives pages, scopes, Seite einrichten) + OfficeExportPage (PDF),
  Pages/                   NewPage, OpenPage + SaveAsPage (places: Zuletzt verwendet, ThixDrive, Dieser PC), DriveBrowser (the place
                           ThixDrive, open and save mode), SettingsPage (Allgemein | Speichern | Cloud | Updates | Erweitert), AboutPage
  Dialogs/                 OfficeDialog (heading, body, OK · Abbrechen; every app's dialogs), QuestionDialog (+ SaveChangesDialog: Speichern, Abbrechen, gap, Nicht speichern), DriveDialogs (conflict,
                           replace, pending upload), TextInputDialog, ProblemReportDialog
  Controls/                ActionBar, IconLabel, InfoBar, FormErrors (from the reference apps), CloudSignInPanel, FindBarBase
                           (the Suchen/Ersetzen bar of every app), ITextField (a text field that is no TextBox keeps undo/copy keys)
src/ThixOffice.Text        (no UI, no native code) ITextMeasurer, FontSpec/FontMetrics, ShapedText, FixedWidthTextMeasurer (tests),
                           Drawing/ display lists (GlyphRunItem, RectangleItem, LineItem in points), Unicode/ LineBreaker (UAX 14),
                           TextBoundaries (graphemes, words)
src/ThixOffice.Text.Skia   SkiaTextMeasurer (SkiaSharp typefaces + HarfBuzz shaping, font fallback), SkiaPageRenderer (display list
                           → any SKCanvas), PdfExport (SKDocument.CreatePdf); SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1 = Avalonia's
src/ThixCalc.Core          (no UI) ThixCalcApp.Definition (.xlsx default, .xlsm, .csv; .ods planned; cloud id calc); Model/ (Workbook, Worksheet,
                           sparse CellStore, styles, rows/columns, merges, names, validation, conditional formats, PageSetup), Calculation/
                           + Formulas/ (parser, dependency graph, spills; FormulaTokens: the editor's scanner – colours, point mode, F4,
                           argument position), Functions/ (library + FunctionHelp: German descriptions and argument names), Formatting/,
                           Globalization/ (de-DE, en-US), Formats/ (xlsx with the Open XML SDK incl. page setup, csv with Detect, Html/
                           HtmlTables for the clipboard), Editing/ (EditingSession: every command undoable – input, array formulas,
                           styles, borders, AutoSumme, sort, filter, find, duplicates, text to columns, conditional presets, page setup,
                           print area/titles), Layout/ (GridMetrics, SheetLayout.Viewport by pixels, GridRenderer, SelectionRenderer,
                           SheetPagination), Documents/CalcDocument
src/ThixCalc               App, Program, CalcSelfTest, Editor/ (CalcEditor, GridSurface, FormulaTextBox + CellEditing, FormulaBar,
                           FormulaAssist, SheetTabBar, CalcFindBar, CalcClipboard, CalcCommands / CalcStyleCommands / CalcDataCommands /
                           CalcPageCommands + CalcCommandIds, CalcRibbon, CalcContextMenus, GridPalette, CalcFonts, SampleWorkbook),
                           Dialogs/ (Zellen formatieren, Funktion einfügen, Sortieren, Datenüberprüfung, Text in Spalten, Duplikate,
                           Namens-Manager, Link, Notiz, bedingte Formatierung, Seite einrichten, Zoom, Zellen einfügen/löschen, Blatt
                           verschieben, Textimport), Pages/ (CalcPrintPage, CalcExportPage)
src/ThixWrite.Core         (no UI) ThixWriteApp.Definition (.docx Supported + default, .txt Supported, .odt/.rtf Planned);
                           Model/ (Document of immutable blocks, styles, lists, sections, tables, preserved content), Editing/ (EditingSession:
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
                                autostart in an in-memory registry (+ the whole setup on Windows), updates, commands, MatCloud copy,
                                CloudTests + DriveTests (against SampleCloud), MatCloudIntegrationTests (real MatCloud, env-gated)
tests/ThixOffice.Desktop.Tests  headless with a small TestOfficeApp: shell, search, backstage, keyboard, unsaved prompt, ribbon,
                                ThixDrive (DriveUiTests: places, sign-in, browser modes, title bar state, conflict, closing, Cloud tab),
                                binding, quick access, zoom,
                                settings + lock, no literal colours, MatDesktop copy
tests/ThixCalc.Core.Tests       model, engine, functions, formats, editing, layout, pagination, selection layer
tests/ThixCalc.Tests            the real ThixCalc headless through keys/text/pointer: modes, point mode, F4, suggestions, autofill, resize,
                                headers, wheel, 100k rows, tabs, ribbon state, find, clipboard, dialogs, printing, CSV, snapshots, colours
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
.github/workflows/         _desktop.yml (reusable: version → windows → macos → publish), thixwrite.yml and thixcalc.yml (callers, own
                           path filters: shared projects trigger both, one app's projects only its own)
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

## ThixCloud and ThixDrive (shared by every app)

- **Opt in:** `OfficeAppDefinition.CloudAppId` (ThixWrite `write`, ThixCalc `calc`, ThixShow `show` – MatCloud's `CloudApps`; the Drive grants
  its routes to these ids). Nothing else per app: the places, the browser, the Cloud tab, the title bar state, the save flow, the self test
  check and the snapshots come from ThixOffice.Core/Desktop. Without a cloud id (or under the policy DisableCloud) all of it is hidden.
- **CloudService** (static, Core): `ConnectInBrowserAsync` (code + verification page, polls), `ConnectAsync` (password, used once),
  `ConnectFromLauncherAsync` (`CloudHandoff`, silently at the start in `OfficeApplication.Start`, then `RefreshAccountAsync`),
  `DisconnectAsync`; `Status`/`StatusChanged` (`CloudState`: Off, Disabled, Connecting, Online, Offline, SignedOut = 401, Forbidden =
  403 "von der Verwaltung abgeschaltet"); the token in `TokenStore` under `cloud-<user>@<server>` (Keychain service = the app's bundle id),
  `CloudSettings` in settings.json; `Explain(exception)` turns every failure into a German `BusinessException` and moves the state.
  Tests, snapshots and the self test plug `SampleCloud` in through `CloudService.HandlerForTests`.
- **DriveService** (static, Core): browse (`ListAsync` with sort + paging of `PageSize`, `SearchAsync`, `RecentAsync`, `CreateFolderAsync`,
  `RenameAsync`, `TrashAsync`, `QuotaAsync`, `CheckName` with the cloud's rules, `Matches` = the type filter); `OpenAsync` downloads into
  `AppPaths.DriveDirectory/<account hash>/<item id>/<name>` + `drive.json` (the `DriveOrigin`: server, user, item, parent, name, location,
  ETag, IsUploadPending) – a pending working copy is NEVER replaced by a download; `SaveAsync` = atomic save of the working copy, sidecar
  pending, then `SaveDriveFileAsync` with If-Match → `DriveSaveResult` (`Uploaded`, `NotUploaded` – offline, 5xx, 401, 403 –,
  `ChangedMeanwhile` with `Current`); 413/507/404 throw (still pending); `UploadAsync` (retry), `OverwriteAsync(current)`, `SaveCopyAsync`
  (a new file "Name (2).ext", the document follows it), `SaveAsAsync(folder, name, replacing)` (`NameTaken` → ask, then `replacing`);
  `OpenRecentAsync` (from the Drive, else the working copy); `PendingUploads`. `DocumentService.SaveAs` to a local path makes a Drive
  document local again; `DocumentService.Save` refuses a Drive document (the window routes it).
- **UI (Desktop):** Öffnen / Speichern unter have places (`DocumentPlace`: Zuletzt verwendet, ThixDrive, Dieser PC) as a row of
  choices above the content (a column left too little width for the list's actions). `DriveBrowser(window, mode)`: toolbar (search across
  the Drive, type filter = the app's types by default, sort), breadcrumbs, list (icon, name, modified, size), "Weitere laden", in the save
  mode file name + type, actions Öffnen/Speichern · Neuer Ordner · Umbenennen · gap · In den Papierkorb; not signed in it shows the
  compact `CloudSignInPanel` (the same panel is the Cloud tab's sign-in). `OfficeApplication.OpenFromDriveAsync/OpenRecentAsync`,
  `OfficeWindow.SaveIntoDriveAsync/RetryUploadAsync/ResolveConflictAsync`; Strg+S on a Drive document runs `DriveService.SaveAsync` (one
  upload at a time, `OfficeWindow.Upload`), a waiting upload is retried every 30 s and when the document opens; closing waits for a
  running upload and asks about a waiting one. `CloudIndicator` at the right of the title bar (Mat tokens): nothing / quiet cloud /
  "In ThixDrive" / "Wird hochgeladen …" / "Nicht hochgeladen" (click = retry) / "Konflikt" (click = the question) / "Offline" / "Abgemeldet".
- **Decisions:** conflict = Als Kopie speichern (primary, default: nobody's work is replaced) · Später · gap · Überschreiben (danger: the
  other version stops being current, it stays a version). Name taken = Anderen Namen wählen · gap · Ersetzen. Closing with a waiting upload
  = Erneut versuchen · Abbrechen · gap · Trotzdem schließen (uploaded at the next open). No backstage entry "Konto": like the reference
  apps the account lives in Einstellungen › Cloud (the title bar state opens it).

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

### Tables

- **Model:** a table is NOT a block. Its cells' paragraphs are ordinary `Paragraph`s in `Document.Blocks`, in reading order (row
  by row, cell by cell), each with `Paragraph.Cell` = `CellRef(Table, Row, Cell)`; the immutable `Table` record holds the
  structure (`TableProperties`, grid widths in points, rows with `TableRowProperties` and `TableCellProperties`: spans, vertical
  merges, borders, shading, margins, alignment; unknown XML kept). A table = the run of blocks naming the same instance
  (`TableSpan.At/All`, `Problems()` for the invariants). Text edits keep the cell (ContentEdits); a structure edit
  (`TableEdits`: load a `TableModel`, change, `Emit` a new Table) replaces the table's blocks in ONE `BlocksStep`, so undo is
  exact. Cells covered by a vertical merge keep a hidden paragraph the caret skips (`CaretStops.IsCaretBlock`).
- **Selections:** `TableSelection`: text inside one cell is a text selection; across cells or over the table's edge it is a
  selection of WHOLE cells (`TableCellRange` rectangle of grid columns × rows): highlighted as cells, formatted per cell
  (`TableSelection.Ranges`), cleared per cell by Entf / typing (`ClearWholeCells`), a table inside the selection goes as a whole,
  copied as a sub-table. Backspace / Entf never join across a cell edge.
- **Layout:** `DocumentLayout.Tables.cs`: the first block of a table places the whole table (geometry from the grid, the
  resolved table style `StyleResolver.ResolveTable`, cell text widths, row heights, vertical merges); rows split at line
  boundaries across pages unless cantSplit / merged; heading rows repeat (`PageLayout.RepeatedLines`, `PlacedCell.IsRepeat`).
  `PageLayout.Cells` carry rectangles, borders and fills for drawing and hit testing. Paragraphs in cells get the table style's
  paragraph/character properties between the defaults and their own style (`EffectiveParagraphFormat.StyleKey`).
- **UI:** Einfügen › Tabelle (`TablePicker`, a `MatRibbonCustom` through `OfficeApplication.CreateRibbonContent`; "Tabelle
  einfügen …"), contextual tabs Tabellenentwurf / Layout (`TableCommands`, context `table`, shown by `WriteEditor` while the caret is
  in a table), dialogs InsertTable / SplitCells / DeleteCells, column borders dragged in `DocumentSurface`.

### Pictures

- **Model:** `ImageData` (the bytes of a PNG/JPEG/GIF/BMP, format, pixel size and resolution read from the header – the core
  decodes nothing; equal by SHA-256 `Key`) inside an `ImageInline` (one object character, size in points, alternative text;
  `SourceXml`/`SourcePart`: the w:drawing as read, written back unchanged while the picture is; `IsFloating`: a wp:anchor,
  shown in the text flow, noted in `UnsupportedContent` as FloatingDrawing). Session: `InsertImage` (fitted to the text or cell
  width, then selected), `SelectedImage`, `SetImageSize`, `ResetImageSize`, `SetImageDescription`, `ReplaceImage`.
- **Drawing:** the layout makes an `ObjectItem` as tall as the picture; the display list has an `ImageItem` (key + bytes);
  `SkiaPageRenderer` decodes each key once (screen, PDF, printing alike).
- **docx:** `DocxPictures` (read pic:pic with one a:blip from its image part; write new pictures as wp:inline with one media part
  per picture and unique wp:docPr ids).
- **UI:** Einfügen › Bilder (`IFileDialogs.PickPictureAsync`, `ImageFiles.Read` in the business layer), paste of a bitmap, a
  picture file dropped on the page, a click selects a picture (frame + eight handles; corners keep the proportions, Umschalt frees
  them), contextual tab Bildformat (`PictureCommands`: Alternativtext, Bild ändern, Größe zurücksetzen, Anordnen, Höhe/Breite).

### Headers, footers, fields

- **Model:** `SectionProperties.Headers/Footers` (`HeaderFooterSet`: Default, First, Even; a kind a section lacks comes from the section
  before) of `Story` (immutable blocks, equal by content, `SourcePart` = the part it was read from); `DocumentSettings.EvenAndOddHeaders`,
  `SectionProperties.TitlePage`; `Document.HeaderFooterOf(section, pageInSection, pageNumber, footer)` picks a page's story.
  `FieldInline` (PAGE, NUMPAGES, DATE) shows its `Result` in the body and the page's numbers where the layout sets
  `LayoutContext.FieldText`; other fields stay preserved.
- **Layout:** `DocumentLayout.HeadersFooters.cs` places every page's header (from the header distance) and footer (up to the footer
  distance) after each update (`PageLayout.HeaderLines/FooterLines`, story layouts cached per page number); the body is not pushed
  down by a tall header (gap).
- **docx:** header/footer references read into stories (`DocxPropertyReader.ReadSection` with `ReadContext.ReadStory`, the part's own
  relationships for links and pictures), written back into the part they came from or new parts (`WriteContext.ReferencesOf`);
  `DocxFields` collapses w:fldSimple and complex PAGE/NUMPAGES/DATE fields, writes w:fldSimple.
- **Editing:** a double click in the margin above / below the text (or Einfügen › Kopfzeile / Fußzeile) opens the page's story in
  place: `WriteEditor.EditHeaderFooter` makes a story document (the body's styles) with its own session, layout (fields show that
  page) and a `DocumentSurface { IsStory = true }` laid over the page in an overlay canvas; the body is dimmed (`DimBody`), the tab
  Kopf- und Fußzeile shows, `WriteEditor.Session` is the story's (the commands act there). Esc, a double click on the body or
  "Schließen" put the story into the document as ONE step (`SetHeaderFooter`); saving commits first (`WordDocument.Writing`).

### Links, dialogs, format painter, symbols, ruler

- **Links:** `Hyperlink` on inlines; session `SetHyperlink` / `RemoveHyperlink` / `LinkRange` (`ContentEdits.MapInlines`), the
  character style "Hyperlink"; `HyperlinkDialog` (Strg+K); Strg+Klick and "Link öffnen" go through `LinkService` (business: only
  http, https, mailto; never files or scripts), then `Ui.OpenExternal`; the surface shows the address as a tooltip.
- **Dialogs:** `FontDialog` (Strg+D, launcher of Start › Schriftart), `ParagraphDialog` (launcher of Start › Absatz; tabs Einzüge und
  Abstände | Zeilen- und Seitenumbruch | Tabstopps), both one undo step; `SymbolDialog` and Einfügen › Symbol's frequent symbols.
- **Format übertragen** (Strg+Umschalt+C): `WriteEditor.ToggleFormatPainter`; the next pointer selection (`SelectedByPointer`) gets the
  characters' format (and the paragraph's when whole paragraphs are selected).
- **Ruler** (Ansicht › Lineal, `Ruler` above the pages): margins, indents and tab stops of the caret's paragraph; dragging markers
  changes them, a click sets a left tab stop, a tab dragged off is removed (positions snap to 1/16 cm).

### Paste from other programs

- `HtmlImport` (ThixWrite.Core/Formats/Html): the clipboard's HTML (Windows "HTML Format" with its header – `FromClipboardFormat` –,
  macOS public.html) → a `DocumentFragment` (styles and lists along); a small tolerant parser; scripts, styles, frames, objects,
  forms, comments dropped; links only http/https/mailto; pictures only from data: URIs (nothing is fetched). Paste order in
  `WriteEditor.PasteAsync`: ThixWrite's fragment, HTML, text, a bitmap. RTF is not read (gap).

### Adding a command to ThixWrite

1. An id in `WriteCommandIds` (`write.<name>`; value commands note their value format).
2. Register it in `WriteCommands.Add` (or `App.AddCommands`): `OfficeCommand` (or `OfficeValueCommand` for a font, size, colour,
   style, number) with German label, sprite icon, `Shortcut`/`MacShortcut`, `Description`, `Keywords`, `CanExecute`,
   `IsChecked` reading `Format()` (the session's cached formatting; mixed = unchecked/null). Act on `Editor()` at run time.
   A shortcut that a text field also uses (copy, select all) gets `YieldsToTextField = true`.
3. Place it in `WriteRibbon` with `OfficeRibbonItems` (or a Mat item whose id is the command id). Menu entries are commands of
   their own (`IsSearchable = false`). Ids must be unique in the definition (`Problems()`): a second place needs an alias command.
4. A test through real input in tests/ThixWrite.Tests; check the snapshots.

## ThixCalc's editor

- **Per window:** `CalcDocument` (Workbook + EditingSession) → `CalcEditor` (an `OfficeEditor`): `FormulaBar` above, `GridSurface` with two
  `ScrollBar`s and an overlay canvas (the cell's `FormulaTextBox`, `FormulaAssist`), below `SheetTabBar` beside the horizontal scrollbar,
  `CalcFindBar` over the grid. One `GridMetrics` per workbook (measurer `CalcFonts.Grid` = a `CachingTextMeasurer` over Skia), one
  `SheetLayout` per sheet. Everything that changes the workbook goes through the session; the views follow `CellsChanged` /
  `SelectionChanged` / `StateChanged` (the ribbon and status bar once per dispatcher round, `ScheduleState`).
- **Drawing:** `GridSurface.Render` lays the viewport out in DEVICE pixels (`SheetLayout.Viewport(width × scaling, height × scaling,
  zoom × scaling, scrollX, scrollY)`), records `GridRenderer.Render`'s display list into an `SKPicture` (only when content in view, the
  scroll, size, zoom or theme changed – a change outside the visible rows does not redraw) and draws it plus `SelectionRenderer`'s layer
  (fill, border, fill handle, coloured reference frames, AutoFill preview), the copy marquee and a resize line through
  `SkiaDrawOperation(devicePixels: true)`: grid lines stay one sharp pixel at every zoom/DPI. Scrolling is by pixels (rows may be cut at
  the pane edge; the renderer clips panes and headers); `View.TopLeft` follows the first whole row/column. The scrollbars span the used
  range + the view + room (`ScrollExtent`). Numbers: a frame of the budget sheet ≈ 1 ms, 100k rows the same (only visible rows are laid out).
- **Colours:** chrome from Mat tokens (headers, selection border = accent); document colours (sheet white also in dark mode, grid lines,
  reference colours, palettes, cell styles, CF presets, the sample) only in `Editor/GridPalette.cs` – the file the colour test exempts.
- **Editing (`CellEditing`):** two `FormulaTextBox`es (cell and formula bar) show the same text. Typing in the grid starts Eingeben (the
  grid keeps the keyboard and forwards keys/text to the cell's field – `GridInputClient` lets an IME compose there); F2 / double click /
  a click in the formula bar = Bearbeiten; in a formula where `FormulaTokens.CanInsertReference` says so, arrows and clicks point
  references (Zeigen; Umschalt extends, drag selects a range, other sheets via the tabs), F4 cycles `$`. References are coloured in the
  text and framed on the grid in `GridPalette.ReferenceColors`. `FormulaAssist`: the function list while a name is typed (Tab takes
  one), else the argument tip (`FormulaTokens.ArgumentAt` + `FunctionHelp`). Eingabe/Tab (+Umschalt) commit and move within the
  selection, Strg+Eingabe fills the selection, Strg+Umschalt+Eingabe makes an array formula, Esc cancels; Excel closes open
  parentheses. A `BusinessException` (bad formula, validation) is explained and the cell stays open. Commands that change cells call
  `Editing.Commit()` first. The grid and the fields implement `ITextField` so the window's undo/copy keys reach the field while editing.
- **Keys** (Bereit): arrows, Strg+arrows (data edge), Umschalt extends, Pos1, Strg+Pos1/Ende, Bild auf/ab (Alt: sideways), Tab/Eingabe
  within the selection, F2, Entf, Rücktaste, Strg/Umschalt+Leertaste; command shortcuts are listed in `CalcCommands`' summary (German
  Excel: Strg+Umschalt+F/K/U, Strg+1, Strg+U/R fill, Strg+Umschalt+4/5/1 formats, Strg+Bild ab/auf sheets, Umschalt+F3, Strg+F3, F9 …;
  the Mac keeps ⌘B/⌘I/⌘U, ⌥⌘←/→ for sheets, ⌘⇧K/J for grouping so ⌥-arrows stay word movement).
- **Clipboard (`CalcClipboard`):** own format (a token naming the process's `ClipboardContent`: exact cells, formulas, styles, merges) +
  tab text + an HTML table (`HtmlTables.Export`, Windows' "HTML Format" envelope); paste order own → HTML table (`HtmlTables.Import`) →
  text (`PasteText`). Inhalte einfügen = `PasteOptions`.
- **Printing:** `SheetPagination` (core) cuts the print area / used range / selection into pages by the sheet's `PageSetup` (scale or fit
  N × M pages, title rows, order) and renders each in points (header/footer codes); `CalcPages` feeds the shared `OfficePrintPage`
  (scopes Aktives Blatt / Arbeitsmappe / Auswahl) and `OfficeExportPage`.
- **Files:** `CalcDocument` sniffs zip vs CSV; a CSV keeps its bytes and `CsvDetection` – ambiguous separator/encoding opens the
  `CsvImportDialog` (reads again, not dirty). `CalcEditor.ConfirmSaveAsync` asks before CSV drops sheets/formulas and before .xlsx drops
  macros; `OpeningWarning` names unsupported content and says that macros never run but stay.

### Adding a command to ThixCalc

1. An id in `CalcCommandIds` (`calc.<name>`; menu entries `calc.<menu>.<entry>`; value commands note their value).
2. Register it in `CalcCommands` / `CalcStyleCommands` / `CalcDataCommands` / `CalcPageCommands`: German label, sprite icon,
   `Shortcut`/`MacShortcut` (no collision with text editing on the Mac), `Description`, `Keywords`, `CanExecute` (usually `Ready`: no cell
   being edited), `IsChecked` from `Session.ActiveStyle`/the sheet. Act through `With(session => …)` (ends the input first). The logic
   belongs in `EditingSession` (core, undoable, UI-free) – the command only calls it.
3. Place it in `CalcRibbon` (and `CalcContextMenus` when it belongs to a right click). A test through real input in tests/ThixCalc.Tests.

### Adding a function

1. In ThixCalc.Core/Functions (the thematic file): `registry.Add("NAME", min, max, kinds, body)`; the German name in
   `GermanFunctionNames`; newer than Excel 2007 → `FutureFunctions` (written with _xlfn.).
2. Its help in `FunctionHelp.German` (description, argument names as German Excel names them, `[optional]`, repeats).
3. Tests in tests/ThixCalc.Core.Tests/Functions; the function then shows in the list while typing, the argument tip, Funktion einfügen
   and the Formeln tab's category menu (the most used per category).

## Build / run / test

- `dotnet build ThixOffice.slnx` – expected: 0 warnings, 0 errors. `dotnet test ThixOffice.slnx` – all green.
- ThixCalc the same way: `dotnet run --project src/ThixCalc` (`THIXCALC_DATA_DIR`), snapshots `-- --snapshot <folder>` (scenes
  `-main-formula`, `-main-view`, `-main-find`, `-dialog-format-cells`, `-dialog-function`, `-dialog-page-setup`, `-backstage-print` on the
  budget sample `SampleWorkbook`), `THIXCALC_SCREENSHOTS=<folder> dotnet test tests/ThixCalc.Tests`; its trimmed publish (~46 MB) runs
  `--self-test` (typed values and German formulas incl. SVERWEIS/SUMMEWENN, format, sort, xlsx round trip, a Skia frame, PDF, GDI print).
- Run: `dotnet run --project src/ThixWrite`; with `THIXWRITE_DATA_DIR=<folder>` an instance of its own (settings, recent list,
  recovery in that folder) that never meets the installed one. A development build never runs the setup.
- **Snapshots:** `dotnet run --project src/ThixWrite -- --snapshot <folder>` draws every window headless in light and dark
  (`thixwrite-main`, `-main-ribbon-collapsed`, `-backstage-{new,open,saveas,settings,settings-save,settings-updates,about}`,
  `-dialog-save-changes`, `-setup-{welcome,options,update,uninstall}` on Windows, and the app's scenes
  (`backstage-open-signin`, `-backstage-open-drive`, `-backstage-saveas-drive`, `-backstage-settings-cloud`, `-main-drive-pending`,
  `-dialog-drive-conflict` for an app with a cloud id, against SampleCloud) and the app's scenes
  (`OfficeApplication.SnapshotScenes`; ThixWrite: `-main-layout`, `-main-view`, `-main-find`, `-dialog-page-setup`,
  `-main-table`, `-main-picture`, `-main-header`, `-backstage-print`, `-backstage-export`)): the sample report (`SampleDocument`), version 0.1.0, ANNA-PC, C:\Users\Anna\Dokumente, a sample recent
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
- **MatCloud contract** (`src/ThixOffice.Core/MatCloud/`) is a COPY (1.4.0): run `node tools/contract-copy.js <path>/ThixOffice/src/ThixOffice.Core`
  inside MatCloud; `CopyTests` holds it.
- **Integration run against a real MatCloud:** a THROWAWAY instance only (own compose project name, own port, tmpfs for /data –
  `uid=1654` – and for the database, `MATCLOUD_ADMIN_USER/PASSWORD` create the account; never the ports 4950–4952 or the volumes of a
  local instance; `docker compose down -v --rmi local` afterwards), then `THIXOFFICE_MATCLOUD_URL=… THIXOFFICE_MATCLOUD_USER=…
  THIXOFFICE_MATCLOUD_PASSWORD=… dotnet test tests/ThixOffice.Core.Tests --filter MatCloudIntegrationTests` (without them it does nothing).

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
   without a reader are `Planned`; `UntitledBaseName` "Mappe", `BlankDocumentLabel` "Leere Arbeitsmappe", `CloudAppId = CloudApps.Calc` for ThixDrive) and the document model
   (derive from `OfficeDocument`).
2. `src/ThixCalc`: copy ThixWrite's csproj / app.manifest / App.axaml / Program.cs; `App : OfficeApplication` with
   `CreateDocument`, `CreateEditor` (an `OfficeEditor`), `AddCommands`, `Ribbon`; `Program.Main => OfficeProgram.Run<App>(…)`.
3. Major/Minor already exist in `Directory.Build.props` (`ThixCalcMajor/Minor`); add the projects to `ThixOffice.slnx`, a test
   project `tests/ThixCalc.Tests`, and `InternalsVisibleTo` entries in ThixOffice.Core / ThixOffice.Desktop if the tests need them.
4. Logos exist (`branding/thixcalc`); `node tools/render-icons.js` fills `src/ThixCalc/Assets` once the project exists.
5. `scripts/macos-document-types/thixcalc.plist`; `.github/workflows/thixcalc.yml` = a copy of thixwrite.yml with its paths,
   app name/id, project and tests.
6. Launcher: catalog entry, logo `branding/launcher/thixcalc.png` (on a branch of ThixLauncher, see PLAN section 6).
