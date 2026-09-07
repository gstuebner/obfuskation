# Obfuskation

[English](README.md) · **Deutsch**

Tauscht Echtdaten in CSV-, JSON- und Textdateien gegen plausible Pseudodaten aus
und kann den Austausch wieder rückgängig machen. Gedacht für den Fall, dass
Beispieldaten an eine KI gegeben werden sollen, die Echtdaten aber das Haus nicht
verlassen dürfen.

Der eigentliche Nutzen liegt im Rückweg: die Antwort der KI — generierter Code,
Analysen, Beispielausgaben — lässt sich mit `deobfuscate` wieder auf die
Echtwerte abbilden.

Es gibt zwei Wege zum selben Werkzeug: das Kommandozeilenprogramm
`obfuskation` für Skripte und die Oberfläche `obfuskation-gui` für die
tägliche Arbeit. Beide nutzen dieselbe Bibliothek und dieselbe
Ersetzungstabelle — was der eine ersetzt, holt der andere zurück.

## Dokumentation

- [Anwenderdokumentation](docs/anwenderdokumentation.md) — für alle, die mit
  der Oberfläche Beispieldaten für eine KI aufbereiten wollen.
- [Entwicklerdokumentation](docs/entwicklerdokumentation.md) — für alle, die
  das Werkzeug bauen, erweitern oder abnehmen.
- [Testdokumentation](docs/testdokumentation.md) — Testkonzept, Testfälle
  und Befunde der Testdurchführung.

## Herunterladen

Fertige Programme für Windows und Linux liegen unter
[Releases](../../releases). Je Plattform zwei Dateien, ohne Installation
lauffähig:

| Datei | Zweck |
|---|---|
| `obfuskation` / `obfuskation.exe` | Kommandozeile |
| `obfuskation-gui` / `obfuskation-gui.exe` | Oberfläche |

Voraussetzung ist die **.NET-8-Runtime**. Wer sie nicht installieren möchte,
baut sich mit `./build-release.sh --self-contained` eine Fassung, die alles
mitbringt.

Unter Linux die Dateien ausführbar machen (`chmod +x`). Unter Windows warnt
SmartScreen beim ersten Start, weil die Programme nicht signiert sind —
*Weitere Informationen* → *Trotzdem ausführen*.

---

## Bitte zuerst lesen

**1. Das Werkzeug pseudonymisiert, es anonymisiert nicht.**
Namen und Kontonummern verschwinden, Struktur und Verteilung bleiben. Eine
Kombination aus Postleitzahl, Geburtsjahr und Kontostand kann weiterhin auf eine
Person zurückführen. Ob das im Einzelfall vertretbar ist, entscheidet der
Datenschutzbeauftragte — nicht dieses Programm.

**2. `mapping.json` ist das schützenswerteste Artefakt des ganzen Vorgangs.**
Die Datei enthält sämtliche Echtdaten in kompakter, maschinenlesbarer Form. Sie
gehört niemals in ein Repository, niemals in ein Verzeichnis, aus dem Dateien
nach außen gegeben werden, und niemals in ein Backup, das das Haus verlässt. Das
Programm legt sie deshalb außerhalb des Projekts ab, setzt die Rechte auf `0600`
und verweigert die Ablage in einem Git-Arbeitsverzeichnis.

**3. Textregeln sind ein Ausschlussverfahren.**
Was kein Muster trifft, bleibt stehen. IBAN, BIC, E-Mail und Telefonnummer lassen
sich zuverlässig erkennen — Personennamen, Firmennamen und Adressen in Freitext
praktisch nicht. Für Freitextfelder ist im Zweifel `redact` oder `drop` die
richtige Wahl, nicht `scanText`.

**4. `scan` vor jeder Weitergabe ausführen.** Nicht optional.

**5. Die Ausgabe erkennbar benennen** (`kunden.pseudo.csv`), damit Original und
Pseudonymisat nicht verwechselt werden.

---

## Ablauf

```fish
# 1. Regelgeruest aus einer echten Datei ableiten
obfuskation init --profile kontoauszuege --from kunden.csv

# 2. obfuskation.json durchgehen: jede Spalte steht auf "error" und
#    braucht eine bewusste Entscheidung

# 3. Ersetzen
obfuskation obfuscate kunden.csv -o kunden.pseudo.csv --strict

# 4. Nachpruefen — erst danach weitergeben
obfuskation scan kunden.pseudo.csv

# 5. Antwort der KI zurueckuebersetzen
pbpaste | obfuskation deobfuscate
```

