# Plan 1.10.0: Regeln & Generatoren, Farben links, Status „Experimentell“

Fassung 1.10.0 (geplant) · Stand 26. September 2026

## Kontext

Gregor hat 1.9.0 getestet und fünf Wünsche geäußert:

1. **Textansicht, Fundliste:** Jede Zeile soll zeigen, ob der Fund aus einer
   Regel des Projekts oder aus einer Regel für alle Projekte stammt.
2. **Textansicht, linke Seite:** Die Farben sollen auch links erscheinen.
   Heute sind sie nur in der Prüffassung sichtbar, also nach „Fertig“ oder
   nach dem Einfügen. Im Eingabefeld (`TextBox`, Zustand „Bearbeiten“) fehlen
   sie. Wer tippt, sieht links nichts.
3. **Neuer Generator von überall:** Wer im Dialog „Immer ersetzen“ eine Regel
   anlegt, kann nur bestehende Generatoren wählen. Dort soll man auch einen
   neuen anlegen können, wahlweise für das Projekt oder für alle Projekte.
   Auch der Reiter „Eigene Generatoren“ hat heute keinen Weg, einen Generator
   anzulegen, und bearbeiten lässt sich dort nur das Präfix.
4. **Einheitliche Einstiege:**
   - Die Dateiansicht bekommt statt „Immer ersetzen…“ den Knopf „Regeln
     bearbeiten…“, wie die Textansicht.
   - Das Fenster „Einstellungen“ heißt künftig **„Regeln & Generatoren“**.
   - Die Startseite bekommt denselben Einstieg. Ohne Projekt zeigt er nur
     die Regeln und Generatoren für alle Projekte.
5. **Veröffentlichen:** Das Projekt wird ausdrücklich als „Experimentell“
   gekennzeichnet (es dient als Testfeld für verschiedene KI-Modelle) und
   danach auf GitHub veröffentlicht. Die Doku wird nur grob nachgezogen;
   perfektioniert wird sie erst, wenn keine Entwicklungsvorschläge mehr
   offen sind.

## Mit Gregor geklärte Entscheidungen

- „Regeln bearbeiten…“ öffnet in **beiden** Ansichten das große Fenster
  „Regeln & Generatoren“, Reiter „Textregeln“. Den kleinen Dialog „Immer
  ersetzen“ gibt es danach nur noch beim Markieren von Text: Kontextmenü,
  Strg+M und „immer…“ in der Fundliste. Sein Knopf „In den Einstellungen
  bearbeiten…“ heißt künftig „Regeln & Generatoren…“.
- Der Status „Experimentell“ steht an zwei Stellen: als Hinweis im README
  (Englisch und Deutsch) sowie in der GitHub-Beschreibung plus Topic
  `experimental`. Kein Pre-release, kein Vermerk im Über-Fenster.
- Nach der Umsetzung folgen Commit, Push und Release 1.10.0 über
  `./upd-github.sh`. Vor dem Push fragt Claude noch einmal nach.
- Das Repo `gstuebner/obfuskation` ist bereits öffentlich (Releases v1.2.0
  bis v1.9.0), und `main` ist aktuell.

## Hinweise für die Umsetzung (gilt für alle Pakete)

- **Stil:** Kommentare und Bezeichner folgen dem Bestand. Kommentare sind
  deutsch, ohne Umlaute (ae, oe, ue) und erklären das Warum. Sichtbare Texte
  haben echte Umlaute und typografische Anführungszeichen „…“. Ansichtsmodelle
  bleiben fensterfrei: Fenster entstehen nur in
  `src/Obfuskation.Gui/Services/DialogService.cs`. Ausführliche
  `<summary>`-Kommentare wie im Bestand.
- **Bauen und Testen:** `dotnet build` und `dotnet test` im Wurzelordner.
  Das läuft dank `<RollForward>Major</RollForward>` auch ohne .NET-8-Runtime.
  Keine neuen NuGet-Pakete. Die `.csproj`-Dateien bleiben unverändert.
- **Kennungen im Code** (`SettingsViewModel`, `SettingsTab`, `ShowSettingsCommand`
  …) werden **nicht** umbenannt. Es ändern sich nur sichtbare Texte und
  Kommentare, die sie zitieren.
- **Tests:** Jede Verhaltensänderung bekommt einen Test in
  `tests/Obfuskation.Gui.Tests` oder `tests/Obfuskation.Core.Tests`. Die
  Testnamen folgen dem Bestand: deutsche Sätze mit Unterstrichen.

---

## P0 · Plan ins Projekt legen

Diesen Plan als `docs/plan-regeln-generatoren.md` ins Projekt kopieren (wie
`docs/plan-einstellungen.md`) und mit committen.

## P1 · Umbenennungen und Einstiege

