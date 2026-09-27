# Plan 1.13.0: Fehler bei eingebauten Namen, schlanke Dateien, restliche Optionen in die Oberfläche

Fassung 1.13.0 (geplant) · Stand 27. September 2026

## Kontext

Gregor hat drei Aufträge gegeben. Sie folgen aus der Prüfung der
Erweiterungsdatei `obfuskation.json`
(siehe `docs/anwenderdokumentation.md`, Kapitel 11, Abschnitt „Aufbau der
Datei: alle Einträge“).

1. **Fehler beheben:** Ein Generator-Eintrag, dessen Schlüssel ein
   eingebauter Generatorname ist und der kein `type` hat, stellt den
   eingebauten Generator um, etwa `"email": { "domain": "firma.test" }` oder
   `"dateShift": { "maxDays": 30 }`. Im Reiter „Eigene Generatoren“ erscheint
   er fälschlich als `token` („allgemeine Kennung (TOK_…)“), weil
   `GeneratorEntryViewModel.TypeLabel` bei leerem `Type` auf `"token"`
   zurückfällt. „Bearbeiten…“ öffnet den Dialog mit der Art „Kennung mit
   Kennzeichnung“, und der Namensfehler „eingebauter Generator“ sperrt
   „Übernehmen“.
2. **Aufräumen:** Beim Speichern sollen nur wirklich gesetzte Werte in der
   Datei stehen. Heute stehen `"maxDays": 400`, `"keepFirst": 0` und
   `"keepLast": 0` an jedem Generator, `"captureGroup": 0` und
   `"ignoreCase": false` an jeder Textregel sowie `"ignoreCase": true` an
   jedem Spalten-Vorschlag. Grund: `JsonIgnoreCondition.WhenWritingNull`
   überspringt nur `null`. **Bestehende Dateien sollen beim nächsten
   Speichern ebenfalls aufgeräumt werden.**
3. **In die Oberfläche holen:**
   - `formats` (`dateShift`, `dateRange`, `dateGeneralize`), `country`
     (`iban`, `bic`) und `domain` (`email`) in den Generator-Dialog,
   - `captureGroup` ins Formular der Textregel.

## Hinweise für die Umsetzung

- Stil, Bauen und Testen wie in `docs/plan-regeln-generatoren.md`,
  Abschnitt „Hinweise für die Umsetzung“:
  - Kommentare deutsch ohne Umlaute, sichtbare Texte mit Umlauten und „…“.
  - Ansichtsmodelle bleiben fensterfrei, ausführliche `<summary>`.
  - Testnamen sind deutsche Sätze.
  - Keine NuGet-Pakete, `.csproj` bleibt unverändert.
  - In XAML-Kommentaren nie `--`, stattdessen „—“.
- Keine Ausnahmen als Kontrollfluss.
- Nach jedem Paket `dotnet build` (ohne neue Warnungen) und `dotnet test`.

---

## P1 · Bibliothek: Optionen mit mehreren Grundlagen

Heute ordnet `ProfileValidator.OptionOwnership`
(`src/Obfuskation.Core/Configuration/ProfileValidator.cs`) jede Option genau
einem Basistyp zu. Die Oberfläche baut daraus ein Wörterbuch Option →
Basistyp (`ToDictionary`, in `GeneratorEditorViewModel` und
`FieldRuleViewModel`). Das trägt nicht mehr: `formats` gilt für drei Arten,
`country` für zwei.

- **`OptionOwnership`** bleibt eine Liste von Paaren `(Option, BaseType)`,
  darf eine Option aber **mehrfach** führen. Neue Einträge:

  | Option | Basistyp |
  |---|---|
  | `formats` | `dateShift` |
  | `formats` | `dateRange` |
  | `formats` | `dateGeneralize` |
  | `country` | `iban` |
  | `country` | `bic` |
  | `domain` | `email` |

  Den Doc-Kommentar anpassen („einzig zulässigen Basistyp“ stimmt dann nicht
  mehr).
