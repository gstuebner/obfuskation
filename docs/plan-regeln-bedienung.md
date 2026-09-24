# Plan: Regeln bedienbar für Anfänger und Profis (Obfuskation 1.9.0)

## Context

Gregor hat die Einstellungen aus 1.8.0 grob getestet. Im Reiter „Alle Projekte
(hauseigen)“ lässt sich eine Regel anlegen (sie erscheint als „regel1 · 50“),
aber nicht bearbeiten. Gefragt war außerdem, ob die Grundstruktur für globale
und projektbezogene Regeln taugt, wer globale Regeln ändern darf (nur wer
Schreibrecht auf die Datei hat) und wie das Werkzeug für Anfänger (kein RegEx)
und Profis gleichermaßen bedienbar wird.

**Gemessen** (veröffentlichte GUI auf Xvfb, 980×680, isolierte XDG-Verzeichnisse):

1. **Layout (Ursache des Symptoms):** `SettingsWindow.axaml`, globaler Reiter,
   stapelt Kopfzeile, `TextRulesPanel` (Regelliste mit fester Höhe von 240 px),
   Generatoren und Spaltenmuster untereinander. Für das Regelformular bleiben
   etwa 10 px: Die neue Regel ist in der Liste, das Formular ist abgeschnitten.
   Im Projektreiter fehlen dieselbe Ursache wegen die Erprobung und der
   Verschieben-Knopf. Selbst bei 990 px Höhe überlappt die Erprobung.
2. **Datei mit Kommentaren wird still ignoriert (schwerwiegend):**
   `ProfileStore.LooksLikeProfile` (ProfileStore.cs:124) parst mit
   `JsonDocument.Parse` ohne `CommentHandling`. Bei Kommentaren entsteht eine
   `JsonException`, die Methode liefert `true`, und `ExtensionLibrary.ResolvePath`
   überspringt die Datei als „Profil“. Die mitgelieferte
   `docs/beispiel/obfuskation-erweiterung-beispiel.json` (38 fieldRules, laut
   Doku zum Kopieren gedacht) wirkt deshalb nie. Die CLI meldet dazu:
   „dort liegt ein Profil, uebergangen“. Die GUI zeigt „bearbeitbar“ und
   überschreibt die Datei bei „Übernehmen“. Kaputtes JSON durchläuft denselben
   Weg: Die Sperre für kaputte Dateien aus 1.8.0 („Fehler 1“, `_extensionLoadError`)
   greift nie. Folge für den Datenschutz: Die hauseigenen Muster fehlen, und
   Echtdaten rutschen durch.
3. Beim Lesen gefunden:
   - Spaltenmuster stehen in einem `ItemsControl` ohne Auswahl. `SelectedFieldRule`
     wird nur von `AddFieldRule` gesetzt, bestehende Einträge lassen sich also
     nicht entfernen.
   - `GetWriteState` prüft nur die Datei. `Save` braucht aber auch Schreibrecht
     auf den Ordner (tmp + Move, `.bak`).
   - `AlwaysReplaceViewModel.CreateRule` ruft `_extensions.Save` ohne
     try/catch auf. Ein Schreibfehler bringt den modalen Dialog zum Absturz.
   - `ExtensionLibrary.Load` reicht IO-Fehler roh durch. `MainViewModel`
     (Zeile ~88) fängt nur `ConfigurationException`, eine unlesbare Datei
     beendet also den Start.

**Bewertung der Grundstruktur:** Sie trägt. Profil und Erweiterungsdatei nutzen
dieselben Typen, werden nach Namen zusammengeführt (`MergeTextRules`), und
`GetWriteState` sperrt ohne Schreibrecht. Liegt die Datei neben der
Programmdatei (Installation, Netzfreigabe), gilt sie für alle, und nur wer
Schreibrecht hat, ändert sie. Das ist Gregors Wunsch. Umgebaut wird die
Bedienung, nicht das Datenmodell.