| Datei | Änderung |
|---|---|
| `src/Obfuskation.Gui/Views/SettingsWindow.axaml` | `Title` an eine neue Eigenschaft `SettingsViewModel.WindowTitle` binden: „Regeln & Generatoren – {ProjectTitle}“, ohne Profil „Regeln & Generatoren – alle Projekte“. Ohne Profil (`!HasProfile`) steht über dem TabControl die Zeile „Kein Projekt geöffnet: Hier stehen nur Regeln und Generatoren für alle Projekte.“ (`muted small`). Die Karte „Dieses Projekt“ im Reiter „Eigene Generatoren“ ist ohne Profil ausgeblendet. |
| `src/Obfuskation.Gui/Views/MainWindow.axaml` | Knopf „⚙ Einstellungen“ → „⚙ Regeln & Generatoren“, Tooltip „Textregeln, Generatoren, Spalten-Vorschläge und Ablageort (Strg+,)“. Menüpunkt „Einstellungen…“ unter „Mehr ▾“ → „Regeln & Generatoren…“. **Bei 760 px Fensterbreite messen** (Dateiansicht mit Profil, breiteste Kopfzeile). Wird etwas abgeschnitten, zeigt der Themen-Knopf nur noch sein Symbol. |
| `src/Obfuskation.Gui/Views/FilesView.axaml` (Z. 131) | „Immer ersetzen…“ → „Regeln bearbeiten…“, `Command="{Binding ShowSettingsCommand}"` (ohne Parameter öffnet der Reiter „Textregeln“). Tooltip: „Textregeln und Generatoren im Fenster „Regeln & Generatoren“ bearbeiten“. |
| `src/Obfuskation.Gui/ViewModels/MainViewModel.cs` | `ShowAlwaysReplaceCommand` und `CurrentFileContextText()` entfernen, falls nichts anderes sie braucht; `ShowAlwaysReplaceAsync` bleibt für die Textansicht. `ShowSettingsAsync` bekommt den Parameter `bool globalOnly = false`. Ist er gesetzt, übergibt es `session: null` an `SettingsViewModel`; das Verwerfen der Engine, `RefreshGenerators` und `Text?.RefreshPreview()` nach dem Übernehmen bleiben unverändert. Neuer Befehl `ShowGlobalRulesCommand` ruft `ShowSettingsAsync(SettingsTab.TextRules, globalOnly: true)` auf und wird an `StartViewModel` durchgereicht. Statuszeilen „Einstellungen übernommen…“ → „Regeln & Generatoren übernommen…“ (Rest der Texte sinngemäß). |
| `src/Obfuskation.Gui/ViewModels/StartViewModel.cs`, `Views/StartView.axaml` | Neuer Konstruktorparameter für den Befehl. Unten zwei flache Knöpfe nebeneinander, mittig: „Regeln & Generatoren…“ und „Kurzhilfe…“. Tooltip des ersten: „Regeln und Generatoren für alle Projekte“. |
| `src/Obfuskation.Gui/Views/AlwaysReplaceWindow.axaml` (Z. 150) | Knopf → „Regeln & Generatoren…“. Den Breitenkommentar oben nachziehen. |
| `src/Obfuskation.Gui/Views/TextView.axaml` | Tooltip von „Regeln bearbeiten…“ → „Regeln im Fenster „Regeln & Generatoren“ bearbeiten“. Hinweis unter dem linken Text (Z. 119): „… oder Regeln unter ⚙ Regeln & Generatoren bearbeiten.“ |
| `src/Obfuskation.Gui/Views/HelpWindow.axaml` | Absätze zu Einstellungen und „Immer ersetzen“ an die neuen Namen und Wege anpassen: Dateiansicht „Regeln bearbeiten…“, Startseite, neuer Generator. |
| `src/Obfuskation.Gui/Services/DialogService.cs` | Den Kommentar zu „In den Einstellungen bearbeiten…“ anpassen. |

Tests:

- `MainViewModelTests`: Den Test zum früheren Knopf „Immer ersetzen…“ der
  Dateiansicht (um Z. 196–208) umschreiben, sodass `ShowSettingsCommand` aus
  der Dateiansicht `LastSettingsViewModel` mit Reiter „Textregeln“ liefert.
  Die Prüfung `WindowTitle == "Immer ersetzen"` bei Z. 1636 anpassen oder
  entfernen, wenn sie am Dateieinstieg hing.
- Neu: `ShowGlobalRulesCommand` öffnet die Einstellungen ohne Profil
  (`HasProfile == false`), **auch wenn ein Profil geladen ist**. Nach dem
  Übernehmen bleibt das geladene Profil unverändert.
- `SettingsViewModelTests`: `WindowTitle` mit und ohne Profil.

## P2 · Herkunft in der Fundliste der Textansicht

Datei: `src/Obfuskation.Gui/ViewModels/TextViewModel.cs`.

