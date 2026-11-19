# ThixOffice – Plan

Stand: 2026-11-19. Dieser Plan ist die Arbeitsgrundlage; er wird mit jedem Meilenstein fortgeschrieben.

## 1. Ziel

Eine Office-Suite aus einzelnen Anwendungen, die Windows-Nutzer sofort bedienen können
(Menüband und Funktionen dort, wo Word/Excel/PowerPoint sie haben) und die aussieht wie die
übrigen Thix-Apps (MatDesktop, „Apple-like“).

| Microsoft | ThixOffice | Format (Standard / zusätzlich) | Reihenfolge |
| --- | --- | --- | --- |
| Word | **ThixWrite** | docx / odt | 1 |
| Excel | **ThixCalc** | xlsx / ods, csv | 2 |
| PowerPoint | **ThixShow** | pptx / odp | 3 |
| Outlook | ThixMail | – | später |
| Contacts | ThixContacts | vCard, CardDAV | später |
| Calendar | ThixCalendar (existiert als eigenes Repo) | iCalendar, CalDAV | später, Fragen offen |
| Access | ThixData | SQL | später |

## 2. Entscheidungen (mit Matthias am 2026-11-18 getroffen)

1. **Editoren nativ in C#/Avalonia.** Kein WebView, keine JS-Editoren im Desktop.
2. **Office-Formate sind Standard**, ODF kommt je App nach dem ersten Release als Import/Export dazu.
3. **Drive wird ein Modul in MatCloud** (= ThixCloud): Web-App plus Datei-API mit Geräte-Token.
   Die Dateidienste werden aus MatDrive portiert.
4. **MatOffice wird in der Cloud nicht verwendet.** Die Web-Apps werden neu gedacht und lehnen
   sich an die ThixOffice-Desktop-Apps an (gleiche Bedienung, gleiches Menüband, gemeinsamer Core).
   ThixOffice hängt von keinem MatOffice-Code ab.
5. **Namen** laut Tabelle: ThixWrite, ThixCalc, ThixShow.
6. Planung mit Fable 5.2, Umsetzung mit Opus 5.5 (medium).

Noch offen (wird zum jeweiligen Zeitpunkt entschieden, blockiert jetzt nichts):

- **Technik der Web-Apps** (Phase 6): entweder dieselbe Avalonia-Oberfläche per WebAssembly im
  Browser oder eigene Razor-Pages-Editoren auf demselben Core. Deshalb gilt ab sofort: Dokumentmodell,
  Befehle, Layout-Berechnung und Formel-Engine liegen UI-frei im Core; die Avalonia-Controls sind
  nur Darstellung und Eingabe. So bleiben beide Wege offen.
- **Kalender/Mail/Kontakte/Data**: eigene Planungsrunde.

## 3. Was aus den bestehenden Thix-Apps übernommen wird

Referenzen: ThixCalendar (Aufbau, Setup, OTA, Cloud), ThixClip (Optik, `--snapshot`),
ThixNote (Dokument-App: Dateizuordnungen, Update beim Beenden), ThixFred (CI, das Windows auch
dann veröffentlicht, wenn macOS scheitert).

- .NET 10 (`global.json` 10.0.100, latestMinor), Avalonia 11.3.22, `.slnx`, xunit + Avalonia.Headless.
- MatDesktop als geprüfte Kopie aus MatDesign (Branch `feature/desktop-and-services`), nie im
  App-Repo ändern. Nur `{DynamicResource Mat.*}`-Farben, Icons nur aus dem MatUi-Sprite.
- Business-Layer als statische Klassen im Core; `BusinessException(message, field)`; UI fasst
  weder Dateien noch Registry noch HTTP an. Berechtigungs-/Policy-Prüfung immer im Business-Layer
  (`AccessPolicy`, HKLM `SOFTWARE\Policies\<App>`).
- Versionierung aus `Directory.Build.props`: Release `<major>.<minor>.<build>-<yyyyMMdd>` (main),
  Nightly `nightly-<build>-<yyyyMMdd>` (dev), Local `local-<yyyyMMdd>`.
- Setup: Die Single-File-Exe installiert sich selbst pro Benutzer (`--update`, `--uninstall`,
  `--quiet`, Uninstall-Eintrag in HKCU). macOS: `.app` als `.zip` (OTA) und `.dmg`.
- OTA über Channel-Manifest `<app>-update.json` in `release-channel` / `nightly-channel`,
  SHA-256-Prüfung; „Updates automatisch installieren“ installiert **beim Beenden** (wie ThixNote).
- Autostart als Einstellung (Windows Run-Key, macOS LaunchAgent), bei Editoren standardmäßig aus.
- ThixCloud: kopierter MatCloud-Vertrag, Anmeldung per Browser-Link, Passwort oder Launcher-Übergabe (umgesetzt, Phase 2).
- Oberfläche deutsch, Code/Kommentare englisch. Commits nur unter Matthias Schmoldt, ohne Trailer.

## 4. Architektur

