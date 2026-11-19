# ThixOffice

![ThixWrite](branding/thixwrite/png/thixwrite-full.png)

Eine Office-Suite aus einzelnen Programmen für **Windows und macOS**, die Windows-Nutzer sofort bedienen können –
Menüband, „Datei“ als Backstage, Schnellzugriff und „Was möchten Sie tun?“ dort, wo Word, Excel und PowerPoint sie haben –
und die aussieht wie die übrigen Thix-Apps.

| Programm | statt | Formate | Stand |
| --- | --- | --- | --- |
| **ThixWrite** | Word | docx, txt, später odt | Meilenstein 1b; Dokumente in ThixDrive (Phase 2) |
| **ThixCalc** | Excel | xlsx, xlsm, csv, später ods | erstes Release (Phase 3), Arbeitsmappen in ThixDrive |
| **ThixShow** | PowerPoint | pptx, später odp | Logo |

Der Plan steht in [docs/PLAN.md](docs/PLAN.md).

## ThixWrite – Stand Meilenstein 1b

ThixWrite ist eine Textverarbeitung mit eigenem, nativem Editor: Seiten wie auf Papier (weiß, auch im dunklen
Erscheinungsbild), Zeichen- und Absatzformat, Formatvorlagen, Aufzählung und Nummerierung, Rückgängig/Wiederholen, Zwischenablage
(mit Formatierung zwischen ThixWrite-Fenstern, „Nur Text einfügen“ mit Strg+Umschalt+V), Suchen und Ersetzen, Seite einrichten,
Zoom (auch Strg+Mausrad, Seitenbreite, Ganze Seite), Formatierungszeichen, „Als PDF exportieren“ und Drucken (Windows und macOS,
mit Vorschau). Dazu Tabellen (Raster einfügen, Zeilen/Spalten, verbinden/teilen, Rahmen, Schattierung, Überschriften wiederholen,
Spaltenbreite ziehen), Bilder (einfügen, einfügen aus der Zwischenablage, ziehen, Größe mit Ziehpunkten, Alternativtext), Kopf- und
Fußzeilen mit Seitenzahlen (Doppelklick in den Rand), Links, Lineal mit Einzügen und Tabstopps, die Dialoge Schriftart und Absatz,
Format übertragen, Symbole und Einfügen aus anderen Programmen (HTML: Formatierung, Listen, Tabellen, Links, Bilder). Eingabemethoden
(Umlaute, Akzente mit Tottasten, Emoji-Panel, chinesisch/japanisch) funktionieren.

Word-Dokumente (.docx) sind das Standardformat; .txt wird als reiner Text gelesen und geschrieben. Inhalte, die ThixWrite noch
nicht anzeigt (Kommentare, Fußnoten, verschachtelte Tabellen, Formeln …), bleiben beim Speichern erhalten – beim Öffnen sagt ein
Hinweis, welche es sind.

![ThixWrite hell](docs/images/thixwrite-main-light.png)

![ThixWrite dunkel](docs/images/thixwrite-main-dark.png)

| Tabelle (Kontext-Karte Layout) | Bild (Kontext-Karte Bildformat) |
| --- | --- |
| ![Tabelle](docs/images/thixwrite-main-table-light.png) | ![Bild](docs/images/thixwrite-main-picture-light.png) |
| **Kopfzeile bearbeiten** (Doppelklick in den Rand) | **Datei › Drucken** (Strg+P) |
| ![Kopfzeile](docs/images/thixwrite-main-header-light.png) | ![Drucken](docs/images/thixwrite-backstage-print-light.png) |
| **Suchen und Ersetzen** (Strg+F, Strg+H) | **Layout und Formatierungszeichen** |
| ![Suchen und Ersetzen](docs/images/thixwrite-main-find-light.png) | ![Layout](docs/images/thixwrite-main-layout-light.png) |
| **Seite einrichten** | **Ansicht › Ganze Seite** |
| ![Seite einrichten](docs/images/thixwrite-dialog-page-setup-light.png) | ![Ganze Seite](docs/images/thixwrite-main-view-dark.png) |
| **Datei › Exportieren** (PDF) | **Öffnen** |
| ![Exportieren](docs/images/thixwrite-backstage-export-light.png) | ![Öffnen](docs/images/thixwrite-backstage-open-light.png) |
| **Einstellungen › Speichern** | **Änderungen speichern?** |
| ![Einstellungen](docs/images/thixwrite-backstage-settings-save-light.png) | ![Änderungen speichern](docs/images/thixwrite-dialog-save-changes-light.png) |