**Entscheidungen von Gregor:**
- Fenster **nach Themen** statt nach Ablageort. Eine gemeinsame Regelliste,
  jede Regel mit „Gilt für: dieses Projekt / alle Projekte“. Liste links,
  Formular rechts.
- Neue Regeln entstehen **aus einem Beispielwert** (genau / alles dieser Form /
  eigener Ausdruck). Vorhandene, so erzeugte Regeln erscheinen wieder im Klartext.
- **„?“-Knopf mit Spickzettel**, KI-Hinweis samt Warnung vor Echtdaten und
  „Anfrage für die KI kopieren“, dazu ein kurzer Tooltip am Musterfeld.

## Konventionen (für den Umsetzer)

- Wie in `docs/plan-einstellungen.md`: Kommentare und Oberflächentexte auf
  Deutsch im Stil des Bestands, Bezeichner englisch. Keine neuen NuGet-Pakete.
  `net8.0` bleibt.
- Ansichtsmodelle bleiben fensterfrei (Fenster nur in `Services/DialogService.cs`).
  Die Zwischenablage darf im Code-behind einer View angesprochen werden, wie
  in `Views/TextView.axaml.cs:249` (`TopLevel.GetTopLevel(this)?.Clipboard`).
- RadioButton-Paare bekommen zwei Eigenschaften mit Settern, wie
  `AlwaysReplaceViewModel.UseProfile/UseExtension`.
- Tests: fish `env DOTNET_ROLL_FORWARD=Major dotnet test` (die .NET-8-Runtime
  fehlt lokal). Nicht committen.

## Teil A – Core-Fehler (zuerst, sicherheitsrelevant)

**A1 Klassifikation statt Ja/Nein** (`src/Obfuskation.Core/Configuration/ProfileStore.cs`)
- Neu: `public enum JsonFileKind { Profile, Other, Unreadable }` und
  `public static JsonFileKind Classify(string path)`. `JsonDocumentOptions` bekommt
  `CommentHandling = Skip` und `AllowTrailingCommas = true`, passend zu `JsonOptions`.
  `JsonException` ergibt `Unreadable`, IO- und Zugriffsfehler ergeben `Other`
  (wie bisher).
- `LooksLikeProfile(path) => Classify(path) is Profile or Unreadable`. Für
  `Discover` und `ProfileCatalog` ändert sich damit nichts, außer dass
  kommentierte Dateien jetzt richtig erkannt werden.
- `ExtensionLibrary.ResolvePath` überspringt nur noch bei `JsonFileKind.Profile`.
  Eine kaputte Datei wird also gewählt, und `Load` wirft wie gewünscht
  `ConfigurationException`: Die GUI sperrt, die CLI meldet einen Fehler.
- `ExtensionLibrary.Load`: `IOException`/`UnauthorizedAccessException` in
  `ConfigurationException($"Erweiterungsdatei nicht lesbar: {pfad} – …")` einpacken.
- Doc-Kommentare anpassen (der Catch-Kommentar „durchlassen“ in
  `LooksLikeProfile`, `ResolvePath`).
- Prüfen, dass die CLI (`CommandContext.cs:75`, `Program.cs:432/484`) eine
  `ConfigurationException` aus dem Laden der Erweiterung sauber mit dem
  Konfigurationsfehler-Exitcode meldet. `extensions path` soll bei kaputter
  Datei „ungültig: …“ zeigen statt „Profil, uebergangen“.
- Tests (`ExtensionLibraryTests`, `ProfileStoreTests`):
  - Erweiterung mit `//`- und `/* */`-Kommentaren im Konfigurationsordner wird
    gewählt und geladen (FieldRules-Anzahl stimmt).
  - Kaputtes JSON wird gewählt, `Load` wirft mit Pfad.
  - Ein Profil mit Kommentaren gilt als `Profile`.
  - Fremdes JSON gilt als `Other`.
  - Eine unlesbare Datei ergibt eine `ConfigurationException` (nur Unix, nicht als root).

