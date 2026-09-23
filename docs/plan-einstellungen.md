# Plan: Einstellungen bearbeitbar und schnell erreichbar (Obfuskation 1.8.0)

## Context

Heute kommt man an Regeln nur über Umwege. Die Textregeln erreicht man über den
Fund in der Textansicht → „immer…“ → „Muster von Hand bearbeiten…“.
„Mehr → Hauseigene Muster…“ zeigt nur Pfad und Namen
(`ExtensionsViewModel`/`ExtensionsWindow`), und „Mehr → Ersetzungstabelle…“
zeigt nur Anzahlen (`MappingViewModel`). Gregor möchte eine bedienfreundliche
Oberfläche. Es soll **globale Einstellungen** geben (Erweiterungsdatei
`obfuskation.json`), die man bearbeiten kann, sofern die Datei beschreibbar ist.
Dazu kommen **Projekteinstellungen** (das geladene Profil), die immer bearbeitbar
sind. Beides soll mit einem Klick erreichbar sein.

Entscheidungen von Gregor:
- Ersetzungstabelle: Werte bleiben verdeckt, bis man sie ausdrücklich
  einblendet. Danach kann man suchen und einzelne Einträge oder ganze
  Namensräume löschen. Pseudonyme lassen sich **nicht** ändern.
- Umsetzung: Dieser Plan wird als `docs/plan-einstellungen.md` ins Projekt
  kopiert. Ein Sonnet-Subagent setzt ihn um, danach reviewt Opus das Kernstück.

Beim Erkunden gefundene Fehler, die mitbehoben werden:
1. **Datenverlust**: Ist `obfuskation.json` kaputt, arbeitet `MainViewModel`
   mit `ExtensionLibrary.Empty` weiter (MainViewModel.cs:71-83). „Immer ersetzen…“
   mit „in allen Projekten“ überschreibt dann die kaputte Datei mit nur der
   neuen Regel.
2. **Verdeckte Schreibziele**: `ExtensionLibrary.ResolveWritePath` schreibt nach
   `~/.config/obfuskation/obfuskation.json`, wenn die geltende Datei im
   Programmverzeichnis schreibgeschützt ist. `ResolvePath` liest aber das
   Programmverzeichnis zuerst. Die Änderung ist also nach dem Neustart
   wirkungslos.
3. Nach dem Schließen von `TextRulesWindow` wird die Vorschau der Textansicht
   nicht neu berechnet (`OnTextRulesChanged` ruft kein `Text?.RefreshPreview()`).
4. In der Textansicht fehlt der Knopf „Speichern“. Profilregeln, die dort
   entstehen, werden nur über die Rückfrage beim Beenden gesichert.

## Konventionen (für den Umsetzer)

- Kommentare und Oberflächentexte auf Deutsch, im Stil des Bestands. Der Bestand
  schreibt Umlaute in Kommentaren oft als ae/oe/ue, in Oberflächentexten echt.
  Bezeichner auf Englisch.
- Keine neuen NuGet-Pakete, also kein `Avalonia.Controls.DataGrid`. Listen mit
  `ListBox` und `Grid`-Template bauen.
- Ansichtsmodelle bleiben fensterfrei. Fenster entstehen nur in
  `Services/DialogService.cs` oder `Views/MainWindow.axaml.cs`, angestoßen über
  Events oder `IDialogService`. Jede neue `IDialogService`-Methode braucht eine
  Entsprechung in `tests/Obfuskation.Gui.Tests/FakeDialogService.cs`.
- `net8.0` bleibt. Tests laufen lokal nur mit Roll-Forward
  (fish: `env DOTNET_ROLL_FORWARD=Major dotnet test`).
- Nicht committen.

## Teil A – Core

### A1 `ExtensionLibrary` (src/Obfuskation.Core/Configuration/ExtensionLibrary.cs)
- Neu: `public static ExtensionWriteState GetWriteState(string? programDirectory = null)`
  gibt `record ExtensionWriteState(string Path, bool CanWrite, string? Reason)` zurück:
  - Keine Datei gefunden: Ziel ist das Konfigurationsverzeichnis, `CanWrite = true`.
  - Eine Datei gefunden und beschreibbar (`IsWritable`): dieser Pfad, `CanWrite = true`.
  - Eine Datei gefunden, aber nicht beschreibbar: dieser Pfad, `CanWrite = false`,
    Reason z. B. „Die geltende Datei liegt im Programmverzeichnis und ist
    schreibgeschützt. Änderungen nimmt dort der Administrator vor.“
