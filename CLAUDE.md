# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.
The binding plan is `docs/PLAN.md` (German): architecture (section 4), phases (section 5), launcher (section 6).

## What ThixOffice is

An office suite of **separate native desktop apps** for **Windows and macOS** in one repository: **ThixWrite** (Word's place,
accent `#185ABD`), **ThixCalc** (Excel, `#107C41`), **ThixShow** (PowerPoint, `#C43E1C`) and **ThixContacts** (Outlook's people, `#8764B8`; a
workspace app, see "The workspace mode") and **ThixMail** (Outlook's mail, `#0F6CBD`; a workspace app, section "ThixMail"). Windows users find everything
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
**ThixCalc charts (done):** the shared `ThixOffice.Charts` (section "Charts"). **Phase 7a, ThixContacts (done, first version):** the
workspace mode of the shell, `ThixOffice.Pim` (+ `.Desktop`), ThixContacts on MatCloud's general sync (contract 1.5.0; section "ThixContacts"). **Phase 7b, ThixMail foundation (done, first version):**
IMAP/SMTP accounts with auto-configuration, offline store and sync, reading pane, composer, Outlook's ribbon and keys (section "ThixMail"). **Phase 7c,
ThixMail's Mail | Kalender | Personen (done, first version):** the calendar and the people of the ThixCloud account inside ThixMail, invitations (section
"ThixMail", "Calendar and people in ThixMail"). **Phase 4, first version of ThixShow (done):** the
slide editor, thumbnails, sorter, show and presenter view, the German PowerPoint ribbon, printing/PDF (section "ThixShow's editor";
gaps in docs/PLAN.md). **ThixWrite 1c (done):** footnotes/endnotes, table of contents, comments, spelling (shared seam), Referenzen/Überprüfen, odt. **ODF for ThixCalc (done):** .ods read and write (section "OpenDocument Spreadsheet"). Next the
launcher entry and ThixWrite 1d (gaps in docs/PLAN.md). Do not build ribbon controls of
your own (a picture gallery is a `RibbonFlyoutButton` through `CreateRibbonContent`).

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
                           DocumentType (Extension, ProgId, Description, Support Supported|Planned|Export, IsDefault, MimeType, UTI;
                           DocumentType.Pdf), ExportTypes (written as a copy only: PDF in every app) + SaveAsTypes + ExportTypeOf,
                           AppSettings (+ CloudSettings: address, account, device, protected token; AppValues: an app's small remembered
                           choices – SettingsService.AppValue/RememberAppValue, no lock like the window size), DriveOrigin (+ DriveSyncState),
                           RecentDocument (+ Drive: RecentDriveItem, Item, Order), BusinessException(message, field), Printing
                           (PrinterInfo, PrintRange, PaperSize: label + DEVMODE code + CUPS name, IPrintablePages: sizes, pixel
                           bands, PDF; PrintJob)
  Formats/Odf/             what every OpenDocument format shares (BCL only): OdfXml (namespaces as …Ns, Points/Length/Centimetres/Inches,
                           Percent, colours, ISO durations and dates, PlainText / TextContent of text:p), OdfFont (text-properties both
                           ways), OdfPackage (reader: HasMimeType, Read(stream, German message, …) turns every ZIP/XML failure into
                           InvalidDataException, Xml/Bytes of parts), OdfPackageWriter (mimetype first and stored, parts, AddPicture once
                           per content, the manifest of every part last, Meta)
  Documents/               OfficeDocument: the UI-free base of every document model (FilePath, FileType, Title (virtual), IsDirty, IsPristine,
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
                           ThixLauncher reads), CloudService + DriveService (ThixCloud / ThixDrive, see below), FileAssociations (ProgIDs, "Öffnen mit",
                           Capabilities for every supported type; the extension's default only for the types chosen in the setup – Register(…,
                           defaults), null = keep), InstallerOptions (SETUP-08 of MatDesign: the app's src/<App>/installer.json – embedded in the
                           app's assembly (AppInfo.AppAssembly), and as "installer" in the update manifest – says what the setup offers: desktop
                           shortcut, automatic updates, the supported types (own format on); no autostart; the command line --quiet [--start] /
                           --update [--restart] / --configure [--desktop-shortcut=yes|no --automatic-updates=yes|no --file-types=.a,.b|none|default],
                           a wrong option or value = exit code 2, nothing installed; Defaults/Current/Apply; Models/InstallerConfig;
                           Platform/MacOS/MacFileAssociations: Launch Services for the chosen types), AutostartService (off by default), UpdateService + MacAppBundle (channel manifest, SHA-256, 6 h,
                           install on exit)
  Infrastructure/          AppInfo (Initialize(definition, appAssembly), Version/Channel/BuildNumber from the app's assembly, Pin for
                           snapshots), AppPaths, JsonFileStore, UserRegistry (IUserRegistry: WindowsUserRegistry | MemoryUserRegistry),
                           TokenStore (DPAPI / Keychain), Log, ProblemReports, FileSystemProbe (snapshots show C:\Users\Anna\…),
                           AppPaths.DriveDirectory (the working copies of ThixDrive documents)
  Platform/                IPrinterSystem (+ SamplePrinterSystem for snapshots/tests); SampleCloud (a ThixCloud in memory: sign-in,
                           Drive, faults, a long poll of at most 2 s – snapshots, self test, tests); Windows/ ShellLink (.lnk through generated
                           COM), GdiPrinterSystem (winspool/gdi32 LibraryImport: pages as Skia pixel bands ≤ 600 dpi via
                           StretchDIBits, paper/orientation per page in the DEVMODE, copies collated here); MacOS/ CupsPrinterSystem
                           (libcups: destinations; the pages as PDF to cupsPrintFile with the media option)
  MatCloud/                COPY of the MatCloud contract 1.5.0 (never edited here): CloudContract, MatCloudClient(.Drive), CloudHandoff