```
ThixOffice.slnx
Directory.Build.props        Versionierung; Major/Minor je App
branding/<app>/              SVG-Master + gerenderte Größen
tools/render-icons.js        Logo-Renderer (Edge, playwright-core)
scripts/package-macos.sh     parametrisiert je App
.github/workflows/           _desktop.yml (wiederverwendbar) + thixwrite.yml, thixcalc.yml, thixshow.yml
src/
  ThixOffice.Core/           UI-frei, von allen Apps genutzt
    Models/                  gemeinsame Typen (Farben, Schrift, Maße, Bilder, Dokument-Metadaten)
    Business/                DocumentFiles, RecentDocuments, SettingsService, AccessPolicy,
                             InstallService, UpdateService, AutostartService, FileAssociations,
                             CloudService, DriveService
    Formats/                 gemeinsame OOXML-/ODF-Bausteine (Paket, Themes, Farben, Einheiten)
    Infrastructure/ Platform/ MatCloud/ (Vertragskopie)
  ThixOffice.Desktop/        gemeinsame Avalonia-Oberfläche
    MatDesktop/              Kopie
    Shell/                   OfficeWindow: Titelleiste, Menüband, Backstage, Statusleiste
    Pages/ Dialogs/          Einstellungen (Tabs), About, Drive-Dateidialog, Setup, Update
    OfficeApp.cs             gemeinsamer Start: Single-Instance, Setup-Modi, Self-Test, Snapshot
  ThixWrite.Core/            Dokumentmodell, Befehle/Undo, Layout (Umbruch, Seiten), docx/odt
  ThixWrite/                 Editor-Control, Menüband-Definition, App-Start
  ThixCalc.Core/  ThixCalc/  Zellmodell, Formel-Engine, xlsx/ods/csv – Raster-Control
  ThixShow.Core/  ThixShow/  Folienmodell, pptx/odp – Folien-Canvas, Präsentationsmodus
tests/                       je Projekt ein Testprojekt, Headless-UI-Tests, Rundreise-Tests der Formate
```

Eine App definiert nur: Akzentfarbe, Logo, Dokumenttypen, Menüband-Inhalt, Editor. Alles andere
kommt aus `ThixOffice.Core` und `ThixOffice.Desktop`.

### Fenster (Thix-Optik + Office-Bedienung)

1. **Titelleiste** (`MatTitleBar`, bleibt wie in den Thix-Apps): Logo, Wortmarke „Thix**Write**“,
   Schnellzugriff (Speichern, Rückgängig, Wiederholen), Dokumentname, in der Mitte die **Suche**
   („Was möchten Sie tun?“ – durchsucht Befehle und das Dokument), rechts Cloud-Status und Fensterknöpfe.
2. **Menüband mit Karteikarten** (neu: `MatRibbon`): Datei | Start | Einfügen | Layout | … | Ansicht.
   Gruppen mit Beschriftung, große/kleine Schaltflächen, Kombinationsfelder, Farbwähler, Galerien;
   einklappbar (Strg+F1), Tastaturtipps (Alt), verkleinert Gruppen bei schmalem Fenster.
   Deklarativ beschrieben, damit die Web-Apps dieselbe Definition verwenden können.
3. **Datei = Backstage** über das ganze Fenster: Neu, Öffnen (Dieser PC / ThixCloud Drive / Zuletzt),
   Speichern, Speichern unter, Drucken, Exportieren (PDF), Konto/Cloud, Einstellungen.
   **Unten links die Versionsnummer → About.**
4. **Arbeitsfläche** mit dem Editor, darunter die **Statusleiste** (Seite/Wörter bzw. Summe/Anzahl, Zoom).

Das letzte Dokumentfenster schließen beendet die App unter Windows (wie Word); unter macOS bleibt sie wie jede Mac-App
im Dock und in der Menüleiste, ein Klick aufs Dock-Symbol öffnet ein neues Dokument, ⌘Q beendet sie (entschieden 2026-11-18).
Die Kurzbefehle folgen dem System: Strg+S/Z/Y/N/O/W, F12, Strg+F1 unter Windows; ⌘S/Z, ⇧⌘Z, ⌘N/O/W, ⇧⌘S, ⌥⌘R unter macOS.

Die bisherigen Thix-Apps haben eine linke Navigation statt eines Menübands. Für Dokument-Apps
ersetzt das Menüband die Navigation; Listen-Seiten (z. B. Drive-Dialog, Zuletzt) folgen weiter
den UI-Richtlinien (Toolbar oben, Aktionen unten links, Löschen abgesetzt).

`MatRibbon`, Backstage und fehlende Icons gehören nach den Hausregeln zuerst nach **MatDesign**
(MatDesktop 0.3.0) und kommen als Kopie zurück.

### Akzentfarben

ThixWrite Blau `#185ABD`, ThixCalc Grün `#107C41`, ThixShow Orange `#C43E1C` – die Farben, an denen
Windows-Nutzer die Apps erkennen. Logo: Thix-Kachel mit Verlauf in der Akzentfarbe, je App ein
eigenes Zeichen, in den Detailstufen icon / small / tray / full / document.

## 5. Phasen

Jede Phase endet mit grünem Build, Tests, Push nach `dev` (Nightly) und – wenn stabil – Merge nach
`main` (Release). Zwischenstände werden laufend gepusht.

### Phase 0 – Fundament
- Repo: `.slnx`, `Directory.Build.props`, `global.json`, `.gitignore`, `CLAUDE.md`, `README.md`, Branches `main`/`dev`.
- `ThixOffice.Core`: Einstellungen, zuletzt verwendete Dokumente, Install/Update/Autostart/Policy,
  Dateizuordnungen, AppInfo; MatCloud-Vertragskopie.
- `ThixOffice.Desktop`: MatDesktop-Kopie, `OfficeWindow`, Backstage, Statusleiste, Einstellungen, About, Setup.
- MatDesign: `MatRibbon` + Backstage + fehlende Icons (Branch `feature/ribbon`), Kopie nach ThixOffice.
- CI: wiederverwendbarer Desktop-Workflow, je App ein Aufrufer (eigene Buildnummern, eigene
  Manifest- und Paketnamen, gemeinsame Concurrency-Gruppe fürs Veröffentlichen, Tags `thixwrite-v…`).
- Logos für ThixWrite/ThixCalc/ThixShow.

### Phase 1 – ThixWrite
- **1a (erstes Release):** Modell (Abschnitte, Absätze, Läufe, Formatvorlagen, Listen), natives
  Seiten-Layout mit Umbruch, Cursor/Auswahl/IME, Zeichen- und Absatzformat, Formatvorlagen,
  Aufzählung/Nummerierung, Rückgängig, Zwischenablage, Suchen/Ersetzen, Seite einrichten,
  docx lesen/schreiben, PDF-Export/Drucken, Zoom. Menüband: Datei, Start, Einfügen, Layout, Ansicht.