- In `RebuildMatches` die Herkunft je Fund über die Referenz bestimmen:
  `_session.Extensions.TextRules.Any(r => ReferenceEquals(r, match.Rule))`
  ergibt „alle Projekte“, sonst „dieses Projekt“. Das ist verlässlich, weil
  `TextRuleEngine.FindMatches` die Regelreferenz in `TextMatch.Rule`
  weitergibt und `ExtensionLibrary.MergeTextRules` die Originalobjekte
  zusammenführt. Eine gleichnamige Projektregel verdrängt die globale, der
  Fund zeigt dann richtig „dieses Projekt“.
- `TextMatchViewModel` bekommt den Parameter `RuleScope scope` sowie die
  Eigenschaften `ScopeLabel` („dieses Projekt“ / „alle Projekte“) und
  `OriginLabel` = `$"{RuleName} · {ScopeLabel}"`. Bei Vorgaberegeln
  (`!IsUserRule`, siehe `EingebauteRegeln`) erklärt ein Tooltip: „Vorgaberegel,
  beim Anlegen des Projekts übernommen“.
- `src/Obfuskation.Gui/Views/TextView.axaml`, Fundzeile (Z. 179): eine
  zusätzliche Spalte vor den Knöpfen mit
  `<TextBlock Text="{Binding OriginLabel}" Classes="muted small">`. Die
  Spaltendefinition wird `Auto,*,Auto,*,Auto,Auto,Auto`, und die Knopfspalten
  rücken entsprechend.

Tests in `TextViewModelTests`: Ein Fund aus einer Profilregel trägt „dieses
Projekt“, einer aus einer Erweiterungsregel „alle Projekte“. Gibt es beide
Regeln unter gleichem Namen, gewinnt „dieses Projekt“.

## P3 · Generator-Dialog: anlegen und bearbeiten

### P3a · Vorschau in der Bibliothek

Neue Datei `src/Obfuskation.Core/Generation/GeneratorPreview.cs`:

```csharp
public static class GeneratorPreview
{
    // Baut ein Wegwerf-Profil mit genau diesem Generator, dazu
    // GeneratorRegistry.Build(profil, deriver, ExtensionLibrary.Empty), und
    // liefert generator.Generate(deriver.Derive(key, sample, 0), sample).
    // Faengt ConfigurationException und GenerationException und gibt deren
    // Meldung als error zurueck.
    public static bool TryExample(string key, GeneratorSettings settings, string sample,
                                  SeedDeriver deriver, out string example, out string? error);
}
```

Der Aufrufer hält einen `SeedDeriver(SeedDeriver.CreateSalt())` je Dialog,
damit das Beispiel beim Tippen nicht springt. Ein Core-Test deckt zwei Fälle
ab: ein Token mit Präfix `FW~` liefert einen Wert, der mit `FW~` beginnt; eine
Werteliste ohne Werte liefert `false` und eine Fehlermeldung.

### P3b · `GeneratorEditorViewModel` (neu, `src/Obfuskation.Gui/ViewModels/GeneratorEditorViewModel.cs`)

Ein Ansichtsmodell für **Anlegen und Bearbeiten**. Es schreibt selbst nichts:
Das Ergebnis liest der Aufrufer aus `Result*`.

Konstruktor:

```csharp
GeneratorEditorViewModel(
    Profile? profile,              // null: kein Projekt, nur "alle Projekte"
    ExtensionLibrary extensions,
    bool canEditGlobal, string? globalLockReason,
    RuleScope initialScope,
    string? existingName = null,   // gesetzt: Bearbeiten
    RuleScope? lockScopeTo = null, // z. B. Global fuer eine Regel fuer alle Projekte
    string? suggestedName = null,
    string? sampleValue = null,
    IReadOnlyList<string>? users = null) // wer den bestehenden Generator verwendet
```

Eigenschaften:

- **Allgemein:** `IsEditMode`, `WindowTitle` („Neuer Generator“ bzw.
  „Generator „x“ bearbeiten“), `ApplyLabel` („Anlegen“ bzw. „Übernehmen“).
- **Name:** `Name` und `NameError`. `NameError` ist gesetzt, wenn der Name
  - leer ist,
  - nicht zu `^[A-Za-z0-9ÄÖÜäöüß_-]+$` passt,
  - ein eingebauter Name aus `GeneratorRegistry.KnownNames` ist
    (Groß- und Kleinschreibung egal),
  - `scanText` lautet,
  - oder im Zielbereich schon vergeben ist, außer an sich selbst.

  `NameHint` meldet ohne Sperre, wenn der Name im anderen Bereich existiert:
  „In diesem Projekt gilt dann dieser Generator, nicht der für alle Projekte.“
  (siehe `ProfileValidator` Z. 124). Der Vorschlag kommt aus
  `ProfileScaffolder.ToGeneratorKey(suggestedName)`, eindeutig gemacht.