src/ThixOffice.Desktop     shared Avalonia UI
  MatDesktop/              COPY of MatDesign's desktop toolkit 0.3.1 (never edited here)
  OfficeApplication        base Application of every app: Definition, CreateDocument, CreateEditor, AddCommands, CreateRibbon(window),
                           CreateBackstageItems(window), SelfTestChecks, SnapshotScenes + ShowSnapshotScene/EndSnapshotScene,
                           FillSampleDocument; start-up (self test, Windows setup modes, single instance forwarding files, recovery,
                           crash reports, theme), NewDocumentWindow, ShowWindow, OpenFilesAsync/OpenFileAsync, HandleActivation,
                           ExitApplication / OnExit (installs a downloaded update); the last window ends the app on Windows, not on
                           macOS (LastWindowEndsApp; ⌘Q ends it, the Dock icon opens a new document); FileDialogs (swappable)
  Rendering/               SkiaDrawOperation (the Skia lease of every editor surface; device-pixel mode for the grid), PrintablePages
                           (display lists → print bands / PDF on the app's paper colour, Grayscale), OfficeFonts (THE measurer of the
                           process: Skia + a cache for the UI thread; CalcFonts/WriteFonts are names for it)
  Shell/PdfSource          the pages + measurer of a PDF; OfficeEditor.Pdf gives it (every app): "Speichern unter" › PDF (local and
                           ThixDrive: DocumentService.ExportTypeOf → OfficeWindow.ExportToAsync / DriveService.ExportAsync) and the
                           command file.saveAsPdf "Als PDF speichern" write a COPY – the document keeps file, type and dirty state;
                           the message bar says "Als PDF gespeichert" with "Öffnen" (InfoBar action)
  OfficeProgram            Main helper: Run<TApp>(args, definition); BuildAvaloniaApp<TApp>()
  OfficeSelfTest, OfficeSnapshot, OfficeSetup (MatSetupWindow; Options/Choices from installer.json: a switch per type "type:.ext"),
                           UpdateCoordinator, UiWatchdog, Ui (helpers); OfficeApplication.ClaimInstance (a second start hands its documents
                           to the running instance – tested per app in InstallOptionsTests)
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
                           (the Suchen/Ersetzen bar of every app), ITextField (a text field that is no TextBox keeps undo/copy keys),
                           SelectionHandles (the eight handles 0–7 clockwise from the top left: positions, hit, Resize math with
                           proportions, cursors turned with the frame, drawing of frames/handles in the accent – ThixWrite's pictures,
                           ThixCalc's charts, ThixShow's shapes), TablePicker (Einfügen › Tabelle's grid; the app's insert callback),
                           RibbonFlyoutButton (a ribbon button whose flyout the app builds on opening: picture galleries)
  Pages/OfficePrintPage    IPrintSource may add a second choice (Variants/Variant/VariantLabel: ThixShow's Farbe/Graustufen)
src/ThixOffice.Text        (no UI, no native code) ITextMeasurer, FontSpec/FontMetrics, ShapedText, FixedWidthTextMeasurer (tests),
                           FontSubstitutes (a missing font → metric-compatible, then look-alike families; SkiaTextMeasurer takes the
                           first installed one for measuring AND drawing – documents keep their font names),
                           Drawing/ display lists in points: DisplayList.cs (RgbColor, GlyphRunItem, RectangleItem, LineItem, ImageItem,
                           ClipBegin/End) + VectorItems.cs = THE vector layer of every app (VectorPath + VectorPathBuilder with arcs as
                           Béziers, Matrix2D, paints – solid, linear/radial gradient, picture –, StrokeStyle with dashes/caps/joins,
                           DropShadow, VectorPathItem (+ Solid(…) for plain colours, Translated), TransformBegin(Matrix)/End (+ At(x, y,
                           rotation, scale)), ClipPathBegin, OpacityBegin/End); no second path or transform type, Unicode/ LineBreaker (UAX 14),
                           TextBoundaries (graphemes, words); Drawing/Geometry/ = THE preset geometry engine of every app (GeometryDefinition,
                           PresetGeometries: 126 presets, GeometryEvaluator: guide formulas → paths, text rectangle, handles; ThixShow's
                           GeometryEngine adapts its ShapeGeometry); Drawing/ColorTransforms (ColorValue: DrawingML colour transforms as Office
                           computes them – lumMod/lumOff in HSL, tint/shade in linear RGB; ThixShow's ColorResolver delegates)
src/ThixOffice.Charts      (no UI) charts for every app (section "Charts"): Model/ (Chart, ChartPlot, ChartSeries, ChartAxis, ChartLegend,
                           DataLabels, ChartColor + ChartColors (theme accents, colour sets), ChartTemplates (+ ChartFactory: Excel's
                           new charts), ChartEdits (pure element changes)), Data/ (IChartDataSource, IChartValueFormatter,
                           ChartDataResolver → ResolvedChart), Layout/ (AxisScaling, ChartRenderer → display items, ChartCanvas),
                           Formats/ (ChartXml reads c:chartSpace, ChartXmlWriter writes it; unknown XML kept)
src/ThixOffice.Text.Skia   SkiaTextMeasurer (SkiaSharp typefaces + HarfBuzz shaping, font fallback), SkiaPageRenderer = the ONE renderer
                           of every display item incl. the vector layer (any SKCanvas: editor surfaces, bitmaps via RenderPng, PDF,
                           print bands; .Vectors.cs: paths, paints, strokes, shadows), PdfExport (SKDocument.CreatePdf);
                           SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1 = Avalonia's
src/ThixOffice.Pim         (no UI) what ThixContacts and ThixMail share: Model/Person (+ PersonGroup: key, the contract's ContactData, time), Rules/
                           (PersonRules, PersonDuplicates, VCard 2.1/3.0/4.0, PersonCsv – a port of MatCloud's module Contacts, held to it by the shared
                           fixtures tests/ThixOffice.Pim.Tests/Fixtures/Contacts = MatCloud's tests/MatCloud.Tests/Fixtures/Contacts), Store/ (PimStore:
                           SQLite via Microsoft.Data.Sqlite, migrations by PRAGMA user_version, items of the general sync with IsDirty/Revision, pictures,
                           cursor; PimStores: one file per app and account, the local one copied into a new account's; PersonItems: persons/groups <-> items,
                           Suggest for address fields), Sync/PimSync (push dirty, pull above the cursor, last writer wins, tombstones, pictures up/down,
                           the loop with long polls, retry 5 s to 5 min, 401 stops, 403 waits; ?app= for a granted app), Calendar/ (ThixCalendar's items of
                           the general sync: CalendarModel – CalendarAccountEntry, CalendarEntry, CalendarAppointment –, CalendarItems: read/serialize with
                           the contract's records and CloudContract.Json, BYTE FOR BYTE what ThixCalendar writes (it compares hashes), own calendars writable,
                           cloud-* read-only; CalendarBook: calendars + appointments of a store, Between/Conflicts, EnsureOwnCalendar/AddOwnCalendar, Save/
                           Delete with ThixCalendar's checks, KeyOfInvitation, meetings sent from here remembered in the store's meta; ICalendar: RFC 5545
                           reader/writer, folding, TZID by Windows/IANA name or VTIMEZONE; Invitations: iTIP REQUEST/REPLY/CANCEL, AppointmentOf an answer)
src/ThixOffice.Pim.Desktop shared views: PersonAvatar (picture or initials), PersonCard (the read-only card), AddressField (suggestions, for ThixMail);
                           People/ = Outlook's "Personen" ONCE for ThixContacts and ThixMail: PeopleView (folders | list + search + letters | card, on a
                           Func<ContactsSession>, remembered choices under a prefix), PeopleCommands (+ PeopleCommandOptions: e-mail, sync, print, keys;
                           every command acts only while its view is given), PeopleRibbon (Create = ThixContacts' ribbon, Tabs(context) = ThixMail's
                           contextual tabs), ContactsCommandIds, CategoryColors, ContactDialog + ContactsDialogs; Calendar/ = CalendarView (toolbar, the
                           calendars, Tag/Arbeitswoche/Woche/Monat as canvases of blocks, OpenRequested/NewRequested), AppointmentDialog (the host's save:
                           the book or a meeting), CalendarPalette (the ONE file turning the calendars' own colours – user data – into brushes; exempted)
src/ThixContacts.Core      (no UI) ThixContactsApp.Definition (Kind Workspace, .vcf as import type, cloud id contacts), ContactsSession (persons and
                           groups in memory, Query: search/filter/sort, every change one undoable step, groups, categories, duplicates, merge, import with
                           pictures, export), ContactsDocument (the workspace document: the session of the account's store, PimSync while signed in,
                           ReconnectAsync on sign-in/out, StopSyncAsync when the cloud ends it), ContactFiles (read an import file, write an export atomically)
src/ThixContacts           App (workspace: CreateDocument = the store, ImportFilesAsync for handed-over .vcf), Editor/ (ContactsEditor: hosts the shared
                           PeopleView + status bar, sign-in changes, the signed-out message; ContactsCommands = PeopleCommands with Drucken and Sync;
                           SampleContacts), Pages/ (ContactsPrintPage: Liste | Karten through OfficePrintPage, ContactsTransferPage, ContactsPdfPage;
                           PrintPalette = the one file with literal colours), ContactsSelfTest
src/ThixMail.Core          (no UI) ThixMailApp.Definition (Kind Workspace, .eml, cloud id mail); Model/MailModel (MailAccount without passwords,
                           ServerSettings, MailFolder + FolderRole, MessageHeader, MessageFlags, MailSignature, MailCategory); Accounts/ (AccountStore:
                           accounts/signatures/categories as items of a PimStore – collections ready for the general sync, protected passwords only in its
                           local meta; AccountService: validation, passwords in the TokenStore, TestAsync, TrustCertificate; AutoConfig: providers,
                           autoconfig/.well-known/ISPDB/MX/SRV/guess; DnsLookup: SRV and MX over UDP); Transport/ (IMailTransport + IImapConnection/
                           ISmtpConnection = THE protocol seam, MailKitTransport (MailKit 5 / MimeKit.Core), SampleMailServer (in memory: folders,
                           flags, MODSEQ, MOVE, IDLE wake-ups, faults, latency, an untrusted certificate), MailConnectionException + MailErrorKind +
                           CertificateInfo); Store/MailStore (SqliteDatabase of ThixOffice.Pim: folders, headers, threads by References/In-Reply-To +
                           subject, bodies as files, FTS5, operations queue, outbox, recipients, trusted senders, size budget); Sync/MailSync (one per
                           account: replay → folders → outbox → folders by UIDVALIDITY/UIDNEXT/CONDSTORE or flag polling → IDLE; back-off; bodies on a
                           second connection); Mime/ (MailMime: parse/bytes/summary, code pages registered by a module initializer; MailReader → ReadMail;
                           MailComposer: Draft → MIME, Reply/Forward like Outlook); Security/MailHtml (the view: HtmlImport in mail mode, remote pictures
                           only after consent, DescribeLink; RemotePictures; AttachmentSafety); MailSession (every action undoable and queued),
                           MessageList (rows: date groups, conversations), MailDocument (the workspace document; Pim), EmlFiles, MailPim (the account's
                           contacts – ThixContacts' ContactsSession – and calendar – CalendarBook – in stores of this app, PimSync with ?app=contacts and
                           ?app=calendar while signed in, ReconnectAsync), MailInvitations (View of a text/calendar part with conflicts, Answer: calendar +
                           REPLY through the outbox, RemoveCancelled, SendMeeting/DeleteAppointment: REQUEST/CANCEL); Draft.Calendar → text/calendar part
src/ThixMail               App (workspace), Editor/ (MailEditor: navigation rail | folder pane | list | MessageView, MailEditor.Views: MailArea Mail/Calendar/
                           People, ShowView, the shared CalendarView/PeopleView or the sign-in, contextual ribbon contexts "calendar"/"people", the invitation
                           bar's answers; MailRows: FolderRow, MessageRow, CategoryTokens; MessageView: header, "Bilder laden", the invitation bar,
                           attachments, body = read-only DocumentSurface; MessageWindow; MailCommands + MailCommandIds (Outlook's keys; act only in the view
                           E-Mail), CalendarCommands (+ CalendarCommandIds: Strg+1/2/3, the calendar's commands and tabs, the people commands), MailRibbon,
                           MoveDialog), Compose/ComposeWindow (own commands + ribbon, AddressField, DocumentSurface
                           editor, HtmlExport on send), Pages/ (AccountsPage + AccountForm, MailSettings: the four settings tabs + SignaturesPanel,
                           MailPages: printing/PDF of a message), MailNotifier (IMailNotifier seam), SampleMailbox, MailSelfTest
src/ThixWrite.Desktop      ThixWrite's text surface shared with ThixMail: DocumentSurface (+ CaretLayer, WriteInputClient; IsStory, IsReadOnly), PagePictures +
                           FormattingMarks, WriteFonts, DocumentPalette (the exempted colour file of ThixWrite and ThixMail), SpellingSupport
                           (spelling as you type for any surface: queue, wavy lines, context menu entries), ClipboardHtml (HTML of other programs)
src/ThixCalc.Core          (no UI) ThixCalcApp.Definition (.xlsx default, .xlsm, .csv, .ods; cloud id calc); Model/ (Workbook, Worksheet,
                           sparse CellStore, styles, rows/columns, merges, names, validation, conditional formats, PageSetup), Calculation/
                           + Formulas/ (parser, dependency graph, spills; FormulaTokens: the editor's scanner – colours, point mode, F4,
                           argument position), Functions/ (library + FunctionHelp/FunctionHelpTexts: German descriptions and argument names), Formatting/,
                           Globalization/ (de-DE, en-US), Formats/ (xlsx with the Open XML SDK incl. page setup, Ods/ (OpenDocument), csv with Detect, Html/
                           HtmlTables for the clipboard), Editing/ (EditingSession: every command undoable – input, array formulas,
                           styles, borders, AutoSumme, sort, filter, find, duplicates, text to columns, conditional presets, page setup,
                           print area/titles, charts), Layout/ (GridMetrics, SheetLayout.Viewport by pixels, GridRenderer,
                           SelectionRenderer, SheetPagination, ChartLayer), Charts/ (WorkbookChartSource, CalcChartFormatter,
                           ChartSources: series from a range), Model/SheetCharts (ChartAnchor, SheetChart, ChartReferences), Model/Pivot (PivotCache,
                           PivotTable, PivotEngine, PivotStyles), Formats/Xlsx/XlsxPivots,
                           Formats/Xlsx/XlsxDrawings, Documents/CalcDocument
src/ThixCalc               App, Program, CalcSelfTest, Editor/ (CalcEditor, GridSurface, FormulaTextBox + CellEditing, FormulaBar,
                           FormulaAssist, SheetTabBar, CalcFindBar, CalcClipboard, CalcCommands / CalcStyleCommands / CalcDataCommands /
                           CalcPageCommands / CalcChartCommands + CalcCommandIds, GridSurface.Charts, CalcRibbon, CalcContextMenus, GridPalette, CalcFonts, SampleWorkbook),
                           Dialogs/ (Zellen formatieren, Funktion einfügen, Sortieren, Datenüberprüfung, Text in Spalten, Duplikate,
                           Namens-Manager, Link, Notiz, bedingte Formatierung, Seite einrichten, Zoom, Zellen einfügen/löschen, Blatt
                           verschieben, Textimport), Pages/ (CalcPrintPage, CalcExportPage)
src/ThixWrite.Core         (no UI) ThixWriteApp.Definition (.docx Supported + default, .odt and .txt Supported, .rtf Planned);
                           Model/ (Document of immutable blocks, styles, lists, sections, tables, preserved content), Editing/ (EditingSession:
                           selection, every command as a reversible transaction, undo, clipboard fragments, find/replace,
                           GetFormatting), Layout/ (DocumentLayout: incremental line breaking + pagination, hit testing, caret and
                           selection geometry, display lists), Formats/ (docx with the Open XML SDK + DocxNotes/DocxComments/DocxToc, Odt/, plain text),
                           Documents/WordDocument (OfficeDocument: Content + Session; LoadedFormat / SaveFormat), BlankDocument +
                           DocumentFonts (new documents: Aptos → Calibri → Helvetica Neue → Arial, whatever the machine has)
src/ThixShow.Core          (no UI) ThixShowApp.Definition (.pptx default, .pptm, .odp planned; cloud id show); Model/ (immutable Presentation:
                           masters → layouts → slides, themes, shape tree – AutoShape, ConnectorShape, PictureShape, GroupShape, GraphicFrame
                           with Table / chart seam / kept SmartArt+OLE, PreservedElement –, fills, outlines, effects, text bodies, notes,
                           sections, transitions, animation timing; ValueList = equality by content; XmlFragment = kept XML with PartReferences;
                           BuiltInThemes + MasterFactory: ThixShow's own designs and layouts; AnimationPresets), Drawing/ (ColorResolver: scheme
                           colours + transforms; GeometryEngine + PresetGeometries: guide formulas, 126 presets, custom geometry, handles),
                           Formatting/ (SlideContext, ShapeResolver: placeholder inheritance, theme styles, text style chain), Layout/
                           (SlideRenderer → display lists, TextBodyLayouter with normAutofit, TableRenderer, Paints, PrintLayouts: slides /
                           handouts / notes pages, SlideHitTesting: hits, corners, handles; IChartRenderer = the chart seam, filled by
                           default by SharedChartRenderer: the part from the kept package → ChartXml.Read → cached values → ChartRenderer
                           in the theme's accents and body font; the part itself is never rewritten), Formats/Pptx
                           (PptxReader/PptxWriter: the original package is kept, only changed parts/elements are rewritten; unchanged elements
                           are written as read and relinked), Editing/ (EditingSession: every command one undo step – the presentation before
                           and after –, typing/nudging groups, Group(label, action), PresentationChanged per slide/shape; Snapping, TextEdits,
                           ShowFragment = clipboard; charts travel with their model: EmbedCharts), Show/ (SlideTimeline: build steps;
                           EffectEvaluator; SlideShow: clicks + clock → ShowFrame layers with opacity/transform/clip, presenter view),
                           Documents/ShowDocument; Layout/TextGeometry (a text body placed on the slide: CaretLine, HitTest, SelectionQuads,
                           TargetAt for shapes and table cells), Model/ShapeCatalog (the shapes gallery by PowerPoint's categories),
                           SlideTitles, TextScaling (Passend skalieren); Editing/ HeaderFooterSettings + SetHeaderFooter/HeaderFooterOf (date/number/
                           footer placeholders from the layout, fields), CropPicture (the frame follows the cut sides), AddAnimationByParagraph;
                           Show/ParagraphMask + SlideTimeline.ParagraphLayers (paragraph effects get layers of their own, other paragraphs
                           drawn transparent so nothing moves); GraphicFrame.Chart = a chart made/changed in ThixShow (written into a part of its own:
                           IPartWriter.ChartId), EditingSession.InsertChart/SetChart, CountMatches
src/ThixShow               App, Program, ShowSelfTest, Editor/ (ShowEditor: normal view = SlideListView pane | SlideCanvas + SlideOverlay over
                           the pasteboard | NotesPane, AnimationPane at the right; sorter = SlideListView in grid mode; ReadingView;
                           SlideCanvas(.Input/.Keys: pointer drags, snapping, draw mode, text keys, IME via SlideInputClient),
                           SlidePictures (SKPicture per slide), ShowCommands + ShowDrawingCommands + ShowDesignCommands +
                           ShowContextCommands (contextual tabs, context menus, table/size commands) + ShowChartCommands (+ ChartDataDialog),
                           ShowCommandIds, ShowRibbon, ShowGalleries (layouts, designs strip, shapes, table picker), ShowClipboard,
                           ShowFindBar, ShowPalette, SampleDeck), Show/ (ShowController: SlideShow on a clock, 60 fps timer only while
                           animating; ShowSurface; ShowNavigation: PowerPoint's keys; SlideShowWindow; PresenterWindow), Pages/ (ShowPages:
                           print layouts, ShowPrintPage, ShowExportPage), Dialogs/ (Zoom, Link, Abschnitt)
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
tests/ThixOffice.Pim.Tests      the shared vCard/CSV fixtures, the store (migrations), the sync against SampleCloud (two computers, conflicts,
                                tombstones, offline, 401/403, pictures, the loop, local to account), suggestions
tests/ThixContacts.Core.Tests   the session on a real store (rules, undo, groups, merge, import/export, 50,000 contacts), the document's sync
tests/ThixContacts.Tests        the real ThixContacts headless: workspace shell, Strg+N/F2/Entf/Strg+E, folders, letters, groups, import (vcf, CSV mapping),
                                export, print, sync and sign-out, merge, snapshots, no literal colours
tests/ThixMail.Core.Tests       EmbeddingTests (7c against SampleCloud + SampleMailServer: two computers, a contact in ThixContacts' model,
                                the invitation round trip, meetings, 401/403), MatCloudEmbeddingIntegrationTests (env-gated, a real MatCloud), the sync against SampleMailServer (CONDSTORE and polling, moves, expunges, UIDVALIDITY, queue replay, outbox, drafts,
                                IDLE, reconnect), threading, FTS5 search, 200,000 messages, the 15 fixture mails (Fixtures/, made by make-fixtures.ps1),
                                composing/replying, the sanitizer as a table of attacks, auto-configuration, accounts and passwords
tests/ThixMail.Tests            EmbeddingUiTests (Strg+1/2/3, the sign-in, appointments, contacts, the invitation bar), the real ThixMail headless: add an account (test, certificate), read (pictures blocked/loaded), reply + Strg+Eingabe,
                                send checks, delete + undo, drag onto a folder, search, conversations, offline/online, notifications, .eml, settings tabs,
                                200,000 rows, the fixture mails as pictures (<folder>/mails), snapshots, no literal colours
tests/ThixShow.Core.Tests       colours against documented Office values, preset formulas against known paths, inheritance, rendering
                                (fixed-width measurer, Skia pixels, PDF), pptx round trips + validator + preservation fixtures, editing
                                (random undo/redo), show engine timelines and frames, 200-slide performance
tests/ThixShow.Tests            the real ThixShow headless through keys/text/pointer: placeholder text, drawing, snapping, sizing, nudge,
                                duplicate, marquee, contextual tabs, charts, thumbnails, sorter, show keys and idling, presenter view,
                                print layouts, find/replace, pptx/pptm flows, 200 slides, snapshots, no literal colours
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
.github/workflows/         _desktop.yml (reusable: version → windows → macos → publish), thixwrite.yml, thixcalc.yml and thixshow.yml
                           (callers, own path filters: shared projects trigger all, one app's projects only its own)
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

## The workspace mode of the shell (ThixContacts, ThixMail)

- `OfficeAppDefinition.Kind = OfficeAppKind.Workspace` (`IsWorkspace`): ONE main window over the app's own data. `OfficeApplication.NewDocumentWindow`
  brings it to the front (made once); a second start / Finder activation does the same and hands files to `ImportFilesAsync(window, files)` (the
  app's `DocumentTypes` are IMPORT types: ThixContacts' .vcf); no recovery, no recent list. `OfficeWindow` registers no Neu/Öffnen/Speichern/Speichern
  unter/Als PDF/Schließen commands, the quick access is Rückgängig/Wiederholen, the recovery timer is off, the backstage has only the app's
  `CreateBackstageItems` + Einstellungen (no tab Speichern) + Info (`OfficeBackstage.Open(null)` falls back to Einstellungen). The document is a
  workspace document (never dirty, no file: ThixContacts' `ContactsDocument`); undo/redo are its. Snapshots: `main`, `main-ribbon-collapsed`, the
  app's scenes, `backstage-settings`, `-settings-updates`, `-about`, `-settings-cloud` (`OfficeApplication.CreateWorkspaceSample` fills the sample
  through `FillSampleDocument`); the self test checks the main window, the app's backstage pages and the settings instead of the document flows.
  `IFileDialogs.PickFileAsync` (default: null) picks a file to import. Document apps behave exactly as before.

## ThixContacts

- **Data:** the contacts are items of MatCloud's general sync (app `contacts`, collections `contacts` / `contact-groups`, data = the contract's
  `ContactData` / `ContactGroupData`; unknown fields kept in `Extra`), kept locally in `PimStore` (`contacts-local.db` or `contacts-<account>.db`
  in the data folder) and synced by `PimSync` while signed in. Pictures: by SHA-256 id, `POST/GET /api/v1/contacts/photos`. The rules are
  `ThixOffice.Pim.Rules` (identical results to MatCloud's module: the shared fixtures; change both and both fixture copies together).
- **Grants (contract 1.5.0):** ThixMail (`mail`) reads and writes the contacts and the calendar of its account (`?app=contacts` / `?app=calendar` on the
  general sync; `MatCloudClient.GetChangesAsync(app, …)`), ThixCalendar reads the contacts. ThixMail uses `new PimSync(store, client, app: CloudApps.Contacts)`.
- **UI:** `ContactsEditor` hosts ThixOffice.Pim.Desktop's `PeopleView` (folder pane | list + letters | card – ThixMail's view "Personen" is the same),
  the commands are `PeopleCommands` (Outlook's keys) through `ContactsCommands` (+ Drucken, Sync), the ribbon `PeopleRibbon.Create` (Start | Ordner
  | Ansicht), the dialogs in ThixOffice.Pim.Desktop/People (change them there: both apps follow). Category colours = Mat tokens by the name's hash (`CategoryColors`). Snapshot scenes `dialog-edit`, `main-groups`,
  `dialog-merge`, `dialog-import-mapping`, `main-signed-out`, `backstage-print`; `THIXCONTACTS_SCREENSHOTS=<folder> dotnet test tests/ThixContacts.Tests`.
- **The cloud ends the sign-in (401):** the sync stops, the account's contacts stay in the window and can be changed (the changes wait as dirty items),
  title bar "Abgemeldet", message bar `ContactsEditor.SignedOutMessage` with "Anmelden". `CloudSettings.IsSignedOutByCloud` keeps this across a restart
  (`CloudService.HasAccount`, the state starts as SignedOut, `PimStores.PathFor` stays on the account's store). A new sign-in of the same account
  restarts the sync on the same store; another account opens its store. Only "Abmelden" in Einstellungen › Cloud (`OfficeApplication.SignOutAsync`,
  also offered in the signed-out state) leaves the account: ThixContacts asks "Kopie behalten" (`PimStores.ReplaceLocalWith`: the local store becomes
  a copy) or "Ohne Kopie abmelden" (the local store shows as before; the account's file stays for its next sign-in).

## ThixMail

- **Accounts:** `MailAccount` items in `AccountStore` (`mailsettings-local.db`, collections `mail-accounts`/`mail-signatures`/`mail-categories`/
  `mail-rules`/`mail-templates`, synced through ThixCloud – see below); passwords ONLY through `AccountService.SetPassword` → `TokenStore` (key
  `mail-<id>-imap|smtp`, the protected value in the store's local meta). Adding/editing always runs `AccountService.TestAsync` first; a certificate
  the system does not trust becomes `MailErrorKind.Certificate` with `CertificateInfo` and is accepted only by `TrustCertificate` (host|SHA-256).
  What only this computer decided never travels: accepted certificates are the store's meta `trust:<id>` (`AccountStore.Save` writes the item without
  them, `Accounts` puts them back), and `AccountStore.Confirm/IsConfirmed` (meta `confirmed:<id>` = the servers' fingerprint, set by AddAccount/
  UpdateAccount after the form's test). An account from another computer – or whose servers another computer changed – gets no `MailSync`
  (`SyncPhase.NeedsConfirmation`, "Prüfen?"), and the form never sends this computer's stored password/token to it; Bearbeiten → test → Speichern.
- **OAuth (Gmail, Microsoft):** `MailAccount.Authentication = OAuth` + `OAuthProvider` ("google"/"microsoft", `Accounts/OAuthProviders`: endpoints,
  scopes, loopback host). `OAuthService` = PKCE code flow in the system browser with a loopback `TcpListener` (random port), refresh, revoke, German
  `OAuthException`s; tokens ONLY via `AccountService.SetOAuthTokens/OAuthTokensOf` (TokenStore, kind `oauth`); `AccountService.CredentialAsync` is what
  `MailSync` signs in with (refresh 2 min before the end and once after a refused token; an ended refresh token = `MailErrorKind.SignInRequired` →
  `SyncPhase.SignInFailed`, mail kept). MailKit uses SASL XOAUTH2 only for OAuth accounts; `SampleMailServer.AcceptAccessToken` takes tokens.
  Client ids ONLY from `OAuthClients` (built in empty; policy texts `OAuth<Provider>ClientId/ClientSecret` via `AccessPolicy.Text`, env
  `THIXMAIL_OAUTH_<PROVIDER>_CLIENT_ID/_SECRET`); never another product's ids. Without one the account form says so. Tests: `FakeOAuthServer`
  (tests/ThixMail.Core.Tests, linked into ThixMail.Tests), `AccountForm.BrowserForTests`. The registrations to make: docs/PLAN.md Phase 7.
- **Settings through ThixCloud:** `MailDocument` runs a `PimSync` (own app `mail`) on `AccountStore.Store` while signed in (`ReconnectSettingsAsync`
  follows the sign-in); collections mail-accounts/-signatures/-categories/-rules/-templates; secrets are meta values and never travel. Remote changes
  raise `AccountStore.Changed` (`PimStore.RemoteChanged`) → `OnAccountsChanged` starts/stops account syncs (`SaveQuietly` around local account writes).
  Another ThixCloud account than the store's meta `cloudAccount`: `MarkAllDirty` – this computer's settings go up into it and its settings join them
  (nothing is lost by switching). A loop ended by a 401 is started again by the next `ReconnectSettingsAsync`. Real MatCloud: `MatCloudSettingsIntegrationTests`
  (env-gated like the embedding tests). `MailDocument.Closing` (stores still open; compose windows keep their draft and close), then `IsClosed`: posted
  callbacks of every view must check it (`Gone`).
- **Big folders:** the sync fetches 1,000 headers per round and loops on at once while a folder has older ones (`_backfill`); without CONDSTORE the flag
  poll is the newest 300 + a moving window (2,000 in a full round), other folders than the Posteingang at most every 15 min. `scripts/greenmail-test.ps1
  -Large 20000` measures it (`LargeMailboxTests`).
- **Everyday:** search folders (`FolderNodeKind.SearchUnread/SearchFlagged`; a message read in an unread view stays listed until the view changes),
  "Später senden" (`Outbox.SendAt`, migration 2; `MailSync.UntilNextSending`), templates (`MailTemplate`, collection mail-templates, `TemplateDialog`),
  `MailSession.MoveToAccountAsync` (copy + original into Gelöschte Elemente; `MailEditor.MoveAsync` asks).
- **Rules:** `MailRule` items in `AccountStore` (collection `mail-rules`), `MailRules` (Validate, Matches, Plan → `RuleOutcome`s: read/flag/categories add
  up, the first move/delete ends), `MailSession.ApplyRules(outcomes, undoable)` / `ApplyRulesNow(folder)` (one step "Regeln anwenden"); on arrival
  `MailSync.Arrived` → `MailDocument.OnArrived` (sync thread, not undoable) BEFORE `NewMail` (which then names only what stays unread in the Posteingang).
  UI: `Pages/RulesPanel` (Einstellungen › Regeln) + `RuleDialog`, commands `mail.rules.create/manage/run`.
- **Notifications:** `IMailNotifier.Show(MailNotice)` (title, text, link `thixmail:message?id=…` = `MailLinks`, Open). Windows: `WindowsToasts`
  (ThixOffice.Core/Platform/Windows: WinRT through combase + vtable calls, no projection) as `OfficeAppDefinition.AppUserModelId` – only shown when the
  Start-menu shortcut carries that AUMID (`ShellLink.Write(…, appUserModelId)`, written by the setup for apps with `ShowsNotifications`; the process sets
  it in `OfficeApplication.Start`); a click starts the URL scheme `thixmail:` (`InstallService.RegisterUrlScheme`), `OfficeApplication.DocumentFilesIn`
  passes the app's own links through, `App.ImportFilesAsync` → `OpenLink`. Fallback MatNotification (development builds, toasts off). macOS:
  `MacNotifications` (NSUserNotificationCenter + a run-time delegate class; click → `MailNotifications.LinkActivated`), fallback osascript.
  "In front" (no notification) = active AND shown AND not minimized (Windows keeps a minimized window active). The decisions go into the log.
- **Protocol:** everything goes through `IMailTransport` (`MailDocument.Transport`; tests/snapshots/self test: `SampleMailServer`). MailKit 5 is
  rooted for trimming (`TrimmerRootAssembly` MailKit, MimeKit.Core); code pages are registered by `MailMime`'s module initializer before MimeKit
  builds its charset table.
- **Store and sync:** `MailStore` (`mail.db` + `mail.db-files/`) on `ThixOffice.Pim.Store.SqliteDatabase`. Local actions change the store at once and
  queue an `OperationData` (folder path + UIDs at the time); `MailSync` replays the queue first, then lists folders, sends the outbox (copy into
  Sent unless `ServerSavesSent`), then syncs folders; placeholders (negative UIDs) adopt the server's UID by Message-ID, so no duplicates. Undo of
  a move takes the waiting operation back, else queues the reverse.
- **Showing mail:** `MailHtml.ToView` = `HtmlImport.Import(html, HtmlImportOptions.Mail(resolver, width))` → `MailHtml.Build` (one page as wide as the
  pane, mail styles) → `MessageView` puts it into a read-only story `DocumentSurface`. Remote pictures go through the resolver only after
  "Bilder laden" (`RemotePictures`, limits); links through `MailHtml.DescribeLink` + `LinkService`. In mail mode `HtmlImport` reads `<style>` blocks
  (`HtmlImport.Css.cs`: tag/.class/#id/descendant selectors, @media and url() left out; matched declarations go in front of the style attribute), drops
  hidden parts, unwraps only stacking wrappers (one content cell per row without colour, or cells holding only tables), keeps every other table as a table
  (widths from width/CSS and the content's natural width, cellpadding/padding as cell margins, CSS borders and backgrounds on the cells) – a table inside a
  cell becomes a carrier paragraph with an `OpaqueInline { Nested = Story }` (read-only, side by side; `DocumentLayout.NestedLinkAt` finds its links,
  `MailHtml.AllInlines` walks into it, `HtmlExport` writes it back as a table), draws tables without lines, quotes as bars, drops a last `<br>`.
- **Composing:** `ComposeWindow` (a `MatWindow` with its own `OfficeCommandRegistry` + ribbon bound by `OfficeRibbonBinding`), the body is an editable
  story surface with spelling as you type (`SpellingSupport` of ThixWrite.Desktop, `DocumentSurface.Misspelled`, suggestions in its context menu) and
  ThixWrite's paste order (own fragment, HTML of other programs through `ClipboardHtml` + `HtmlImport`, text, bitmap); sending exports
  `HtmlExport.Export` (pictures as cid:) + plain text through `MailSession.Send` (validation, outbox).
- **Shell extensions used:** `OfficeApplication.CreateSettingsTabs` (Lesen, Verfassen, Signaturen, Benachrichtigungen after Allgemein;
  `SettingsPage.ShowTab(id)`/`AppTab(id)`), `CreateBackstageItems` (Konten, Drucken, Als PDF speichern), `ImportFilesAsync` (.eml → `MessageWindow`).
- Snapshot scenes `main-conversation`, `main-newsletter`, `compose`, `backstage-add-account` (+ `-browser`, `-microsoft`), `main-attachments`, `main-offline`,
  `backstage-settings-signatures`, `main-calendar-signin`, `main-calendar`, `main-calendar-month`, `dialog-appointment`, `reminder`, `backstage-print-calendar`, `main-people`,
  `main-invitation` on `SampleMailbox` (+ `SampleMailbox.FillPim` after a sign-in at the snapshots' cloud in memory); `THIXMAIL_SCREENSHOTS=<folder> dotnet test tests/ThixMail.Tests` also draws the 15 fixture mails
  into `<folder>/mails`. The integration run against a real IMAP/SMTP server: `scripts/greenmail-test.ps1` (throwaway GreenMail container, env-gated
  `RealServerTests`).

### Calendar and people in ThixMail (7c)

- **7d additions:** dragging in `CalendarView` (`EnableDrag`: a block moves, its lower 6 px change the end; `CalendarMoves` in ThixOffice.Pim: quarter
  hours, length kept, month = another day at the same time) raises `ChangeRequested(CalendarChange)`; the host calls `MailDocument.ChangeAppointment`
  (one step of `MailSession` via `RecordDone`; refuses read-only calendars and meetings with attendees). Reminders: `CalendarReminders` (Due/Dismiss/
  Snooze, answers in the calendar store's meta `reminder:<key>`, per trigger) + `MailReminders` (30 s timer, `ReminderWindow`; quiet while the mutex
  "ThixCalendar.SingleInstance" exists; `NowForTests`/`CalendarRunsForTests` – tests, snapshots and the self test keep it quiet). Printing: `CalendarPrinting`
  (ThixOffice.Pim.Desktop: Tages-/Wochen-/Monatsformat, A4 landscape; print colours in `CalendarPalette`), `CalendarView.PrintPages/PrintStyle`;
  `MailPrintPage`/`MailPdfPage` print the calendar while the view Kalender is shown (Strg+P works there too).

- **Views:** `MailEditor.ShowView(MailArea)` (the navigation bar, Strg+1/2/3, Ansicht › Navigation). Kalender = `CalendarView`, Personen = `PeopleView`
  (the same as ThixContacts'), both on `MailDocument.Pim` (`MailPim`: stores `mailcontacts` and `mailcalendar` of the account, `PimSync` with
  `?app=contacts` / `?app=calendar` – the grants of contract 1.5.0). **Local first** (2026-11-23, PLAN.md "Kalender und Personen ohne ThixCloud"):
  the views always work; without a ThixCloud account a slim bar offers the sign-in (closable for good). Personen opens ThixContacts' own file
  (`MailPim.ContactsDirectory` = `AppPaths.DataDirectoryOf(ThixContactsApp.Definition)`: `contacts-local.db` / `contacts-<account>.db`; two processes on
  one SQLite file – WAL, busy_timeout, `BEGIN IMMEDIATE`, `PimStore.CheckForOtherChanges` via `PRAGMA data_version`; only the holder of `<file>.sync`
  talks to the cloud). PimStore migrations stay additive and ThixMail + ThixContacts ship together. The calendar is ThixMail's own
  `mailcalendar-local.db` (ThixCalendar's calendar.db stays separate; `CalendarIcs` import/export). Sign-in merges local into the account
  (`PimStores.MergeLocalInto`, `CalendarTransfer.MergeInto`), sign-out asks "Kopie behalten" / "Ohne Kopie abmelden". Each module has its own ribbon
  tabs (`OfficeRibbonBinding.ShowTabs`, `OfficeEditor.RibbonBound`; `MailNewItems` = "Neue Elemente"). Sign-in changes reach `MailPim.ReconnectAsync` through the editor (`CloudService.StatusChanged`); 401 stops BOTH syncs (the second meets
  "not signed in": `PimSync` stops on SignedOut/Off), data stays, changes wait; `ReconnectAsync` after a new sign-in starts new syncs on the same stores
  (`IsSyncing` = a loop runs). A 403 for one granted app (ThixCalendar or ThixContacts switched off in MatCloud) is that sync's own state ("ThixCalendar
  ist in der ThixCloud abgeschaltet", `PimSync` with `app`: no app-wide Forbidden; `CloudService.RefreshAccountAsync` decides whether ThixMail itself
  is off); the other sync goes on. `MailPim.SyncStatusChanged` keeps the views' status bar current. The title bar names the view
  (`MailDocument.ViewName` → `Title`; `OfficeDocument.Title` is virtual for that).
- **Commands and keys:** every mail command acts only in the view E-Mail (`MailCommands` wraps them), the calendar's only in Kalender, the people
  commands only in Personen – so Strg+N, Entf, Strg+E, F9 belong to the active view: `OfficeWindow` runs the FIRST ENABLED command of a key (a text field
  keeps a key no command can use now). Ribbon: the contextual tabs of the contexts `calendar` (Kalender) and `people` (Personen, Kontaktgruppen,
  Personenansicht) are shown by `ShowView`; the mail tabs stay (their commands disabled).
- **Calendar data:** ThixCalendar's items (`CalendarItems`): own calendars (account kind Local, "Eigene Kalender") are written – events, and an
  account + calendar "Kalender" when the account has none –, calendars MatCloud keeps in step (cloud-*) are shown read-only; online accounts each computer
  syncs itself are not in the cloud and do not show. The contract has no series, participants or organizer for own events: invitations enter their first
  meeting; the participants of meetings sent from ThixMail are kept in the store's meta (not synced). Reminders are ThixCalendar's (ThixMail fires none).
  `CalendarBook.Normalize/Validate` are ThixCalendar's (`CalendarService` there): trimmed texts, all-day at midnight UTC, the meeting address taken from
  location/notes (`MeetingLinks`, its regex) and checked as http(s) – an item ThixCalendar would normalize differently is written back by it. Own
  calendars are made like its `AddLocal` (no default reminder) and only after the store's first sync (`CalendarBook.AwaitsFirstSync`, set by
  `MailPim`): before, ThixCalendar's own calendars may still be on their way and a second "Eigene Kalender" would appear everywhere.
- **Checked against the real thing:** `MatCloudEmbeddingIntegrationTests` (ThixMail.Core.Tests, the env variables of the MatCloud integration run) –
  contacts with ThixContacts' sync, the calendar with ThixCalendar's sync and its item shapes/rules copied into the test (`ThixCalendarShapes`), grants,
  401, 403 (switches ThixCalendar off through Verwaltung › Apps: the user must be the instance's administrator). `THIXOFFICE_MATCLOUD_KEEP_APPOINTMENTS=1`
  leaves ThixMail's appointments in the account for a look with ThixCalendar itself (its core can be driven from a copy of its sources outside both repos).
- **Invitations:** `MailReader` keeps a text/calendar part (`ReadMail.Calendar`), `MailInvitations.View` reads it (`Invitations.Read`) with the
  overlaps; `MessageView.ShowInvitation` draws the bar (Zusagen | Mit Vorbehalt | Absagen, "Aus Kalender entfernen" for a CANCEL); answers write the
  appointment (key `CalendarBook.KeyOfInvitation(uid)`, the same on every computer) and send the REPLY. Sent invitations: multipart/alternative with
  text/calendar;method=… plus invite.ics (`MailComposer.WithCalendar`).

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
- **Nested tables, gaps, turned text, floating tables:** a w:tbl inside a cell becomes a paragraph (`DocxReader.NestedCarrierProperties`)
  with ONE `OpaqueInline` (Kind Table, `IsBlockContent`, `Nested` = a read-only story read through `ReadContext.ReadDisplayOnly` – nothing
  counted, comments/notes stay XML); `LayoutContext.MeasureNested` sizes it (a `StoryBox`, the line is exactly the table), `DrawObject`
  draws it; `DocxWriter.WriteParagraphElements` writes the w:tbl between the cell's paragraphs again. `TableRowProperties.GridBefore/
  GridAfter` shift `GridColumnOf`/`CellAtColumn` (and `TableEdits.TableModel`). `TableCellProperties.TextDirection`/`Rotation`: the
  cell's lines are laid out as long as the row (`CellBox.Rotation`, `PlaceTurnedCell`) and drawn through `PlacedLine.Transform`.
  `TableProperties.Position` (`TablePosition`, read from w:tblpPr, which stays in UnknownXml): `FloatingTableLeft`, `Flow.TryPlaceFloating`
  places the rows in one piece and adds a square-wrap `PlacedFloat` (Inline = an empty TextRun, Offset -1) the text flows around.
- **UI:** Einfügen › Tabelle (`TablePicker`, a `MatRibbonCustom` through `OfficeApplication.CreateRibbonContent`; "Tabelle
  einfügen …"), contextual tabs Tabellenentwurf / Layout (`TableCommands`, context `table`, shown by `WriteEditor` while the caret is
  in a table), dialogs InsertTable / SplitCells / DeleteCells, column borders dragged in `DocumentSurface`.

### Pictures

- **Model:** `ImageData` (the bytes of a PNG/JPEG/GIF/BMP, format, pixel size and resolution read from the header – the core
  decodes nothing; equal by SHA-256 `Key`) inside an `ImageInline` (one object character, size in points, alternative text;
  `SourceXml`/`SourcePart`: the w:drawing as read, written back unchanged while the picture is; `IsFloating` + `Anchor`
  (`DrawingAnchor`): a wp:anchor, placed on the page – see "Floating objects"). Session: `InsertImage` (fitted to the text or cell
  width, then selected), `SelectedImage`, `SetImageSize`, `ResetImageSize`, `SetImageDescription`, `ReplaceImage`.
- **Drawing:** the layout makes an `ObjectItem` as tall as the picture; the display list has an `ImageItem` (key + bytes);
  `SkiaPageRenderer` decodes each key once (screen, PDF, printing alike).
- **docx:** `DocxPictures` (read pic:pic with one a:blip from its image part, `Crop` from a:srcRect, `Shadow` from a:outerShdw; write
  new pictures as wp:inline with one media part per picture and unique wp:docPr ids). A picture read from a file keeps its `SourceXml`
  through size/position/wrap/alt-text changes: `DocxAnchors.Patch` puts only what differs into it (extent, wp:inline ⇄ wp:anchor,
  positionH/V, wrap, behindDoc, distances) – unchanged pictures are written byte for byte as read.
- **Floating pictures (UI):** `EditingSession.SetImageWrap(PictureWrap, at)` (Bildformat › Textumbruch, `write.picture.wrap.*`; `at` =
  `DocumentLayout.InlineOffsetsOf`, so it stays where it was), `MoveImage(offsetX, offsetY)` (`DocumentSurface`: dragging a floating
  picture, `MoveHandle`; `DocumentLayout.AnchorOffsetsFor` turns the page point into offsets from the anchor's frames).
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

### Floating objects, drawings, text boxes, charts (display of what Word wrote)

- **Model** (`Model/Drawings.cs`): `DrawingAnchor` (frames page/margin/column/paragraph/line/…, offset or alignment, `DrawingWrap`
  Square/Tight/Through/TopAndBottom/None, behind the text, distances, z order); `DrawingGraphic` (size, anchor, elements:
  `DrawingShape` – preset/custom geometry, adjust values, `DrawingFill`/`DrawingLine` with `DrawingColor` = sRGB or scheme + transforms,
  text as a `Story`, insets, vertical anchor –, `DrawingPicture`, `DrawingChart` = the chart part's XML). It is DISPLAY data:
  `OpaqueInline.Graphic` describes a w:drawing / mc:AlternateContent / w:pict the model keeps as XML (written back unchanged).
- **docx:** `DocxDrawings` (wps shapes and text boxes – w:txbxContent read like the body through `ReadContext.ReadTextBox` –, wpg groups
  flattened through their transforms, pictures in groups, c:chart parts, SmartArt through its dsp drawing part, VML fallbacks).
- **Layout** (`DocumentLayout.Floats.cs`): `Flow.PlaceFloats` puts a paragraph's anchored objects on its page (`PlacedFloat`,
  `PageLayout.Floats`; header/footer ones in `HeaderFooterFloats`), `Flow.Wrapped` lays the paragraph out again around the
  exclusions of the page (`ParagraphLayouter.Layout(…, exclusions)`: each line in the wider gap, a line without room moves below;
  such layouts are `IsWrapped`, keep their `Exclusions` and are never reused; `Flow.Rewrapped` breaks the rest of a paragraph again for
  the room of the next page/column). An object on the page/margin that reaches up beside text placed before its anchor becomes an
  "early float" of that page (`_earlyFloats`, `Flow.ReachesBack` → `RestartFrom`: `Paginate` lays the page out again from its first
  block with `SeedFloats`; at most 20 restarts per pagination). Paragraph borders take room (`DocumentLayout.BorderSpace`). Drawing: `DrawObject` (pictures, shapes through `GeometryEvaluator` + the vector
  layer, text boxes as a cached `DocumentLayout` of their story inside a clip, charts through `ChartRenderer`), behind-text floats
  first, in-front floats last (z order). Drop caps (`ParagraphProperties.DropCapLines` from w:framePr) are an exclusion the next
  paragraph flows around.
- **Columns:** `SectionProperties.ColumnCount/ColumnSpacing/ColumnSeparator/Columns` (+ `ColumnFrames()`); the flow keeps a column
  and its top (`FlowState.Column/ColumnTop`), `NextColumn` before a new page, column breaks (`LineEnding.ColumnBreak` from the
  OpaqueInline ColumnBreak), `BalanceColumns` before a continuous section break, `PageLayout.ColumnLines` for w:sep.
- **Content controls:** a w:sdt around paragraphs (and tables between them) opens as its paragraphs with `ContentControlMarkInline`
  marks (start keeps w:sdtPr/w:sdtEndPr); `DocxWriter.ControlNesting` writes the w:sdt back. Inline content controls (w:sdt in a
  paragraph) open the same way with marks `IsInline = true`; `DocxWriter.WriteParagraph` opens a w:sdt at the start mark (containers:
  paragraph / open control → link → tracked change).
- **Tracked changes (model):** `Inline.Revision` (`Revision`: Kind Insertion/Deletion/MoveTo/MoveFrom, Author, Date, Id): w:ins/w:del/
  w:moveTo/w:moveFrom content is ordinary inlines naming the change (`TextRun.CanJoin` compares it); deleted text is read from
  w:delText and written back as w:delText inside w:del (`DocxRevisions`, ids kept where free). The layout shows them by
  `LayoutContext.View` (`RevisionView`; `Hides`: removals hidden in the final views, insertions in Original). Format changes (rPrChange)
  stay in `CharacterProperties.UnknownXml`. odt/txt write the final text.
- **Recording (`EditingSession.Revisions.cs`):** `TrackChanges` (= `DocumentSettings.TrackRevisions`, `SetTrackChanges`), `RevisionAuthor`
  (the editor sets it from `SettingsService.AuthorName`); `InsertText`, `DeleteRange`, `Paste`, `PasteText` go through `TrackDelete` /
  `MarkInserted` / `ReplaceTracked` while it is on; `RevisionAt`, `NextRevision`, `ResolveRevision(accept)`. Display: `DocumentLayout.RevisionView`
  (AllMarkup colours via `ParagraphLayouter.RevisionColor`, `LineLayout.ChangeBar` drawn left of the margin). UI in `ReviewCommands`.
- **Formats shown from files:** table style conditions (`Style.Conditions`, `TableProperties.Look`, `StyleResolver.ConditionMask/
  ConditionsOf`), character caps/small caps/hidden/spacing/scale/position/shading, paragraph borders and shading, `Document.PageColor`,
  headers/footers with tables (`PlaceStoryWithTables`: the story as its own layout). Tracked changes show as the final text; the
  session's `AcceptAllRevisions`/`RejectAllRevisions` resolve them.

### Formulas (OMML, display only)

- `DocxMath.Read` turns m:oMathPara / m:oMath into a `MathNode` tree (`Model/Math.cs`: MathText, MathRow, MathFraction, MathScripts,
  MathRadical, MathNary, MathDelimiter, MathMatrix, MathAccent, MathBar, MathGroupChar, MathFunction, MathLimit, MathBorderBox,
  MathEquationArray) on the `OpaqueInline` (`Math`, `IsDisplayMath` for m:oMathPara); the XML stays and is written back unchanged.
- `Layout/MathLayouter` sets it into a `MathBox` (pieces: `MathGlyphs`, `MathRule`, `MathStroke`; x from the left, y from the baseline) in
  "Cambria Math" (`FontSubstitutes`), letters through the math alphabets; `LayoutContext.MeasureMath` sizes the object (ascent +
  `Segment.ObjectDescent`; a display formula takes the line's width and is centred), `DocumentLayout.DrawMath` draws it.

### Footnotes and endnotes

- **Model:** `NoteInline(Kind, Content: Story, …, SourceId)` = the reference (one object character) WITH its text; `NoteMarkInline` = the
  number mark inside the text (w:footnoteRef). Deleting/copying/undoing the reference does the same to the note – no second list.
  `NoteNumbering` (labels 1, 2, 3 / i, ii, iii; styles FootnoteText/FootnoteReference/Endnote*; `NewContent`; `All(document)`).
- **Layout** (`DocumentLayout.Notes.cs`): note numbers per paragraph (`_noteStarts`, a paragraph whose numbers change is laid out again;
  `LayoutContext.NoteStart`/`NoteMarkText`); the flow places a line only when it and its notes fit (`NoteReserve`), adds its footnotes
  (`AddFootnotes`, `PageLayout.FootnoteHeight` shrinks `Bottom`), carries what does not fit (`FlowState.Carry`, placed first on the next
  page, continuation separator across the width); `Finish` places the carry and the endnotes after the last block. `PageLayout.Footnotes`
  / `Endnotes` (`PlacedNote`: reference, label, lines of the note's story), `FootnoteSeparator` / `EndnoteSeparator`; `NoteAt`,
  `FindNote`, `NoteLabel`.
- **Editing:** `EditingSession.InsertNote(kind)` (returns the reference), `SetNoteContent(reference, story)` (one step), `NextNote`.
  `WriteEditor.EditNote(reference, at)` opens the note like a header (story surface over its text, `NoteMarkText` = its label; no dimming);
  a click on the body, Esc or saving commits. **docx:** `DocxNotes` (ids reused, separators kept, notes referenced from preserved XML kept).

### Table of contents, navigation

- **Model:** `TocMarkInline(IsEnd, Instruction)` (a marker: `Inline.IsMarker` – invisible, no width, sticks to the next character; every
  marker check uses `{ IsMarker: true }`) around ordinary entry paragraphs (style TOC1–9, right dotted tab at the text width, text + tab +
  page number as runs with `Hyperlink(null, "_Toc…")`). `TableOfContents` (instruction, levels from `\o "1-3"`, entry styles).
- **Session** (`EditingSession.Toc.cs`): `FindTableOfContents` (start mark → end mark, else the TOC-styled paragraphs after it),
  `Headings(levels)` (by resolved outline level), `InsertTableOfContents`, `UpdateTableOfContents`, `RemoveTableOfContents` – each ONE
  step; headings get "_Toc…" bookmarks (OpaqueInline bookmark markers) when they have none; page numbers come from
  `ICaretNavigator.PageNumberOf` (the layout, synchronously inside the step) and are fixed again after the entries are in.
  `FindBookmark` / `GoToBookmark`: links with an anchor jump (`LinkCommands.Follow`).
- **docx:** written as Word's complex TOC field; `DocxToc.Collapse` turns Word's field (also inside the "Table of Contents" content
  control, which is unwrapped) back into the marks; nested PAGEREF fields stay preserved and are not reported.
- **UI:** Referenzen › Inhaltsverzeichnis (menu: Automatisches Verzeichnis, entfernen), Tabelle aktualisieren; Ansicht ›
  Navigationsbereich (`NavigationPane`, column 0 of the editor's grid, refreshed with the ribbon state).

### Comments

- **Model** (`Model/Comments.cs`): `CommentAnchorInline(Id)` (start marker) … `CommentInline(Comment)` (end marker carrying the
  `Comment`: id, author, initials, date, `Content` story, `ParaId`/`ParentParaId`/`Done` of Word's threads, `SourceXml`/`SourceContent`);
  `Comments.All(document)` pairs them (`PlacedComment`: range, reference). Session (`EditingSession.Comments.cs`): `AddComment`,
  `SetCommentText`, `SetCommentDone`, `DeleteComment` (with its replies), `DeleteAllComments`, `NextComment`, `CommentAtCaret`, `RepliesTo`.
- **docx** (`DocxComments`): read lazily in the body only (comments in stories stay preserved); ids paired in document order before the
  body is written (`Allocation.Prepare`, the model's id when free); an unchanged comment is written from `SourceXml`; thread parts tidied.
- **UI:** `CommentsPane` (column 2 of the editor's grid; cards, text committed on LostFocus), `DocumentSurface.Shading` (ranges drawn over
  the pictures, `DocumentPalette.CommentRange*`), `ReviewCommands`, tab `review`. Author: `SettingsService.AuthorName/AuthorInitials`
  (`AppSettings.UserName/UserInitials`, default `UserIdentity.DisplayName`, pinned to "Anna Berger" in snapshots).

### Spelling (shared seam, ThixWrite's use)

- **ThixOffice.Core/Spelling:** `ISpellChecker` (Language, Check → `SpellingError`s, Suggest, Add), `ISpellingProvider`, `SpellingService`
  (CheckerFor(language) with fallback, Resolve, Ignore/WordsChanged, AddToDictionary, DisplayName, `UseForTests`), `SpellingQueue`
  (background checks keyed by an immutable owner, `TryGet`/`CheckNow`/`Clear`, `Checked` on a worker thread), `MemorySpellChecker` /
  `MemorySpellingProvider` (tests; snapshots use `OfficeSnapshot.SnapshotSpelling`, the UI tests `TestData.Spelling` – never the machine's
  checker). Platforms: `Platform/Windows/WindowsSpellChecking` (generated COM on its own MTA thread, `ComThread.Invoke`),
  `Platform/MacOS/MacSpellChecking` + `ObjectiveC` (objc_msgSend shapes, NSString, autorelease pool). `OfficeApplication.ChecksSpelling`
  shows the setting `AppSettings.CheckSpellingAsYouType`.
- **ThixWrite:** `SpellingText.Of(paragraph)` (markers out, objects as spaces, offsets back), `DocumentLanguage` (w:lang of the document
  defaults; `EditingSession.Language/SetLanguage`, `ReplaceWord`), `Editor/SpellingSupport` (queue per language, `Misspelled(pages)` for
  `DocumentSurface.Misspelled` – the wavy lines, `ErrorAt`, `NextError`, `CheckNow`), `WriteEditor.SpellingMenuItems` (context menu),
  `Dialogs/SpellingDialog` (F7), `Dialogs/WordCountDialog`; commands in `ReviewCommands` (write.spelling, write.wordCount, write.language.*).

### OpenDocument Text (odt)

- `Formats/Odt` (on the shared `ThixOffice.Core/Formats/Odf`: namespaces, units, package): `OdtXml` (character/paragraph properties both ways), `OdtWriter` (ZIP: mimetype first and
  stored, content.xml, styles.xml with the page layout and master page, meta.xml, Pictures/, manifest), `OdtReader` (named styles mapped
  to ThixWrite's ids by display name – "Heading 1" → Heading1 –, automatic styles as direct formatting, lists from list styles, tables with
  covered cells, notes, annotations, sections/indexes as paragraphs; anything else counted in `UnsupportedContent`, the opening warning says
  it is not kept for odt). `WordDocument` sniffs the mimetype (`OdtReader.IsOdt`), `WordFileFormat.Odt`; `WriteEditor.ConfirmSaveAsync`
  asks before Word content is left out of an .odt. No dependency beyond the BCL.

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

### Shapes, text boxes, Einfügen

- `ShapeInline` (`Model/Shapes.cs`: one `DrawingShape` + size + `Anchor`, `IsTextBox`, `SourceXml`/`ReadAs` = written as read while
  `IsUnchanged`); `DocxShapes` reads a w:drawing with exactly one wps:wsp into it and writes the model (wps:wsp, txbxContent via
  `WriteBlocks`, `DocxAnchors.Place` for inline/anchor). Layout treats it like a picture (`AnchoredIn`, `DrawObject` → `DrawGraphic`).
- Session (`EditingSession.Shapes.cs`): `SelectedObject` (picture, shape, or a one-shape `OpaqueInline` – converted by `ToShape` on the
  first change), `Frame`, `InsertShape`, `SetObjectSize`, `MoveObject`, `SetObjectWrap`, `SetShapeFill/Line/Text`, `DefaultShape/TextBox`.
  `DocumentLayout.IsSelectable`, `ParagraphOrigin`. UI: `DocumentSurface.DrawRequest` (draw by drag), `ShapeTextRequested`
  (double click) → `WriteEditor.EditShapeText` (story overlay like notes), `ShapeCommands` (Formen, Textfeld, tab Formformat).
- `EditingSession.Insert.cs`: `InsertCoverPage(CoverDesign …)` (in a "Cover Pages" w:sdt), `InsertBlankPage`, `SetDropCap(lines)`
  (w:framePr in UnknownXml), `Bookmarks/AddBookmark/DeleteBookmark/BookmarkNameProblem`; `SetHyperlink(…, anchor)`; UI `InsertCommands`,
  `Dialogs/BookmarkDialog`.

### Start extras (sort, borders, multi-level lists, keys)

- `EditingSession.Home.cs`: `Sort(SortKind, descending, column)` (paragraphs or table rows via `TableEdits.TableModel`), `SetParagraphBorders`,
  `SetParagraphShading`, `SetPageBorders` (`SectionProperties.PageBorders` read from w:pgBorders, kept/replaced in UnknownXml; drawn by
  `DisplayListBuilder`), `ApplyMultilevelList(style)` (`BuiltInLists.CreateMultilevel`), `CurrentListLevel`, `ToggleSmallCaps`,
  `ToggleDoubleUnderline`, `ToggleSpaceBefore`, `ChangeHangingIndent`, `ResetParagraphFormatting`. UI: `HomeCommands`, `Dialogs/HomeDialogs`
  (`SortDialog`, `BordersDialog` with tabs Rahmen | Seitenrand | Schattierung).

### Entwurf (designs, watermarks)

- Model `Design.cs`: `ThemeColorSet`, `ThemeFontSet`, `DocumentDesign` (Word's sets), `Watermark`; `Theme.WithColors/WithFonts/ContentEquals`,
  `ColorsName/FontsName`. The built-in styles name theme fonts (`FontTheme`) and theme colours (`TextColor.ThemeColor` + tint/shade) like
  Word, so they follow the theme; `StyleResolver.ThemedColor` computes tint/shade (HSL luminance). `DocxStyles.WriteTheme` patches only
  a:clrScheme/a:fontScheme of a theme part as read (untouched while equal), writes a whole Office theme into a package without one.
- Session (`EditingSession.Design.cs`): `ApplyDesign`, `SetThemeColors`, `SetThemeFonts` (one `DocumentSteps.Theme` step), `CurrentThemeColors/Fonts`,
  `CurrentWatermark`, `SetWatermark(watermark | null)` (every header of the document; section 0 gets Default/First/Even where needed).
- docx: `DocxWatermarks` – text = Word's VML WordArt (`PowerPlusWaterMarkObject`, kept as an `OpaqueInline` whose graphic is a
  `DrawingWordArt`, drawn stretched/rotated/translucent by `DocumentLayout.DrawWordArt`); picture = VML `WordPictureWatermark` read into an
  `ImageInline { IsWatermark, Washout }` and written back as VML. UI: `DesignCommands` (Entwurf › Dokumentformatierung, Seitenhintergrund ›
  Wasserzeichen), `Dialogs/WatermarkDialog`; snapshot scene `-main-design`.
- Ansicht › Mehrere Seiten: `DocumentSurface.PagesAcross` (rows of pages side by side; `PageRect`, `HitAt`, `PageUnder`, measure and
  render go through the rows when > 1), `WriteEditor.ZoomToMultiplePages` / `ZoomToWholePage` (Eine Seite) / `ZoomToPageWidth` set it.

### Formatvorlagen

- Session (`EditingSession.Styles.cs`): `GalleryStyles` (`BuiltInStyles.GalleryOrder` + the document's quick styles), `PaneStyles`,
  `CurrentParagraphStyle`, `StyleNameProblem`, `ModifyStyle(style)`, `CreateStyle(...)` (applies it), `StyleFromSelection(name)` – each one
  step (the sheet is swapped; the resolver drops its caches, the layout re-lays everything).
- UI: `WriteRibbon.StyleEntriesOf(session)` (gallery per window; `WriteEditor.RefreshStyleGallery` after a new style), gallery menu
  (`write.style.fromSelection/new/modify`, `write.stylesPane`), `Dialogs/StyleDialog` (fields show the resolved style, only changes are
  written into it), `Editor/StylesPane` (grid column 3).

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
  A formula that fails in German punctuation is read with "," and "." (`CalcLocale.AlternatePunctuation`, `EditingSession.ParseInput`).
- **Functions for the user:** fx / Umschalt+F3 → `InsertFunctionDialog` (search by German/English name and description) →
  `FunctionArgumentsDialog` (a field per argument, live values through `EditingSession.PreviewExpression/PreviewFormula`, the result;
  `FunctionCallText` splits/joins the call); fx on a cell holding one call opens Funktionsargumente on it. Every function is a command
  `calc.function.<NAME>` (searchable with `SearchPenalty` 3, so ordinary commands rank first) in the Formeln tab's category menus
  (main categories + Weitere Funktionen; each ends with Funktion einfügen …).
- **Keys** (Bereit): arrows, Strg+arrows (data edge), Umschalt extends, Pos1, Strg+Pos1/Ende, Bild auf/ab (Alt: sideways), Tab/Eingabe
  within the selection, F2, Entf, Rücktaste, Strg/Umschalt+Leertaste; command shortcuts are listed in `CalcCommands`' summary (German
  Excel: Strg+Umschalt+F/K/U, Strg+1, Strg+U/R fill, Strg+Umschalt+4/5/1 formats, Strg+Bild ab/auf sheets, Umschalt+F3, Strg+F3, F9 …;
  the Mac keeps ⌘B/⌘I/⌘U, ⌥⌘←/→ for sheets, ⌘⇧K/J for grouping so ⌥-arrows stay word movement).
- **Clipboard (`CalcClipboard`):** own format (a token naming the process's `ClipboardContent`: exact cells, formulas, styles, merges) +
  tab text + an HTML table (`HtmlTables.Export`, Windows' "HTML Format" envelope); paste order own → HTML table (`HtmlTables.Import`) →
  text (`PasteText`). Inhalte einfügen = `PasteOptions` (mode, transpose, skip blanks, `PasteOperation`; dialog Strg+Alt+V). A selected
  chart is copied as a chart (pasted at the active cell). Strg+Plus/Minus, Reihe (`FillSeries`) and the edit dialogs: `CalcEditCommands`.
- **Filters:** `FilterMenu` (values, sort, Zahlen-/Text-/Datumsfilter → `CustomFilterDialog`, `TopTenDialog`, averages) acts on
  `EditingSession.ActiveFilter` (the active cell's table, else the sheet's autofilter). **Find:** `CalcFindBar` with Alle suchen (results list).
- **Cell editing undo:** `FormulaTextBox` keeps its own undo/redo while a cell is edited (typed runs coalesce; `SetText` resets it).
- **Rich text** in cells is kept (`Worksheet.RichText`: the si element per cell, written back while the text is unchanged), drawn plain.
- **Printing:** `SheetPagination` (core) cuts the print area / used range / selection into pages by the sheet's `PageSetup` (scale or fit
  N × M pages, title rows, order) and renders each in points (header/footer codes); `CalcPages` feeds the shared `OfficePrintPage`
  (scopes Aktives Blatt / Arbeitsmappe / Auswahl) and `OfficeExportPage`.
- **Files:** `CalcDocument` sniffs zip vs CSV; a CSV keeps its bytes and `CsvDetection` – ambiguous separator/encoding opens the
  `CsvImportDialog` (reads again, not dirty). `CalcEditor.ConfirmSaveAsync` asks before CSV drops sheets/formulas and before .xlsx drops
  macros; `OpeningWarning` names unsupported content and says that macros never run but stay.

### Charts (shared: ThixOffice.Charts; ThixCalc's part)

- **Shared API** (UI-free, depends on ThixOffice.Text only): a `Chart` record (plots → series with `ChartDataRef` = formula text +
  cached values, axes, title, legend, colour set; `SourceXml` / `ShapeXml` / `ExtraXml` keep what the model does not own). Drawing:
  `ChartDataResolver.Resolve(chart, IChartDataSource?)` (null = the file's caches: ThixShow/ThixWrite until they embed a workbook) →
  `ChartRenderer.Render(resolved, new ChartRenderOptions { Measurer, Formatter, Accents, FontFamily, X, Y, Width, Height, Scale (target
  units per point), PixelSize (a device pixel for hairline snapping, 0 for print/PDF) })` → display items for screen, PDF and print
  alike. Files: `ChartXml.Read(xml)` / `ChartXmlWriter.Write(chart, source)` give the part's XML (a pptx/docx host manages only the part
  and its graphicFrame). New charts: `ChartFactory.Create(ChartTemplates.Find(id), specs)`; edits: `ChartEdits` (each clears the edited
  part's SourceXml). Unsupported types: `Chart.UnsupportedType` → a labelled frame, written back exactly. Tests live in
  tests/ThixCalc.Core.Tests/Charts (layout determinism, axis tables, DrawingML round trips) and `ChartPictureTests`.
- **ThixCalc:** `Worksheet.Charts` (`SheetChart`: id, two-cell `ChartAnchor` in points, chart, name; the loaded part) back to front; in the
  undo snapshot (`SheetState`), rewritten with rows/columns (`ReferenceRewriter.RewriteCharts`), renamed with sheets (`Workbook.RenameSheet`),
  copied by `CopySheet`. Session: `InsertChart`, `SelectedChart`/`SelectChart` (any cell selection clears it), `SetChartAnchor`,
  `ChangeChart`, `ChangeChartType`, `SwitchRowColumn`, `SetChartData`, `OrderChart`, `DeleteChart`, `PasteChart`. `GridRenderer` draws
  them over the cells through `ChartLayer` (printing and PDF get them; `SheetPagination.PrintedRange` covers them). xlsx: `XlsxDrawings`
  (unchanged charts keep their parts; the drawing is rewritten only when charts changed; pictures and shapes in it stay). UI:
  `GridSurface.Charts`, `CalcChartCommands` (context `chart`: tabs `chartDesign`, `chartFormat`), `Dialogs/ChartDialogs`.

### Tables (ThixCalc)

- **Model:** `TableDefinition` (name, range, columns, header/total rows, style + options, totals functions/labels, its own `Filter`) in
  `Worksheet.Tables`; `TableStyles` resolves Office's style names in the theme (`LookAt` per cell: fill, bold, text colour) – the grid
  draws them in `GridRenderer.Present` under the cell's own format, plus header/total lines and the header's filter arrows
  (`GridRenderer.FilterHeaders`). Structured references: stored English with ","; `StructuredSpecifiers` translates to German items and
  ";" for display; `StructuredReferences.ResolveRange` evaluates, `CalcEngine` registers only the named cells as precedents.
- **Session** (`EditingSession.Tables`): `CreateTable`, `SetTableOptions`, `SetTotalsRow`, `SetTableHeader`, `RenameTable` (formulas follow),
  `ResizeTable`, `ConvertTableToRange`, growth on input (`GrowTableFor`, `RenameColumnFor`), `ActiveTable`, `ActiveFilter` (filters act on the
  active cell's table, else the sheet's autofilter). Every table change marks the command structural (dependencies rebuilt).
- **xlsx:** `XlsxTables` (read; write into the table's part or a new one with a workbook-unique id; parts of removed tables go).
  **UI:** `CalcTableCommands` (context `table`, tab `tableDesign`), `Dialogs/TableDialogs`.

### PivotTables (ThixCalc)

- **Model** (`Model/Pivot`, UI-free, immutable): `PivotCache` (source sheet+range or table, `PivotCacheField`s with distinct items,
  per field per record the item index; `Build` reads the source; `Workbook.PivotCaches` by id) and `PivotTable` (location = top left of
  the body, filter fields above it; `PivotField` per source field: axis, hidden items by `PivotItems.KeyOf`, selected filter item,
  label/value `PivotFilter`, `PivotSort`, subtotals, `PivotDateGroups`/`PivotNumberGroup`, caption; Row/Column/FilterFields with
  `ValuesField` −2 = Σ Werte; `PivotDataField`s: function, `PivotShowAs` + base field/item, number format; layout Kurzformat/
  Gliederung/Tabelle, repeat labels, grand totals, subtotals top/bottom, style + options; `Range` = the cells it occupies; `SourceXml` =
  unchanged as read; `Unsupported` = shown and kept, never edited). `Worksheet.PivotTables` is in the undo snapshot.
- **Engine** (`PivotEngine.Compute` → `PivotOutput`): visible records (filter fields, hidden items, label filters) → levels (a grouped
  date field expands into Jahre/Quartale/Monate/Tage levels) → row and column trees and ONE pass of accumulators for every pair of
  nodes (100 000 records: cache ≈ 95 ms + compute ≈ 50 ms) → value filters (second pass) → sort → slots → cells with `PivotCellKind`,
  indent, number format, field; `Values` (conditions per value cell: `Lookup` = PIVOTDATENZUORDNEN, `RecordsOf` = Details anzeigen),
  `FilterButtons`, `RowItems/ColumnItems` (the file's rowItems/colItems). German captions (Zeilenbeschriftungen, Gesamtergebnis,
  "Summe von …"). `Worksheet.OutputOf(pivot)` caches it per table/cache instance; `PivotStyles` draws by it (`GridRenderer.Present`:
  header fill, bold totals, compact indent, the line above the grand total, field arrows).
- **Session** (`EditingSession.Pivot`): `CreatePivotTable(source text, PivotDestination)`, `AddPivotField` (numbers → Werte/Summe,
  others → Zeilen, like Excel), `MovePivotField`, `RemovePivotField`, data field change/move/remove, `ChangePivotField`,
  `GroupPivotField`/`Ungroup…`, `SetPivotOptions`, `RefreshPivot`/`RefreshAllPivots` (fields matched by name), `ChangePivotSource`,
  `MovePivot`, `DeletePivot`, `ShowPivotDetails` (new sheet with a table). Each = one undo step: the record is swapped and the cells are
  drawn anew as ordinary cells; `SetCell` refuses value changes inside a pivot (`GuardPivot`) except while the pivot draws itself.
- **xlsx** (`XlsxPivots`): reads cache definitions (shared items, inline record values, fieldGroup/rangePr → grouping, databaseField=0
  grouping fields mapped onto their base), records, table definitions (pivotFields, row/col/page/data fields, filters, sort,
  autoSortScope, layout flags, style); writes changed/new ones in Excel's shape (refreshOnLoad cache, records with `x v`, extra cache
  fields for coarser date groups, rowItems/colItems, schema order: … dataFields, pivotTableStyleInfo, filters); unchanged tables and
  caches keep their parts; orphan parts and `pivotCaches` entries go. Calculated fields/items, OLAP, discrete grouping → `Unsupported`.
- **UI:** `CalcPivotCommands` (Einfügen › PivotTable; context `pivot`: tabs `pivotAnalyze` PivotTable-Analyse and `pivotDesign` Entwurf),
  `PivotFieldList` (right pane: field check boxes with search, the four areas with chips, own pointer drag between list and areas, chip
  menus for the keyboard), `PivotFilterMenu` (arrow on the field headers), `Dialogs/PivotDialogs` (PivotTable erstellen,
  Wertfeldeinstellungen, Feldeinstellungen, Gruppierung, Datenquelle ändern/Verschieben), `CalcContextMenus.ForPivot`, double click on a
  value = Details anzeigen. Snapshot scenes `-main-pivot`, `-main-pivot-design` (`SampleWorkbook.AddPivot`).

### Analysis, auditing, protection (ThixCalc)

- `EditingSession.Analysis`: `AddSubtotals`/`RemoveSubtotals` (Teilergebnis: rows with TEILERGEBNIS, "… Ergebnis", Gesamtergebnis,
  outline levels 2/1), `ShowDetail`, `ShowOutlineLevel`, `GoalSeek` (secant steps, Excel's 100 iterations/0,001, one undo step),
  `TracePrecedents`/`TraceDependents`/`RemoveArrows` (`TraceArrows`, a view state the grid draws; names are followed),
  `NextError` + `ExplainError` (Fehlerüberprüfung), `EvaluateFormulaSteps` (Formel auswerten), `CreateNamesFromSelection`,
  `SelectSpecial` (Leerzellen, Konstanten, Formeln, Fehler, Notizen, aktueller Bereich, letzte Zelle, sichtbare Zellen).
- Protection (`Model/SheetProtection`, `EditingSession.Protection`): `Worksheet.ProtectionXml` parses into `Worksheet.Protection`
  (allowed actions); passwords as Excel's SHA-512 with salt and 100 000 spins (legacy 16-bit hash verified); the session refuses
  value changes of locked cells and unallowed format/row/column changes (`SetCell`, `Shift`, `ChangeAxis`), and sheet structure
  changes while `Workbook.ProtectionXml` locks the structure. UI: `CalcAnalysisCommands`, `Dialogs/AnalysisDialogs`.
- Typing: Eingabe after a run of Tab returns below the cell where the run started (`EditingSession._tabStartColumn`), as Excel.

### OpenDocument Spreadsheet (ods)

- `Formats/Ods` (BCL only, on `ThixOffice.Core/Formats/Odf`): `OdsWriter.Write` (content.xml: sheets with `table:table-column`s, rows and
  cells – values with `office:value-type` (float/percentage/currency/date/time by the cell's format, boolean, string; errors as
  `calcext:value-type="error"`), `table:formula` in OpenFormula with the cached result, matrix spans for array/spill formulas, merges with
  covered cells, notes as `office:annotation`, links as `text:a`, validations, names (`named-range` / `named-expression`, sheet-local
  ones inside the table), the autofilter as an anonymous `database-range`, `calcext:conditional-formats` with named condition styles;
  styles.xml: the default cell style, every data style, page layouts + master pages (one per distinct page setup, header/footer
  regions with fields); settings.xml: active sheet, frozen panes, zoom, cursor, grid). The grid is written by boundaries: every run of
  rows (and cells) where nothing changes is ONE element with `number-rows/columns-repeated`; filler rows/columns reach 1,048,576 rows
  and 1,024 columns like LibreOffice's. `OdsReader.Read` is the inverse and tolerant (unknown elements skipped; empty repeated rows are
  never materialized, content rows at most 65,536 times; the default row height / column width are the most common ones); notes for
  `Workbook.UnsupportedContent`. `OdsWriter.Losses(workbook)` lists in German what an .ods cannot hold (charts, tables as such,
  colour scales/data bars/icon sets, untranslatable formulas, outlines, pattern fills, macros, rich text, protection password …);
  `CalcEditor.ConfirmSaveAsync` asks before saving that (once per set of losses); `OpeningWarning` says what an .ods had that is not
  taken over. `CalcDocument` sniffs the mimetype (`OdsReader.IsOds`), `WorkbookFileFormat.Ods`.
- `OdsFormulas`: model → OpenFormula on the AST (`[.A1]`, `[$Tabelle2.A1:.B3]`, `[.A:.A]`, `;`, `~` union, `!` intersection, `{1;2|3;4}`,
  TRUE()/FALSE(), Excel's newer functions as `COM.MICROSOFT.…` unless OpenFormula has them, `LEGACY.…` distributions, structured
  references as the range they name now); OpenFormula → canonical text by a small translator (null for LibreOffice-only functions,
  external references, unions: the cell keeps its value, the opening warning names it).
- `OdsNumberFormats`: format codes ⇄ data styles (number, percentage, scientific, fraction, currency with `[$€-407]` ⇄ language/country,
  date/time incl. `[h]`, text, literals, colours, sections as `style:map`); `OdsCellStyles`: fonts (`OdfFont`), fill, borders (width →
  thin/medium/thick/hair), alignment, rotation, wrap, shrink, protection. `OdsFeatures`: header/footer codes, validations, filters,
  conditional formats. Column widths: characters ⇄ points with Excel's 7 px + 5 px rule (exact both ways).
- LibreOffice is not installed here: fixtures are hand-written in its style; interop with its real output is unverified.
### Adding a command to ThixCalc

1. An id in `CalcCommandIds` (`calc.<name>`; menu entries `calc.<menu>.<entry>`; value commands note their value).
2. Register it in `CalcCommands` / `CalcStyleCommands` / `CalcDataCommands` / `CalcPageCommands`: German label, sprite icon,
   `Shortcut`/`MacShortcut` (no collision with text editing on the Mac), `Description`, `Keywords`, `CanExecute` (usually `Ready`: no cell
   being edited), `IsChecked` from `Session.ActiveStyle`/the sheet. Act through `With(session => …)` (ends the input first). The logic
   belongs in `EditingSession` (core, undoable, UI-free) – the command only calls it.
3. Place it in `CalcRibbon` (and `CalcContextMenus` when it belongs to a right click). A test through real input in tests/ThixCalc.Tests.

### Adding a function

1. In ThixCalc.Core/Functions (the thematic file): `registry.Add("NAME", min, max, kinds, body)`; the German name in
   `GermanFunctionNames`; newer than Excel 2007 → `FutureFunctions` (written with _xlfn.). A function that cannot run offline
   (CUBE…, RTD, STOCKHISTORY, IMAGE, TRANSLATE, COPILOT …) goes to `UnsupportedFunctions` (`unsupported: true`: the body throws
   `UnavailableFunctionException`, a cell keeps the value of its file, a new one shows #DATEN_ABRUFEN). A function name without
   parentheses is an eta lambda (`NameNode.EtaFunction`, file `_xleta.SUM`).
2. Its help in `FunctionHelpTexts.German` (`FunctionHelp.German` for the most used ones, which come first in the lists):
   description and argument names as German Excel names them, `[optional]`, repeats.
3. Tests in tests/ThixCalc.Core.Tests/Functions with Microsoft's documented results. `FunctionCoverageTests` hold the library
   against `ExcelFunctions.tsv` (every Excel function: English, German, category, since): implemented or unsupported, the German
   name, German help. The function then shows in the list while typing, the argument tip, Funktion einfügen, Funktionsargumente,
   the Formeln tab's category menu (ALL functions of a category) and "Was möchten Sie tun?" (by German or English name).

## ThixShow's editor

- **Per window:** `ShowDocument` (Presentation + EditingSession) → `ShowEditor` (an `OfficeEditor`): the normal view is a grid of the
  thumbnail pane (`SlideListView`, own drawing, virtualized: only thumbnails in the scroll viewer's view are drawn; sections collapse),
  the slide (`SlideCanvas` in a ScrollViewer, the `SlideOverlay` above it) and the notes (`NotesPane`, plain text committed as one step
  on leaving it / switching slides / saving), the `AnimationPane` at the right; the sorter is a second `SlideListView` (grid); the
  reading view and the full-screen show share `ShowController` + `ShowSurface`. `FocusedArea` (Slide / Slides / Notes) decides where
  Entf, Kopieren, Strg+A act. Status bar: "Folie X von Y" (`ShowEditor.SlideItem`), Notizen, the views, Bildschirmpräsentation,
  "An Fenster anpassen" (`FitsWindow` keeps the zoom fitted until a zoom is chosen).
- **Drawing:** slides are recorded once per slide instance into `SKPicture`s (`SlidePictures`: editor with prompts, thumbnails
  without; a design or order change records anew) from `SlideRenderer` display lists in points and drawn scaled (crisp at any zoom/DPI).
  Selection, handles, guides, caret and text selection are chrome on the overlay (Mat tokens, `SelectionHandles`); the caret blinks
  without redrawing the slide. Vorschau plays the slide on the canvas through a `ShowController`.
- **Pointer (`SlideCanvas.Input.cs`):** a press finds a handle of a selected shape (resize 0–7, rotate at a fixed screen distance,
  adjust, line ends), else text being edited, else the shape under it (`SlideHitTesting.HitTest`; double click opens a group): text shapes
  get the caret at once (a press on the frame band moves), others select and move. Drags keep slide-point coordinates and snap through
  `Snapping.Snap/SnapPoint` (Alt frees); release = ONE session command (`SetBounds`, `MoveShapes`, `Rotate`, `DragAdjustHandle`;
  Strg+drag = `DuplicateShapes` + move in a `Group`). Resizing a rotated shape works along its own axes; children of groups map back
  into the child space. Formen/Textfeld set `PendingShape`: the next drag draws it.
- **Text (`SlideCanvas.Keys.cs`):** `TextGeometry.Of` lays the edited body out where the renderer draws it (`PlacedTextLayout`): caret line,
  hit test, selection quads; ↑/↓ by lines through the layout; typing, Rücktaste/Entf, Eingabe, Umschalt+Eingabe, Tab levels, Esc go to the
  session. `SlideInputClient` gives the IME the caret's rectangle and paragraph (preedit drawn by the overlay).
- **Commands:** `ShowCommands` (+ Drawing/Design/Context/Chart files) register on the window; aliases (`AddAliases`) give second ribbon
  places and the custom controls their commands. Contextual tabs follow the selection in `ShowContextMenus.UpdateContextTabs` (contexts
  `shape`, `picture`, `table`, `chart`). Galleries with pictures are `RibbonFlyoutButton`s / `DesignStrip` made in
  `ShowGalleries.Create` (`App.CreateRibbonContent`); plain galleries (Übergänge, Animationen, Schnellformatvorlagen) are `MatRibbonGallery`
  bound to value commands. German PowerPoint shortcuts are listed in `ShowCommands`' summary and the README.
- **Show:** `ShowController` runs the core's `SlideShow` on a `Stopwatch`; its `DispatcherTimer` (60 fps) runs only while
  `IsAnimating` (`IsTicking` for tests). `SlideShowWindow` (borderless, full screen on the audience's screen: `ShowSettings.AudienceScreen`
  or the non-primary one) + `PresenterWindow` (on the other screen when there are two and `UsePresenterView`); `ShowNavigation` holds the
  keys, wheel and menu. Esc/end closes both and selects the slide the show was on in the editor.
- **Colours:** chrome Mat tokens only; document colours (paper, the show's black/white, palettes of the theme, the sample picture) only in
  `Editor/ShowPalette.cs` – the file the colour rules test exempts.
- **Files:** `ShowEditor.ConfirmSaveAsync` asks before .pptx drops macros; `OpeningWarning` lists `UnsupportedContent` and macros.
- **Snapshots** (`-- --snapshot <folder>`, `THIXSHOW_SCREENSHOTS` for the tests): the shell's scenes plus `main-content` (chart selected),
  `main-design`, `main-shapes` (gallery open), `main-transitions`, `main-animations` (pane open), `main-sorter`, `presenter`,
  `backstage-print` on `SampleDeck` ("Nordlicht Reisen – Jahresrückblick 2026"). Self test: design, typed title, slide, shape, picture,
  table, chart, transition, animation, pptx round trip, Skia slide, show engine, PDF, the sample through pptx.

### Adding a command to ThixShow

1. An id in `ShowCommandIds` (`show.<name>`; entries of a menu `show.<menu>.<entry>`; value commands note their value).
2. Register it in `ShowCommands` (or the Drawing/Design/Context/Chart file of its tab): German label, sprite icon, `Shortcut`/`MacShortcut`,
   `Description`, `CanExecute` (what it needs: a slide, shapes, text, a table cell, a chart), `IsChecked` from `Session.GetFormatting()` or the
   slide. Act through `run(label, editor => editor.Session.…)` – the logic belongs in ThixShow.Core's `EditingSession` (undoable, UI-free).
3. Place it in `ShowRibbon` (a second place needs an alias in `AddAliases`) and in `ShowContextMenus` when it belongs to a right click.
4. A test through real input in tests/ThixShow.Tests; look at the snapshots.

## Build / run / test

- `dotnet build ThixOffice.slnx` – expected: 0 warnings, 0 errors. `dotnet test ThixOffice.slnx` – all green.
- ThixCalc the same way: `dotnet run --project src/ThixCalc` (`THIXCALC_DATA_DIR`), snapshots `-- --snapshot <folder>` (scenes
  `-main-formula`, `-main-view`, `-main-find`, `-dialog-format-cells`, `-dialog-function`, `-dialog-page-setup`, `-backstage-print`,
  `-main-chart`, `-main-chart-design`, `-main-table`, `-main-pivot`, `-main-pivot-design`, `-dialog-function-arguments` on the budget sample `SampleWorkbook`), `THIXCALC_SCREENSHOTS=<folder> dotnet test
  tests/ThixCalc.Tests` (also every chart type into `<folder>/charts`: `ChartPictureTests`); its trimmed publish (~46 MB) runs
  `--self-test` (typed values and German formulas incl. SVERWEIS/SUMMEWENN/XVERWEIS/GRUPPIERENNACH, format, sort, a chart through the ribbon command, xlsx round
  trip with the chart, a Skia frame and a chart picture, the sample with its charts, PDF, GDI print).
- ThixShow the same way: `dotnet run --project src/ThixShow` (`THIXSHOW_DATA_DIR`), snapshots and self test as above (its trimmed publish
  ≈ 45 MB runs the self test in CI).
- Run: `dotnet run --project src/ThixWrite`; with `THIXWRITE_DATA_DIR=<folder>` an instance of its own (settings, recent list,
  recovery in that folder) that never meets the installed one. A development build never runs the setup.
- **Real-world corpus:** tests/ThixWrite.Core.Tests/Fixtures/RealWorld (generated by `RealWorld/CorpusDocuments` +
  `WordPackage`/`WordStyles`/`OfficeTheme`/`Wml` the way German Word writes docx – `THIXWRITE_CORPUS_WRITE=1 dotnet test
  tests/ThixWrite.Core.Tests --filter Regenerate` rewrites them – plus ApachePOI/ files Word wrote, Apache 2.0). `RealWorldTests`: every
  file opens, lays out and round-trips without losing a part, plus facts per defect class. LOOK at them:
  `THIXWRITE_RENDER=<folder> [THIXWRITE_RENDER_FILTER=<part of a name>] dotnet test tests/ThixWrite.Core.Tests --filter CorpusRendering`
  draws every page as PNG.
- **Snapshots:** `dotnet run --project src/ThixWrite -- --snapshot <folder>` draws every window headless in light and dark
  (`thixwrite-main`, `-main-ribbon-collapsed`, `-backstage-{new,open,saveas,settings,settings-save,settings-updates,about}`,
  `-dialog-save-changes`, `-setup-{welcome,options,update,uninstall}` on Windows, and the app's scenes
  (`backstage-open-signin`, `-backstage-open-drive`, `-backstage-saveas-drive`, `-backstage-settings-cloud`, `-main-drive-pending`,
  `-dialog-drive-conflict` for an app with a cloud id, against SampleCloud) and the app's scenes
  (`OfficeApplication.SnapshotScenes`; ThixWrite: `-main-layout`, `-main-view`, `-main-find`, `-dialog-page-setup`,
  `-main-table`, `-main-picture`, `-main-header`, `-backstage-print`, `-backstage-export`, `-main-word-floating` and `-main-word-letter` = two corpus documents embedded in the app, `-main-design`)): the sample report (`SampleDocument`), version 0.1.0, ANNA-PC, C:\Users\Anna\Dokumente, a sample recent
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
- **MatCloud contract** (`src/ThixOffice.Core/MatCloud/`) is a COPY (1.5.0): run `node tools/contract-copy.js <path>/ThixOffice/src/ThixOffice.Core`
  inside MatCloud; `CopyTests` holds it.
- **Integration run against a real MatCloud:** a THROWAWAY instance only (own compose project name, own port, tmpfs for /data –
  `uid=1654` – and for the database, `MATCLOUD_ADMIN_USER/PASSWORD` create the account; never the ports 4950–4952 or the volumes of a
  local instance; `docker compose down -v --rmi local` afterwards), then `THIXOFFICE_MATCLOUD_URL=… THIXOFFICE_MATCLOUD_USER=…
  THIXOFFICE_MATCLOUD_PASSWORD=… dotnet test tests/ThixOffice.Core.Tests --filter MatCloudIntegrationTests` (without them it does nothing); the
  contacts the same way: `dotnet test tests/ThixOffice.Pim.Tests --filter MatCloudContactsIntegrationTests` (needs MatCloud with contract 1.5.0); ThixMail's
  calendar and people: `dotnet test tests/ThixMail.Core.Tests --filter MatCloudEmbeddingIntegrationTests` (the account must be the administrator; MatCloud
  lets ten sign-ins a minute through – the tests wait for the next window, so a run takes about a minute).

## Rules

- **Microsoft C# coding conventions**, file-scoped namespaces, `_camelCase` fields, human-readable names, early returns, no
  needless nesting.
- **Business layer = static classes** in ThixOffice.Core. UI and models never touch files, the registry or HTTP: pages call
  `DocumentService`, `RecentDocuments`, `SettingsService`, `FileAssociations` …; file dialogs only return paths. Validation **and
  permission checks** (AccessPolicy) live in the business layer. Errors are `BusinessException(message, field)`; the UI shows them
  at the `MatFormField` whose `controls:FormErrors.Field` is that field, or in a message.
- **Documents are never lost:** save atomically (temporary file, then replace), recovery files while unsaved, ask before closing
  (Speichern | Abbrechen | gap | Nicht speichern), never open a format the app cannot read (planned types are not registered and
  are refused), never take a file type silently (FileAssociations: "Öffnen mit" for every supported type; the default only for the
  types the user chose in the setup / ThixLauncher – installer.json, SETUP-08; Windows' own UserChoice stays). Updates install when the app closes.
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
  (manifest `<id>-update.json` with `"installer"` = src/<App>/installer.json, sparse checkout; rolling releases `release-channel` / `nightly-channel` SHARED by all apps – only the app's own
  files are uploaded with `--clobber`, the release keeps its title; one concurrency group per app and branch, `thixoffice-publish-<id>-<branch>` – a group shared by all apps cancelled waiting publish jobs;
  versioned release `<id>-v<version>` on main). Windows publishes even when macOS fails.
- Launcher (docs/PLAN.md section 6): per app id, manifest, `<App>Setup.exe`, `<App>-macOS-<arch>.zip/.dmg`, bundle id; the uninstall
  entry has DisplayVersion / InstallLocation / DisplayIcon / UninstallString / QuietUninstallString; macOS `CFBundleGetInfoString`
  is "<App> <version>"; `branding/launcher/<id>.png` is the catalog logo.

## Adding an app to the suite (ThixCalc, ThixShow, ThixContacts)

A workspace app (one window over its own data, like ThixContacts) sets `Kind = OfficeAppKind.Workspace`, returns its workspace document from
`CreateDocument` (never dirty), puts its pages into `CreateBackstageItems`, imports handed-over files in `ImportFilesAsync`, and lists its import
types as `DocumentTypes` (installer.json offers them unticked).

1. `src/ThixCalc.Core`: `ThixCalcApp.Definition` (id `thixcalc`, name `ThixCalc`, accent, description, document types – formats
   without a reader are `Planned`; `UntitledBaseName` "Mappe", `BlankDocumentLabel` "Leere Arbeitsmappe", `CloudAppId = CloudApps.Calc` for ThixDrive) and the document model
   (derive from `OfficeDocument`).
2. `src/ThixCalc`: copy ThixWrite's csproj / app.manifest / App.axaml / Program.cs; `App : OfficeApplication` with
   `CreateDocument`, `CreateEditor` (an `OfficeEditor`), `AddCommands`, `Ribbon`; `Program.Main => OfficeProgram.Run<App>(…)`.
3. Major/Minor already exist in `Directory.Build.props` (`ThixCalcMajor/Minor`); add the projects to `ThixOffice.slnx`, a test
   project `tests/ThixCalc.Tests`, and `InternalsVisibleTo` entries in ThixOffice.Core / ThixOffice.Desktop if the tests need them.
4. Logos exist (`branding/thixcalc`); `node tools/render-icons.js` fills `src/ThixCalc/Assets` once the project exists.
5. `src/ThixCalc/installer.json` (embedded; every supported type, the own format on; a test holds it to the definition);
   `scripts/macos-document-types/thixcalc.plist`; `.github/workflows/thixcalc.yml` = a copy of thixwrite.yml with its paths,
   app name/id, project and tests.
6. Launcher: catalog entry, logo `branding/launcher/thixcalc.png` (on a branch of ThixLauncher, see PLAN section 6).
