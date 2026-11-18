# ThixOffice – Logos

Die drei Apps tragen die Thix-Kachel (abgerundetes Quadrat mit Verlauf, wie ThixClip und ThixCalendar) in ihrer
Akzentfarbe – den Farben, an denen Windows-Nutzer Word, Excel und PowerPoint erkennen – und je ein eigenes Zeichen:

| App | Akzent | Kachelverlauf | Zeichen |
|---|---|---|---|
| **ThixWrite** | `#185ABD` | `#4F8DEA → #1450B0` | beschriebenes Blatt mit Eselsohr, ein Stift schreibt die letzte Zeile |
| **ThixCalc** | `#107C41` | `#34B26C → #0C6B37` | Tabellenblatt mit Kopfzeile und -spalte, markierte Zelle, Säulendiagramm als Plakette |
| **ThixShow** | `#C43E1C` | `#F0784F → #B0361A` | Folie auf dem Ständer mit Titel, Kreisdiagramm (ein Stück hervorgehoben) und Text |

Je App ein Ordner `branding/<id>/` mit den Detailstufen:

| Datei | Detailstufe | Einsatz |
|---|---|---|
| `logo-icon.svg` | voll (ab 48 px) | App-Icon (Windows .ico ab 48 px, macOS .icns), Titelleiste, Info-Seite, Setup |
| `logo-small.svg` | reduziert (16–32 px): Kachel randlos und einfarbig, nur die kräftigen Formen | kleine .ico-Größen, Taskleiste, Explorer-Listen |
| `logo-document.svg` | Dateisymbol (ab 48 px): weißes Blatt mit Eselsohr, App-Kachel als Plakette unten rechts | Dokumente im Explorer / Finder |
| `logo-document-small.svg` | Dateisymbol reduziert (16–32 px): kräftiger Umriss, große Plakette | kleine Größen des Dateisymbols |
| `logo-full.svg` | Icon + Wortmarke „Thix" + Suffix in der Akzentfarbe | README, Kopfzeilen |

Gerendert (nach jeder Änderung an einer SVG):

```
cd tools && npm install && node render-icons.js
node render-icons.js --preview <datei.png>   # alle Apps in allen kleinen Größen hell/dunkel – ANSEHEN
```

Das Skript rendert mit dem installierten Microsoft Edge (headless) nach

- `branding/<id>/png/` – App-Icon 16–1024 px, macOS-Varianten `-mac-*` (mit Apples Rand; `scripts/package-macos.sh` baut
  daraus das `.icns`), Dateisymbol `-document-*` und `-document-mac-*`, das Logo mit Wortmarke `-full.png`;
- `branding/<id>/<id>.ico` und `<id>-document.ico` (16–256 px);
- `branding/launcher/<id>.png` (256 px) – für ThixLauncher (`src/ThixLauncher/Assets/apps/<id>.png`);
- `src/<App>/Assets/` (`<id>.ico`, `<id>-document.ico`, `logo-256.png`, `logo-64.png`), sobald es das Projekt der App gibt.

Farben in den SVGs sind Markenfarben, keine Oberflächenfarben: die Oberfläche selbst nimmt Farben nur aus MatDesktop
(`{DynamicResource Mat.*}`), einzige Literale ist der Akzent der App.
