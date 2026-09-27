# Plan 1.11.0: Generator-Dialog nur mit einstellbaren Arten, neuer Generator „Ausdruck“

Fassung 1.11.0 (geplant) · Stand 27. September 2026

## Kontext

Gregor hat den neuen Generator-Dialog aus 1.10.0 getestet. Unter „Art der
Ersetzung“ stehen alle 20 eingebauten Generatoren mit technischem Namen. Das
sind dieselben wie unter „Ersetzen durch“. Wählt man etwa `firstName` oder
`email`, erscheinen keine Einstellungen, denn nur 7 Arten haben welche. Das
wirkt, als könne man nur bestehende Generatoren kopieren.

Dazu kommt ein neuer Wunsch: Einstellbare Generatoren sollen zufällige Werte
nach einem Ausdruck erzeugen, angelehnt an RegEx. So lassen sich ohne
Programmierung Muster wie ein KFZ-Kennzeichen bauen: 1–3 Zeichen aus einer
Tabelle (Kreis/Stadt), dann zwei zufällige Buchstaben, dann 2 oder 3 Ziffern,
dann optional ein „E“.

**Mit Gregor geklärt:**

- Der Dialog zeigt **nur noch die einstellbaren Arten**. Die übrigen (etwa
  `numericId` als eigener Namensraum) gibt es nur noch per JSON.
- Die **Tabellen liegen im Generator selbst**. Er ist damit in sich
  vollständig und wird samt Tabellen für alle Projekte übernommen.
- Ein Tabellenverweis im Ausdruck lautet **`{name}`**, zum Beispiel
  `{kreis}-[A-Z]{2} \d{2,3}E?`.

## Hinweise für die Umsetzung (gilt für alle Pakete)

- Stil, Bauen und Testen wie in `docs/plan-regeln-generatoren.md`,
  Abschnitt „Hinweise für die Umsetzung“:
  - Kommentare deutsch ohne Umlaute, sichtbare Texte mit Umlauten und „…“.
  - Ansichtsmodelle bleiben fensterfrei.
  - Keine NuGet-Pakete, `.csproj` bleibt unverändert.
  - Testnamen sind deutsche Sätze.
  - Bauen mit `dotnet build`, testen mit `dotnet test` im Wurzelordner.
- **Keine Ausnahmen als Kontrollfluss:** Der Parser liefert `TryParse` mit
  einem Fehlerobjekt, er wirft nicht.
- Bestehende Profile mit `pattern` bleiben unverändert gültig. Der neue
  Generator ist ein **eigener Typ**, keine Erweiterung der Maskensyntax: Dort
  ist `9` ein Platzhalter, in RegEx wäre es wörtlich.

---

## P0 · Plan ins Projekt

Diesen Plan als `docs/plan-ausdruck-generator.md` ins Projekt kopieren.

## P1 · Bibliothek: Ausdruck parsen und erzeugen

### P1a · `src/Obfuskation.Core/Generation/GeneratorExpression.cs` (neu)

```csharp
public sealed record ExpressionError(int Position, string Message); // Position 1-basiert

public sealed class GeneratorExpression
{
    public static bool TryParse(string text, out GeneratorExpression? expression, out ExpressionError? error);
    public IReadOnlyList<string> TableReferences { get; }                     // in Reihenfolge, ohne Doppel
    public string Generate(ref SeedReader reader, IReadOnlyDictionary<string, IReadOnlyList<string>> tables);
    public long ValuePool(IReadOnlyDictionary<string, IReadOnlyList<string>> tables, long cap); // saettigend
    public int MinLength(IReadOnlyDictionary<string, IReadOnlyList<string>> tables);
    public int MaxLength(IReadOnlyDictionary<string, IReadOnlyList<string>> tables);
}
```

Intern ist das ein kleiner Syntaxbaum: Folge, Alternative, Wiederholung,
Zeichenklasse, Tabellenverweis und wörtliches Zeichen. Er wird rekursiv
absteigend geparst.

**Syntax (Teilmenge von RegEx, nur zum Erzeugen):**