`init --from` liest die Spaltenköpfe aus und legt für jede eine Regel mit
`"action": "error"` an, zusammen mit einem Vorschlag im Kommentar. Der Vorschlag
wird bewusst nicht übernommen: was mit einem Feld geschieht, soll ein Mensch
entscheiden. Solange auch nur eine Spalte auf `error` steht, bricht der Lauf ab,
bevor irgendetwas geschrieben wurde.

---

## Die Oberfläche

```fish
obfuskation-gui                        # sucht obfuskation.json wie die CLI
obfuskation-gui kunden.csv             # Datei gleich mit öffnen
obfuskation-gui --config profil.json kunden.csv
```

Ohne Angabe sucht sie eine `obfuskation.json` im aktuellen Verzeichnis und
fällt sonst auf das zuletzt benutzte Profil zurück.

**Aufbau:** oben die geöffnete Datei mit erkanntem Format, Zeichensatz und
Trennzeichen. Links die Felder, jedes mit einem Statuspunkt — gefüllt und
türkis heißt *entschieden*, ein roter Kreis heißt *offen*, und solange auch nur
einer davon offen ist, bricht jeder Lauf ab. Rechts die Behandlung des
gewählten Feldes samt einer Vorschau am echten Wert aus der Datei
(»Max Mustermann → Paul Gerber«). Unten die drei Vorgänge.

Über **Mehr** erreichbar:

- **Textregeln** — mit einem Erprobungsfeld. Muster an echtem Text ausprobieren,
  bevor sie auf Daten losgelassen werden; zu weit gefasste Muster fallen dort
  sofort auf.
- **Ersetzungstabelle** — Pfad, Anzahl je Namensraum und die Dateirechte.
  Zeigt **keine Werte**, gleich wie `mapping list`.
- **Kurzhilfe** — sechs kurze Karten für alle, die die Oberfläche zum ersten
  Mal öffnen: wofür das Werkzeug da ist, was ein Profil ist und wozu es gut
  ist, und der Weg durch das Programm.
- **Über** — die fünf Hinweise von oben und die verwendeten Pfade.

**Hell und dunkel:** der Umschalter rechts oben geht durch drei Zustände —
Systemvorgabe (folgt der Einstellung des Betriebssystems), dunkel, hell. Die
Wahl wird in `~/.config/obfuskation/gui.json` gemerkt. Dort stehen nur
Bequemlichkeiten: gewählte Ansicht, zuletzt geöffnete Profile, Fenstergröße —
**keine Dateiinhalte und keine verarbeiteten Werte**.

Bei großen Dateien zeigt die Oberfläche den Fortschritt und lässt sich
abbrechen; ein Abbruch schreibt weder eine Ausgabedatei noch Einträge in die
Ersetzungstabelle.

**Profile verwalten.** Ein Profil bündelt Feldregeln und Ersetzungstabelle
unter einem Namen; **Neu aus Datei…** fragt jetzt nach Name, optionaler
Beschreibung und Ablageort (Vorgabe: `~/.config/obfuskation/profile/<name>.json`),
und **Profile…** öffnet eine sortier- und durchsuchbare Übersicht aller
bekannten Profile samt der zuletzt bearbeiteten Dateien. Ein Umbenennen aus
dieser Übersicht heraus lässt die Einträge der Ersetzungstabelle unangetastet
— es ändert sich nur der Name, im Profil selbst und, sofern die Tabelle
schon existiert, auch dort, sodass kein Pseudonym ungültig wird. Beim
Schließen des Fensters oder beim Wechsel zu einem anderen Profil fragt die
Oberfläche nach, falls noch ungespeicherte Regeländerungen vorliegen
(Speichern, Verwerfen oder Abbrechen).

### Ins Anwendungsmenü aufnehmen (Linux)

Siehe `packaging/README.md` — `.desktop`-Datei und Symbol für GNOME.

---

## Mehrere Dateien mit gemeinsamen Schlüsselfeldern

Der übliche Fall: Stammdaten, Konten und Buchungen liegen in getrennten Dateien
und hängen über eine Personennummer zusammen. Diese Nummer muss in allen
Dateien gleich ersetzt werden — sonst zerfallen die Verknüpfungen und die
Testdaten sind wertlos.

**Das geschieht von selbst.** Es ist keine Einstellung nötig, nur eine
Bedingung: **alle Dateien mit demselben Profil verarbeiten.**