- `ResolveWritePath` fällt nicht mehr stillschweigend auf das
  Konfigurationsverzeichnis zurück. Entweder wird die Methode entfernt und alle
  Aufrufer (grep: `ResolveWritePath` in src/ und tests/, auch CLI) nutzen
  `GetWriteState`, oder sie wirft bei `CanWrite == false`
  `InvalidOperationException`. Die erste Variante ist vorzuziehen. Den
  Doc-Kommentar anpassen, `ExtensionLibraryTests` anpassen und erweitern.
- Neu: `ExtensionLibrary Clone()` als JSON-Rundreise mit `ProfileStore.JsonOptions`
  sowie `void ReplaceWith(ExtensionLibrary other)`. Letzteres kopiert
  Generators/TextRules/FieldRules **in dieselbe Instanz**. Das ist wichtig,
  weil `ProfileSession`, `TextViewModel` und `MainViewModel` dasselbe Objekt
  teilen.

### A2 `MappingStore` (src/Obfuskation.Core/Mapping/MappingStore.cs)
- `public bool Remove(string namespaceName, string plaintext)`: entfernt den
  Eintrag aus `_document.Namespaces` und `_reverse`. Ein leerer Namensraum
  entfällt ganz. Setzt `_dirty`.
- `public int RemoveNamespace(string namespaceName)`: gibt die Anzahl der
  entfernten Einträge zurück und setzt `_dirty`.
- Tests in `tests/Obfuskation.Core.Tests/MappingStoreTests.cs`: nach Entfernen und
  `Save` neu öffnen, der Eintrag fehlt, `TryGetPlaintext` findet das Pseudonym
  nicht mehr, die Dateirechte bleiben 0600.

### A3 Profil-Klon
- `ProfileStore` bekommt, falls noch nicht vorhanden, einen Klon per JSON-Rundreise.
  Ein Hilfsmittel für TextRule-, GeneratorSettings- und FieldNameRule-Listen
  reicht auch, z. B. `static T DeepCopy<T>(T value)` in `ProfileStore`.

## Teil B – Neues Fenster „Einstellungen“

Das Fenster ersetzt `ExtensionsWindow` und den Einsprung in `TextRulesWindow`.
Es hat zwei Reiter (`TabControl`):

1. **„Dieses Projekt – ‹Profilname›“**: Nur aktiv, wenn ein Profil geladen ist,
   sonst ein Hinweis „Kein Profil geladen“. Inhalt:
   - **Textregeln**: bearbeitbar. Globale Regeln stehen grau darunter mit dem
     Zusatz „gilt für alle Projekte“ und dem Knopf „Dort bearbeiten“, der auf
     Reiter 2 wechselt und die Regel auswählt.
   - **Eigene Generatoren** (Profil `Generators`): Liste mit Name, Typ und
     Präfix. Name und Präfix sind bearbeitbar, „Entfernen“ geht nur, wenn keine
     Regel oder kein Feld den Generator nutzt. Sonst ist der Knopf gesperrt,
     mit Tooltip, der den Nutzer nennt.
2. **„Alle Projekte (hauseigen)“**: Kopfzeile mit Pfad und Zustand, entweder
   „bearbeitbar“ oder der gelbe `Reason` aus `GetWriteState` bzw. die
   Ladefehlermeldung. Knöpfe „Ordner öffnen“ und „Im Editor öffnen“ über
   `Process.Start` mit `UseShellExecute = true`, im Bestand nach einem
   Vorbild suchen. Inhalt:
   - **Textregeln**, **Eigene Generatoren** wie oben.
   - **Spaltenmuster** (`FieldRules`, Typ `FieldNameRule`): Liste mit
     Muster, Generator (ComboBox), „Groß/klein egal“ und Kommentar, dazu
     Hinzufügen und Entfernen.
   - Ist `CanWrite == false` oder die Datei kaputt: alles schreibgeschützt
     (`IsEnabled=false`), die Ursache steht oben.
   - Hat die Datei Kommentare (`ExtensionLibrary.HasComments`), erscheint vor
     dem Speichern der Hinweis, dass eine `.bak`-Kopie entsteht. So macht es
     `AlwaysReplaceWindow` heute auch.