- **Neue öffentliche Helfer** in `ProfileValidator`:
  - `public static bool OptionBelongsTo(string option, string baseType)`,
    ohne Groß-/Kleinunterscheidung;
  - `public static IReadOnlyList<string> OwnersOf(string option)`.
- **`ValidateOptionOwnership`:**
  - Die Methode geht die **verschiedenen** Optionen durch. Ist eine gesetzt
    und `baseName` nicht unter ihren Besitzern, folgt ein Fehler.
  - Bei genau einem Besitzer bleibt der Wortlaut wie heute. Bei mehreren
    lautet er: `'{option}' gilt nur für die Generatortypen 'iban' und 'bic'.
    Generator '{key}' erzeugt Werte vom Typ '{baseName}' und wertet
    '{option}' nicht aus.`
  - Der Sonderfall `prefix` bleibt.
- **`IsOptionSet`** kennt zusätzlich:
  - `formats` → `settings.Formats is { Count: > 0 }`,
  - `country` und `domain` → jeweils `!string.IsNullOrWhiteSpace(…)`.
- **Neue Prüfungen** in `ValidateGeneratorEntry`, jeweils nur wenn gesetzt:
  - `country`: genau zwei ASCII-Buchstaben, sonst **Fehler**, denn der
    Generator übergeht jeden anderen Wert stillschweigend (siehe
    `IbanGenerator.Configure`). Pfad `….country`.
  - `domain`: Nach `TrimStart('@')` nicht leer, ohne Leerraum, ohne weiteres
    `@`, mit mindestens einem Punkt; sonst **Fehler**.
    Zusätzlich eine **Warnung**, wenn die Domain nicht zu den reservierten
    gehört, also weder auf `.invalid`, `.test`, `.example` oder
    `.localhost` endet noch `example.com`, `example.net` oder `example.org`
    ist: „Die Domain '{d}' ist möglicherweise echt erreichbar – erzeugte
    Adressen könnten echten Postfächern gehören. Eine reservierte Domain wie
    firma.test ist sicherer.“
  - `formats`: kein leerer Eintrag, sonst **Fehler**.
- **Textregeln** (`ValidateTextRuleList`, um Z. 567): Kompiliert das Muster
  und ist `captureGroup` größer als die höchste Gruppennummer
  (`regex.GetGroupNumbers().Max()`), gibt es einen **Fehler**: „Das Muster
  hat keine Gruppe {k} – die Regel fände nie etwas.“ Heute läuft die Regel
  dann still ins Leere (`TextRuleEngine`: `group.Success` ist falsch).
  Dafür das bereits erzeugte `Regex`-Objekt wiederverwenden.

## P2 · Bibliothek: nur gesetzte Werte speichern

- **Neues Attribut**, neue Datei
  `src/Obfuskation.Core/Configuration/OmitFromJsonWhenAttribute.cs`:

  ```csharp
  [AttributeUsage(AttributeTargets.Property)]
  public sealed class OmitFromJsonWhenAttribute(object value) : Attribute
  {
      public object Value { get; } = value;
  }
  ```

  Doc-Kommentar mit dem Warum: Die Dateien sollen nur zeigen, was jemand
  eingestellt hat. `WhenWritingDefault` hilft nicht, weil es mit
  `default(T)` vergleicht, `maxDays` aber 400 als Vorgabe hat.
- **Anbringen**, jeweils an der Eigenschaft in `Profile.cs`:

  | Eigenschaft | Wert |
  |---|---|
  | `GeneratorSettings.MaxDays` | `GeneratorSettings.DefaultMaxDays` |
  | `GeneratorSettings.KeepFirst` | `0` |
  | `GeneratorSettings.KeepLast` | `0` |
  | `TextRule.CaptureGroup` | `0` |
  | `TextRule.IgnoreCase` | `false` |
  | `FieldNameRule.IgnoreCase` | `true` |

  Sonst nichts. `priority`, `generator`, `version` sowie die Felder des
  Profils (`action`, `hasHeaderRecord` usw.) bleiben, wie sie sind.