- **Stand 1a (2026-11-19): erledigt** bis auf das direkte Drucken. Nativer Editor (Seiten als Skia-Bilder, nur sichtbare
  Seiten gezeichnet, Zoom 10–500 % inkl. Strg+Mausrad / Seitenbreite / Ganze Seite, Papier weiß auch im dunklen Modus),
  Tastatur mit Windows- und Mac-Konventionen, Maus (Klick, Umschalt+Klick, Ziehen mit Auto-Scroll, Doppel-/Dreifachklick),
  Eingabemethoden (IME-Client mit Umgebungstext und Cursor-Rechteck), Zwischenablage (eigenes Fragment + Text, „Nur Text
  einfügen“), Menüband Start / Einfügen / Layout / Ansicht mit Zustand aus der Auswahl, Suchen/Ersetzen-Leiste, Dialoge Seite
  einrichten und Zoom, Datei › Exportieren (PDF), docx als Standardformat (Dateizuordnung Windows + macOS-Plist als
  „Alternate“), Hinweis bei nicht unterstützten Inhalten, Statusleiste (Seite X von Y, Wörter), Selbsttest der getrimmten exe.
  **Lücken (→ 1b):** Lineal, Format übertragen, Symbol einfügen, Dialoge Schriftart… /
  Absatz…, Kontextmenü ohne Format-Einträge, IME-Komposition als Überlagerung (nicht im
  Textfluss), Zwischenablage-Import von HTML/RTF aus anderen Programmen, Formatvorlagen-Galerie mit festen Einträgen.
- **Stand 1b (2026-11-19):** MatDesktop 0.3.1 übernommen. **Drucken erledigt:** Datei › Drucken (Strg+P) wie in Word –
  Drucker, Exemplare, Alle Seiten / Aktuelle Seite / Benutzerdefiniert („1-3, 5“), Ausrichtung und Papier aus dem Dokument,
  Vorschau mit Blättern. Windows druckt über GDI (P/Invoke, kein System.Drawing/WPF, net10.0 bleibt; getrimmt geprüft im
  Selbsttest über „Microsoft Print to PDF“): Seiten als Skia-Bänder in Druckerauflösung (max. 600 dpi), Papier/Ausrichtung je
  Seite. macOS druckt über CUPS (libcups): die gewählten Seiten als PDF an cupsPrintFile. Richtlinie DisablePrinting.
- **Tabellen erledigt (1b):** Modell (Zellabsätze im Textfluss mit Zellbezug, unveränderliche Tabellenstruktur), Layout (Raster,
  verbundene Zellen waagrecht/senkrecht, Rahmen, Schattierung, Zellränder, vertikale Ausrichtung, Zeilen über Seitenumbrüche,
  wiederholte Überschriftszeilen, Tabellenausrichtung), Bearbeiten (Tab/Umschalt+Tab, Tab in der letzten Zelle fügt eine Zeile an,
  Zell-/Zeilen-/Spalten-/Tabellenauswahl, Löschen wie Word), Einfügen › Tabelle (Raster 10 × 8 und Dialog), Kontext-Karten
  Tabellenentwurf und Layout, Spaltenbreite per Ziehen, docx lesen/schreiben (unbekannte Eigenschaften bleiben erhalten;
  verschachtelte Tabellen bleiben als Ganzes erhalten und werden gemeldet). **Lücken:** verschachtelte Tabellen bearbeiten,
  bedingte Formate der Tabellenformatvorlagen (w:tblStylePr), Formatvorlagen-Galerie für Tabellen, automatische Spaltenbreite nach
  Inhalt (das Raster aus der Datei gilt), Zeilen-/Spaltenauswahl per Klick am Rand (über Layout › Auswählen).
- **Bilder erledigt (1b):** PNG, JPEG, GIF, BMP aus einer Datei, aus der Zwischenablage und per Ziehen; Auswahlrahmen mit
  Ziehpunkten (Ecken proportional, Umschalt frei), Kontext-Karte Bildformat (Alternativtext, Bild ändern, Größe zurücksetzen,
  Ausrichtung, Höhe/Breite); docx lesen/schreiben (Medienteil je Bild einmal; frei positionierte Bilder werden im Textfluss
  angezeigt, gemeldet und mit ihrer Position zurückgeschrieben). **Lücken:** SVG (SkiaSharp 2.88 hat keinen SVG-Decoder),
  Zuschneiden, Bildeffekte, Textumbruch um frei positionierte Bilder, EMF/WMF bleiben Platzhalter.
- **Kopf-/Fußzeilen und Seitenzahlen erledigt (1b):** je Abschnitt Standard / erste Seite / gerade Seiten (geerbt vom
  Abschnitt davor), Bearbeiten an Ort und Stelle per Doppelklick (Text abgeblendet, Karte Kopf- und Fußzeile: Seitenzahl,
  Seitenanzahl, Datum, Erste Seite anders, Gerade & ungerade Seiten unterschiedlich, Schließen), Einfügen › Kopfzeile / Fußzeile /
  Seitenzahl (oben/unten, links/zentriert/rechts), Felder PAGE / NUMPAGES / DATE, docx lesen/schreiben (Teile werden
  wiederverwendet, andere Felder bleiben mit ihrem Ergebnis erhalten). **Lücken:** hohe Kopfzeilen schieben den Text nicht nach
  unten, Seitenzahlformate und Neubeginn (w:pgNumType) werden nur erhalten, Tabellen in Kopfzeilen werden als Absätze gezeigt.