Aktionen je Textregel in beiden Reitern: „In alle Projekte verschieben“ bzw.
„Nur in dieses Projekt verschieben“. Beim Verschieben wird ein eigener
Generator, den die Regel nutzt und den es im Ziel nicht gibt, mitkopiert, nicht
verschoben. Den Namen hält `MakeUniqueRuleName`-ähnliche Logik eindeutig; dafür
die Logik aus `AlwaysReplaceViewModel` in eine gemeinsame statische Hilfe
auslagern.

**Semantik OK/Abbrechen:** Das Fenster arbeitet auf Kopien (A1 `Clone`, A3).
- „Abbrechen“ verwirft alles.
- „Übernehmen“ prüft zuerst mit
  `ProfileValidator.Validate(profileCopy, extensionsCopy)`. Fehler mit
  `ValidationSeverity.Error` erscheinen als Liste im Fenster und blockieren.
  Bei Erfolg:
  - Global geändert: `extensionsCopy.Save(state.Path)`, danach
    `_extensions.ReplaceWith(extensionsCopy)`.
  - Projekt geändert: die Listen ins Profil zurückschreiben,
    `_session.MarkChanged()`. Das Sternchen im Titel folgt der bisherigen Logik.
  - Immer: `_session?.InvalidateEngine()`, `RefreshGenerators()`,
    `RefreshIssues()`, `Text?.RefreshPreview()`, `OnPropertyChanged` für
    `ProfileTitle` und `HasUnsavedChanges`, dazu eine Statuszeile.
- Schließen mit ungespeicherten Änderungen im Fenster fragt nach, mit
  `ConfirmWindow`: „Übernehmen / Verwerfen / Weiter bearbeiten“.

**Umsetzung der Dateien:**
- `ViewModels/SettingsViewModel.cs` (neu): hält die Kopien, `SelectedTab`,
  `ProjectRules`, `GlobalRules`, Generatoren, Spaltenmuster, `ApplyCommand`,
  `CancelCommand` und die Events `CloseRequested` und `OpenFolderRequested`.
  Konstruktor-Parameter: `ProfileSession? session`, `ExtensionLibrary extensions`,
  `ExtensionWriteState writeState`, `string? extensionLoadError`,
  `SettingsTab initialTab`, `string? selectRuleName`.
- `TextRulesViewModel` verallgemeinern statt duplizieren. Er bekommt die
  editierbare Liste (`List<TextRule>`), die nur lesend angezeigten Regeln des
  anderen Bereichs, eine Funktion für die Probe-Vereinigung, die
  Generatorauswahl und `bool isReadOnly`. Die Erprobung bleibt und nutzt weiter
  `ExtensionLibrary.MergeTextRules` auf den **Kopien**. `NextName` und
  eindeutige Namen gelten über beide Bereiche.
- `Views/TextRulesPanel.axaml` (neu, UserControl): Inhalt aus
  `TextRulesWindow.axaml` (Liste, Formular, Erprobung). `TextRulesWindow`
  entfällt, ebenso `ExtensionsWindow` und `ExtensionsViewModel`.
- `Views/SettingsWindow.axaml(.cs)` (neu): TabControl mit `TextRulesPanel`,
  Generator- und Spaltenmusterlisten, Fußzeile „Abbrechen / Übernehmen“.
  Größe etwa 980×680. Stil wie die übrigen Fenster (`Classes="card"`,
  `muted`, `mono`).
- `IDialogService.ShowSettingsAsync(SettingsViewModel)` gibt `bool` zurück
  (übernommen), mit Entsprechung in `DialogService` und `FakeDialogService`.
- `MainViewModel`:
  - `ShowSettingsCommand` (Parameter optional `SettingsTab`) und
    `ShowSettingsAsync(SettingsTab tab, string? ruleName = null)`.
  - `_extensionLoadError` merken (Konstruktor, Zeilen 71-83). Solange er gesetzt
    ist, darf **kein** Pfad in die Erweiterungsdatei schreiben (Fehler 1). Das
    gilt für `AlwaysReplaceViewModel` (Option „in allen Projekten“ gesperrt,
    Hinweistext) und für `RemoveTextRuleAsync`.
  - `ShowExtensionsCommand`, `ExtensionsRequested`, `CreateExtensionsViewModel`
    und `CreateTextRulesViewModel` entfernen. Die Tests in
    `MainViewModelTests.cs` entsprechend umstellen.