- **`ProfileStore.JsonOptions`** bekommt
  `TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitConfiguredDefaults } }`.
  Der Modifier geht `typeInfo.Properties` durch. Trägt
  `property.AttributeProvider` ein `OmitFromJsonWhenAttribute`, setzt er
  `property.ShouldSerialize = (_, value) => !Equals(value, attribute.Value)`.
  Kommentar dazu: Das greift auch beim Laden und Wiederspeichern einer alten
  Datei, und nur so verschwinden die Vorgabewerte aus bestehenden Dateien.
  Beim Einlesen ändert sich nichts, denn eine fehlende Eigenschaft behält
  ihre Vorgabe.
- **Betroffen** sind alle Aufrufer von `ProfileStore.JsonOptions`: Profile,
  `ExtensionLibrary.Save` und `DeepCopy`. `MappingStore` und `ProfileIndex`
  haben eigene Optionen und bleiben unberührt.

## P3 · Oberfläche: Generator-Dialog und Liste „Eigene Generatoren“

### P3a · Zuordnung nutzen

- In `GeneratorEditorViewModel` und `FieldRuleViewModel` entfällt das
  Wörterbuch `OptionBaseTypes`. Die `Show…`-Eigenschaften fragen
  `ProfileValidator.OptionBelongsTo(option, <gewählter Basistyp>)`.
  `FieldRuleViewModel` behält dabei sein `_action == Pseudonymize` aus
  `IsBaseType`.
- `GeneratorEditorViewModel.BuildResult` leert jede Option, die nicht zum
  gewählten Basistyp gehört. Es geht dafür die verschiedenen Optionen durch.
  `ClearOption` kennt `formats`, `country` und `domain` (jeweils `null`).
  Der Doc-Kommentar über „Felder außerhalb dieser Zuordnung“ entfällt,
  denn es gibt keine mehr.
- `FieldRuleViewModel.HasOptions` bleibt ohne `formats`, `country` und
  `domain`, wie heute schon ohne `expression` und `maxDays`. Den Kommentar
  daran nachziehen.

### P3b · Neue Arten und Blöcke

- **`GeneratorKindOption.Configurable`** wird zu dieser Reihenfolge:

  | Name | Titel | Beispiel |
  |---|---|---|
  | `token` | wie bisher | |
  | `expression` | wie bisher | |
  | `pattern` | wie bisher | |
  | `wordlist` | wie bisher | |
  | `partialMask` | wie bisher | |
  | `redact` | wie bisher | |
  | `email` | E-Mail-Adresse | `lena.koch042@example.invalid` |
  | `iban` | IBAN | `DE89370400440532013000` |
  | `bic` | BIC | `KQZTDEWR` |
  | `dateShift` | wie bisher | |
  | `dateRange` | wie bisher | |
  | `dateGeneralize` | wie bisher | |

  Der bestehende Test „BaseTypes entsprechen den Basistypen aus
  OptionOwnership“ muss danach weiter gelten.