```fish
obfuskation obfuscate stammdaten.csv -o stammdaten.pseudo.csv --strict
obfuskation obfuscate konten.csv     -o konten.pseudo.csv     --strict
obfuskation obfuscate buchungen.csv  -o buchungen.pseudo.csv  --strict
```

In der Oberfläche entsprechend: Profil einmal öffnen, dann die Dateien
nacheinander über **Öffnen…** hereinholen und je **Pseudodatei
erzeugen…**. Das Profil bleibt dabei geladen.

Aus `4711` wird in allen drei Dateien derselbe Wert, weil das Pseudonym
deterministisch aus dem Klartext abgeleitet und in der gemeinsamen
Ersetzungstabelle festgehalten wird. Der zweite und dritte Lauf melden dann
`0 neue Einträge` für dieses Feld — ein guter Hinweis darauf, dass die
Verknüpfung sitzt.

**Die Spaltennamen dürfen sich unterscheiden.** Heißt die Spalte in der einen
Datei `Personennummer` und in der anderen `PersNr`, genügt es, für beide eine
Regel mit **demselben Generator** anzulegen — der Generator bestimmt den
Namensraum, nicht der Spaltenname.

```jsonc
{ "match": "Personennummer", "action": "pseudonymize", "generator": "numericId" },
{ "match": "PersNr",         "action": "pseudonymize", "generator": "numericId" }
```

### Wenn zwei Felder *nicht* zusammengehören

Umgekehrt gilt dasselbe, und das ist der Fallstrick: Personennummer `4711` und
Belegnummer `4711` bekämen mit demselben Generator auch dasselbe Pseudonym. Die
Testdaten zeigten dann eine Verbindung, die es nie gab.

Dagegen hilft ein eigener Namensraum — ein Eintrag unter `generators`, der auf
einen eingebauten Generator aufsetzt:

```jsonc
"generators": {
  "belegNummer": { "type": "numericId" }
}
```

```jsonc
{ "match": "Personennummer", "action": "pseudonymize", "generator": "numericId" },
{ "match": "Belegnummer",    "action": "pseudonymize", "generator": "belegNummer" }
```

Beide erzeugen Zahlenkennungen derselben Form, ziehen aber aus getrennten
Töpfen. In der Oberfläche erscheint `belegNummer` danach in der
Generatorauswahl, gekennzeichnet als eigener Namensraum.

### Wichtig für die Rückabbildung

Auch `deobfuscate` braucht dasselbe Profil — es liest aus derselben Tabelle.
Ein anderes Profil heißt: andere Tabelle, anderes Salt, keine Zuordnung.

---

## Konfiguration

```jsonc
{
  "version": 1,
  "profileName": "kontoauszuege",
  "mappingStore": "~/.local/share/obfuskation/kontoauszuege/mapping.json",

  "input": {
    "csvDelimiter": null,   // null = erkennen (";" ist der deutsche Normalfall)
    "encoding": null,       // null = erkennen (BOM, sonst UTF-8-Pruefung, sonst Windows-1252)
    "hasHeaderRecord": true
  },

  "defaults": {
    "unknownField": "error",        // error | pseudonymize | passthrough
    "redactionPlaceholder": "***"
  },

  // Gilt fuer CSV-Spalten und JSON-Eigenschaften.
  // Die erste passende Regel gewinnt — die Reihenfolge ist die Prioritaet.
  "fields": [
    { "match": "Kundenname",       "matchType": "exact", "action": "pseudonymize", "generator": "personName" },
    { "match": "^IBAN",            "matchType": "regex", "action": "pseudonymize", "generator": "iban" },
    { "match": "Geburtsdatum",     "matchType": "exact", "action": "pseudonymize", "generator": "dateShift" },
    { "match": "Betrag",           "matchType": "exact", "action": "passthrough" },
    { "match": "Verwendungszweck", "matchType": "exact", "action": "scanText", "textRules": ["iban", "email"] },
    { "match": "$.kunden[*].ssn",  "matchType": "jsonPath", "action": "redact" }
  ],

  "textRules": [
    { "name": "iban", "priority": 100, "generator": "iban", "pattern": "..." }
  ],

  "generators": {
    "dateShift": { "maxDays": 400, "formats": ["dd.MM.yyyy"] },
    "email":     { "domain": "example.invalid" }
  }
}
```

### Behandlungen