## ThixCalc – erstes Release

ThixCalc ist eine Tabellenkalkulation mit eigenem, nativem Raster und eigener Formel-Engine (über 400 Funktionen mit deutschen
Namen: SUMME, SVERWEIS, SUMMEWENNS, XVERWEIS, WENNFEHLER, dynamische Arrays …). Das Raster scrollt pixelgenau und bleibt auch bei
100.000 Zeilen flüssig; Linien sind bei jedem Zoom und jeder Bildschirmskalierung scharf.

- **Eingeben wie in Excel:** Tippen ersetzt (*Eingeben*), F2 oder Doppelklick bearbeitet (*Bearbeiten*), in Formeln zeigen Pfeiltasten
  und Klicks Bezüge (*Zeigen*) – jeder Bezug bekommt eine Farbe in der Formel und denselben farbigen Rahmen im Raster; F4 wechselt
  $A$1 → A$1 → $A1 → A1. Beim Tippen eines Namens erscheint die Funktionsliste (Tab übernimmt), in den Klammern der Argument-Tipp
  „SVERWEIS(Suchkriterium; Matrix; Spaltenindex; [Bereich_Verweis])“. Eingabe/Tab bewegen sich innerhalb der Markierung,
  Strg+Eingabe füllt alle markierten Zellen, Strg+Umschalt+Eingabe gibt eine Matrixformel ein. Eingabemethoden (Umlaute, Tottasten,
  CJK) funktionieren direkt in der Zelle.
- **Bearbeitungsleiste** mit Namenfeld (Zelle, Bereich, „Tabelle2!B4“ oder Name eingeben und Eingabe; ein neuer Name wird definiert),
  fx (Funktion einfügen mit Suche, Kategorien und Argumenthilfe), aufklappbar für lange Formeln.
- **Maus:** markieren, Umschalt/Strg-Klick, Zeilen- und Spaltenköpfe, Spaltenbreite und Zeilenhöhe ziehen (Doppelklick: optimal),
  Ausfüllkästchen ziehen (Reihen: 1, 2, 3 …, Monate, Wochentage, Datumswerte), Kontextmenüs für Zellen, Zeilen, Spalten und Blätter.
- **Blätter** unten links: neu, umbenennen per Doppelklick, ziehen zum Verschieben, Registerfarbe, ausblenden/einblenden, verschieben
  oder kopieren. Die Statusleiste zeigt Bereit/Eingeben/Bearbeiten/Zeigen und Mittelwert, Anzahl, Summe (Rechtsklick: auswählen).
- **Menüband wie im deutschen Excel:** Start (Zwischenablage mit Inhalte einfügen, Schriftart, Rahmen, Füll- und Schriftfarbe,
  Ausrichtung, Verbinden, Zahlenformate, Bedingte Formatierung mit Regeln, Datenbalken und Farbskalen, Zellenformatvorlagen, Zellen
  einfügen/löschen/formatieren, AutoSumme, Ausfüllen, Löschen, Sortieren und Filtern, Suchen), Einfügen (Link, Notiz), Seitenlayout,
  Formeln (Funktionsbibliothek, Namens-Manager, Formeln anzeigen, Berechnung), Daten (Sortieren, Filtern, Text in Spalten, Duplikate
  entfernen, Datenüberprüfung, Gruppieren), Überprüfen (Notizen), Ansicht (Gitternetzlinien, Überschriften, Bearbeitungsleiste, Zoom,
  Fenster fixieren). Dialog **Zellen formatieren** (Strg+1) mit Zahlen | Ausrichtung | Schrift | Rahmen | Ausfüllen.
- **Zwischenablage:** zwischen ThixCalc-Fenstern exakt (Formeln, Formate), für andere Programme als Text und HTML-Tabelle; Tabellen
  aus Browsern oder Word und Text werden Zelle für Zelle übernommen.
- **Drucken und PDF:** Seite einrichten (Papier, Hoch-/Querformat, Ränder, Skalierung oder „auf N × M Seiten“, Druckbereich,
  Wiederholungszeilen, Gitternetzlinien, Überschriften, Kopf- und Fußzeile mit Seitenzahl), Datei › Drucken mit Vorschau (aktives
  Blatt, ganze Mappe oder Auswahl), Datei › Exportieren als PDF.