**A2 Schreibrecht auf den Ordner** (`ExtensionLibrary.GetWriteState`)
- Ist die Datei beschreibbar, zusätzlich den Ordner prüfen: Probedatei
  `.obfuskation-schreibprobe-<guid>` mit `FileOptions.DeleteOnClose` anlegen.
  Scheitert das, `CanWrite = false` mit dem Grund „Die Datei ist beschreibbar,
  ihr Ordner aber nicht. Zum sicheren Speichern (Zwischendatei, Sicherungskopie)
  braucht das Programm Schreibrecht auf den Ordner.“
- Test: Ordner `chmod a-w`, Datei beschreibbar ergibt `CanWrite == false`
  (Muster des vorhandenen Schreibschutztests nutzen, unter Windows und als root
  überspringen).

**A3 Absturz in „Immer ersetzen“** (`ViewModels/AlwaysReplaceViewModel.cs`, `CreateRule`)
- `_extensions.Save` in try/catch (`IOException`, `UnauthorizedAccessException`).
  Bei Fehler Regel und einen eben angelegten eigenen Generator zurücknehmen,
  neue Eigenschaft `ErrorText` setzen (im Fenster als `warning small` unter den
  Knöpfen) und `false` liefern.

**A4 Muster wiedererkennen** (`Configuration/PatternFromSample.cs`)
- Neu: `public sealed record RecognizedPattern(bool IsShape, string Sample, string Description)`
  und `public static RecognizedPattern? TryRecognize(string pattern)`.
  - Führendes und abschließendes `\b` abtrennen. Den Rumpf an `\d{n}` zerlegen:
    Ziffernläufe werden zu n Ziffern aus „1234567890…“, die übrigen Stücke per
    `Regex.Unescape` entschlüsseln (bei Fehler `null`).
  - **Rundreise als Beweis:** Nur wenn `Shape(sample)?.Pattern == pattern`
    zutrifft, ist das Ergebnis eine Form. Nur wenn ohne `\d{`-Stücke
    `Literal(sample).Pattern == pattern` zutrifft, ist es ein wörtlicher Wert.
    Sonst `null`, der Ausdruck gilt dann als eigener.
- Tests (`PatternFromSampleTests`): Rundreise für „FW123456“, „2024-0815“,
  „+49 30 123456“, „Müller & Co.“ (Literal und Shape). `null` für
  `\b[A-Z]{2}\d{2}`, `.*`, `FW\d+` und den leeren String.

## Teil B – Einstellungsfenster nach Themen

**B1 Reiter neu** (`ViewModels/SettingsViewModel.cs`)
- `SettingsTab` bekommt die Werte `TextRules, Generators, FieldRules, Location`.
  Die alten Werte `Project` und `Global` entfallen.
- Neu: `public enum RuleScope { Project, Global }`.
- Alle Verwendungen umstellen (per grep `SettingsTab`, 11 Stellen, darunter
  `MainWindow.axaml:108` mit `CommandParameter`, `MainViewModel`,
  `AlwaysReplaceViewModel.EditRulesRequested` und Tests):
  - `MainViewModel.ShowSettingsAsync(SettingsTab tab = TextRules, string? ruleName = null, RuleScope? scope = null)`.
  - „bearbeiten…“ in der Textansicht übergibt den Ablageort als `scope`
    (heute `inErweiterung`, MainViewModel ~950). Bei gleichem Namen gewinnt `Project`.
  - `EditRulesRequested` wird zu einem parameterlosen `Action`.
  - `SettingsViewModel` bekommt zusätzlich das `ExtensionResolution` aus
    `ExtensionLibrary.ResolvePath()`, für die Seite „Ablageort“.