- **Drei neue Blöcke** im XAML (`GeneratorEditorWindow.axaml`), gebaut wie
  die vorhandenen: Titel `card-title`, Eingabe, Erklärung `muted small`.
  - **„Eigene Datumsformate“** (`ShowFormats`) steht **nach** den Blöcken
    der drei Datumsarten, weil er für alle drei gilt.
    - Eingabe: mehrzeilige TextBox `FormatsText`, ein Format je Zeile, mono,
      `MinHeight` 60, Platzhalter „z. B. dd.MM.yy“.
    - Das Ansichtsmodell zerlegt wie `ValuesText` und schreibt eine leere
      Liste als `null`.
    - Erklärung: „Werden vor den eingebauten Formaten geprüft. Schreibweise:
      dd Tag, MM Monat, yyyy Jahr, yy zweistellig, HH:mm Uhrzeit. Eingebaut:
      {BuiltInFormatsText}“. `BuiltInFormatsText` ergibt
      `string.Join(", ", DateValues.FallbackFormats)`.
  - **„Land“** (`ShowCountry`):
    - Eingabe: TextBox `Country`, `MaxLength` 2, Breite 80, Platzhalter
      `DE`. Der Setter schreibt Großbuchstaben, leer wird `null`.
    - Erklärung: „Zweistelliger Ländercode, etwa AT oder CH. Gilt nur, wenn
      das Original selbst keinen Ländercode erkennen lässt; ohne Angabe DE.“
  - **„Domain“** (`ShowDomain`):
    - Eingabe: TextBox `Domain`, Platzhalter `example.invalid`.
    - Erklärung: „Ohne Angabe example.invalid – eine reservierte Domain,
      unter der es keine echten Postfächer gibt. Eine eigene Domain sollte
      ebenso wenig erreichbar sein, etwa firma.test.“
    - Die Warnung aus P1 erscheint nicht in `Errors`, die nur Fehler
      sammelt. Das Ansichtsmodell zeigt sie darum als `DomainWarning`
      (`warning small`). Die Bedingung dafür liegt als öffentlicher Helfer
      `ProfileValidator.IsReservedDomain(string)` in der Bibliothek, damit
      Prüfung und Oberfläche dieselbe Liste nutzen.
- **Vorschau:** Das Vorgabebeispiel folgt der Art (`DefaultSampleFor`):
  - `email` → `anna.berg@firma.de`,
  - `iban` → `AT611904300234573201`,
  - `bic` → `COBADEFFXXX`,
  - Datumsarten wie bisher.

  `RaiseOptionVisibilityChanged` nachziehen.

### P3c · Einträge, die einen eingebauten Generator umstellen (der Fehler)

- **Begriff:** Ein Eintrag „stellt einen eingebauten Generator um“, wenn
  sein Schlüssel in `GeneratorRegistry.KnownNames` steht und sein `Type`
  leer ist oder dem Schlüssel gleicht (beides ohne
  Groß-/Kleinunterscheidung).
- **`GeneratorEntryViewModel`** (`SettingsViewModel.cs`):
  - `TypeLabel` → der Basistyp: `Type`, wenn gesetzt, sonst der
    **Schlüssel**. Diese Logik gilt überall sonst (`GeneratorRegistry.Build`,
    `ProfileValidator`). Das behebt die falsche Beschriftung.
  - Neu `IsBuiltInOverride`. `DescriptionLabel` hängt dann
    „ · stellt den eingebauten Generator um“ an.
  - `RemoveCommand` ist bei einer Umstellung auch dann erlaubt, wenn
    Regeln den Namen verwenden. Entfernen setzt nur den eingebauten
    Generator zurück, die Regeln laufen weiter.
    `RemoveTooltip` lautet dann: „Entfernen setzt den eingebauten
    Generator „{name}“ auf seine Vorgaben zurück.“
  - `OptionsSummary` ergänzen, mehrere Teile verbunden mit „ · “:
    - `email` → „Domain {d}“,
    - `iban`/`bic` → „Land {c}“,
    - bei den Datumsarten „1 eigenes Format“ bzw. „{n} eigene Formate“,
      bei `dateShift` nach „bis ± … Tage“.
- **`GeneratorEditorViewModel`**, Bearbeiten einer Umstellung
  (`_isBuiltInOverride = IsEditMode && KnownNames.Contains(existingName)`):
  - `_selectedBaseTypeName` ist der Schlüssel, nicht `token`.
  - `CanRename` und `CanChangeType` sind falsch; `NameError` ist `null`.
  - Neu `OverrideHint`: „Dieser Eintrag stellt den eingebauten Generator
    „{name}“ um – er gilt überall, wo „{name}“ gewählt ist. Name und
    Grundlage stehen deshalb fest.“ `UsersHint` bleibt dann verborgen.
  - `BuildResult` lässt `Type` bei einer Umstellung so, wie er im Entwurf
    stand, damit die Datei unverändert `"email": { … }` bleibt.
  - Ist die Art nicht einstellbar (etwa `"numericId": {}`), greift der
    bestehende Weg über `GeneratorKindOption.ForUnconfigurable`.