- **Art:** `BaseTypes` = `GeneratorOption.BuiltIn`, dazu `SelectedBaseType`
  (Vorgabe `token`) und `BaseTypeDescription`.
- **Sperren beim Bearbeiten:** `CanRename` und `CanChangeType` sind falsch,
  sobald `users` nicht leer ist. Hinweis: „Wird verwendet von …: Name und
  Art lassen sich erst ändern, wenn nichts mehr darauf verweist.“
  Hintergrund: Der Name ist zugleich der Namensraum der Ersetzungstabelle.
- **Optionen:** Namen und Sichtbarkeit wie in `FieldRuleViewModel` (Z.
  239–357), damit sich das XAML aus `GeneratorOptionsWindow.axaml` fast
  wörtlich übernehmen lässt: `ShowPrefix/Prefix`,
  `ShowPlaceholder/Placeholder`, `ShowDateRange/From/To`,
  `ShowGranularity/SelectedGranularity/GranularityOptions`,
  `ShowPatternMask/Pattern`, `ShowWordlist/ValuesText`,
  `ShowPartialMask/KeepFirst/KeepLast/MaskChar`. Die Sichtbarkeit richtet
  sich nach `ProfileValidator.OptionOwnership`. Gearbeitet wird auf einem
  Entwurf (`ProfileStore.DeepCopy` des bestehenden Eintrags bzw.
  `new GeneratorSettings { Type = "token" }`).
- **Ergebnis:** `BuildResult()` setzt `Type` auf die gewählte Art und
  übernimmt nur die Optionen, die dieser Art gehören (`OptionOwnership`). Die
  übrigen Felder (`MaxDays`, `Formats`, `Country`, `Domain`) bleiben aus dem
  Entwurf erhalten, damit von Hand gepflegtes JSON beim Bearbeiten nicht
  verloren geht. Vorgabewerte werden nicht als gesetzt geschrieben
  (`MaskChar "*"`, `Granularity "month"`, wie in `FieldRuleViewModel`).
- **Bereich:** `IsProjectScope` und `IsGlobalScope` als Radio-Paar, mit
  Settern nach dem Muster `UseProfile/UseExtension`.
  `CanChooseScope = !IsEditMode && lockScopeTo is null && profile is not null && canEditGlobal`.
  `ScopeHint` nennt den Grund, wenn die Wahl fehlt: „Kein Projekt geöffnet.“,
  `globalLockReason` oder „Eine Regel für alle Projekte braucht einen
  Generator für alle Projekte.“
- **Vorschau:** `SampleInput`, vorbelegt mit `sampleValue` oder sonst
  „Beispiel 4711“, dazu `PreviewText` und `PreviewError` über
  `GeneratorPreview.TryExample`, neu berechnet bei jeder Änderung. Darunter
  der Hinweis „Nur ein Beispiel – die echten Werte entstehen beim Lauf.“
- **Prüfung:** `Errors` entsteht aus `ProfileValidator.Validate`, angewandt
  auf ein Wegwerf-Profil, das nur diesen Generator enthält, mit
  `ExtensionLibrary.Empty`. Übernommen werden die Fehler (`Error`), deren
  `Path` mit `generators.{Name}` beginnt; so greift die vorhandene
  Präfixprüfung. `CanApply = NameError is null && Errors.Count == 0`.
- **Befehle und Ergebnis:** `ApplyCommand` setzt `Confirmed`, `ResultName`,
  `ResultSettings` und `ResultScope` und löst dann `CloseRequested` aus.
  `CancelCommand` löst nur `CloseRequested` aus.

### P3c · `GeneratorEditorWindow` (neu, `Views/GeneratorEditorWindow.axaml(.cs)`)

Aufbau wie `AlwaysReplaceWindow`: Breite 600, `SizeToContent="Height"`,
`MaxHeight` 760, Inhalt im ScrollViewer. Von oben nach unten:

1. Bezeichnung mit Fehler- und Hinweiszeile.
2. „Art der Ersetzung“: ComboBox mit Name und Beschreibung, wie im
   AlwaysReplace-Dialog, darunter die Beschreibung.
3. Die Optionsblöcke aus `GeneratorOptionsWindow.axaml`, Z. 23–98, ohne das
   Präfix `Field.`.
4. „Gilt für“: Radio-Paar samt `ScopeHint`; im Bearbeiten-Modus nur als
   Text.
5. Karte „Beispiel“: Eingabe und `→` Vorschau.
6. Fehlerliste.
7. Knöpfe „Abbrechen“ und `{ApplyLabel}` (Klasse `primary`).

In `DialogService` die neue Methode
`Task<bool> ShowGeneratorEditorAsync(GeneratorEditorViewModel vm, Window owner)`
(privat oder intern).