**B2 Eine gemeinsame Regelliste** (`ViewModels/TextRulesViewModel.cs`, umbauen)
- Der Konstruktor bekommt statt zweier Bereichsinstanzen:
  `Profile? profile` (Kopie), `ExtensionLibrary extensions` (Kopie),
  `bool canEditGlobal`, `string? globalLockReason` und `Action<RuleScope> onChanged`.
- `Rules` enthält zuerst die Projektregeln, dann die globalen. Eine globale
  Regel mit gleichem Namen wie eine Projektregel bekommt `IsOverridden`
  („in diesem Projekt durch gleichnamige Regel ersetzt“), gleiche Logik wie
  `MergeTextRules`.
- Filter-ComboBox „Zeigen: alle / dieses Projekt / alle Projekte“. Ohne Profil
  wird er ausgeblendet. Bei jedem Wechsel die Liste neu aufbauen und die
  Auswahl halten, wenn möglich.
- „+ Neue Regel“: Ziel ist das Projekt, wenn ein Profil geladen ist, sonst
  global (bei `canEditGlobal`), sonst ist der Knopf gesperrt (Tooltip mit Grund).
  Anlegen mit Name `TextRuleNaming.MakeUnique("regel", …beide Bereiche)`,
  Priorität 60 (wie „Immer ersetzen“), Generator `token` und leerem Muster.
  Die neue Regel wird ausgewählt.
- „Löschen“ geht nur bei bearbeitbaren Regeln.
- **Bereich wechseln** = `SettingsViewModel.MoveRule` hierher verlegen (Logik
  unverändert: eindeutiger Name, eigener Generator wird kopiert). Danach die
  Liste neu aufbauen, die Regel wieder auswählen und `onChanged` für beide
  Bereiche rufen.
- **„Für dieses Projekt anpassen“** bei gesperrter globaler Regel (nur mit
  Profil): `ProfileStore.DeepCopy(rule)` mit **gleichem** Namen in die
  Projektregeln. Dadurch ersetzt sie die globale Regel für dieses Projekt.
- Die Erprobung (`Evaluate`, `SampleText`, `Matches`) bleibt, mit
  `extensions.MergeTextRules(profile?.TextRules ?? [])`.
- `SelectByName(string name, RuleScope? scope)`.

**B3 `TextRuleViewModel` erweitern** (gleiche Datei)
- Bereich:
  - `Scope`, `ScopeLabel` („Dieses Projekt“/„Alle Projekte“).
  - `IsProjectScope`/`IsGlobalScope` als RadioButton-Paar. Der Setter ruft den
    Bereichswechsel beim Elternmodell auf.
  - `CanChangeScope`: Profil vorhanden, `canEditGlobal`, Regel bearbeitbar.
  - `ScopeLockReason` für den Tooltip.
  - `IsEditable`, `IsLocked` (global und nicht beschreibbar), `IsOverridden`.
- Erfassungsmodus: `enum PatternMode { Exact, Shape, Custom }` mit
  `IsExactMode`/`IsShapeMode`/`IsCustomMode`.
  - Weitere Eigenschaften: `Sample`, `CanUseShape`, `ExactDescription` und
    `ShapeDescription` (aus `PatternFromSample`).
  - Beim Aufbau: `TryRecognize(rule.Pattern)` ergibt Exact oder Shape samt
    Beispielwert. Ein leeres Muster ergibt Shape mit leerem Beispiel. Sonst
    gilt Custom.
  - `Sample`-Setter (nicht im Custom-Modus): Das Muster wird neu erzeugt,
    Shape nur bei `CanUseShape`, sonst Literal. Solange der Name automatisch
    ist (`_nameIsAuto`, true nur bei neuen Regeln und nur bis zur ersten
    Handänderung), wird er über `PatternFromSample.SuggestRuleName` neu
    gebildet und über einen Callback des Elternmodells eindeutig gemacht.
  - Wechsel zu Custom behält das Muster und macht das Feld bearbeitbar.