| Schreibweise | Bedeutung | Fehlerfälle |
|---|---|---|
| `x` | wörtliches Zeichen | — |
| `\x` | Nicht-Buchstabe wörtlich (`\.`, `\{`, `\\`, `\-`) | `\` + Buchstabe außer `d` → „unbekannte Kurzform“; `\` am Ende |
| `\d` | Ziffer 0–9 | — |
| `[A-Z0-9ÄÖÜ]` | ein Zeichen aus der Menge; Bereiche `a-z`; `-` am Rand wörtlich; `\` escaped | `[^…]`, leer, `Z-A`, nicht geschlossen |
| `(a\|b)`, `(?:…)` | Gruppe, eine der Alternativen; `\|` auch auf oberster Ebene | nicht geschlossen, `)` zu viel |
| `{2}`, `{2,3}`, `{0,2}` | Anzahl, nach einem Element | `{2,}`, `{3,2}`, `{0}`, Obergrenze > 64, ohne Element davor |
| `?` | optional (= `{0,1}`) | ohne Element davor |
| `{kreis}` | ein Wert aus der Tabelle „kreis“ (Name `^[A-Za-z][A-Za-z0-9_-]*$`) | — (fehlende Tabelle meldet der Validator) |
| `^` am Anfang, `$` am Ende | werden ignoriert (Gewohnheit aus RegEx) | an anderer Stelle |
| `*`, `+` | — | „unbegrenzt; bitte {n,m} mit Obergrenze“ |
| `.` | — | „für einen Punkt `\.` schreiben, für beliebige Zeichen eine Klasse wie `[A-Z0-9]`“ |

`{` gefolgt von einer Ziffer ist eine Anzahl, gefolgt von einem Buchstaben ein
Tabellenverweis. Grenzen: Ausdruck höchstens 500 Zeichen, Schachtelung
höchstens 10 Ebenen. Die Fehlertexte sind deutsch und nennen die Stelle,
etwa „Stelle 9: „]“ fehlt – die Zeichenklasse ab Stelle 3 ist nicht
geschlossen.“

**Erzeugen** (deterministisch über `SeedReader`, siehe `SeedReader.cs`):

- Alternative: gleichverteilte Wahl.
- Anzahl: gleichverteilt in `[n, m]`; jede Wiederholung wird neu gezogen.
- Klasse: `reader.NextInt(Anzahl)`.
- Tabelle: `reader.Pick`.

**Wertevorrat:** Folge = Produkt, Alternative = Summe, `{n,m}` = Σ pᵏ,
Klasse = Größe, Tabelle = Anzahl. Die Rechnung sättigt bei `cap` und dient nur
der Warnung.

### P1b · `ExpressionGenerator` (neue Datei neben `PatternGenerator.cs`)

- `Name => "expression"`, `IsReversible => true`, `IsWordLike => true`.
  Begründung für den Kommentar: Die Werte sind kurz und unterschiedlich lang.
  Ohne Wortgrenze würde `ReverseTextMapper` „M-AB 12“ auch mitten in
  „M-AB 123“ zurückführen. Die Grenze greift ohnehin nur an Rändern aus
  Buchstaben oder Ziffern.
- `Configure`: Ausdruck parsen und Tabellen in ein Wörterbuch ohne
  Groß-/Kleinunterscheidung kopieren. Einen Parserfehler merken.
- `Generate`: Fehlt der Ausdruck, ist er fehlerhaft oder fehlt eine Tabelle,
  folgt eine `GenerationException` mit deutscher Meldung (Muster:
  `WordlistGenerator`).
- In `GeneratorRegistry.CreateDefaults` eintragen.
- `GeneratorDescriptions`: `["expression"] = "Wert nach Ausdruck, etwa Kennzeichen – mit Zeichenklassen, Anzahl und Tabellen"`.
- `Pseudonymizer.RemedyFor`: `"expression"` → „den Ausdruck erweitern (mehr
  Stellen, größere Zeichenklassen oder längere Tabellen).“

### P1c · Einstellungen und Prüfung

- `GeneratorSettings` (`Configuration/Profile.cs`) bekommt zwei
  Eigenschaften mit Doc-Kommentar:
  - `string? Expression`
  - `Dictionary<string, List<string>>? Tables`

  In JSON heißen sie `expression` und `tables`. `DictionaryKeyPolicy` ist in
  `ProfileStore.JsonOptions` nicht gesetzt, Tabellennamen bleiben also, wie
  sie sind. Ein Test sichert das ab.
- `ProfileValidator.OptionOwnership` um `("expression", "expression")` und
  `("tables", "expression")` erweitern, dazu `IsOptionSet`.
- **Alle Stellen suchen, die Optionsnamen einzeln aufzählen** (`grep -rn
  '"maskChar"' src`), etwa `GeneratorEditorViewModel.ClearOption`, und
  ergänzen.
- Neu `ValidateExpression`, aufgerufen für den Basistyp `expression`.
  - **Fehler (`Error`):**
    - Ausdruck fehlt oder ist leer.
    - Parserfehler (Meldung mit Stelle).
    - Verweis auf eine fehlende Tabelle.
    - Tabelle leer oder mit einem leeren Eintrag.
    - Tabellenname passt nicht zu `^[A-Za-z][A-Za-z0-9_-]*$`.
    - `MinLength == 0` („kann einen leeren Wert erzeugen“ – ein leeres
      Pseudonym ließe sich nicht zurückführen).
    - `MaxLength > 256`.
    - Wertevorrat 1 („erzeugt immer denselben Wert“).
  - **Warnung (`Warning`):**
    - Tabelle ist angelegt, wird aber nicht verwendet.
    - Wertevorrat unter `MinimumPatternValuePool`, mit dem Wortlaut der
      bestehenden Warnung für `pattern`.

### P1d · Vorschau mit mehreren Beispielen

In `GeneratorPreview` neu:
`TryExamples(key, settings, sample, deriver, int count, out IReadOnlyList<string> examples, out string? error)`.
Die Methode nutzt die Versuche `0..count-1` von `deriver.Derive(key, sample, attempt)`
und gibt nur verschiedene Werte zurück. `TryExample` ruft sie mit `count = 1`
auf.

## P2 · Oberfläche: Generator-Dialog

Dateien:

- `src/Obfuskation.Gui/ViewModels/GeneratorEditorViewModel.cs`
- `src/Obfuskation.Gui/Views/GeneratorEditorWindow.axaml`

### P2a · Nur einstellbare Arten

- Neuer Datensatz `GeneratorKindOption(string Name, string Title, string Description, string Example)`,
  als Datei neben `GeneratorEditorViewModel`. `Configurable` ist die feste
  Reihenfolge:

  | Name | Title | Example |
  |---|---|---|
  | token | Kennung mit Kennzeichnung | `FW~TOK_A1B2C3D4` |
  | expression | Ausdruck mit Tabellen | `M-AB 123E` |
  | pattern | Zeichenmaske | `AB-1234` |
  | wordlist | Werteliste | `Rot · Grün · Blau` |
  | partialMask | Teilmaskierung | `****1234` |
  | redact | Platzhalter | `***` |
  | dateRange | Datum aus Zeitraum | `2021-03-14` |
  | dateGeneralize | Datum gerundet | `2024-01-01` |

  `Description` kommt aus `GeneratorDescriptions.For`.
- `BaseTypes` wird zu `IReadOnlyList<GeneratorKindOption>`.
  - **Bearbeiten eines per JSON angelegten Generators mit nicht einstellbarer
    Art** (etwa `numericId`): Die Liste bekommt diesen einen Eintrag
    angehängt (Titel = Name, Beispiel leer), damit nichts still auf `token`
    fällt.
  - Dazu der Hinweis `KindHint`: „Diese Art hat hier keine Einstellungen –
    sie trennt nur die Ersetzungstabelle.“
- **XAML:**
  - Überschrift „Grundlage“ statt „Art der Ersetzung“.
  - Darunter der Satz (`muted small`): „Ein eigener Generator nutzt eines
    dieser Verfahren – mit eigenen Einstellungen und eigener
    Ersetzungstabelle.“
  - Die ComboBox bleibt, um Höhe zu sparen. Ihre Zeile zeigt links den
    Titel (SemiBold), rechts das Beispiel (mono, muted).
  - Unter der ComboBox steht die Beschreibung der gewählten Art.

### P2b · Einstellungsblock „Ausdruck“ (`ShowExpression`)

- **Eingabe:** TextBox (mono) für `Expression`, Platzhaltertext
  `{kreis}-[A-Z]{2} \d{2,3}E?`.
- **Schreibweise:** Darunter immer sichtbar ein kleines zweispaltiges Raster
  (mono links, Erklärung rechts, `small`):

  | Schreibweise | Erklärung |
  |---|---|
  | `[A-Z]` | ein Zeichen aus dem Bereich |
  | `\d` | eine Ziffer |
  | `{2}` / `{2,3}` | genau 2-mal / 2- bis 3-mal |
  | `?` | kann fehlen |
  | `(E\|H)` | eines davon |
  | `{kreis}` | ein Wert aus der Tabelle „kreis“ |
  | `\.` | Sonderzeichen wörtlich |

- **„Beispiel einsetzen ▾“** ist ein Knopf mit `MenuFlyout`. Er ersetzt
  Ausdruck und Tabellen durch eine Vorlage:
  - **KFZ-Kennzeichen:** `{kreis}-[A-Z]{2} \d{2,3}E?` mit der Tabelle
    `kreis` = B, HH, M, K, F, S, D, DO, E, L, HB, DD, H, N, DU, BO, W, BI,
    BN, MS.
  - **Kundennummer:** `KD-\d{6}`.
  - **Artikelnummer:** `[A-Z]{3}-\d{4}(-[A-Z])?`.

  Wie ein Befehl einen Parameter bekommt, zeigt `ViewModels/RelayCommand.cs`.
  Gibt es keine Parameterform, bekommt jede Vorlage ein eigenes kleines
  Ansichtsmodell mit `ApplyCommand`.
- **Tabellen:**
  - `ObservableCollection<ExpressionTableViewModel>` mit `Name`,
    `ValuesText` (ein Wert je Zeile), `CountLabel` („20 Werte“),
    `UnusedHint` („Im Ausdruck nicht verwendet.“) und `RemoveCommand`.
  - Darunter der Knopf „+ Tabelle“. Sein Name ist der erste verwendete, aber
    fehlende Tabellenname, sonst `tabelle`, eindeutig gemacht.
  - Fehlen verwendete Tabellen, erscheint ein Hinweis mit Knopf: „Im Ausdruck
    verwendet, aber nicht angelegt: kreis“ und „Anlegen“.
- **Ergebnis:** `BuildResult` schreibt `Tables` aus der Liste, in der
  Reihenfolge der Liste. `ClearOption` kennt jetzt auch `expression` und
  `tables`.
- **Eigene Fehler:** Doppelte oder leere Tabellennamen erkennt das
  Ansichtsmodell selbst. Ein Wörterbuch kann sie nicht halten. Die Fehler
  landen in `Errors` und sperren „Anlegen“.
- **Vorschau:**
  - `PreviewText` zeigt bis zu drei Beispiele aus `TryExamples`, getrennt
    durch „  ·  “. Das gilt für alle Arten.
  - `HasPreviewError` ist nur wahr, wenn `Errors` leer ist. Sonst stünde
    dieselbe Meldung zweimal da.

### P2c · Übrige Stellen

- `SettingsViewModel.GeneratorEntryViewModel.OptionsSummary`: Für
  `expression` gilt „Ausdruck {kreis}-[A-Z]{2}…“. Der Ausdruck wird nach 40
  Zeichen gekürzt; gibt es Tabellen, folgt „· 1 Tabelle“ bzw. „· n Tabellen“.
- `FieldRuleViewModel` bleibt unverändert. Für `expression` ist `HasOptions`
  falsch, der Optionsknopf der Dateiansicht bleibt also verborgen.
  Bearbeitet wird unter „Regeln & Generatoren“. Das bitte als Kommentar
  festhalten.

## P3 · Tests

**Neu: `tests/Obfuskation.Core.Tests/ExpressionGeneratorTests.cs`**

- **KFZ-Ausdruck mit kurzer Tabelle:** Über 200 Seeds passt jeder Wert zu
  `^(B|HH|M)-[A-Z]{2} \d{2,3}E?$`. Beide Längen und „E“ kommen jeweils
  mindestens einmal vor.
- **Determinismus:** Derselbe Seed ergibt denselben Wert.
- **Schreibweisen:**
  - `\.` und `\{` sind wörtlich.
  - `(?:a|b)` wird akzeptiert.
  - `^…$` werden ignoriert.
  - Umlaute in Klassen funktionieren.
- **Parserfehler mit Stelle** (Theory):
  - `[A-Z`, `(ab`, `ab)`, `{2,}`, `a{3,2}`, `a{0}`, `a*`, `a+`, `.`,
    `[^a]`, `[Z-A]`, `\q`, `{2}` ohne Element davor, Obergrenze 65.
- **Vorrat und Länge:**
  - `[A-Z]{2}\d{3}` ergibt einen Vorrat von 676 000.
  - `E?` verdoppelt ihn.
  - Eine Tabelle zählt ihre Einträge.
  - `MinLength` bzw. `MaxLength` von `A?` ist 0 bzw. 1.
- **Rundlauf:** Eine CSV-Spalte mit Ausdruck-Generator wird pseudonymisiert
  und zurückgeführt. Vorlage ist das Muster in `RoundtripTests`.
- **JSON:** Die Tabellennamen „Kreis“ und „kreis2“ bleiben nach
  Speichern und Laden unverändert (`ProfileStore`).

**`ProfileValidator` (bestehende Testdatei zum Validator suchen)**

- Fehler:
  - Der Ausdruck fehlt.
  - Die Tabelle fehlt.
  - Die Tabelle ist leer.
  - Ein leerer Wert ist möglich.
  - Der Wertevorrat ist 1.
  - `tables` hängt an einem `token`-Generator.
- Warnungen:
  - Eine Tabelle wird nicht verwendet.
  - Der Wertevorrat ist klein.

**`GeneratorPreviewTests`**

- `TryExamples` liefert bei `token` drei verschiedene Werte.
- Bei `redact` liefert es genau einen.

**`GeneratorDescriptionTests`** deckt `expression` über den vorhandenen
Jeder-hat-eine-Erklärung-Test automatisch ab.

**`GeneratorEditorViewModelTests`**

- Die Namen in `BaseTypes` entsprechen genau den verschiedenen Basistypen aus
  `OptionOwnership`. Das sichert gegen Auseinanderlaufen ab. `firstName` ist
  nicht darunter.
- Bearbeiten eines Generators vom Typ `numericId` behält `numericId`.
- Die Vorlage KFZ füllt Ausdruck und Tabelle. Die Vorschau zeigt Werte, die
  zum Muster passen.
- „+ Tabelle“ schlägt den fehlenden Namen vor, „Anlegen“ legt ihn an.
- Doppelte Tabellennamen sperren `CanApply`.
- Wechselt man von `expression` zu `token`, fallen Ausdruck und Tabellen aus
  dem Ergebnis.

## P4 · Version und Doku (grob)

- **Version:** `Directory.Build.props` auf `1.11.0`, dazu ein Absatz im
  Änderungskommentar.
- **`docs/anwenderdokumentation.md`** (`version: 1.11.0`):
  - Im Abschnitt zum Generator-Dialog „Grundlage“ und die einstellbaren Arten
    nachziehen.
  - In der Generatortabelle (um Z. 836) die Zeile `expression`.
  - Ein kurzer Unterabschnitt „Ausdruck“ mit der Schreibweise-Tabelle und dem
    KFZ-Beispiel als JSON:

    ```json
    "kfz": { "type": "expression", "expression": "{kreis}-[A-Z]{2} \\d{2,3}E?",
             "tables": { "kreis": ["B", "HH", "M"] } }
    ```

    Der Rückwärtsschrägstrich ist in JSON doppelt.
- **`docs/entwicklerdokumentation.md`** (`version: 1.11.0`): Absatz zu
  `GeneratorExpression`/`ExpressionGenerator`: Parser, `TryParse` statt
  Ausnahme, Vorrat und Länge für den Validator, `IsWordLike`.
- **README.md und README.de.md:** In der Generatortabelle je eine Zeile
  `expression`.
- **`docs/Feature.md`:** Abschnitt „Umgesetzt in 1.11.0“. Als neue Ideen
  vormerken:
  - `domain` (email), `country` (iban/bic) und `maxDays` (dateShift) im
    Dialog einstellbar machen;
  - Gewichte für `?` und Tabelleneinträge;
  - eine vollständige Kreisliste als mitgelieferte Tabelle.
- Keine Bilder, kein Glätten (siehe Memory „Experimenteller Status“).

## P5 · Review und Sichtprüfung (Opus)

1. `dotnet build` ohne neue Warnungen, `dotnet test` für alle drei
   Testprojekte grün.
2. **Review der Kernstücke:**
   - `GeneratorExpression`:
     - Parserfälle und Stellenangaben,
     - `{`-Unterscheidung,
     - Sättigung des Vorrats,
     - Min- und Maxlänge bei Alternativen und Tabellen.
   - `ValidateExpression`.
   - Tabellenabgleich im Ansichtsmodell: Liste → `Tables`, Doppel,
     Typwechsel.
3. **Sichtprüfung auf Xvfb** (Muster aus `docs/bilder/aufnehmen.sh`, Bilder
   ins Scratchpad):
   - „Regeln & Generatoren“ → „+ Neuer Generator…“: Die Liste zeigt 8
     Arten mit Beispiel.
   - „Ausdruck“ → „Beispiel einsetzen ▾ → KFZ-Kennzeichen“: Die Vorschau
     zeigt drei Kennzeichen.
   - Den Tabellennamen ändern: Der Hinweis zur fehlenden Tabelle erscheint,
     „Anlegen“ behebt ihn.
   - Eine Textregel mit diesem Generator anlegen. In der Textansicht wird
     „M-XY 123“ durch ein Pseudo-Kennzeichen ersetzt, und „Klartext
     wiederherstellen“ führt es zurück.
   - Helles und dunkles Thema, Dialoghöhe mit Bildlauf.

## Delegation (Token-Bewusstsein)

| Paket | Wer | Warum |
|---|---|---|
| P0–P4 | ein Sonnet-Subagent, nacheinander, mit dem Pfad `docs/plan-ausdruck-generator.md` | klar spezifiziert: Parser nach Tabelle, Ansichtsmodell nach bestehendem Muster, Tests, grobe Doku |
| P5 | Opus | Review der Kernstücke und Sichtprüfung |

Auftrag an den Subagenten: „Setze P0 bis P4 aus
`docs/plan-ausdruck-generator.md` um. P5 nicht anfassen. Nach jedem Paket
`dotnet build` und `dotnet test`. Nichts committen. Am Ende berichten:
geänderte Dateien, neue Tests, offene Punkte und Abweichungen vom Plan mit
Begründung.“

Commit, Push und Release folgen **nur auf ausdrückliche Aufforderung**. Nach
P5 frage ich nach.

## Verifikation (Zusammenfassung)

- **Automatisch:** `dotnet test`, alle drei Projekte grün, mit den neuen Tests
  aus P3.
- **Von Hand:** die Sichtprüfung aus P5.3. Dazu ein Rundlauf über die
  Kommandozeile mit einer kleinen CSV und einem Profil mit
  `"type": "expression"`: pseudonymisieren, dann zurückführen, Ergebnis
  gleich dem Original.