**Verschachtelte Dialoge:** `AlwaysReplaceViewModel` und `SettingsViewModel`
bekommen je die Eigenschaft
`Func<GeneratorEditorViewModel, Task<bool>>? ShowGeneratorEditor { get; set; }`.
`DialogService.ShowAlwaysReplaceAsync` und `ShowSettingsAsync` setzen sie vor
`ShowDialog`, mit dem jeweiligen Fenster als Besitzer. Tests setzen sie
direkt, etwa auf einen Handler, der Felder füllt und `ApplyCommand` ausführt.
`IDialogService` bleibt unverändert.

### P3d · Einbindung

**1. Dialog „Immer ersetzen“** (`AlwaysReplaceViewModel`, `AlwaysReplaceWindow.axaml` Z. 88–105)

- Neben der Generator-ComboBox steht ein flacher Knopf „Neuer Generator…“
  (`Grid ColumnDefinitions="*,Auto"`), gebunden an `NewGeneratorCommand`
  (`AsyncRelayCommand` aus `ViewModels/RelayCommand.cs`).
- Aufruf: `initialScope = UseExtension ? Global : Project`,
  `suggestedName = RuleNameBase`, `sampleValue = Trimmed`.
- Nach der Bestätigung landet der Generator in `_profile.Generators` bzw.
  `_extensions.Generators`.
- Im globalen Fall wird sofort `_extensions.Save(ExtensionPath)` gerufen, mit
  Rücknahme und `ErrorText`, wenn das scheitert (Muster aus `CreateRule` Z.
  515–534). Danach `_onApplied(resultScope == Project)`: Der Aufrufer
  markiert das Profil bzw. verwirft die Engine und baut die Generatorenlisten
  neu.
- Anschließend `Generators` neu befüllen (`GeneratorOption.For`) und den neuen
  Generator auswählen.
- `DialogService.ShowAlwaysReplaceAsync` setzt `ShowGeneratorEditor`.

**2. Reiter „Eigene Generatoren“** (`SettingsViewModel`, `SettingsWindow.axaml` Z. 45–102)

- Oben ein Einleitungssatz und der Knopf „+ Neuer Generator…“ mit
  `NewGeneratorCommand`, aktiv bei `HasProfile || CanEditGlobal`. Die
  Vorgabe für den Bereich ist „Projekt“, wenn ein Profil geladen ist.
- Das Ergebnis geht in `_profileCopy.Generators` bzw.
  `_extensionsCopy.Generators`. Danach `BuildProjectGenerators()`,
  `BuildGlobalGenerators()` und `OnProject/GlobalGeneratorsChanged()`.
  Gespeichert wird wie bisher erst bei „Übernehmen“.
- Die Zeilen werden nur noch gelesen: Name (fett), Art als
  `GeneratorDescriptions.For(type)`, dazu eine Kurzfassung der Optionen
  („Kennzeichnung FW~“, „Maske AA-9999“, „3 Werte“, „Zeitraum 2020-01-01 bis
  …“, sonst leer). Rechts die Knöpfe „Bearbeiten…“ und „Entfernen“; beim
  Entfernen bleiben Sperre und Tooltip wie bisher.
- Die bisherigen Eingabefelder für Name und Präfix in der Zeile entfallen.
  Name und Präfix ändert man jetzt im Dialog.
- `GeneratorEntryViewModel` bekommt dafür `EditCommand`, `DescriptionLabel`
  und `OptionsSummary`. Die Setter von `Name` und `Prefix` entfallen oder
  werden `internal`.
- **Bearbeiten** öffnet den Dialog mit `existingName`, `users = entry.Users`
  und dem Bereich des Eintrags. Nach der Bestätigung: Wurde der Name
  geändert (nur ohne Verwender möglich), wird der alte Schlüssel entfernt und
  der neue gesetzt. Danach die Einstellungen ersetzen, die Listen neu
  aufbauen und den Stand als geändert markieren.
- Leerzustand (Idee C4): „Noch keine eigenen Generatoren. „+ Neuer
  Generator…“ legt einen an – etwa ein Token mit Kennzeichnung FW~, damit
  Pseudonyme erkennbar bleiben.“

**3. Regelformular im Reiter „Textregeln“** (`TextRulesPanel.axaml` Z. 150–165, `TextRulesViewModel`)

- Neben „Ersetzen durch“ steht ebenfalls „Neuer Generator…“.
- `TextRulesViewModel` bekommt vom `SettingsViewModel` einen Rückruf
  `Func<RuleScope, string?, Task<string?>>? createGenerator`. Er öffnet den
  Dialog, mit `lockScopeTo = Global` bei einer globalen Regel. Er schreibt in
  die Kopien, baut die Listen neu (`RefreshGeneratorsList`) und liefert den
  neuen Namen.
- Anschließend `Selected.Generator` auf den neuen Eintrag setzen.

Tests:

- **`GeneratorEditorViewModelTests` (neu):**
  - Namensprüfung: leer, eingebaut, vergeben, erlaubt.
  - Sichtbarkeit der Optionen je Art.
  - Ein Wechsel von `token` mit Präfix zu `pattern` lässt das Präfix aus dem
    Ergebnis fallen.
  - Ohne Profil ist der Bereich fest auf „alle Projekte“.
  - Im Bearbeiten-Modus mit Verwendern ist `CanRename` falsch.
  - Die Vorschau liefert einen Wert.
- **`AlwaysReplaceViewModelTests`:**
  - Ein neuer Generator steht danach im Ziel und ist ausgewählt.
  - Im globalen Fall wird er in die Datei geschrieben.
  - Scheitert das Schreiben, wird er zurückgenommen.
- **`SettingsViewModelTests`:**
  - Anlegen im Projekt und global wirkt erst nach „Übernehmen“.
  - Bearbeiten ändert das Präfix.
  - Umbenennen ist gesperrt, solange eine Regel den Generator verwendet.

## P4 · Regeln für alle Projekte nehmen ihren Projekt-Generator mit

**Heutige Lücke:** Eine globale Regel (Textregel der Erweiterungsdatei) kann
einen Generator verwenden, den es nur im Projekt gibt. Das geht im Formular
und im AlwaysReplace-Dialog mit „Immer, in allen Projekten“. In diesem
Projekt läuft das, in jedem anderen fehlt der Generator.
`TextRulesViewModel.ChangeScope` kopiert ihn zwar schon mit, die anderen Wege
tun das nicht.

- Neu in `src/Obfuskation.Core/Configuration/ExtensionLibrary.cs`:
  `public IReadOnlyList<string> AdoptProjectGenerators(Profile profile)`.
  - Die Methode geht die Einträge von `TextRules` und `FieldRules` der
    Erweiterung durch. Deren Generator gilt als „nur im Projekt“, wenn er
    nicht eingebaut ist (`GeneratorRegistry.KnownNames`), nicht `scanText`
    heißt, nicht in `Generators` steht, aber in `profile.Generators`.
  - Ein solcher Generator wird per `ProfileStore.DeepCopy` nach `Generators`
    kopiert. Rückgabe: die kopierten Namen.
- Aufrufen an zwei Stellen:
  - in `SettingsViewModel.Apply` vor der Prüfung; kopierte Namen setzen
    `_globalDirty`,
  - in `AlwaysReplaceViewModel.CreateRule`, wenn `UseExtension` gilt, vor
    `Save`. Scheitert das Speichern, werden die kopierten Generatoren wieder
    entfernt.
- Unter der Generator-ComboBox im Regelformular steht bei einer globalen
  Regel mit Projekt-Generator der Hinweis: „Dieser Generator gilt bisher nur
  in diesem Projekt – beim Übernehmen wird er für alle Projekte mitkopiert.“
  Dazu dient `TextRuleViewModel.GeneratorScopeHint`. Der AlwaysReplace-Dialog
  bekommt denselben Hinweis.
- Core-Tests in `ExtensionLibraryTests`:
  - Kopiert werden nur Generatoren, die es nur im Projekt gibt.
  - Ein vorhandener globaler Generator wird nicht überschrieben.
  - Eingebaute Generatoren und `scanText` werden ignoriert.

## P5 · Farben auch im Eingabefeld (übernimmt Opus selbst)

**Ziel:** Links erscheinen dieselben Farben wie in der Prüffassung, auch
während des Bearbeitens. Die Umschaltung „Bearbeiten/Fertig“ bleibt.

- **Ansichtsmodell** (`TextViewModel.cs`):
  - Neu `public readonly record struct TextHighlight(int Start, int Length, TextSegmentKind Kind)`.
  - Neu die Eigenschaft `InputHighlights`: `IReadOnlyList<TextHighlight>`.
    `RecomputeResultText` füllt sie aus `_lastFound` und dem Häkchenstand
    (`Replaced` bzw. `Excluded`).
  - Der Getter liefert nur dann Einträge, wenn `_inputText == _lastRunInput`.
    Sonst zeigten die Positionen während der Entprellung auf verschobenen
    Text.
  - Der Setter von `InputText` und alle Wege über `SetResult` melden
    `InputHighlights` als geändert.