- Anzeige:
  - `PatternDescription` für Zeile 2 der Liste: erkannte Beschreibung, sonst
    das gekürzte Muster, sonst „noch kein Muster“. Dazu „→ Generator“.
  - `PatternError` live: leeres Muster ergibt einen `muted`-Hinweis, bei
    ungültigem Ausdruck die Meldung von `new Regex(...)`.
  - `PatternWarning`: Trifft das Muster den leeren Text (`Regex.IsMatch("", p)`),
    lautet sie „Trifft auch leeren Text – vermutlich zu weit gefasst.“
  - Die Setter bleiben bei `!IsEditable` wirkungslos (wie heute `IsReadOnly`).

**B4 `SettingsViewModel`**
- Statt `ProjectRules`/`GlobalRules` gibt es eine Instanz `TextRules`.
  `OnProjectChanged`/`OnGlobalChanged` bleiben als Ziel von `onChanged(scope)`.
- Neue Eigenschaften für die Seite „Ablageort“:
  - `GlobalReachText`: bei `ExtensionOrigin.ProgramDirectory` „Neben der
    Programmdatei – gilt für alle, die das Programm von dort starten. Ändern
    kann sie, wer dort Schreibrecht hat.“, sonst „Im persönlichen
    Konfigurationsordner – gilt für alle Ihre Projekte auf diesem Rechner.“
  - Liste der geprüften Fundorte aus `resolution.Candidates` mit Zustand
    (vorhanden, nicht vorhanden, als Profil übergangen, gilt).
- Sperrleiste oben, auf jeder Seite, nur bei `!CanEditGlobal`:
  „🔒 Regeln für alle Projekte sind nur lesbar: <Grund>“ mit dem Knopf
  „Details“, der zum Reiter „Ablageort“ führt.
- `.bak`-Hinweis in die Fußzeile verlegen, sichtbar nur bei `_globalDirty` und
  `HasGlobalComments`.
- Spaltenmuster:
  - Pro Zeile die Knöpfe „Entfernen“ sowie „↑“/„↓“ (Reihenfolge zählt, die
    erste passende Zeile gewinnt). Sie arbeiten auf `_extensionsCopy.FieldRules`
    und `FieldRules` synchron.
  - `SelectedFieldRule`, `RemoveFieldRuleCommand` und das untere „Entfernen“
    entfallen.
- Validierungsfehler: Prüfen, wie `ValidationIssue.Path` für Profil- und
  Erweiterungsregeln aussieht. Wo eindeutig, zu „Regel „fw“ (Dieses Projekt):
  <Meldung>“ übersetzen, sonst den Pfad belassen.

**B5 Views**
- `Views/SettingsWindow.axaml`:
  - Grid mit den Zeilen Sperrleiste (Auto), `TabControl` (*) und Fußzeile (Auto).
  - Reiter „Textregeln“, „Eigene Generatoren“, „Spalten-Vorschläge“, „Ablageort“.
  - Größe 980×680 und `MinHeight` 560 bleiben.
  - Keine festen Höhen und kein `MaxHeight` 120/140 mehr. Listen bekommen die
    ganze Höhe und scrollen selbst.
- `Views/TextRulesPanel.axaml`:
  - `ColumnDefinitions="300,12,*"`.
  - Links eine Karte mit Filter, `ListBox` (zweizeiliges Template: Name
    halbfett, Bereich und 🔒 rechts, darunter `muted small` die
    `PatternDescription`; überschriebene Regeln `muted`) sowie den Knöpfen
    „+ Neue Regel“ und „Löschen“.
  - Rechts ein `ScrollViewer` mit Formularkarte und Erprobungskarte untereinander.
  - Formular:
    1. Sperr- oder Ersetzt-Hinweis, bei Bedarf mit „Für dieses Projekt anpassen“.
    2. „Was wird gesucht?“ mit dem ?-Knopf (B6), Beispielwert-Feld
       (Platzhalter „z. B. FW123456“) und den Radios „Alles dieser Form“
       (mit Beschreibung, nur bei `CanUseShape`), „Genau dieser Wert“ und
       „Eigener Ausdruck (für Profis)“.
       - Im Custom-Modus das Musterfeld (`mono`, Tooltip, darunter
         `PatternError`/`PatternWarning`).
       - Sonst eine Zeile `muted small mono` „Ausdruck: …“.
    3. „Ersetzen durch“ (ComboBox wie bisher).
    4. „Gilt für“ (zwei Radios, Tooltip mit Sperrgrund).
    5. `Expander` „Erweitert“ mit Name, Priorität (samt Hinweis „höhere
       Priorität gewinnt bei Überlappung“) und „Groß-/Kleinschreibung egal“.