| `action` | Wirkung | Umkehrbar |
|---|---|:--:|
| `pseudonymize` | Wert durch ein typgerechtes Pseudonym ersetzen | ja |
| `passthrough` | Wert unverändert übernehmen | — |
| `scanText` | Inhalt mit den Textregeln durchsuchen und Treffer ersetzen | ja |
| `redact` | durch `***` ersetzen | **nein** |
| `drop` | Feld ganz aus der Ausgabe entfernen | **nein** |
| `error` | Lauf abbrechen — es fehlt noch eine Entscheidung | — |

`matchType` ist `exact` (Vorgabe, Groß-/Kleinschreibung egal), `regex` oder
`jsonPath` (nur JSON, etwa `$.kunden[*].iban`).

### Generatoren

| Name | Ergebnis |
|---|---|
| `personName`, `firstName`, `lastName` | Namen aus eingebetteten deutschen Wortlisten |
| `companyName` | Firmenname mit Rechtsform |
| `iban` | Ländercode und Länge des Originals, **Prüfziffer nach ISO 7064 korrekt** |
| `bic` | gültiges BIC-Format |
| `email` | Adresse unter `example.invalid` (per RFC 2606 reserviert) |
| `phone` | Ziffern ersetzt, Gliederung des Originals erhalten |
| `numericId` | Stellenzahl erhalten, führende Nullen bleiben |
| `dateShift` | alle Daten um denselben Betrag verschoben — Reihenfolge und Abstände bleiben |
| `street`, `city`, `postalCode` | Anschriftsbestandteile aus Wortlisten |
| `token` | generisch `TOK_A1B2C3D4` |
| `redact` | fest `***` |

---

## Wie die Umkehrbarkeit funktioniert

Das Pseudonym wird deterministisch abgeleitet:

```
seed = HMAC-SHA256(Salt des Profils, Generatorname + Klartext + Zähler)
```

Daraus folgen zwei Eigenschaften, die die Testdaten erst brauchbar machen:
derselbe Wert bekommt in allen Dateien und über alle Läufe hinweg dasselbe
Pseudonym, und Verknüpfungen über Kundennummern bleiben deshalb intakt.

Beim Eintragen wird geprüft, dass ein Pseudonym weder schon vergeben ist noch
selbst als Klartext im Bestand steht; andernfalls wird der Zähler erhöht und neu
abgeleitet.

**`dateShift` bekommt bewusst keinen Tabelleneintrag.** Ein verschobenes Datum
kann mit einem echten Datum desselben Bestands zusammenfallen, ein Eintrag wäre
dann mehrdeutig. Zurückgerechnet wird stattdessen über den konstanten Offset. Die
Folge: **Datumsangaben lassen sich nur in CSV und JSON zurückholen**, wo die
Spaltenregel den Bezug liefert — nicht in freiem Text.

### Bekannte Eigenschaft

Ein formaterhaltender Generator schöpft aus demselben Wertevorrat wie die
Echtdaten. Bei engem Vorrat — etwa fünfstelligen Kundennummern — kann ein früher
vergebenes Pseudonym später selbst als Klartext auftauchen. Die Rückabbildung
bleibt trotzdem eindeutig, weil der gleichlautende Klartext seinerseits durch
sein eigenes Pseudonym ersetzt wurde. Sichtbar wird es nur daran, dass ein Wert
im Bestand auf beiden Seiten vorkommt.

---

## Befehle

```
obfuskation init [--profile <name>] [--from <datei>] [--description <text>]
                 [--central] [--force]
obfuskation obfuscate <datei> [-o <ziel>] [--strict] [--dry-run] [--json]
obfuskation deobfuscate [<datei>] [-o <ziel>] [--json]
obfuskation scan <datei> [--json]
obfuskation mapping list|path
obfuskation profile list [--sort name|used|changed] [--json]
```

Gemeinsame Optionen: `--config <pfad-oder-profilname>`, `--format csv|json|text`,
`--allow-unsafe-store`.

`--config` nimmt statt eines Pfades auch einen bloßen Profilnamen entgegen —
`--config demo` löst, sofern keine wörtliche Datei namens `demo` existiert,
zu `~/.config/obfuskation/profile/demo.json` auf, demselben zentralen Ort,
den auch die Oberfläche verwendet. `init --central` schreibt direkt dorthin
statt nach `obfuskation.json` im aktuellen Verzeichnis, und `profile list`
zeigt alle bekannten Profile — den zentralen Ordner plus alles, was der
Nutzungs-Index gemerkt hat — mit Name, Anzahl der Dateien sowie den
Zeitpunkten der letzten Benutzung und Änderung.