- **Links, Lineal, Dialoge erledigt (1b):** Link einfügen/bearbeiten (Strg+K), Strg+Klick öffnet nur http/https/mailto (Prüfung im
  Business-Layer), Tooltip; Lineal mit Rändern, Einzügen und Tabstopps (ziehen, klicken), Dialoge Schriftart (Strg+D) und Absatz
  (mit Tabstopps) als Gruppen-Starter, Format übertragen, Symbol (Menü + Dialog). **Lücken:** Links auf Textmarken (Sprung im
  Dokument), Format übertragen per Doppelklick dauerhaft, Schriftfarbe/Abstände im Schriftart-Dialog, Lineal im Kopfzeilen-Modus.
- **Einfügen aus anderen Programmen erledigt (1b):** HTML der Zwischenablage (Windows „HTML Format“, macOS public.html) mit
  Absätzen, Überschriften, Zeichenformat, Listen, Tabellen, Links (nur http/https/mailto) und eingebetteten Bildern (data:-URIs);
  Skripte, Stile, Rahmen und Formulare fallen weg, nichts wird nachgeladen. **Lücken:** RTF, Tabellenformatierung aus HTML
  (Rahmen/Farben der Zellen), CSS-Klassen (nur style-Attribute).
- **Stand 1b insgesamt: erledigt** (Lücken siehe die Punkte oben).
- **1b:** Drucken, Tabellen, Bilder, Kopf-/Fußzeilen, Seitenzahlen, Hyperlinks, Tabstopps (Lineal), Dialoge Schriftart/Absatz,
  HTML/RTF einfügen.
- **1c:** Inhaltsverzeichnis, Fußnoten, Kommentare, Rechtschreibprüfung, odt; Karten Referenzen/Überprüfen.
- **Fußnoten und Endnoten erledigt (1c):** Modell (die Notiz reist mit ihrem Verweis: `NoteInline` mit dem Text als Story; Löschen,
  Kopieren, Rückgängig ohne zweite Liste), Nummerierung in Dokumentreihenfolge (Fußnoten 1, 2, 3; Endnoten i, ii, iii), Layout am
  Seitenende unter einem kurzen Trennstrich (eine Zeile mit Verweis kommt nur auf die Seite, wenn kurze Notizen ganz und lange mit
  zwei Zeilen passen; der Rest läuft unter einem Strich über die ganze Breite auf der nächsten Seite weiter), Endnoten nach dem Text,
  Bearbeiten an Ort und Stelle (Klick auf die Notiz, Doppelklick auf die Nummer; Esc / Klick in den Text / Speichern übernimmt als
  ein Schritt), Referenzen › Fußnote einfügen (Strg+Alt+F), Endnote einfügen (Strg+Alt+D), Nächste/Vorherige Fuß-/Endnote, docx
  (footnotes.xml/endnotes.xml: Trennlinien bleiben, Ids werden wiederverwendet, Notizen, auf die erhaltenes XML zeigt, bleiben).
  **Lücken:** Nummernformat/Neubeginn je Abschnitt (w:footnotePr wird nur erhalten), eigene Zeichen (customMarkFollows bleibt
  erhalten, nicht bearbeitbar), Fußnoten in Tabellenzellen schieben die Zeile nicht auf die nächste Seite, Endnoten am
  Abschnittsende, Fußnoten unter dem Text statt am Seitenende.
- **Inhaltsverzeichnis erledigt (1c):** Referenzen › Inhaltsverzeichnis (Überschrift „Inhalt“, Einträge der Gliederungsebenen 1–3 mit
  Punkt-Tabstopp und Seitenzahl, Formatvorlagen Verzeichnis 1–3), Tabelle aktualisieren (neue Überschriften, neue Seitenzahlen, ein
  Schritt), Inhaltsverzeichnis entfernen; jeder Eintrag ist ein Link auf eine Textmarke „_Toc…“ an der Überschrift (Strg+Klick springt;
  Links auf Textmarken springen jetzt allgemein). docx: ein echtes Word-Feld TOC (begin / instrText / separate … end) – Word kann es
  selbst aktualisieren; ein Verzeichnis aus Word (auch im Inhaltssteuerelement „Table of Contents“, mit PAGEREF-Feldern) wird erkannt
  und aktualisiert, Words Textmarken werden wiederverwendet. Ansicht › Navigationsbereich: die Überschriften links, Klick springt.
  **Lücken:** Verzeichnis-Galerie (nur „Automatisches Verzeichnis“), Ebenen/Optionen-Dialog, Seitenzahlen als PAGEREF-Felder (ThixWrite
  schreibt die Zahl als Text), Abbildungs-/Tabellenverzeichnis, Ziehen im Navigationsbereich.
- Danach: Launcher-Eintrag (siehe 6).

### Phase 2 – Drive in ThixCloud
- MatCloud: App-IDs `write`, `calc`, `show`, `drive`; Modul `MatCloud.Apps.Drive` (Dateibaum, Versionen,
  Papierkorb, Freigaben, Blob-Speicher, Chunk-Upload) mit Web-App und Geräte-API `/api/v1/drive/…`.