- **Anlegen** einer Umstellung über den Dialog bleibt gesperrt. Die
  Namensprüfung beim Anlegen ist unverändert.

## P4 · Oberfläche: `captureGroup` im Textregel-Formular

- **`TextRuleViewModel`** (`src/Obfuskation.Gui/ViewModels/TextRulesViewModel.cs`)
  bekommt `CaptureGroup`, gebaut wie `Priority` (um Z. 688): dieselbe
  Sperre über `IsEditable`, dieselben Benachrichtigungen, dieselbe
  Neuberechnung von Erprobung und Prüfung.
- Dazu `CaptureGroupWarning`: Hat das gültige Muster keine Gruppe mit
  dieser Nummer, lautet sie „Das Muster hat keine Gruppe {k} – die Regel
  fände nie etwas.“ Bei ungültigem Muster oder `0` ist sie `null`. Neu
  berechnet wird sie auch, wenn sich das Muster ändert.
- **`TextRulesPanel.axaml`**, Bereich „Erweitert“: neue Zeile unter
  „Priorität“ mit der Beschriftung „Ersetzte Gruppe“.
  - Eingabe: NumericUpDown, 0 bis 99, Breite 110.
  - Erklärung: „0 = der ganze Treffer. Mit einem eigenen Ausdruck wie
    IBAN:\s*(\S+) und Gruppe 1 bleibt „IBAN:“ stehen.“
  - Darunter die Warnung.

## P5 · Tests

**Core** (`ScanAndConfigTests` oder eine neue, passende Datei):

- **Mehrere Besitzer:**
  - `country` an `bic` ist kein Befund, an `token` ein Fehler, dessen Text
    „'iban' und 'bic'“ nennt.
  - `formats` an `dateRange` ist in Ordnung, an `email` ein Fehler.
- **Neue Prüfungen:**
  - Fehler: `country` „DEU“, `domain` „firma test“, `domain` ohne Punkt,
    `formats` mit leerem Eintrag.
  - Warnung: `domain` „firma.de“.
  - Kein Befund: `domain` „firma.test“ und „@example.org“.
- **`captureGroup`:** 2 bei einem Muster mit nur einer Gruppe ist ein
  Fehler; 1 bei `IBAN:\s*(\S+)` ist in Ordnung.
- **Speichern** (`ProfileStoreTests` oder `ExtensionLibraryTests`):
  - Eine Erweiterung mit Token-Generator (nur `prefix`), Textregel (ohne
    `captureGroup`/`ignoreCase`) und Spalten-Vorschlag (`ignoreCase` true)
    speichern. Der Dateitext enthält weder `maxDays`, `keepFirst`,
    `keepLast` noch `captureGroup`, und `ignoreCase` kommt nicht vor.
  - `maxDays` 30, `keepLast` 2, `captureGroup` 1 und ein `ignoreCase` false
    beim Spalten-Vorschlag stehen dagegen drin.
  - Laden danach ergibt dieselben Werte.
  - Eine alte Datei mit `"maxDays": 400` laden und speichern: Der Eintrag
    ist danach weg.

**Gui** (`GeneratorEditorViewModelTests`, `SettingsViewModelTests`,
`TextRuleViewModel`-Tests; die vorhandene Testdatei zu den Textregeln
suchen):

- **Umstellung eines eingebauten Generators:**
  - Ein Eintrag `"email": { Domain = "firma.test" }` in der Erweiterung
    zeigt in der Liste `TypeLabel` „email“, und die Beschreibung enthält
    „stellt den eingebauten Generator um“.
  - Bearbeiten öffnet mit `SelectedBaseType.Name == "email"`,
    `CanApply` ist wahr. Nach dem Übernehmen ist `ResultSettings.Type`
    `null` und `Domain` geändert.
  - Entfernen ist trotz Verwendung durch eine Regel möglich.
