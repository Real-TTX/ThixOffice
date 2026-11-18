# ThixOffice – Plan

Stand: 2026-11-18. Dieser Plan ist die Arbeitsgrundlage; er wird mit jedem Meilenstein fortgeschrieben.

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
- ThixCloud: kopierter MatCloud-Vertrag, Anmeldung per Browser-Link, Passwort oder Launcher-Übergabe.
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
- **1b:** Tabellen, Bilder, Kopf-/Fußzeilen, Seitenzahlen, Seitenumbrüche, Hyperlinks, Tabstopps.
- **1c:** Inhaltsverzeichnis, Fußnoten, Kommentare, Rechtschreibprüfung, odt; Karten Referenzen/Überprüfen.
- Danach: Launcher-Eintrag (siehe 6).

### Phase 2 – Drive in ThixCloud
- MatCloud: App-IDs `write`, `calc`, `show`, `drive`; Modul `MatCloud.Apps.Drive` (Dateibaum, Versionen,
  Papierkorb, Freigaben, Blob-Speicher, Chunk-Upload) mit Web-App und Geräte-API `/api/v1/drive/…`.
- ThixOffice: `DriveService` + Drive-Dateidialog; Öffnen/Speichern direkt im Drive, Konflikt über Versionen.

### Phase 3 – ThixCalc
Zellmodell, Formel-Engine (Parser, Abhängigkeitsgraph, Funktionsbibliothek), virtualisiertes Raster,
Formate, Sortieren/Filtern, Fixieren, bedingte Formatierung, Diagramme, xlsx/csv, danach ods.

### Phase 4 – ThixShow
Folienmodell, Layouts/Designs, Formen/Text/Bilder/Tabellen, Folienübersicht, Übergänge/Animationen,
Präsentations- und Referentenansicht, pptx, danach odp.

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