- **Diagramme** (Einfügen › Diagramme): Säulen und Balken (gruppiert, gestapelt, 100 %), Linie (mit Datenpunkten, gestapelt), Kreis
  und Ring, Fläche (gestapelt), Punkt (XY, mit geraden oder interpolierten Linien) und Verbund (Säulen + Linie, auch auf der
  Sekundärachse). Die Reihen kommen wie in Excel aus der Markierung (Überschriften, Kategorien, Datumsachse) und folgen den Zellen
  live. Ein Klick wählt ein Diagramm (Rahmen mit Ziehpunkten): ziehen verschiebt, Ziehpunkte ändern die Größe, Pfeiltasten
  verschieben fein, Entf löscht, Strg+C/V kopiert. Die Karten **Diagrammentwurf** (Diagrammelement hinzufügen: Titel, Achsen,
  Achsentitel, Datenbeschriftungen, Gitternetzlinien, Legende; Farben ändern; Zeile/Spalte wechseln; Daten auswählen; Diagrammtyp
  ändern mit Vorschau) und **Format** (Anordnen, Höhe/Breite). Diagramme werden gedruckt und als PDF exportiert, in .xlsx gelesen und
  geschrieben; Diagrammtypen, die ThixCalc noch nicht zeichnet (3D, Netz, Kurs, Wasserfall …), bleiben erhalten und erscheinen als
  beschrifteter Rahmen.
- **Dateien:** Excel-Arbeitsmappen (.xlsx) sind Standard; .xlsm wird gelesen und mit Makros wieder geschrieben (Makros werden nie
  ausgeführt); CSV mit Erkennung von Trennzeichen und Zeichensatz (bei Zweifel fragt der Textimport) und einem Hinweis beim
  Speichern, dass CSV nur die Werte des aktiven Blatts enthält. Was ThixCalc noch nicht zeigt (Bilder, Formen, Pivot-Tabellen …),
  bleibt beim Speichern erhalten – ein Hinweis nennt es beim Öffnen.

![ThixCalc hell](docs/images/thixcalc-main-light.png)

![ThixCalc dunkel](docs/images/thixcalc-main-dark.png)

| Formel eingeben (farbige Bezüge, Funktionsliste) | Zellen formatieren (Strg+1) |
| --- | --- |
| ![Formel](docs/images/thixcalc-main-formula-light.png) | ![Zellen formatieren](docs/images/thixcalc-dialog-format-cells-light.png) |
| **Funktion einfügen** (Umschalt+F3) | **Suchen und Ersetzen** |
| ![Funktion einfügen](docs/images/thixcalc-dialog-function-light.png) | ![Suchen](docs/images/thixcalc-main-find-light.png) |
| **Datei › Drucken** (Strg+P) | **Seite einrichten** |
| ![Drucken](docs/images/thixcalc-backstage-print-light.png) | ![Seite einrichten](docs/images/thixcalc-dialog-page-setup-light.png) |
| **Diagramme** (Einfügen) | **Diagrammentwurf** |
| ![Diagramme](docs/images/thixcalc-main-chart-light.png) | ![Diagrammentwurf](docs/images/thixcalc-main-chart-design-light.png) |
| **Formeln** | **Diagramme dunkel** |
| ![Formeln](docs/images/thixcalc-main-view-dark.png) | ![Diagramme dunkel](docs/images/thixcalc-main-chart-dark.png) |

### Tastenkürzel in ThixCalc

Wie im deutschen Excel (unter macOS ⌘ statt Strg):

| Kürzel | Befehl |
| --- | --- |
| Pfeile, Strg+Pfeile, Umschalt+…, Pos1, Strg+Pos1 / Strg+Ende | bewegen, zum Datenrand springen, markieren |
| Bild auf/ab, Alt+Bild auf/ab | eine Bildschirmseite nach oben/unten, links/rechts |
| Eingabe / Tab (Umschalt: zurück) | Eingabe beenden und innerhalb der Markierung weiter |
| F2, Esc, F4, Alt+Eingabe | Bearbeiten, Abbrechen, $ in Bezügen, Zeilenumbruch in der Zelle |
| Strg+Eingabe, Strg+Umschalt+Eingabe | alle markierten Zellen füllen, Matrixformel |
| Entf, Rücktaste | Inhalte löschen, Zelle leeren und bearbeiten |
| Strg+Leertaste, Umschalt+Leertaste, Strg+A | Spalte, Zeile, Bereich/alles markieren |
| Strg+Umschalt+F / K / U, Strg+5 (Mac: ⌘B / ⌘I / ⌘U) | Fett, Kursiv, Unterstrichen, Durchgestrichen |
| Strg+1 | Zellen formatieren |
| Strg+Umschalt+4 / 5 / 1 | Buchhaltung (€), Prozent, Zahl mit Tausenderpunkt |
| Strg+U, Strg+R (Mac: ⌘D, ⌘R) | nach unten / nach rechts ausfüllen |
| Alt+Umschalt+0 | AutoSumme |
| Strg+F, Strg+H, F5 | Suchen, Ersetzen, Gehe zu (Namenfeld) |
| Strg+Umschalt+L | Filter ein/aus |
| Strg+K, Umschalt+F2 | Link, Notiz |
| Umschalt+F3, Strg+F3, F9 | Funktion einfügen, Namens-Manager, Neu berechnen |
| Strg+Bild ab / auf (Mac: ⌥⌘→ / ⌥⌘←), Umschalt+F11 | nächstes / voriges Blatt, neues Blatt |
| Strg+9, Strg+0 | Zeilen / Spalten ausblenden |
| Strg+C/X/V, Strg+Umschalt+V, Strg+Z/Y, Strg+S, F12, Strg+P | Zwischenablage, Nur Text, Rückgängig/Wiederholen, Speichern, Speichern unter, Drucken |
| Strg+Mausrad | Zoom |
| Diagramm gewählt: Pfeile (Strg: weiter), Entf, Esc | verschieben, löschen, zurück zu den Zellen |