- Seite Generatoren: Einleitungssatz („Varianten der eingebauten Ersetzungen,
  etwa ein Token mit Kennzeichnung FW~ – entstehen meist über „Immer ersetzen“.“),
  darunter die Abschnitte „Dieses Projekt“ und „Alle Projekte“ (🔒) mit den
  bisherigen Zeilentemplates.
- Seite Spalten-Vorschläge:
  - Einleitungssatz: „Beim Anlegen eines Profils aus einer Datei schlägt das
    Programm je Spalte eine Ersetzung vor. Maßgeblich ist der Spaltenname, die
    erste passende Zeile gewinnt.“
  - Kopfzeile mit den Spalten Muster, Ersetzen durch, Groß/klein egal und
    Kommentar, dann die Zeilen mit ↑/↓/Entfernen. Unten „Hinzufügen“.
  - ?-Knopf im Spaltennamen-Kontext.
- Seite Ablageort: Pfad (`mono`), Reichweite, Zustand, Fundortliste,
  „Ordner öffnen“ und „Im Editor öffnen“ (bestehende Befehle) sowie ein
  Hinweis für Verwalter („Für alle Anwender: Datei neben die Programmdatei
  legen und nur Verwaltern Schreibrecht geben.“).

**B6 Hilfe zu Mustern** (neu: `Views/PatternHelpButton.axaml(.cs)`, UserControl)
- Ein Knopf „?“ (`flat`, Tooltip „Hilfe zu Mustern“) mit `Button.Flyout`,
  Vorbild `FilesView.axaml:48-86`. Inhalt `MaxWidth="480"`:
  - „Was ist ein Muster?“: Ein Muster (regulärer Ausdruck, kurz RegEx)
    beschreibt die Form eines Werts statt des Werts selbst: „FW, dann sechs
    Ziffern“ statt „FW123456“. Meist genügt „Alles dieser Form“, ein eigener
    Ausdruck ist nur für Sonderfälle nötig.
  - Spickzettel (Raster mit `mono` links):
    - `\d` eine Ziffer, `\d{6}` genau sechs, `\d{4,6}` vier bis sechs
    - `[A-Z]` ein Großbuchstabe, `[A-Za-z]+` ein oder mehr Buchstaben
    - `\b` Wortgrenze, `-?` Zeichen optional, `a|b` a oder b
    - `\.` ein echter Punkt (Sonderzeichen mit `\` schützen)
  - Beispiele, je nach Eigenschaft `IsFieldNameContext` (StyledProperty bool):
    - Text: `\bFW\d{6}\b` findet FW123456, `\bINV-\d{4}-\d{3}\b` findet
      INV-2024-001, `\b\d{5}\b` findet eine Postleitzahl.
    - Spaltenname: `.*iban.*` = enthält „iban“, `.*nr` = endet auf „nr“.
  - KI-Hinweis (hervorgehoben): „KI-Assistenten wie Claude oder ChatGPT
    schreiben solche Ausdrücke zuverlässig. Beschreiben Sie das Format in
    Worten und nennen Sie **ausgedachte** Beispiele – niemals echte Werte,
    denn genau die soll dieses Programm schützen. Den Vorschlag hier einfügen
    und in der Erprobung prüfen. Gebraucht wird die .NET-Schreibweise.“
  - Knopf „Anfrage für die KI kopieren“ (im Code-behind über
    `TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync`), danach kurz
    „Kopiert.“. Vorlage mit Platzhaltern: .NET-Regex, Format in Worten,
    passende und nicht passende Beispiele (ausgedacht), Wortgrenzen setzen,
    nur den Ausdruck ausgeben. Für den Spaltennamen-Kontext eine Variante mit
    „ganzer Spaltenname, Groß-/Kleinschreibung egal“.
- Kurzer Tooltip am Musterfeld (Textregel und Spaltenmuster): „Regulärer
  Ausdruck – Hilfe und Beispiele unter „?“. Eine KI kann ihn schreiben, bitte
  keine echten Werte hineingeben.“

**B7 Tests** (`tests/Obfuskation.Gui.Tests/SettingsViewModelTests.cs` umbauen, dazu neue Fälle)
- Die bisherigen elf Fälle auf die gemeinsame Liste umstellen. Aus „Verschieben“
  wird ein Bereichswechsel per `IsGlobalScope = true`.
- Neue Fälle:
  - Beispielwert „FW123456“ + Shape ergibt `\bFW\d{6}\b` mit dem Namen „fw“.
  - Umschalten auf Exact ergibt das Literal-Muster.
  - Eine vorhandene erzeugte Regel öffnet im Shape-Modus mit Beschreibung.
  - Ein freier Regex öffnet im Custom-Modus.
  - Ein manuell geänderter Name bleibt beim Tippen im Beispielfeld erhalten.
  - „Für dieses Projekt anpassen“ ergibt eine gleichnamige Projektregel, die
    globale Regel steht auf `IsOverridden`.
  - Ohne Profil: Filter aus, neue Regel global. Bei gesperrter Datei ist
    „+ Neue Regel“ gesperrt.
  - Spaltenmuster: vorhandene Zeile entfernen, ↑/↓ ändern die Reihenfolge in
    der Kopie.
- `MainViewModelTests`, `AlwaysReplaceViewModelTests` und `FakeDialogService`
  an die neue Signatur anpassen. Dazu ein Test für A3 (Speicherfehler: Regel
  zurückgenommen, `ErrorText` gesetzt), falls sich der Fehler ohne root
  erzeugen lässt (Zielpfad als Verzeichnis).

## Teil C – Version, Doku, Kurzhilfe

- `Directory.Build.props`: **1.9.0** mit einem Kommentar im bestehenden Stil.
  Neue Funktionen: Einstellungen nach Themen, Regel aus Beispielwert, Hilfe zu
  Mustern. Behoben: Kommentar-Datei ignoriert, Layout, Spaltenmuster nicht
  entfernbar, Ordnerrechte, Absturz beim Speichern, unlesbare Datei.
- `docs/anwenderdokumentation.md`:
  - Kopf `version: 1.9.0`, Datum und Fassungszeile unter der H1.
  - Abschnitt „Einstellungen“ neu schreiben (Themen, Gilt für, Beispielwert,
    Hilfe mit KI-Hinweis, Sperre, „Für dieses Projekt anpassen“).
  - Kapitel 11: Kommentare sind erlaubt und wirken jetzt. Eine kaputte Datei
    führt zu einem Fehler statt stillem Übergehen. Wer globale Regeln ändern
    darf, ergibt sich aus Ablageort und Schreibrecht.
  - Bilder `gui-textregeln.png` u. a. als „neu aufzunehmen“ vormerken.
- `docs/entwicklerdokumentation.md`: `JsonFileKind`/`Classify`,
  `GetWriteState` mit Ordnerprüfung, `TryRecognize` (Rundreise),
  `TextRulesViewModel` als gemeinsame Liste, neue `SettingsTab`-Werte.
  Fassungszeile nachziehen.
- `docs/testdokumentation.md`: Wenn dort Befunde geführt werden, die Punkte
  1–3 aus „Context“ als Befunde mit Behebung in 1.9.0 eintragen. Fassungszeile.
- `Views/HelpWindow.axaml` (Absatz „Alle Regeln an einem Ort“) und die
  README-Dateien per grep nach „Alle Projekte (hauseigen)“ / „Reiter“ nachziehen.
- PDFs erzeugt Gregor mit md2pdf. `docs/bilder/aufnehmen.sh` nicht ausführen.

## Umsetzung und Delegation

1. Nach Freigabe diesen Plan als `docs/plan-regeln-bedienung.md` ins Projekt
   kopieren.
2. **Sonnet-Subagent** setzt die Teile A → B → C in dieser Reihenfolge um. Er
   bekommt nur den Pfad zur Plandatei. Nach jedem Teil `dotnet build` ohne
   neue Warnungen und die Tests grün.
3. **Opus reviewt die Kernstücke:**
   - `Classify`/`ResolvePath`/`Load` (Datenschutz: nichts wird still übergangen)
   - `GetWriteState`
   - `TryRecognize` (Rundreise)
   - Bereichswechsel, „anpassen“ und `Apply` im Einstellungsmodell
   - Rücknahme in `CreateRule`
   Danach folgt die Sichtprüfung auf Xvfb (siehe unten).

## Verifikation

1. `dotnet build Obfuskation.slnx` ohne Warnungen und
   `env DOTNET_ROLL_FORWARD=Major dotnet test`, alle grün.
2. CLI mit isoliertem `XDG_CONFIG_HOME` im Scratchpad:
   - Die Beispieldatei mit Kommentaren als `obfuskation.json` in den
     Konfigurationsordner legen. `obfuskation extensions path` nennt sie als
     geltend, `extensions list` zeigt 38 Spaltenmuster.
   - Mit kaputtem JSON gibt es eine klare Fehlermeldung samt Pfad und
     Konfigurationsfehler-Exitcode.
3. GUI auf Xvfb, wie bei der Messung: `Xvfb :78`, `unset WAYLAND_DISPLAY`,
   XDG-Verzeichnisse im Scratchpad, Start über `dotnet run --project
   src/Obfuskation.Gui` mit Roll-Forward, dann `xdotool` und `import`.
   Screenshots bei 980×680:
   - Ohne Profil „+ Neue Regel“, „FW123456“ eintippen: Das Formular ist
     vollständig sichtbar, es erscheinen „Alles dieser Form: „FW“ + 6 Ziffern“
     und „Ausdruck: \bFW\d{6}\b“. Die Erprobung zeigt einen Treffer, wenn
     FW123456 im Probetext steht.
   - Mit `profil-demo.json` und Beispiel-Erweiterung: Die Liste zeigt
     Projektregeln und globale Regeln mit Bereich. Spalten-Vorschläge zeigt
     38 Zeilen. Die 3. Zeile entfernen, eine Zeile hochschieben, „Übernehmen“:
     Es entsteht eine `.bak`, und die Datei hat 37 Einträge in der neuen
     Reihenfolge.
   - `chmod a-w` auf die Datei: Die Sperrleiste erscheint, globale Regeln
     tragen 🔒, „Gilt für: alle Projekte“ ist gesperrt mit Tooltip, „Für dieses
     Projekt anpassen“ wirkt. Danach `chmod u+w`.
   - Nur der Ordner `chmod a-w`: gesperrt mit dem Ordnergrund.
   - Der ?-Knopf öffnet den Spickzettel, „Anfrage für die KI kopieren“ legt
     den Text in die Zwischenablage (unter Xvfb mit `xclip -o -selection clipboard`
     prüfen, falls installiert).
   - Strg+, sowie „bearbeiten…“ in der Textansicht öffnen „Textregeln“ mit
     der richtigen Regel.