- ThixOffice: `DriveService` + Drive-Dateidialog; Öffnen/Speichern direkt im Drive, Konflikt über Versionen.
- **Stand (2026-11-19): ThixOffice-Seite erledigt** gegen MatCloud-Vertrag 1.4.0 (Drive-Geräte-API, App-IDs write/calc/show).
  Gemeinsam in ThixOffice.Core/Desktop, eine App schaltet es mit `OfficeAppDefinition.CloudAppId` ein (ThixWrite = `write`):
  `CloudService` (Anmeldung im Browser mit Code, mit Passwort, Übergabe von ThixLauncher still beim Start; Abmelden; Konto;
  Zustand; 401 meldet ab, 403 = „von der Verwaltung abgeschaltet“; Richtlinie DisableCloud), `DriveService` (Ordner mit Seiten
  und Sortierung, Suche, Zuletzt, Neuer Ordner, Umbenennen, Papierkorb, Speicherplatz; Öffnen in eine **lokale Arbeitskopie** mit
  Begleitdatei drive.json – Element-ID, ETag, Server, Konto; Speichern = erst atomar lokal, dann neue Version mit If-Match;
  Konflikt → Als Kopie speichern / Später / Überschreiben; offline → „Nicht hochgeladen“, nie verloren, Wiederholung alle 30 s
  und beim nächsten Öffnen; Kontingent/zu groß als klare Meldung; Speichern unter mit Namenskonflikt → Ersetzen?). Oberfläche:
  Orte in Öffnen/Speichern unter (Zuletzt verwendet, ThixDrive, Dieser PC), der ThixDrive-Browser (Suche, Typfilter, Sortierung,
  Pfad, „Weitere laden“, Aktionen unten), Einstellungen › Cloud, Zustand rechts in der Titelleiste, „Zuletzt verwendet“ mit
  Wolke (öffnet aus dem Drive, offline aus der Arbeitskopie). Geprüft gegen eine MatCloud im Speicher (Tests, Selbsttest,
  Snapshots) und einmal gegen eine echte MatCloud (feature/drive, Wegwerf-Instanz). **Lücken:** Versionen ansehen/wiederherstellen
  und Freigaben nur im Browser; „Zuletzt verwendet“ des Kontos (alle Geräte) wird nicht angezeigt (nur die eigene Liste); Speichern
  unter ohne Verbindung speichert nicht (das Dokument bleibt ungespeichert mit Wiederherstellungsdatei); Hochladen in Stücken über
  64 MB nur über den Vertrag, ohne Fortsetzen nach einem Neustart; kein Konto-Eintrag im Backstage (wie in den Referenz-Apps:
  Einstellungen › Cloud).

### Phase 3 – ThixCalc
Zellmodell, Formel-Engine (Parser, Abhängigkeitsgraph, Funktionsbibliothek), virtualisiertes Raster,
Formate, Sortieren/Filtern, Fixieren, bedingte Formatierung, Diagramme, xlsx/csv, danach ods.

- **Stand (2026-11-19): erstes Release von ThixCalc erledigt** (ohne Diagramme). Core (UI-frei): Zellmodell, Formel-Engine mit über
  400 Funktionen (deutsche Namen, Hilfe mit Argumentnamen), Bearbeiten als rückgängig machbare Befehle, xlsx/xlsm/csv, Viewport mit
  Pixel-Scrollen, Auswahl-Ebene, Seitenumbruch (SheetPagination) mit Seitenlayout und xlsx-Rundreise. App: Raster über die Skia-Lease
  in Gerätepixeln (scharfe Linien bei jedem Zoom/DPI, nur Sichtbares wird gelegt; ein Bild ≈ 1 ms, auch bei 100.000 Zeilen), Zeilen-/
  Spaltenköpfe, fixierte Bereiche, Bildlaufleisten wie Excel, Strg+Mausrad; Eingeben/Bearbeiten/Zeigen mit farbigen Bezügen, F4,
  Funktionsliste und Argument-Tipp, IME in der Zelle; Bearbeitungsleiste mit Namenfeld; alle Excel-Tasten zum Bewegen und Markieren;
  Maus inkl. Ausfüllkästchen, Spaltenbreite/Zeilenhöhe ziehen und optimal; Blattregister (neu, umbenennen, ziehen, Farbe,
  aus-/einblenden, verschieben/kopieren); Statusleiste mit Modus und wählbaren Kennzahlen; Menüband Start/Einfügen/Seitenlayout/
  Formeln/Daten/Überprüfen/Ansicht wie im deutschen Excel, Dialoge Zellen formatieren, Funktion einfügen, Sortieren,
  Datenüberprüfung, Text in Spalten, Duplikate entfernen, Namens-Manager, bedingte Formatierung (Regeln, Datenbalken, Farbskalen,
  Regeln verwalten), Seite einrichten, Zoom, Link, Notiz, Textimport; Suchen/Ersetzen (gemeinsame Leiste); Zwischenablage (eigen,
  Text, HTML-Tabelle hin und zurück); Filterpfeile mit Werteliste und Sortieren, Auswahlliste der Datenüberprüfung neben der Zelle
  (Alt+Pfeil unten), Notiz-Dreieck mit Anzeige beim Zeigen, Links per Strg+Klick; Drucken mit Vorschau (aktives Blatt, Mappe, Auswahl) und PDF; Hinweise bei CSV und Makros;
  ThixDrive über die Shell. Gemeinsam gemacht (ThixWrite nutzt sie mit): Skia-Zeichenoperation, Druck- und Exportseite,
  Dialograhmen, Suchleiste, Kontextmenüs aus Befehlen, Speichern-Rückfrage des Editors.