- **Neues Steuerelement** `src/Obfuskation.Gui/Views/HighlightTextBox.cs`
  (`HighlightTextBox : TextBox`):
  - `StyleKeyOverride => typeof(TextBox)`, damit die Fluent-Vorlage greift.
  - Stil-Eigenschaft `Highlights`.
  - In `OnApplyTemplate` `PART_TextPresenter` suchen. Darunter kommt eine
    Ebene `TextHighlightLayer : Control`, die für jede Hervorhebung
    `presenter.TextLayout.HitTestTextRange(start, length)` zeichnet (die API
    ist in Avalonia 12.1.2 vorhanden). Die Rechtecke werden um den Versatz
    des Presenters relativ zur Ebene verschoben.
  - **Platzierung:** Ist das Elternelement des Presenters ein `Panel`, sitzt
    die Ebene dort vor dem Presenter, also darunter. Sie scrollt dann mit und
    kann die Farben der Prüffassung verwenden (`#8C3EA25B` / `#8CD97706`).
    Sonst dient `AdornerLayer` als Ausweg, mit schwächerer Deckkraft (etwa
    `#553EA25B`), weil die Ebene dann über dem Text liegt. Welcher Fall
    vorliegt, zeigt vorher ein Blick in den Visual Tree zur Laufzeit.
  - Neu zeichnen bei: `Highlights`, `Text` und `LayoutUpdated` des
    Presenters (Umbruch, Größe). Positionen außerhalb der Textlänge werden
    übersprungen.
- **Ansicht:** In `TextView.axaml` Z. 88 wird `TextBox` zu
  `views:HighlightTextBox` mit `Highlights="{Binding InputHighlights}"`.
  `FindControl<TextBox>("InputBox")` im Codebehind funktioniert dank
  Vererbung weiter.
- **Pinsel:** Die beiden Farbwerte liegen heute in `TextView.axaml.cs` (Z.
  40–42). Sie wandern an eine gemeinsame Stelle, etwa statische Felder in
  `HighlightTextBox`, auf die auch `TextView` zugreift.
- **Tests** (`TextViewModelTests`):
  - `InputHighlights` entsprechen den Fundpositionen.
  - Ein abgewählter Fund hat die Art `Excluded`.
  - Nach einer Textänderung ist die Liste bis zum nächsten Lauf leer.
- **Sichtprüfung auf Xvfb** (siehe `docs/bilder/aufnehmen.sh`):
  - kurzer Text mit drei Funden,
  - langer umbrechender Text, dazu gescrollt,
  - dunkles Thema,
  - Fenster verkleinert.

  Die Rechtecke müssen deckungsgleich auf den Fundstellen liegen.

## P6 · Version, Doku grob, README „Experimentell“

- **Version:** `Directory.Build.props` auf `<Version>1.10.0</Version>`, dazu
  ein Absatz 1.10.0 im Änderungskommentar darüber (Stil der vorigen Absätze).
- **Anwenderdokumentation** (`docs/anwenderdokumentation.md`): Front-Matter
  `version: 1.10.0`. Grob nachziehen:
  - Abschnitte „Die Textansicht“ (Z. 58), „Farbe zeigt …“ (Z. 88) und
    „Übersehenes markieren“ (Z. 105): Farben auch beim Bearbeiten, Herkunft
    in der Fundliste, „Neuer Generator…“.
  - Abschnitt „Einstellungen …“ (Z. 462) → „Regeln & Generatoren …“: Einstieg
    über Startseite (nur alle Projekte), Kopfzeile, Strg+, und „Regeln
    bearbeiten…“ in beiden Ansichten; Reiter „Eigene Generatoren“ mit
    „+ Neuer Generator…“ und „Bearbeiten…“; Mitkopieren von
    Projekt-Generatoren bei Regeln für alle Projekte.
  - Alle übrigen Stellen mit „Einstellungen“ oder „Immer ersetzen…“
    (Dateiansicht) anpassen.
  - Unter der Fassungszeile der Satz: „Die Bilder zeigen teilweise noch den
    Stand 1.9.“
- **Entwicklerdokumentation** (`docs/entwicklerdokumentation.md`):
  `version: 1.10.0`. Kurz beschreiben:
  - `GeneratorEditorViewModel/Window`,
  - das Muster für verschachtelte Dialoge (`ShowGeneratorEditor`),
  - `GeneratorPreview`, `ExtensionLibrary.AdoptProjectGenerators`,
  - `HighlightTextBox` samt Ebene,
  - `ShowSettingsAsync(globalOnly)`.
- **`docs/Feature.md`:** Fassung 1.10.0. In 3.1 (Wege) Einstiege und Namen
  nachziehen. Neuer Abschnitt „Umgesetzt in 1.10.0“ mit den fünf Wünschen,
  jeweils einer Zeile. Idee C4 teilweise erledigt, Generatoren. Als neue Idee
  vormerken: „Prüffassung und Umschalter entfallen lassen, wenn die Farben im
  Eingabefeld sich bewähren“.
- **README.md** direkt unter der Umschaltzeile:

  ```markdown
  > [!WARNING]
  > **Experimental.** This project is a testbed for working with different AI
  > models. It is usable, but not free of bugs, and the documentation is
  > incomplete. This notice will be removed once the project is finished.
  ```

  Dazu ein Badge `![Status: experimental](https://img.shields.io/badge/status-experimental-orange)`.
  Absatz über die GUI (Startseite, Regeln aus Markierung): „Rules &
  Generators“ ergänzen, nur grob.