## ThixCloud und ThixDrive

ThixWrite und ThixCalc öffnen und speichern Dokumente direkt in **ThixDrive**, der Ablage Ihrer **ThixCloud** (MatCloud) – auf jedem
Computer und im Browser. ThixShow bekommt dasselbe, sobald es sie gibt.

**Verbinden:**

1. *Datei › Einstellungen › Cloud* (oder *Datei › Öffnen › ThixDrive*): die Adresse Ihrer ThixCloud eintragen
   (z. B. `https://cloud.example.com`).
2. **Im Browser anmelden** (empfohlen, für jedes Konto, auch über MatIdentity): ThixWrite zeigt einen Code und öffnet die
   Anmeldeseite; dort anmelden und prüfen, dass derselbe Code steht – ThixWrite verbindet sich, sobald Sie bestätigen.
   Oder **Mit Benutzername und Passwort** eines Kontos der ThixCloud. Das Passwort wird nur für die Anmeldung verwendet und nie
   gespeichert; das Geräte-Token liegt geschützt (Windows: DPAPI, macOS: Schlüsselbund).
3. Hat **ThixLauncher** ThixWrite installiert, ist es beim ersten Start schon angemeldet.

Abmelden unter *Einstellungen › Cloud* entfernt diesen Computer aus Ihrem Konto; Ihre Dokumente bleiben, wo sie sind.

**Arbeiten:** *Datei › Öffnen* und *Datei › Speichern unter* haben die Orte „Zuletzt verwendet“, „ThixDrive“ und „Dieser PC“.
In ThixDrive suchen Sie über die ganze Ablage, filtern nach Typ, sortieren, legen Ordner an, benennen um und legen in den
Papierkorb (30 Tage, im Browser wiederherstellbar). Strg+S speichert ein ThixDrive-Dokument zuerst auf diesem Computer, dann
als neue Version in ThixDrive; oben rechts in der Titelleiste steht, wo es steht („In ThixDrive“, „Wird hochgeladen …“).

- **Ohne Verbindung geht nichts verloren:** Das Dokument bleibt gespeichert (als Arbeitskopie auf diesem Computer) und ist als
  **„Nicht hochgeladen“** markiert; ThixWrite versucht es alle 30 Sekunden erneut, ein Klick darauf sofort. Beim Schließen
  fragt ThixWrite nach; wer trotzdem schließt, dem wird es beim nächsten Öffnen hochgeladen.
- **Hat jemand die Datei inzwischen geändert**, fragt ThixWrite: **Als Kopie speichern** (Vorschlag – beide Fassungen bleiben),
  **Später** oder – abgesetzt – **Überschreiben** (Ihre Fassung wird die aktuelle; die andere bleibt als ältere Version).
- Ist der Speicherplatz voll oder die Datei zu groß, sagt ThixWrite das; das Dokument bleibt auf diesem Computer gespeichert.

| | |
| --- | --- |
| ![ThixDrive öffnen](docs/images/thixwrite-backstage-open-drive-light.png) | ![In ThixDrive speichern](docs/images/thixwrite-backstage-saveas-drive-light.png) |
| ![Anmelden](docs/images/thixwrite-backstage-open-signin-dark.png) | ![Einstellungen › Cloud](docs/images/thixwrite-backstage-settings-cloud-light.png) |
| ![Nicht hochgeladen](docs/images/thixwrite-main-drive-pending-light.png) | ![Datei wurde inzwischen geändert](docs/images/thixwrite-dialog-drive-conflict-light.png) |