- **Stand Diagramme (2026-11-19): erledigt.** Gemeinsame, UI-freie Komponente `ThixOffice.Charts` (Modell, Datenanbindung über
  `IChartDataSource`, Layout und Zeichnen in die Display-Liste, DrawingML-Teile `c:chartSpace` lesen/schreiben) – ThixShow und
  ThixWrite binden dieselben Diagramme später ein (Daten aus dem Cache oder einer eingebetteten Mappe). Typen: Säulen/Balken
  (gruppiert, gestapelt, 100 %), Linie (mit Datenpunkten, gestapelt, geglättet beim Lesen), Kreis, Ring, Fläche (gestapelt), Punkt
  (XY; Punkte, gerade oder interpolierte Linien), Verbund Säule + Linie (auch Sekundärachse). Titel, Achsentitel, Rubriken-, Wert- und
  Datumsachse mit automatischer Skalierung wie Excel (Null als Start, 5 % Luft, Einheiten 1-2-5, Dichte nach Höhe), Zahlenformate
  über ThixCalcs Formatierer (`IChartValueFormatter`), Gitternetzlinien, Legende (vier Seiten, umbrechend), Datenbeschriftungen ohne
  Überlappung, Farben aus den Designfarben (Farbig 1–4, Einfarbig 1–6), Lückenbreite/Überlappung, ausgeblendete und leere Zellen.
  In ThixCalc: Diagramme schweben verankert über den Zellen (wählen, verschieben, Größe mit Ziehpunkten, Pfeiltasten, Entf,
  Kopieren/Einfügen, Ebenen), folgen eingefügten Zeilen, umbenannten und kopierten Blättern, Einfügen › Diagramme, Karten
  Diagrammentwurf und Format, Dialoge Diagrammtyp ändern (mit Vorschau) und Datenquelle auswählen, alles rückgängig machbar; Druck
  und PDF mit dem Blatt; xlsx-Rundreise mit Zeichnungsankern (unveränderte Diagramme bleiben Byte für Byte, Bilder/Formen in der
  Zeichnung bleiben erhalten, nicht unterstützte Typen und Chartex werden als beschrifteter Rahmen gezeigt und unverändert gespeichert).
  **Lücken:** „Empfohlene Diagramme“, Diagrammblätter (bleiben erhalten), einzelne Diagrammelemente anklicken und formatieren
  (Formatbereich, Schriftarten, Füllungen je Reihe in der Oberfläche), logarithmische Achsen, Trendlinien und Fehlerindikatoren (nur
  erhalten), mehrstufige Rubriken (gelesen und erhalten, nur innerste Stufe gezeigt), Diagrammformatvorlagen-Galerie (nur Farben).