- **README.de.md:** gleichwertig auf Deutsch, mit dem Kern „**Experimentell.**
  Dieses Projekt ist ein Testfeld für die Arbeit mit verschiedenen
  KI-Modellen …“. Die drei Stellen mit „Immer ersetzen“ prüfen und anpassen.
- **Nicht in diesem Schritt:** Bilder neu aufnehmen, Testdokumentation
  fortschreiben, Doku glätten.

## P7 · Prüfung (Opus)

1. `dotnet build` ohne neue Warnungen, dann `dotnet test` für alle drei
   Testprojekte, alles grün.
2. Oberfläche auf Xvfb, gebaut mit `dotnet build`, gestartet mit
   `dotnet run --project src/Obfuskation.Gui`; Bildschirmfotos mit `import`,
   Muster aus `docs/bilder/aufnehmen.sh`:
   - Startseite mit „Regeln & Generatoren…“; öffnen ergibt das Fenster ohne
     Projektkarte, mit Hinweiszeile und Titel „– alle Projekte“.
   - Textansicht: Text tippen, Farben links im Eingabefeld, Fundliste mit
     „fw · dieses Projekt“ bzw. „… · alle Projekte“.
   - Markieren, Strg+M, „Neuer Generator…“, Token mit Präfix `FW~`
     anlegen: Er ist ausgewählt, die Regel übernehmen, die Fundliste zeigt
     das Präfix im Ersatz.
   - Dateiansicht: „Regeln bearbeiten…“ öffnet „Regeln & Generatoren“ im
     Reiter „Textregeln“.
   - Reiter „Eigene Generatoren“: anlegen, bearbeiten, entfernen.
   - Kopfzeile bei 760 px ohne Abschneiden, in hellem und dunklem Thema.
3. Review der Kernstücke: P3b (Ergebnisbildung, Namensregeln), P3d
   (Schreiben und Rücknahme im AlwaysReplace-Dialog), P4
   (`AdoptProjectGenerators` samt Aufrufstellen).

## P8 · Veröffentlichen (Opus, nach erneuter Rückfrage)

1. PDFs neu erzeugen (Skill `md2pdf`): `docs/anwenderdokumentation.pdf` und
   `docs/entwicklerdokumentation.pdf`.
2. Commit auf `main` (englische Nachricht, Co-Authored-By-Zeile). Den
   Arbeitsordner `features+bugs/` ignoriert `.gitignore` bereits.
3. **Rückfrage an Gregor.** Danach `./upd-github.sh` mit einer Datei für die
   Release-Hinweise aus dem Scratchpad (`--notes-file`): kurze
   Änderungsliste plus Hinweis „Experimental“. Das Skript testet, baut für
   Linux und Windows, pusht `main`, setzt das Tag `v1.10.0` und legt das
   Release an.
4. `gh repo edit gstuebner/obfuskation --description "Experimental: Reversibly
   pseudonymize sensitive data in CSV, JSON, and text files. CLI and GUI for
   Windows and Linux." --add-topic experimental`
5. Memory-Eintrag (Projekt): Die Doku wird bis zum Abschluss nur grob
   nachgezogen; der Status „Experimentell“ fällt erst, wenn keine
   Entwicklungsvorschläge mehr offen sind.

---

## Delegation (Token-Bewusstsein)

| Paket | Wer | Warum |
|---|---|---|
| P0–P4, P6 | **Ein** Sonnet-Subagent, nacheinander, bekommt nur den Pfad `docs/plan-regeln-generatoren.md` | klar spezifizierte Routinearbeit: Umbenennungen, Ansichtsmodell nach bestehendem Muster, Tests, grobe Doku |
| P5 Farben im Eingabefeld | Opus selbst, nach dem Subagenten | kein Routinemuster: Vorlagenstruktur und Messen der Deckungsgleichheit |
| P7 Review und Sichtprüfung, P8 Veröffentlichung | Opus | Kernstück-Review; außenwirksame Schritte mit Rückfrage |

Auftrag an den Subagenten: „Setze P0 bis P4 und P6 aus
`docs/plan-regeln-generatoren.md` um. P5, P7 und P8 nicht anfassen. Nach
jedem Paket `dotnet build` und `dotnet test`. Nichts committen. Am Ende
berichten: geänderte Dateien, neue Tests, offene Punkte und Abweichungen vom
Plan mit Begründung.“

## Verifikation (Zusammenfassung)

- Automatisch: `dotnet test`, alle drei Testprojekte grün, mit den neuen
  Tests aus P1–P5.
- Sichtprüfung auf Xvfb nach P7.2, Bilder im Scratchpad (nicht in
  `docs/bilder/`).
- Nach der Veröffentlichung: Release-Seite `v1.10.0` mit zwei Archiven und
  `SHA256SUMS.txt`, das README zeigt den Hinweis „Experimental“, die
  Repo-Beschreibung beginnt mit „Experimental:“.