- `--strict` — Felder ohne eigene Regel führen zum Abbruch, unabhängig von der
  Vorgabe im Profil.
- `--dry-run` — schreibt weder Ausgabe noch Tabelleneinträge, liefert aber den
  vollständigen Bericht.
- `--json` — Bericht als JSON auf die Standardausgabe. Erfordert `-o`, sonst
  vermengten sich Bericht und Nutzdaten. Der Bericht enthält **nur Zähler und
  Feldnamen, niemals Werte** und darf deshalb protokolliert werden.
- `deobfuscate` ohne Dateiangabe liest von der Standardeingabe.

### Rückgabewerte

| Wert | Bedeutung |
|---|---|
| 0 | Erfolg |
| 1 | allgemeiner Fehler |
| 2 | Konfiguration fehlt oder ist fehlerhaft |
| 3 | Feld ohne Regel im strengen Modus |
| 4 | `scan` hat Verdachtsfälle gefunden |
| 5 | Ersetzungstabelle widersprüchlich oder gesperrt |

---

## Aufbau

```
src/Obfuskation.Core/    Klassenbibliothek — die gesamte Fachlogik
src/Obfuskation.Cli/     Kommandozeilenprogramm, eine dünne Hülle darum
src/Obfuskation.Gui/     Oberfläche (Avalonia), ebenfalls nur eine Hülle
tests/                   xUnit — Core und Oberfläche getrennt
assets/                  SVG-Vorlagen des Symbols
build/icon-erzeugen.sh   erzeugt daraus ICO und PNG
packaging/               .desktop-Datei für Linux
```

Die Bibliothek kennt keine Konsole und gibt nichts aus. Beide Programme binden
sie unmittelbar ein: alle Vorgänge laufen über `ObfuscationEngine`, das Profil
ist ein reines Datenobjekt zum Binden an Formulare, und `ProfileValidator`
liefert Befunde mit Feldpfad, sodass sich jeder Hinweis am zugehörigen
Eingabefeld anzeigen lässt.

Die Oberfläche kennt ihrerseits keine Fenster in den Ansichtsmodellen —
Dateidialoge kommen als Fabrik herein, Nebenfenster werden als Ereignis
erbeten. Deshalb ist die Bedienlogik ohne laufende Anwendung prüfbar, und
`tests/Obfuskation.Gui.Tests` weist unter anderem nach, dass Oberfläche und
Kommandozeile dasselbe Ergebnis liefern.

### Das Symbol

Motiv: ein Blatt, dessen obere Zeilen im Klartext stehen und dessen untere
ersetzt sind. Zwei Vorlagen — die volle ab 32 px, eine vereinfachte für 16 und
24 px, weil ein herunterskalierter Haarstrich dort zu grauem Brei würde.

```fish
./build/icon-erzeugen.sh              # erzeugt ICO und PNG
./build/icon-erzeugen.sh --behalten   # einzelne Größen zum Nachsehen
```

Das Ergebnis liegt im Repository; das Skript ist nur bei einer Motivänderung
nötig. Es braucht `rsvg-convert` und ImageMagick.

## Bauen

```fish
dotnet build
dotnet test

./build-release.sh                     # win-x64 UND linux-x64, je beide Programme
./build-release.sh --rid linux-x64     # nur diese Laufzeit
./build-release.sh --cli-only          # ohne Oberfläche
./build-release.sh --self-contained    # ohne installiertes .NET lauffähig
```

Das Ergebnis liegt unter `publish/<RID>/` und besteht je Laufzeit aus zwei
Dateien: `obfuskation` und `obfuskation-gui`. Die nativen Bibliotheken der
Oberfläche sind eingebettet.

Zielframework ist `net8.0`. Auf diesem Rechner ist keine 8.0-Laufzeit
installiert; `RollForward=Major` in `Directory.Build.props` sorgt dafür, dass
`dotnet run` und `dotnet test` trotzdem auf der vorhandenen Laufzeit laufen.

---

## Lizenz

[MIT](LICENSE) — Verwendung, Änderung und Weitergabe sind frei, der
Copyright-Hinweis muss erhalten bleiben. Ohne Gewährleistung; wer damit
personenbezogene Daten verarbeitet, bleibt selbst dafür verantwortlich (siehe
*Bitte zuerst lesen*).

Erstellt von **Gregor Stübner** und **Claude (Anthropic)**.