## Teil C – Schnelle Einstiege

1. **Kopfzeile** (`MainWindow.axaml`): Knopf „⚙ Einstellungen“ zwischen
   „Profile…“ und „Mehr ▾“, immer sichtbar. Tastenkürzel **Strg+,** als
   `KeyBinding` am Fenster. Im Menü „Mehr“ ersetzt „Einstellungen…“ den Eintrag
   „Hauseigene Muster…“, den veralteten Kommentar dort anpassen.
2. **Textansicht, Fundleiste** (`TextView.axaml`, `TextMatchViewModel` in
   `TextViewModel.cs`):
   - Bei eigenen Regeln (`IsUserRule`) steht neben „Regel entfernen“ ein Knopf
     „bearbeiten…“. Er öffnet die Einstellungen im richtigen Reiter (Profil oder
     global, je nachdem, wo die Regel liegt) mit ausgewählter Regel. Neuer
     Callback `onEditRuleRequested` analog zu `onRemoveRuleRequested`
     (verdrahtet in `MainViewModel.ShowText`).
   - Rechts in der Kopfzeile der Fundleiste, vor der Legende, ein flacher Knopf
     „Regeln bearbeiten…“, der auf den Projektreiter führt.
   - Den Hinweistext unter dem Eingabefeld (TextView.axaml:117-119) um
     „… oder Regeln unter ⚙ Einstellungen bearbeiten“ ergänzen.
3. **„Immer ersetzen“** (`AlwaysReplaceViewModel.EditManually`): statt
   `TextRulesViewModel` zu erzeugen, das Event `EditRulesRequested(SettingsTab)`
   auslösen. Der Reiter folgt `UseExtension`. `DialogService.ShowAlwaysReplaceAsync`
   schließt den Dialog, danach öffnet `MainViewModel` die Einstellungen. Der
   Konstruktorparameter `createTextRulesViewModel` entfällt,
   `AlwaysReplaceViewModelTests` anpassen. Die Beschriftung lautet dann „In den
   Einstellungen bearbeiten…“.
4. **Speichern in der Textansicht** (Fehler 4): Den Knopf „Speichern“ in der
   Kopfzeile auch in der Textansicht zeigen, sobald `HasUnsavedChanges` gilt:
   `IsVisible` = `IsFilesView || (IsTextView && HasUnsavedChanges)`, als neue
   Eigenschaft `ShowSaveButton` im MainViewModel.

## Teil D – Ersetzungstabelle bearbeitbar

`MappingViewModel` und `MappingWindow.axaml` umbauen:
- Oben wie bisher: Pfad, Rechte, Gesamtzahl, Namensräume mit Anzahl. An jedem
  Namensraum der Knopf „Leeren…“.
- Knopf **„Werte anzeigen“** (Umschalter). Davor steht der Warnhinweis
  „Die Tabelle enthält alle Echtdaten. Nur einblenden, wenn niemand mitliest.“
  Beim Öffnen ist er immer aus.
- Eingeblendet: Suchfeld, das über Klartext und Pseudonym filtert, ohne
  Groß-/Kleinschreibung zu beachten, plus die Namensraum-Auswahl (ComboBox,
  „alle“). Dazu eine Liste in `ListBox` mit virtualisiertem Panel und den
  Spalten Namensraum | Klartext | → | Pseudonym, Mehrfachauswahl möglich.
  Knopf „Ausgewählte löschen…“.
- Löschen: Rückfrage über `IDialogService.AskRemoveMappingEntriesAsync(int count, string? namespaceName)`
  mit dem Text: „Bereits erzeugte Pseudodateien, die diese Pseudonyme
  enthalten, lassen sich an diesen Stellen nicht mehr zurückübersetzen. Ein
  neuer Lauf vergibt für den Klartext voraussichtlich wieder dasselbe Pseudonym.“
  Die Aussage zu „voraussichtlich dasselbe“ vorher an `Pseudonymizer` und
  `SeedDeriver` prüfen und bei Bedarf anpassen. Danach die Tabelle mit
  `readOnly: false` öffnen (Sperre), `Remove`/`RemoveNamespace`, `Save`,
  `Dispose` aufrufen. `MappingLockedException` wird mit „Die Tabelle ist gerade
  durch einen Lauf gesperrt“ gemeldet. Anschließend neu laden und den Callback
  `onChanged` rufen: `MainViewModel` → `RefreshMappingSummary()`,
  `Text?.RefreshPreview()`.