- **Neue Arten und Felder:**
  - `domain`, `country` und `formats` landen im Ergebnis, ein Wechsel der
    Art leert sie.
  - `Country` „at“ wird zu „AT“.
  - `DomainWarning` erscheint bei „firma.de“, nicht bei „firma.test“.
  - Die Vorschau für `email` mit Domain „firma.test“ endet auf
    „@firma.test“.
- **`captureGroup`:**
  - `CaptureGroup` wird in die Regel geschrieben.
  - `CaptureGroupWarning` erscheint bei einer Gruppe, die das Muster nicht
    hat.

## P6 · Version und Doku

- **Version:** `Directory.Build.props` auf `1.13.0`, dazu ein Absatz im
  Änderungskommentar (Stil der vorigen).
- **`docs/anwenderdokumentation.md`** (`version: 1.13.0`), Kapitel 11,
  Abschnitt „Aufbau der Datei: alle Einträge“:
  - In den Tabellen `formats`, `country`, `domain` und `captureGroup` auf
    „ja“ setzen, jeweils mit dem Ort in der Oberfläche.
  - `type`: „ja für die zwölf einstellbaren Arten“.
  - Die Liste „Nur mit dem Texteditor“ schrumpft auf zwei Punkte:
    - Generatoren auf Basis der neun übrigen Arten (etwa `numericId`,
      `firstName`),
    - das **Anlegen** einer Umstellung eines eingebauten Generators.
      Bearbeiten und Entfernen gehen jetzt im Reiter „Eigene Generatoren“.
  - Den Absatz „Vorgabewerte in der Datei“ ersetzen: Gespeichert werden nur
    gesetzte Werte, eine ältere Datei wird beim nächsten Speichern
    aufgeräumt.
  - Im Abschnitt zum Generator-Dialog (Kapitel 5) die Zahl der Arten und die
    neuen Blöcke kurz nachziehen.
- **`docs/entwicklerdokumentation.md`** (`version: 1.13.0`):
  - `OptionOwnership` mit mehreren Besitzern und die Helfer
    `OptionBelongsTo`/`OwnersOf`.
  - `OmitFromJsonWhenAttribute` samt Modifier.
  - Den Absatz „`int`- und `bool`-Felder schreibt `ProfileStore` immer mit“
    und „Was die Oberfläche nicht einstellt“ an den neuen Stand anpassen.
- **`docs/Feature.md`:**
  - Fassung 1.13.0.
  - Neuer Abschnitt „3.8 Umgesetzt in 1.13.0“ mit drei Punkten.
  - C6 als erledigt markieren.
- Keine Bilder, kein Glätten.

## P7 · Review und Sichtprüfung (Opus)

- **Review:**
  - P1: Mehrfachbesitz, Fehlermeldungen, `captureGroup`-Prüfung.
  - P2: Modifier, Rundlauf, Aufräumen alter Dateien.
  - P3c: die Umstellung.
- **Sichtprüfung auf Xvfb:**
  - die Dialogblöcke für `email`, `iban` und `dateShift` mit Formaten;
  - die Umstellung `email` in der Liste und im Dialog;
  - das Feld „Ersetzte Gruppe“ im Textregel-Formular.
- **Echte Datei:** Gregors Erweiterungsdatei
  (`~/.config/obfuskation/obfuskation.json`) wird **nicht** angefasst. Das
  Aufräumen prüfen Tests und eine Kopie im Scratchpad.

## Delegation

| Paket | Wer |
|---|---|
| P1–P6 | ein Sonnet-Subagent, mit dem Pfad dieser Datei |
| P7 | Opus |

Auftrag an den Subagenten: „Setze P1 bis P6 aus
`docs/plan-optionen-und-speichern.md` um. P7 nicht anfassen. Nach jedem
Paket `dotnet build` und `dotnet test`. Nichts committen. Die Datei
`~/.config/obfuskation/obfuskation.json` nicht verändern. Am Ende berichten:
geänderte Dateien, neue Tests, offene Punkte und Abweichungen vom Plan mit
Begründung.“