- **Stand Tabellen (2026-11-19): erledigt.** Einfügen › Tabelle (Strg+T/Strg+L, Dialog mit Bereich und „Tabelle hat
  Überschriften“; ohne Überschriften kommt eine Zeile „Spalte1 …“ darüber), Start › Als Tabelle formatieren, Formatvorlagen Hell 1–21,
  Mittel 1–28, Dunkel in den Designfarben (Galerie: Hell 1, Hell 9, Mittel 2–7), gebänderte Zeilen/Spalten, erste/letzte Spalte,
  Ergebniszeile (TEILERGEBNIS(109;[Spalte]) im Zahlenformat der Spalte), Filterpfeile mit eigenem Filter je Tabelle, automatische
  Erweiterung beim Tippen darunter/daneben, Überschrift aus/ein, Tabellengröße ändern, In Bereich konvertieren (Aussehen bleibt,
  strukturierte Verweise werden zu A1-Bezügen), Umbenennen von Tabelle und Spalte mit Formeln, strukturierte Verweise deutsch
  angezeigt (#Alle, #Daten, #Kopfzeilen, #Ergebnisse, #Diese Zeile, „;“) und englisch gespeichert, Karte Tabellenentwurf, xlsx
  (Tabellenteile neu, geändert, gelöscht; Spaltenformate und berechnete Spalten bleiben). **Lücken:** berechnete Spalten (eine Formel
  füllt nicht von selbst die ganze Spalte), Auswahl der Ergebnisfunktion je Spalte in der Zelle (Dropdown), Tabellenformatvorlagen
  ändern/neu, Datenschnitte.
- **Stand Filter und Einfügen (2026-11-19): erledigt.** Zahlen-, Text- und Datumsfilter aus dem Filtermenü (Benutzerdefinierter
  AutoFilter mit zwei Bedingungen und Und/Oder, Platzhalter * und ?), Top 10 (Elemente/Prozent, oberste/unterste), über/unter dem
  Durchschnitt; Dialog Inhalte einfügen (Strg+Alt+V) mit Vorgang, Leerzellen überspringen, Transponieren; Strg+Plus/Strg+Minus (auch
  Ziffernblock); Ausfüllen › Reihe (arithmetisch, geometrisch, Datum: Tag, Wochentag, Monat, Jahr; Inkrement, Endwert); Alle suchen
  (Liste aller Treffer, Klick springt hin); Rückgängig/Wiederholen innerhalb der Zellbearbeitung (getippte Zeichen als ein Schritt);
  gedrehter und senkrechter Text, An Zellgröße anpassen; Teilformatierung in Zellen bleibt in xlsx erhalten (Anzeige einheitlich).
  **Lücken:** Datumsgruppen (Jahr/Monat/Tag) in der Werteliste und dynamische Datumsfilter (Heute, Diesen Monat …), Farbfilter,
  Teilformatierung anzeigen und bearbeiten, Vorgang für Formeln (nur Konstanten werden verrechnet), gedrehter Text überläuft nicht.
- **Lücken (wichtig für Excel-Umsteiger zuerst):** Blitzvorschau, Fenster teilen, Seitenumbruchvorschau, ods.

### Icons für MatDesign

Das Sprite gehört MatDesign; die MatDesktop-Kopie wird hier nie geändert. ThixCalc nutzt bis dahin Platzhalter. Diese Symbole
fehlen (Name → wo, Platzhalter heute):

| Fehlendes Symbol | Verwendet in ThixCalc | Platzhalter |
| --- | --- | --- |
| `function` (fx) | Formeln › Funktion einfügen (`calc.insertFunction`), Bearbeitungsleiste fx | `sum` bzw. Text „fx“ |
| `thousands-separator` | Start › Zahl › 1.000er-Trennzeichen (`calc.thousands`) | `decimals-more` |
| `conditional-formatting` | Start › Formatvorlagen › Bedingte Formatierung (`calc.cf`) | `palette` |
| `text-to-columns` | Daten › Text in Spalten (`calc.data.textToColumns`) | `columns` |
| `remove-duplicates` | Daten › Duplikate entfernen (`calc.data.removeDuplicates`) | `list-checks` |
| `data-validation` | Daten › Datenüberprüfung (`calc.data.validation`) | `check-square` |
| `group-rows` / `ungroup-rows` | Daten › Gruppieren / Gruppierung aufheben (`calc.data.group`, `calc.data.ungroup`) | `layers` |
| `name-manager` | Formeln › Namens-Manager, Namen definieren (`calc.nameManager`, `calc.defineName`) | `tag` |
| `freeze-top-row`, `freeze-first-column`, `unfreeze` | Ansicht › Fenster fixieren (Menü `calc.freeze.*`) | `freeze`, `x` |
| `chart-area` | Einfügen › Diagramme › Fläche, Diagrammtyp ändern | `chart-line` |
| `chart-scatter` | Einfügen › Diagramme › Punkt (XY) | `bar-chart` |
| `chart-combo` | Einfügen › Diagramme › Verbund | `chart-column` |
| `chart-doughnut` | Diagrammtyp ändern (Ring) | `chart-pie` |
| `chart-elements` | Diagrammentwurf › Diagrammelement hinzufügen | `plus` |
| `chart-type` | Diagrammentwurf › Diagrammtyp ändern | `chart-column` |
| `switch-row-column` | Diagrammentwurf › Zeile/Spalte wechseln | `refresh` |
| `select-data` | Diagrammentwurf › Daten auswählen | `table` |
| `bring-forward`, `send-backward` | Format › Anordnen (vier Befehle) | `layers` |
| `chart-legend`, `chart-gridlines`, `chart-axes`, `chart-data-labels` | Diagrammelement hinzufügen (Untermenüs) | `list`, `grid`, `chart-line`, `tag` |
| `slide-layout` | ThixShow: Start › Layout, Neue Folie mit Layout (`show.layout`) | `template` |
| `slide-sorter`, `reading-view`, `normal-view` | ThixShow: Statusleiste und Ansicht (`show.view.*`) | `grid`, `book`, `sidebar` |
| `transition-*` (je Übergang) | ThixShow: Übergänge-Galerie (`show.transition`) | `transition` |
| `animation-entrance`, `animation-emphasis`, `animation-exit`, `motion-path` | ThixShow: Animationen-Galerie (`show.animation`) | `sparkles`, `star`, `logout`, `route` |
| `slide-size` | ThixShow: Entwurf › Foliengröße | `orientation` |
| `flip-horizontal`, `flip-vertical`, `rotate-left`, `distribute-horizontal`, `distribute-vertical` | ThixShow: Anordnen | `columns`, `line-spacing`, `undo` |
| `align-top`, `align-middle`, `align-bottom` | ThixShow: Text ausrichten, Formen ausrichten | `arrow-up`, `minus`, `arrow-down` |
| `shape-outline`, `shape-effects` (Schatten), `section` | ThixShow: Formkontur, Formeffekte, Abschnitt | `edit`, `layers`, `list` |
| `presenter-view`, `blank-screen` | ThixShow: Referentenansicht, Bildschirm schwarz | `monitor`, `moon` |

### Phase 4 – ThixShow
Folienmodell, Layouts/Designs, Formen/Text/Bilder/Tabellen, Folienübersicht, Übergänge/Animationen,
Präsentations- und Referentenansicht, pptx, danach odp.

- **Stand (2026-11-19): UI-freier Kern erledigt (ThixShow.Core, Branch feature/show-core).** Modell (Masters → Layouts → Folien mit
  Platzhalter-Vererbung, Designs mit Farb-/Schrift-/Formatschema, Formen mit 126 Vorgabegeometrien aus den Formeln der Spezifikation,
  Bilder, Gruppen, Linien, Tabellen, Diagramm-Rahmen als Naht, Füllungen, Konturen, Schatten, Textkörper mit Aufzählungen und
  AutoAnpassen, Notizen, Abschnitte, Übergänge, Animationen), Darstellung als Anzeigelisten (Miniaturen, Handzettel 1/2/3/4/6/9,
  Notizenseiten, PDF über Skia), pptx/pptm lesen und schreiben (Unbekanntes bleibt erhalten, unveränderte Folien bleiben unberührt),
  eigene Designs (ThixShow, Klar, Kontrast, Natur, Abend, Papier), Bearbeitungssitzung mit Rückgängig für alle Befehle, Fangen an
  Hilfslinien, Zwischenablage, Suchen/Ersetzen, Bildschirmpräsentation als Zustandsmaschine mit Ebenen je Bild und Referentenansicht.
  **Lücken:** Diagramme zeichnet erst der gemeinsame Diagrammkern (Naht `IChartRenderer`), SmartArt/OLE als Platzhalter, Morphen und
  p14-Übergänge als Verblassen, Absatz-Animationen bewegen die ganze Form, Effekte in Gruppen bewegen die ganze Gruppe, Schriftgrößen
  skalieren beim Ändern der Foliengröße nicht mit, Diagramme zwischen Präsentationen kopieren, odp (Naht: `Formats/`).
- **Stand (2026-11-19): erste Version der App ThixShow (src/ThixShow, dev).** Eine Zeichenschicht für alle Apps (`VectorPath`/`VectorPathItem`,
  `TransformBeginItem(Matrix2D)`, ein Renderer `SkiaPageRenderer`); Diagramme in pptx zeichnet ThixOffice.Charts (`SharedChartRenderer`),
  neue Diagramme bekommen einen eigenen Teil, kopierte Diagramme behalten ihre Daten. Normalansicht wie PowerPoint (Miniaturen mit
  Abschnitten, Folie mit Arbeitsfläche und Zoom „An Fenster anpassen“, Notizen, Statusleiste mit Ansichten), Foliensortierung,
  Leseansicht; auf der Folie markieren, Rahmen, verschieben/Größe/drehen/Ziehpunkte, intelligente Führungslinien, Pfeiltasten,
  Strg+Ziehen, Gruppen öffnen, Formen aufziehen, Text in Platzhaltern/Formen/Zellen mit Eingabemethoden; Bilder (Datei, Ablage,
  Ziehen), Tabellen, Diagramme mit Beispieldaten und „Daten bearbeiten“, Links, Symbole; Menüband Start, Einfügen, Entwurf, Übergänge,
  Animationen (mit Animationsbereich), Bildschirmpräsentation, Ansicht und die Kontextkarten Formformat, Bildformat, Tabellenentwurf,
  Layout, Diagrammentwurf; Bildschirmpräsentation im Vollbild (60 fps nur bei Bewegung) mit Referentenansicht auf dem zweiten
  Bildschirm; Drucken (Folien, Notizenseiten, Handzettel 1–9, Graustufen) und PDF; pptx/pptm mit Makro-Frage; Selbsttest, Bilder,
  UI-Tests; 200 Folien: Folienwechsel ≈ 9 ms, Miniaturen-Scrollen ≈ 5 ms je Schritt.
  Nachgezogen (2026-11-20): Datei › Exportieren als Bild (PNG/JPEG, alle Folien oder die aktuelle, 1280/1920/3840 Pixel),
  Kopf- und Fußzeile mit Datum (automatisch oder fest), Foliennummer, Fußzeile, „Auf Titelfolie nicht anzeigen“ (Felder bleiben in
  pptx), Tests der Abläufe Wiederherstellen und ThixDrive,
  Bildformat › Zuschneiden (der sichtbare Teil bleibt am Platz), Animationen › Effektoptionen › Nach Absatz, Bildschirm/Referentenansicht/Wiederholen bleiben
  zwischen Sitzungen gespeichert (`AppSettings.AppValues`).
  Kern-Lücken geschlossen: Diagramme behalten beim Kopieren zwischen Präsentationen ihre Daten, „Passend skalieren“ verkleinert die
  Schrift mit (`TextScaling`), Effekte „Nach Absatz“ bewegen nur ihre Absätze (`ParagraphMask`, Absatz-Ebenen im Zeitplan).
  In die gemeinsamen Projekte gehoben: `SelectionHandles` (Ziehpunkte von ThixWrite-Bildern, ThixCalc-Diagrammen, ThixShow-Formen),
  `OfficeFonts`, `TablePicker`, `RibbonFlyoutButton`, Druckseite mit zweiter Wahl und Graustufen.
  **Lücken gegenüber PowerPoint:** keine Rechtschreibprüfung (Überprüfen fehlt), kein Folienmaster-Bearbeiten, keine Kopf- und
  Fußzeile/Foliennummer/Datum einfügen (Kern fehlt), Notizen nur als einfacher Text, Diagrammdaten nur als kleine Tabelle (keine
  eingebettete Arbeitsmappe, Diagrammelemente/Formatierung nicht bearbeitbar), keine
  Laserpointer/Stift, keine Notizenseiten-Ansicht, Lineal/Gitternetzlinien nicht sichtbar (nur Am Raster ausrichten), Bilder als
  PNG exportieren fehlt, Größen in verschachtelten gedrehten Gruppen nur angenähert, Einfügen aus ThixWrite/ThixCalc nur als Text.

### Phase 5 – ODF-Nachzug und Feinschliff
Rundreise-Tests gegen LibreOffice-Dateien, Druckqualität, Barrierefreiheit, Englisch als zweite Sprache.

### Phase 6 – Web-Apps in ThixCloud
Write/Calc/Show im Browser, an die Desktop-Apps angelehnt, auf demselben Core, Dateien im Drive.
Technikentscheidung zu Beginn der Phase (siehe 2).

### Später
ThixMail, ThixContacts, ThixCalendar-Integration, ThixData.

## 6. Launcher

Mehrere Apps aus einem Repo funktionieren mit dem Channel-Manifest. Je App:

| | ThixWrite | ThixCalc | ThixShow |
| --- | --- | --- | --- |
| Id | `thixwrite` | `thixcalc` | `thixshow` |
| Manifest | `thixwrite-update.json` | `thixcalc-update.json` | `thixshow-update.json` |
| Setup | `ThixWriteSetup.exe` | `ThixCalcSetup.exe` | `ThixShowSetup.exe` |
| macOS | `ThixWrite-macOS-<arch>.zip/.dmg` | … | … |
| Bundle-ID | `info.schmoldt.thixwrite` | `info.schmoldt.thixcalc` | `info.schmoldt.thixshow` |

Im Launcher je App: Katalogeintrag, Logo, Tests. Einmalig vorab: `SampleGitHub` so ändern, dass es
die App über den Manifest-/Paketnamen findet statt über das Repo. Launcher-Änderungen laufen über
einen Branch; nach `main` wird dort nur nach Rückfrage gepusht.

## 7. Betroffene Nachbar-Repos

| Repo | Änderung | Branch |
| --- | --- | --- |
| MatDesign | MatRibbon, Backstage, Icons → MatDesktop 0.3.0 | `feature/ribbon` (von `feature/desktop-and-services`) |
| MatCloud | App-IDs, Drive-Modul, später Web-Apps | Feature-Branches |
| ThixLauncher | SampleGitHub-Korrektur, Katalogeinträge | Feature-Branches |

## 8. Risiken

- **Nativer Texteditor** ist der schwierigste Teil (Umbruch, Auswahl, IME, Tabellen über Seiten).
  Deshalb zuerst ein schmales, sauberes 1a und Layout strikt UI-frei testbar.
- **Formattreue** docx/xlsx/pptx: Unbekannte Inhalte dürfen beim Speichern nicht still verloren
  gehen – beim Öffnen wird gewarnt, wenn ein Dokument nicht unterstützte Teile enthält.
- **Keine Code-Signatur** (wie bei allen Thix-Apps): SmartScreen/Gatekeeper-Hinweise bleiben.
- **Produktiv-URL der ThixCloud** ist in keinem Repo hinterlegt; der Nutzer trägt sie ein oder der Launcher übergibt sie.