- `MappingViewModel` bekommt `IDialogService` bzw. einen Bestätigungs-Callback
  und `Action onChanged`. Das Ansichtsmodell bleibt fensterfrei.
- Neue Tests `tests/Obfuskation.Gui.Tests/MappingViewModelTests.cs`: Werte sind
  beim Start verborgen, Filter, Löschen mit Bestätigung und mit Abbruch.

## Teil E – Doku, Version, Hilfe

- `Directory.Build.props`: Version **1.8.0** mit Kommentar im bestehenden Stil
  (Nebenversion, weil neue Funktionen).
- `docs/anwenderdokumentation.md`: Kopfzeile `version: 1.8.0` und Datum. Neuer
  Abschnitt „Einstellungen“ (zwei Reiter, Schreibschutz, OK/Abbrechen,
  Verschieben zwischen Projekt und global). Die Abschnitte zu „Hauseigene
  Muster“ und „Ersetzungstabelle“ überarbeiten. Die Bilder
  `gui-ersetzungstabelle.png`, `gui-textregeln.png` und `gui-mehr-menue.png`
  als „neu aufzunehmen“ vormerken (`docs/bilder/aufnehmen.sh` nicht selbst
  ausführen). Die PDF bleibt, Gregor erzeugt sie mit md2pdf.
- `docs/entwicklerdokumentation.md`: neue Klassen und die geänderte
  Schreibregel der Erweiterungsdatei (Fehler 2).
- `Views/HelpWindow.axaml` (Kurzhilfe): Einstellungen und Strg+, erwähnen.

## Reihenfolge

A1 → A2 → A3 → B (ViewModel, dann Panel, dann Fenster, dann MainViewModel) →
C → D → E. Nach jedem Teil `dotnet build` ohne neue Warnungen.

## Verifikation

1. `dotnet build Obfuskation.slnx` ohne Warnungen.
2. `env DOTNET_ROLL_FORWARD=Major dotnet test` – alle Tests grün, einschließlich
   der neuen Tests zu `GetWriteState`, `Remove`/`RemoveNamespace`,
   `SettingsViewModel` (Übernehmen schreibt global und Profil, Abbrechen ändert
   nichts, bei kaputter oder schreibgeschützter Datei ist der globale Reiter
   gesperrt, Verschieben einer Regel samt Generator) und `MappingViewModel`.
3. GUI von Hand mit `dotnet run --project src/Obfuskation.Gui` (Roll-Forward):
   - Textansicht, Beispieltext aus `docs/beispiel/antwort-der-ki.txt` einfügen,
     „immer…“ → Regel anlegen, in der Fundleiste „bearbeiten…“ → die
     Einstellungen öffnen sich mit ausgewählter Regel. Das Muster ändern,
     übernehmen, die Vorschau aktualisiert sich sofort.
   - `chmod a-w ~/.config/obfuskation/obfuskation.json` → der globale Reiter
     ist gesperrt, „Immer ersetzen“ bietet „alle Projekte“ nicht an. Danach
     wieder `chmod u+w`.
   - Kaputte Datei (Kopie von `docs/beispiel/kaputt-profil.json` bzw. Datei mit
     ungültigem JSON) → der Reiter ist gesperrt, und die Datei bleibt nach
     „Immer ersetzen“ unverändert (Fehler 1).
   - Ersetzungstabelle: Werte verborgen → einblenden → suchen → einen Eintrag
     löschen → die Anzahl sinkt, die Datei hat weiter die Rechte 0600.
   - Strg+, öffnet die Einstellungen in jeder Ansicht.
4. Review durch Opus: SettingsViewModel (Kopie/Übernehmen), Schreibregeln der
   Erweiterungsdatei, MappingStore.Remove, Löschpfad der Tabelle.