### Tastenkürzel

Wie im deutschen Word, wo sie nicht kollidieren (unter macOS ⌘ statt Strg, ⌥ für Wort-Bewegungen):

| Kürzel | Befehl |
| --- | --- |
| Strg+Umschalt+F / K / U (auch Strg+B / I / U) | Fett / Kursiv / Unterstrichen |
| Strg+L / E / R / J | Linksbündig / Zentriert / Rechtsbündig / Blocksatz (Strg+B ist Fett, nicht Blocksatz) |
| Strg+Leertaste (⇧⌘Leertaste) | Formatierung löschen |
| Strg+F / Strg+H (⌥⌘F) | Suchen / Ersetzen |
| Strg+A, Strg+C/X/V, Strg+Umschalt+V | Alles markieren, Zwischenablage, Nur Text einfügen |
| Strg+Eingabe, Umschalt+Eingabe | Seitenumbruch, Zeilenumbruch |
| Tab / Umschalt+Tab | in Listen: Ebene tiefer / höher; in Tabellen: nächste / vorige Zelle (Tab in der letzten Zelle: neue Zeile) |
| Alt+Umschalt+D | Datum und Uhrzeit |
| Strg+P | Drucken |
| Strg+K, Strg+Klick | Link einfügen, Link öffnen |
| Strg+D | Schriftart … |
| Strg+Umschalt+C | Format übertragen |
| Esc (in Kopf-/Fußzeile) | zurück zum Text |
| Strg+Z / Strg+Y (⇧⌘Z), Strg+S, F12, Strg+F1 | Rückgängig / Wiederholen, Speichern, Speichern unter, Menüband |

## Installieren

- **Windows:** `ThixWriteSetup.exe` bzw. `ThixCalcSetup.exe` aus dem Release-Kanal herunterladen und starten. Das Programm installiert sich selbst für
  das eigene Benutzerkonto (ohne Administratorrechte) nach `%LocalAppData%\Programs\ThixWrite`, trägt sich im Startmenü, unter
  „Installierte Apps“ und unter „Öffnen mit“ für Word-Dokumente (.docx) und Textdateien ein – Standardprogramm wird ThixWrite
  nur, wenn Sie es in den „Standard-Apps“ von Windows wählen. Entfernen über „Installierte Apps“.
- **macOS:** `ThixWrite-macOS-arm64.dmg` (Apple Silicon) bzw. `-x64.dmg` (Intel) öffnen und ThixWrite in „Programme“ ziehen.
- Updates kommen automatisch aus dem eigenen Kanal (`release-channel` für main, `nightly-channel` für dev) und werden
  installiert, wenn ThixWrite geschlossen wird – nie mitten in der Arbeit.
- ThixLauncher kann die Apps installieren (je App eigenes Manifest `<app>-update.json`, siehe Plan Abschnitt 6).

![Einrichtung](docs/images/thixwrite-setup-welcome-light.png)

## Für Administratoren

Richtlinien je App (Wert 1 = an): Windows `HKLM\SOFTWARE\Policies\ThixWrite` (DWORD), macOS ein Konfigurationsprofil für
`info.schmoldt.thixwrite` – `LockConfiguration` (Einstellungen gesperrt), `DisableUpdates`, `DisableCloud` (keine Anmeldung
an der ThixCloud, kein ThixDrive), `DisableFileAssociations`, `DisablePrinting`. In der ThixCloud selbst lässt sich ThixDrive (oder
die App) für alle abschalten; ThixWrite zeigt dann „von der Verwaltung abgeschaltet“.

## Entwickeln

```
dotnet build ThixOffice.slnx
dotnet test ThixOffice.slnx
dotnet run --project src/ThixWrite                      # mit THIXWRITE_DATA_DIR=<Ordner> eine eigene Instanz
dotnet run --project src/ThixWrite -- --snapshot shots  # alle Fenster hell und dunkel als PNG
dotnet run --project src/ThixCalc                       # ThixCalc (THIXCALC_DATA_DIR=<Ordner>, --snapshot <Ordner> ebenso)
cd tools && npm install && node render-icons.js         # Logos rendern
```

Aufbau, Regeln und wie eine weitere App dazukommt: [CLAUDE.md](CLAUDE.md). Logos: [branding/README.md](branding/README.md).

© Matthias Schmoldt
