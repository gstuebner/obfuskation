using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die vier Themen des Einstellungsfensters (Plan Teil B) -- eine gemeinsame
/// Regelliste statt getrennter Reiter je Ablageort ("Dieses Projekt" /
/// "Alle Projekte"): welcher Bereich (<see cref="RuleScope"/>) eine Regel
/// betrifft, steht jetzt an ihr selbst statt am Reiter.
/// </summary>
public enum SettingsTab
{
    TextRules,
    Generators,
    FieldRules,
    Location,
}

/// <summary>Wo eine Regel (oder ein Generator) gilt.</summary>
public enum RuleScope
{
    /// <summary>Nur im geladenen Profil.</summary>
    Project,

    /// <summary>In der Erweiterungsdatei -- gilt fuer alle Projekte.</summary>
    Global,
}

/// <summary>
/// Das Einstellungsfenster: eine gemeinsame Textregel-Liste (Projekt- und
/// Erweiterungsregeln nebeneinander, mit "Gilt für"-Umschalter je Regel),
/// eigene Generatoren, Spaltenmuster und der Reiter "Ablageort".
///
/// <b>Kopien statt Original:</b> das Fenster arbeitet auf einer Kopie des
/// Profils (<see cref="ProfileStore.DeepCopy{T}"/>) und einer Kopie der
/// Erweiterung (<see cref="ExtensionLibrary.Clone"/>). "Abbrechen" verwirft
/// einfach die Kopien. "Übernehmen" prueft zuerst mit
/// <see cref="ProfileValidator.Validate"/> -- Fehler blockieren -- und
/// schreibt bei Erfolg:
/// - eine geaenderte Erweiterung in die Datei (<see cref="ExtensionLibrary.Save"/>)
///   und per <see cref="ExtensionLibrary.ReplaceWith"/> in dieselbe, von der
///   Oberflaeche geteilte Instanz zurueck (nicht in eine neue -- <see cref="Services.ProfileSession"/>,
///   <c>TextViewModel</c> und <c>MainViewModel</c> teilen sich dasselbe Objekt);
/// - ein geaendertes Profil per Listen-Ruecktransfer in <see cref="Services.ProfileSession.Profile"/>,
///   gefolgt von <see cref="Services.ProfileSession.MarkChanged"/>.
///
/// Fensterfrei wie jedes Ansichtsmodell dieses Projekts: das Fenster entsteht
/// nur in <see cref="Services.DialogService"/>, angestossen ueber
/// <see cref="CloseRequested"/>.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ProfileSession? _session;
    private readonly ExtensionLibrary _extensions;
    private readonly ExtensionLibrary _extensionsCopy;
    private readonly Profile? _profileCopy;
    private readonly ExtensionWriteState _writeState;
    private readonly ExtensionResolution _resolution;
    private readonly string? _extensionLoadError;

    private SettingsTab _selectedTab;
    private bool _projectDirty;
    private bool _globalDirty;
    private readonly HashSet<string> _baselineErrors;

    /// <summary>
    /// Die Regelnamen, die die Freitextfelder des Profils beim Oeffnen
    /// ausdruecklich auswaehlen (<see cref="FieldRule.TextRules"/>), je Feld in
    /// der Reihenfolge von <see cref="Profile.Fields"/> -- <c>null</c> fuer ein
    /// Feld ohne eigene Auswahl. Grundlage fuer <see cref="RewriteFieldRuleReferences"/>.
    /// </summary>
    private readonly List<List<string>?> _originalFieldReferences = new();

    /// <summary>
    /// Welches Regelobjekt ein Name beim Oeffnen meinte -- wie im echten Lauf
    /// (<see cref="ExtensionLibrary.MergeTextRules"/>) gewinnt die Projektregel
    /// vor einer gleichnamigen globalen.
    /// </summary>
    private readonly Dictionary<string, TextRule> _originalRuleByName = new(StringComparer.OrdinalIgnoreCase);

    public SettingsViewModel(
        ProfileSession? session,
        ExtensionLibrary extensions,
        ExtensionWriteState writeState,
        string? extensionLoadError,
        ExtensionResolution? resolution = null,
        SettingsTab initialTab = SettingsTab.TextRules,
        string? selectRuleName = null,
        RuleScope? selectRuleScope = null)
    {
        _session = session;
        _extensions = extensions;
        _writeState = writeState;
        _extensionLoadError = extensionLoadError;
        _resolution = resolution ?? ExtensionLibrary.ResolvePath();

        _extensionsCopy = extensions.Clone();
        _profileCopy = session is null ? null : ProfileStore.DeepCopy(session.Profile);

        // Was schon vor dem Oeffnen falsch war (etwa ein halb eingerichtetes
        // Feld der Dateiansicht), soll "Übernehmen" nicht blockieren -- das
        // hat dieses Fenster weder verursacht noch kann es dort behoben werden.
        _baselineErrors = ErrorKeys(ProfileValidator.Validate(_profileCopy ?? new Profile(), _extensionsCopy));

        foreach (var regel in _extensionsCopy.TextRules)
            _originalRuleByName[regel.Name] = regel;
        foreach (var regel in _profileCopy?.TextRules ?? new List<TextRule>())
            _originalRuleByName[regel.Name] = regel;
        foreach (var feld in _profileCopy?.Fields ?? new List<FieldRule>())
            _originalFieldReferences.Add(feld.TextRules is null ? null : new List<string>(feld.TextRules));

        _selectedTab = initialTab;

        ApplyCommand = new RelayCommand(Apply);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        OpenFolderCommand = new RelayCommand(
            () => OpenFolderRequested?.Invoke(Path.GetDirectoryName(_writeState.Path) ?? _writeState.Path));
        OpenEditorCommand = new RelayCommand(() => OpenEditorRequested?.Invoke(_writeState.Path));
        ShowLocationDetailsCommand = new RelayCommand(() => SelectedTab = SettingsTab.Location);
        NewGeneratorCommand = new AsyncRelayCommand(NewGeneratorAsync, () => HasProfile || CanEditGlobal);

        BuildProjectGenerators();
        BuildGlobalGenerators();
        BuildFieldRules();

        TextRules = new TextRulesViewModel(
            _profileCopy, _extensionsCopy, CanEditGlobal, GlobalLockReason, OnTextRuleChanged,
            onGeneratorCopied: OnGeneratorCopiedByScopeChange,
            createGenerator: CreateGeneratorForRuleAsync);

        if (selectRuleName is not null)
            TextRules.SelectByName(selectRuleName, selectRuleScope);
        else if (selectRuleScope is { } vorgabeBereich)
            TextRules.SelectedFilterOption = TextRules.FilterOptions.First(o => o.Scope == vorgabeBereich);

        BuildCandidates();
    }

    // ----------------------------------------------------------- Reiter

    public SettingsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value))
                return;

            OnPropertyChanged(nameof(SelectedTabIndex));
        }
    }

    /// <summary>Wie <see cref="SelectedTab"/>, als Zahl fuer <c>TabControl.SelectedIndex</c>.</summary>
    public int SelectedTabIndex
    {
        get => (int)_selectedTab;
        set => SelectedTab = (SettingsTab)value;
    }

    /// <summary>Ob ueberhaupt ein Profil geladen ist -- ohne Profil bleibt der Filter der Regelliste ausgeblendet.</summary>
    public bool HasProfile => _profileCopy is not null;

    public string ProjectTitle => _profileCopy is null ? "kein Profil geladen" : _session!.DisplayName;

    /// <summary>
    /// Fenstertitel (Plan Teil P1): "Regeln & Generatoren – {Projekt}", ohne
    /// Profil "Regeln & Generatoren – alle Projekte" -- der Reiter "Textregeln"
    /// bleibt dabei unerwaehnt, das Fenster heisst so unabhaengig vom
    /// gewaehlten Reiter.
    /// </summary>
    public string WindowTitle
        => "Regeln & Generatoren – " + (_profileCopy is null ? "alle Projekte" : _session!.DisplayName);

    // ----------------------------------------------------------- Sperrleiste

    /// <summary>Pfad der geltenden Erweiterungsdatei -- fuer die Statuszeile des Aufrufers nach "Übernehmen".</summary>
    public string GlobalPath => _writeState.Path;

    /// <summary>Ob der globale Bereich (Erweiterungsdatei) bearbeitet werden darf.</summary>
    public bool CanEditGlobal => _extensionLoadError is null && _writeState.CanWrite;

    /// <summary>Grund fuer die Sperre -- entweder der Ladefehler (Fehler 1) oder <see cref="ExtensionWriteState.Reason"/> (Fehler 2). <c>null</c> wenn bearbeitbar.</summary>
    public string? GlobalLockReason => _extensionLoadError ?? _writeState.Reason;

    /// <summary>Zustandstext fuer den Reiter "Ablageort": "bearbeitbar", oder die Ursache der Sperre.</summary>
    public string GlobalStateText => GlobalLockReason ?? "bearbeitbar";

    public bool HasGlobalWarning => !CanEditGlobal;

    /// <summary>Sperrleiste oben, auf jeder Seite -- nur sichtbar, wenn der globale Bereich gesperrt ist.</summary>
    public bool ShowLockBar => !CanEditGlobal;

    public string LockBarText => $"🔒 Regeln für alle Projekte sind nur lesbar: {GlobalLockReason}";

    /// <summary>Fuehrt von der Sperrleiste zum Reiter "Ablageort".</summary>
    public RelayCommand ShowLocationDetailsCommand { get; }

    public bool HasGlobalComments => File.Exists(_writeState.Path) && ExtensionLibrary.HasComments(_writeState.Path);

    /// <summary>
    /// Der .bak-Hinweis in der Fußzeile: nur sichtbar, wenn tatsächlich etwas
    /// Globales geändert wurde (sonst entstünde beim Schließen ohne
    /// "Übernehmen" gar keine Sicherungskopie) und die Datei Kommentare trägt.
    /// </summary>
    public bool ShowGlobalCommentsWarning => _globalDirty && HasGlobalComments;

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenEditorCommand { get; }

    /// <summary>Bittet den Aufrufer, den Ordner der Erweiterungsdatei zu oeffnen (Pfad im Argument).</summary>
    public event Action<string>? OpenFolderRequested;

    /// <summary>Bittet den Aufrufer, die Erweiterungsdatei im Editor zu oeffnen.</summary>
    public event Action<string>? OpenEditorRequested;

    // ------------------------------------------------------- Textregeln

    /// <summary>Die gemeinsame Regelliste -- Projekt- und Erweiterungsregeln nebeneinander (Plan Teil B2).</summary>
    public TextRulesViewModel TextRules { get; private set; } = null!;

    private void OnTextRuleChanged(RuleScope scope)
    {
        if (scope == RuleScope.Project)
            _projectDirty = true;
        else
            _globalDirty = true;

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowGlobalCommentsWarning));
        RefreshGeneratorUsage();
    }

    /// <summary>
    /// Ein Bereichswechsel (siehe <see cref="TextRulesViewModel.ChangeScope"/>)
    /// hat einen eigenen Generator in den Zielbereich mitkopiert -- die
    /// Listen "Eigene Generatoren" halten ihren eigenen Bestand
    /// (<see cref="ProjectGenerators"/>/<see cref="GlobalGenerators"/>) und
    /// muessen deshalb neu aufgebaut werden, sonst zeigte die Seite den
    /// frisch kopierten Generator erst nach einem Neustart des Fensters.
    /// </summary>
    private void OnGeneratorCopiedByScopeChange()
    {
        BuildProjectGenerators();
        BuildGlobalGenerators();
    }

    /// <summary>
    /// Meldet allen Eintraegen der Generatorenlisten, dass sich anderswo etwas
    /// geaendert haben koennte (eine Regel, die ihren Generator gewechselt
    /// hat) -- Sperre und Tooltip von "Entfernen" lesen sonst einen veralteten
    /// Stand.
    /// </summary>
    private void RefreshGeneratorUsage()
    {
        foreach (var eintrag in ProjectGenerators)
            eintrag.NotifyUsageChanged();
        foreach (var eintrag in GlobalGenerators)
            eintrag.NotifyUsageChanged();
    }

    // ---------------------------------------------------- Eigene Generatoren

    public ObservableCollection<GeneratorEntryViewModel> ProjectGenerators { get; } = new();
    public ObservableCollection<GeneratorEntryViewModel> GlobalGenerators { get; } = new();

    public bool HasProjectGenerators => ProjectGenerators.Count > 0;
    public bool HasGlobalGenerators => GlobalGenerators.Count > 0;

    /// <summary>"+ Neuer Generator…" (Plan P3d): aktiv, sobald irgendein Ziel infrage kommt.</summary>
    public AsyncRelayCommand NewGeneratorCommand { get; }

    /// <summary>
    /// Oeffnet den Generator-Dialog (Plan P3) verschachtelt in diesem Fenster --
    /// gesetzt von <see cref="Services.DialogService.ShowSettingsAsync"/>, mit
    /// diesem Fenster als Besitzer. Ein Test setzt sie direkt, ohne echtes
    /// Fenster.
    /// </summary>
    public Func<GeneratorEditorViewModel, Task<bool>>? ShowGeneratorEditor { get; set; }

    private void BuildProjectGenerators()
    {
        ProjectGenerators.Clear();
        if (_profileCopy is not null)
        {
            foreach (var name in _profileCopy.Generators.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
            {
                GeneratorEntryViewModel? entry = null;
                entry = new GeneratorEntryViewModel(
                    _profileCopy.Generators, name,
                    onEdit: () => EditGeneratorAsync(entry!, RuleScope.Project),
                    onRemoved: () =>
                    {
                        ProjectGenerators.Remove(entry!);
                        OnPropertyChanged(nameof(HasProjectGenerators));
                        OnProjectGeneratorsChanged();
                    },
                    findUsers: n => FindGeneratorUsers(n, _profileCopy.TextRules, _profileCopy.Fields, null));

                ProjectGenerators.Add(entry);
            }
        }

        OnPropertyChanged(nameof(HasProjectGenerators));
    }

    private void BuildGlobalGenerators()
    {
        GlobalGenerators.Clear();

        foreach (var name in _extensionsCopy.Generators.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
        {
            GeneratorEntryViewModel? entry = null;
            entry = new GeneratorEntryViewModel(
                _extensionsCopy.Generators, name,
                onEdit: () => EditGeneratorAsync(entry!, RuleScope.Global),
                onRemoved: () =>
                {
                    GlobalGenerators.Remove(entry!);
                    OnPropertyChanged(nameof(HasGlobalGenerators));
                    OnGlobalGeneratorsChanged();
                },
                findUsers: n => FindGeneratorUsers(
                    n, _extensionsCopy.TextRules.Concat(_profileCopy?.TextRules ?? Enumerable.Empty<TextRule>()),
                    _profileCopy?.Fields, _extensionsCopy.FieldRules),
                canEdit: CanEditGlobal);

            GlobalGenerators.Add(entry);
        }

        OnPropertyChanged(nameof(HasGlobalGenerators));
    }

    /// <summary>
    /// "+ Neuer Generator…" (Plan P3d): die Vorgabe fuer den Bereich ist
    /// "Projekt", wenn ein Profil geladen ist -- wer aus einem geladenen
    /// Projekt heraus einen Generator anlegt, will ihn meist zunaechst dort
    /// erproben.
    /// </summary>
    private async Task NewGeneratorAsync()
    {
        if (ShowGeneratorEditor is null)
            return;

        var initialScope = HasProfile ? RuleScope.Project : RuleScope.Global;
        var editor = new GeneratorEditorViewModel(
            _profileCopy, _extensionsCopy, CanEditGlobal, GlobalLockReason, initialScope);

        if (!await ShowGeneratorEditor(editor) || !editor.Confirmed)
            return;

        var ziel = editor.ResultScope == RuleScope.Project ? _profileCopy!.Generators : _extensionsCopy.Generators;
        ziel[editor.ResultName] = editor.ResultSettings;

        BuildProjectGenerators();
        BuildGlobalGenerators();

        if (editor.ResultScope == RuleScope.Project)
            OnProjectGeneratorsChanged();
        else
            OnGlobalGeneratorsChanged();
    }

    /// <summary>
    /// "Bearbeiten…" an einer Zeile der Liste "Eigene Generatoren" (Plan P3d):
    /// oeffnet den Dialog mit dem bestehenden Eintrag, seinem Bereich und
    /// seinen Verwendern (die den Namen und die Art dort sperren). Wurde der
    /// Name geaendert -- nur ohne Verwender ueberhaupt moeglich --, wird der
    /// alte Schluessel entfernt und der neue gesetzt.
    /// </summary>
    private async Task EditGeneratorAsync(GeneratorEntryViewModel entry, RuleScope scope)
    {
        if (ShowGeneratorEditor is null)
            return;

        var owner = scope == RuleScope.Project ? _profileCopy!.Generators : _extensionsCopy.Generators;

        var editor = new GeneratorEditorViewModel(
            _profileCopy, _extensionsCopy, CanEditGlobal, GlobalLockReason, scope,
            existingName: entry.Name, users: entry.Users);

        if (!await ShowGeneratorEditor(editor) || !editor.Confirmed)
            return;

        if (!string.Equals(editor.ResultName, entry.Name, StringComparison.OrdinalIgnoreCase))
            owner.Remove(entry.Name);

        owner[editor.ResultName] = editor.ResultSettings;

        BuildProjectGenerators();
        BuildGlobalGenerators();

        if (scope == RuleScope.Project)
            OnProjectGeneratorsChanged();
        else
            OnGlobalGeneratorsChanged();
    }

    /// <summary>
    /// Rueckruf fuer <see cref="TextRulesViewModel"/> (Plan P3d): "Neuer
    /// Generator…" neben "Ersetzen durch" im Regelformular. Eine globale Regel
    /// bekommt den Bereich fest auf "alle Projekte" -- ein Generator, den nur
    /// diese eine Regel braucht, soll nicht erst durch
    /// <see cref="ExtensionLibrary.AdoptProjectGenerators"/> beim Übernehmen
    /// nachgereicht werden muessen. Eine Projektregel darf frei waehlen, wie
    /// der AlwaysReplace-Dialog auch. Liefert den neuen Namen, oder
    /// <c>null</c> bei Abbruch.
    /// </summary>
    private async Task<string?> CreateGeneratorForRuleAsync(RuleScope scope, string? suggestedName)
    {
        if (ShowGeneratorEditor is null)
            return null;

        var editor = new GeneratorEditorViewModel(
            _profileCopy, _extensionsCopy, CanEditGlobal, GlobalLockReason,
            initialScope: scope,
            lockScopeTo: scope == RuleScope.Global ? RuleScope.Global : null,
            suggestedName: suggestedName);

        if (!await ShowGeneratorEditor(editor) || !editor.Confirmed)
            return null;

        var ziel = editor.ResultScope == RuleScope.Project ? _profileCopy!.Generators : _extensionsCopy.Generators;
        ziel[editor.ResultName] = editor.ResultSettings;

        BuildProjectGenerators();
        BuildGlobalGenerators();

        if (editor.ResultScope == RuleScope.Project)
            OnProjectGeneratorsChanged();
        else
            OnGlobalGeneratorsChanged();

        return editor.ResultName;
    }

    /// <summary>Findet alle Regeln, Felder und Spaltenmuster, die einen Generator verwenden -- fuer die Sperre und den Tooltip an "Entfernen".</summary>
    private static IReadOnlyList<string> FindGeneratorUsers(
        string generatorName, IEnumerable<TextRule> textRules, IEnumerable<FieldRule>? fields,
        IEnumerable<FieldNameRule>? fieldRules)
    {
        var treffer = new List<string>();

        foreach (var regel in textRules)
        {
            if (string.Equals(regel.Generator, generatorName, StringComparison.OrdinalIgnoreCase))
                treffer.Add($"Regel „{regel.Name}“");
        }

        if (fields is not null)
        {
            foreach (var feld in fields)
            {
                if (string.Equals(feld.Generator, generatorName, StringComparison.OrdinalIgnoreCase))
                    treffer.Add($"Feld „{feld.Match}“");
            }
        }

        if (fieldRules is not null)
        {
            foreach (var muster in fieldRules)
            {
                if (string.Equals(muster.Generator, generatorName, StringComparison.OrdinalIgnoreCase))
                    treffer.Add($"Spaltenmuster „{muster.Pattern}“");
            }
        }

        return treffer;
    }

    private void OnProjectGeneratorsChanged()
    {
        _projectDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowGlobalCommentsWarning));
        TextRules?.RefreshGeneratorsList();
    }

    private void OnGlobalGeneratorsChanged()
    {
        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowGlobalCommentsWarning));
        TextRules?.RefreshGeneratorsList();
    }

    // -------------------------------------------------------- Spaltenmuster

    public ObservableCollection<FieldNameRuleViewModel> FieldRules { get; } = new();

    public RelayCommand AddFieldRuleCommand { get; private set; } = null!;

    private void BuildFieldRules()
    {
        FieldRules.Clear();
        foreach (var rule in _extensionsCopy.FieldRules)
            FieldRules.Add(MakeFieldRuleEntry(rule));

        AddFieldRuleCommand = new RelayCommand(AddFieldRule, () => CanEditGlobal);
        OnPropertyChanged(nameof(AddFieldRuleCommand));
    }

    private FieldNameRuleViewModel MakeFieldRuleEntry(FieldNameRule rule)
    {
        FieldNameRuleViewModel? entry = null;
        entry = new FieldNameRuleViewModel(
            rule, _profileCopy, _extensionsCopy, OnFieldRulesChanged, CanEditGlobal, FieldRules,
            onRemove: () => RemoveFieldRule(entry!));
        return entry;
    }

    private void AddFieldRule()
    {
        var rule = new FieldNameRule { Pattern = "", Generator = "token" };
        var viewModel = MakeFieldRuleEntry(rule);
        FieldRules.Add(viewModel);

        OnFieldRulesChanged();
    }

    private void RemoveFieldRule(FieldNameRuleViewModel entry)
    {
        FieldRules.Remove(entry);
        OnFieldRulesChanged();
    }

    /// <summary>
    /// Gleicht <see cref="ExtensionLibrary.FieldRules"/> an die Reihenfolge
    /// und den Bestand der Ansichtscollection an -- gerufen nach jeder
    /// Aenderung (Hinzufuegen, Entfernen, Verschieben, Feldbearbeitung); die
    /// Objekte selbst bleiben dieselben, nur Reihenfolge/Bestand koennten sich
    /// geaendert haben.
    /// </summary>
    private void OnFieldRulesChanged()
    {
        _extensionsCopy.FieldRules.Clear();
        _extensionsCopy.FieldRules.AddRange(FieldRules.Select(f => f.Rule));

        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowGlobalCommentsWarning));
        RefreshGeneratorUsage();

        foreach (var eintrag in FieldRules)
            eintrag.NotifyPositionChanged();
    }

    // -------------------------------------------------------- Ablageort

    /// <summary>
    /// Erklaert, wer die geltende Datei aendern darf: neben der Programmdatei
    /// nur, wer dort Schreibrecht hat, im Konfigurationsordner jeder Anwender
    /// selbst.
    /// </summary>
    public string GlobalReachText => _resolution.Origin == ExtensionOrigin.ProgramDirectory
        ? "Neben der Programmdatei – gilt für alle, die das Programm von dort starten. "
          + "Ändern kann sie, wer dort Schreibrecht hat."
        : "Im persönlichen Konfigurationsordner – gilt für alle Ihre Projekte auf diesem Rechner.";

    /// <summary>Die geprueften Fundorte samt Zustand, fuer den Reiter "Ablageort".</summary>
    public IReadOnlyList<ExtensionCandidateInfo> Candidates { get; private set; } = Array.Empty<ExtensionCandidateInfo>();

    private void BuildCandidates()
    {
        Candidates = _resolution.Candidates.Select(kandidat =>
        {
            var origin = kandidat.Origin == ExtensionOrigin.ProgramDirectory
                ? "neben der Programmdatei"
                : "im Konfigurationsordner";

            var zustand = !kandidat.Exists
                ? "nicht vorhanden"
                : kandidat.SkippedAsProfile
                    ? "als Profil übergangen"
                    : string.Equals(kandidat.Path, _resolution.Path, StringComparison.Ordinal)
                        ? "gilt"
                        : "vorhanden";

            return new ExtensionCandidateInfo(kandidat.Path, origin, zustand);
        }).ToList();

        OnPropertyChanged(nameof(Candidates));
    }

    // ------------------------------------------------------ Übernehmen/Abbrechen

    public ObservableCollection<string> ValidationErrors { get; } = new();
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    /// <summary>Ob seit dem Oeffnen etwas geaendert wurde -- fuer die Rueckfrage beim Schliessen.</summary>
    public bool HasUnsavedChanges => _projectDirty || _globalDirty;

    /// <summary>Ob "Übernehmen" erfolgreich war. Nur danach aussagekraeftig.</summary>
    public bool Applied { get; private set; }

    /// <summary>Ob der globale Bereich tatsaechlich geschrieben wurde (fuer die Statuszeile des Aufrufers).</summary>
    public bool GlobalChanged { get; private set; }

    /// <summary>Ob der Projektbereich tatsaechlich ins Profil zurueckgeschrieben wurde.</summary>
    public bool ProjectChanged { get; private set; }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action? CloseRequested;

    private void Apply()
    {
        ValidationErrors.Clear();

        // Vor der Pruefung: ein umbenanntes "iban" soll nicht als "Die
        // Textregel 'iban' ist nicht definiert" das Übernehmen blockieren.
        if (RewriteFieldRuleReferences())
            _projectDirty = true;

        // Ebenfalls vor der Pruefung (Plan P4): eine globale Regel, die einen
        // bislang nur im Projekt vorhandenen Generator verwendet, bekommt ihn
        // jetzt mitkopiert -- sonst liefe sie in jedem anderen Projekt ins
        // Leere. Ohne Profil (globalOnly) ist das ein No-Op.
        if (_profileCopy is not null && _extensionsCopy.AdoptProjectGenerators(_profileCopy).Count > 0)
            _globalDirty = true;

        var issues = ProfileValidator.Validate(_profileCopy ?? new Profile(), _extensionsCopy);
        var fehler = issues
            .Where(i => i.Severity == ValidationSeverity.Error && !_baselineErrors.Contains(ErrorKey(i)))
            .ToList();

        if (fehler.Count > 0)
        {
            foreach (var issue in fehler)
                ValidationErrors.Add(DescribeValidationIssue(issue));

            OnPropertyChanged(nameof(HasValidationErrors));
            return;
        }

        GlobalChanged = _globalDirty;
        ProjectChanged = _projectDirty && _profileCopy is not null;

        if (GlobalChanged)
        {
            // Zuerst die Datei: scheitert das Schreiben, bleibt alles beim
            // Alten und das Fenster offen, statt die Sitzung mit einem Stand zu
            // fuettern, den es auf der Platte nicht gibt.
            try
            {
                _extensionsCopy.Save(_writeState.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GlobalChanged = false;
                ProjectChanged = false;
                ValidationErrors.Add($"{_writeState.Path}: {ex.Message}");
                OnPropertyChanged(nameof(HasValidationErrors));
                return;
            }

            _extensions.ReplaceWith(_extensionsCopy);
        }

        if (ProjectChanged && _session is not null)
        {
            _session.Profile.TextRules.Clear();
            _session.Profile.TextRules.AddRange(_profileCopy!.TextRules);

            _session.Profile.Generators.Clear();
            foreach (var (name, settings) in _profileCopy.Generators)
                _session.Profile.Generators[name] = settings;

            // Nur die Regelauswahl der Felder -- alles andere an den Feldern
            // bearbeitet dieses Fenster nicht, und die Reihenfolge ist dieselbe
            // wie in der Kopie.
            for (var i = 0; i < _session.Profile.Fields.Count && i < _profileCopy.Fields.Count; i++)
                _session.Profile.Fields[i].TextRules = _profileCopy.Fields[i].TextRules;

            _session.MarkChanged();
        }

        Applied = true;
        OnPropertyChanged(nameof(HasValidationErrors));
        _projectDirty = false;
        _globalDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowGlobalCommentsWarning));

        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Zieht die Regelauswahl der Freitextfelder nach, wenn eine dort genannte
    /// Regel umbenannt wurde -- von Hand ueber "Bezeichnung" oder beim
    /// Bereichswechsel, der einen doppelten Namen eindeutig macht.
    ///
    /// Aufgeloest wird ueber das Regelobjekt, nicht ueber Zwischenstaende beim
    /// Tippen: jeder beim Oeffnen genannte Name zeigt auf die Regel, die er
    /// damals meinte (<see cref="_originalRuleByName"/>), und bekommt deren
    /// heutigen Namen. Gibt es die Regel nicht mehr, bleibt der Name stehen --
    /// die Pruefung meldet ihn dann als nicht definiert, und das zu Recht.
    /// Jedes Mal frisch aus <see cref="_originalFieldReferences"/> gebildet,
    /// damit ein zweites "Übernehmen" nach einem Pruefungsfehler dasselbe
    /// Ergebnis liefert.
    /// </summary>
    /// <returns>Ob sich an mindestens einem Feld gegenueber dem Stand beim Oeffnen etwas geaendert hat.</returns>
    private bool RewriteFieldRuleReferences()
    {
        if (_profileCopy is null)
            return false;

        var vorhanden = new HashSet<TextRule>(
            _profileCopy.TextRules.Concat(_extensionsCopy.TextRules), ReferenceEqualityComparer.Instance);

        var geaendert = false;
        for (var i = 0; i < _profileCopy.Fields.Count && i < _originalFieldReferences.Count; i++)
        {
            if (_originalFieldReferences[i] is not { } namen)
                continue;

            var neu = namen
                .Select(name => _originalRuleByName.TryGetValue(name, out var regel) && vorhanden.Contains(regel)
                    ? regel.Name
                    : name)
                .ToList();

            _profileCopy.Fields[i].TextRules = neu;
            geaendert |= !neu.SequenceEqual(namen, StringComparer.Ordinal);
        }

        return geaendert;
    }

    private static string ErrorKey(ValidationIssue issue) => issue.Path + "\n" + issue.Message;

    private static HashSet<string> ErrorKeys(IEnumerable<ValidationIssue> issues)
        => issues.Where(i => i.Severity == ValidationSeverity.Error).Select(ErrorKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Uebersetzt einen Befund in eine fuer den Menschen lesbare Zeile. Fuer
    /// eine Textregel (<c>textRules[i]…</c> bzw. <c>extensions.textRules[i]…</c>,
    /// siehe <see cref="ProfileValidator"/>) wird daraus "Regel „fw"
    /// (Dieses Projekt): <c>&lt;Meldung&gt;</c>" -- der rohe Index sagt einem
    /// Anwender ohne Kenntnis des Dateiformats nichts. Fuer alles andere
    /// (Generatoren, Spaltenmuster, …) bleibt der Pfad stehen.
    /// </summary>
    private string DescribeValidationIssue(ValidationIssue issue)
    {
        var treffer = TextRulePathPattern.Match(issue.Path);
        if (!treffer.Success)
            return $"{issue.Path}: {issue.Message}";

        var istGlobal = treffer.Groups[1].Success;
        var index = int.Parse(treffer.Groups[2].Value);
        var regeln = istGlobal ? _extensionsCopy.TextRules : _profileCopy?.TextRules;

        if (regeln is null || index < 0 || index >= regeln.Count)
            return $"{issue.Path}: {issue.Message}";

        var bereich = istGlobal ? "Alle Projekte" : "Dieses Projekt";
        return $"Regel „{regeln[index].Name}“ ({bereich}): {issue.Message}";
    }

    private static readonly Regex TextRulePathPattern = new(@"^(extensions\.)?textRules\[(\d+)\]", RegexOptions.Compiled);
}

/// <summary>Ein gepruefter Fundort der Erweiterungsdatei, fuer den Reiter "Ablageort".</summary>
/// <param name="Path">Der geprueft Pfad.</param>
/// <param name="OriginLabel">"neben der Programmdatei" oder "im Konfigurationsordner".</param>
/// <param name="StateLabel">"gilt", "vorhanden", "als Profil übergangen" oder "nicht vorhanden".</param>
public sealed record ExtensionCandidateInfo(string Path, string OriginLabel, string StateLabel);

/// <summary>Ein eigener Generator (<see cref="Profile.Generators"/> bzw. <see cref="ExtensionLibrary.Generators"/>) in der Liste "Eigene Generatoren".</summary>
public sealed class GeneratorEntryViewModel : ObservableObject
{
    private readonly Dictionary<string, GeneratorSettings> _owner;
    private readonly Func<string, IReadOnlyList<string>> _findUsers;
    private readonly bool _canEdit;
    private readonly string _name;

    /// <summary>
    /// Die Zeile ist seit Plan P3d nur noch Anzeige: Name, Art und Optionen
    /// aendert man im Generator-Dialog (<see cref="EditCommand"/>), nicht mehr
    /// hier inline. <paramref name="onEdit"/> oeffnet diesen Dialog --
    /// <see cref="SettingsViewModel.EditGeneratorAsync"/> baut danach die
    /// Listen komplett neu, ein bestehendes <see cref="GeneratorEntryViewModel"/>
    /// muss sich also nicht mehr selbst umbenennen koennen.
    /// </summary>
    public GeneratorEntryViewModel(
        Dictionary<string, GeneratorSettings> owner, string name,
        Func<Task> onEdit, Action onRemoved,
        Func<string, IReadOnlyList<string>> findUsers, bool canEdit = true)
    {
        _owner = owner;
        _name = name;
        _findUsers = findUsers;
        _canEdit = canEdit;

        EditCommand = new AsyncRelayCommand(onEdit, () => CanEdit);
        RemoveCommand = new RelayCommand(
            () =>
            {
                _owner.Remove(_name);
                onRemoved();
            },
            () => CanEdit && Users.Count == 0);
    }

    private GeneratorSettings Settings => _owner[_name];

    /// <summary>Ob dieser Eintrag ueberhaupt bearbeitet werden darf (siehe <see cref="SettingsViewModel.CanEditGlobal"/>).</summary>
    public bool CanEdit => _canEdit;

    public string Name => _name;

    public string TypeLabel => string.IsNullOrWhiteSpace(Settings.Type) ? "token" : Settings.Type!;

    /// <summary>Deutsche Erklaerung der Art, etwa "allgemeine Kennung (TOK_…), mit Kennzeichnung davor".</summary>
    public string DescriptionLabel => GeneratorDescriptions.For(TypeLabel);

    /// <summary>
    /// Kurzfassung der wichtigsten Option, etwa "Kennzeichnung FW~" oder
    /// "3 Werte" -- sonst leer. Nur eine Auswahl, keine vollstaendige
    /// Wiedergabe aller Optionen: die Zeile soll knapp bleiben, Einzelheiten
    /// zeigt der Generator-Dialog.
    /// </summary>
    public string OptionsSummary
    {
        get
        {
            var settings = Settings;
            var baseType = TypeLabel;

            if (string.Equals(baseType, "token", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(settings.Prefix))
                return $"Kennzeichnung {settings.Prefix}";

            if (string.Equals(baseType, "pattern", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(settings.Pattern))
                return $"Maske {settings.Pattern}";

            if (string.Equals(baseType, "wordlist", StringComparison.OrdinalIgnoreCase) && settings.Values is { Count: > 0 } werte)
                return werte.Count == 1 ? "1 Wert" : $"{werte.Count} Werte";

            if (string.Equals(baseType, "dateRange", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(settings.From) || !string.IsNullOrWhiteSpace(settings.To)))
            {
                return $"Zeitraum {settings.From ?? "…"} bis {settings.To ?? "…"}";
            }

            if (string.Equals(baseType, "dateShift", StringComparison.OrdinalIgnoreCase))
            {
                var tage = settings.MaxDays > 0 ? settings.MaxDays : GeneratorSettings.DefaultMaxDays;
                return $"bis ± {tage} Tage";
            }

            if (string.Equals(baseType, "expression", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(settings.Expression))
            {
                var ausdruck = settings.Expression.Length > 40
                    ? settings.Expression[..40] + "…"
                    : settings.Expression;
                var text = $"Ausdruck {ausdruck}";

                if (settings.Tables is { Count: > 0 } tabellen)
                    text += tabellen.Count == 1 ? " · 1 Tabelle" : $" · {tabellen.Count} Tabellen";

                return text;
            }

            return "";
        }
    }

    public bool HasOptionsSummary => OptionsSummary.Length > 0;

    /// <summary>Wer diesen Generator gerade nutzt -- gesperrt, solange die Liste nicht leer ist.</summary>
    public IReadOnlyList<string> Users => _findUsers(_name);

    public bool IsInUse => Users.Count > 0;

    public string RemoveTooltip => IsInUse
        ? "Wird verwendet von: " + string.Join(", ", Users)
        : "";

    /// <summary>Oeffnet den Generator-Dialog (Plan P3d) fuer diesen Eintrag.</summary>
    public AsyncRelayCommand EditCommand { get; }

    public RelayCommand RemoveCommand { get; }

    /// <summary>
    /// Meldet, dass sich die Verwendung anderswo geaendert haben koennte --
    /// <see cref="_findUsers"/> liest live aus den Kopien und braucht darum
    /// keinen eigenen Zustand, nur die Benachrichtigung der Bindung.
    /// </summary>
    public void NotifyUsageChanged()
    {
        OnPropertyChanged(nameof(Users));
        OnPropertyChanged(nameof(IsInUse));
        OnPropertyChanged(nameof(RemoveTooltip));
        RemoveCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Ein Spaltenmuster (<see cref="ExtensionLibrary.FieldRules"/>) in der Liste "Spaltenmuster".</summary>
public sealed class FieldNameRuleViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly bool _canEdit;
    private readonly ObservableCollection<FieldNameRuleViewModel> _siblings;

    public FieldNameRuleViewModel(
        FieldNameRule rule, Profile? profile, ExtensionLibrary extensions, Action onChanged, bool canEdit,
        ObservableCollection<FieldNameRuleViewModel> siblings, Action onRemove)
    {
        Rule = rule;
        _onChanged = onChanged;
        _canEdit = canEdit;
        _siblings = siblings;

        // Der Sonderwert "scanText" (Freitextfeld durchsuchen) steht neben den
        // eingebauten und eigenen Generatoren zur Wahl -- ein Spaltenmuster
        // kennt keinen Profilbezug, darum eine leere Vorgabe fuer die
        // Profilseite der Generatorenliste.
        var eintraege = new List<GeneratorOption> { new("scanText", "Freitextfeld durchsuchen") };
        eintraege.AddRange(GeneratorOption.For(profile ?? new Profile(), extensions));
        Generators = new ObservableCollection<GeneratorOption>(eintraege);

        RemoveCommand = new RelayCommand(onRemove, () => CanEdit);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => Move(1), () => CanMoveDown);
    }

    public FieldNameRule Rule { get; }

    public ObservableCollection<GeneratorOption> Generators { get; }

    public bool CanEdit => _canEdit;

    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public bool CanMoveUp => CanEdit && _siblings.IndexOf(this) > 0;

    public bool CanMoveDown
    {
        get
        {
            var index = _siblings.IndexOf(this);
            return CanEdit && index >= 0 && index < _siblings.Count - 1;
        }
    }

    private void Move(int delta)
    {
        if (!CanEdit)
            return;

        var index = _siblings.IndexOf(this);
        var ziel = index + delta;
        if (index < 0 || ziel < 0 || ziel >= _siblings.Count)
            return;

        _siblings.Move(index, ziel);
        _onChanged();
    }

    /// <summary>Meldet, dass sich die Position in <see cref="_siblings"/> geaendert haben koennte -- nach jeder strukturellen Aenderung.</summary>
    public void NotifyPositionChanged()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
    }

    public string Pattern
    {
        get => Rule.Pattern;
        set
        {
            if (!CanEdit || Rule.Pattern == value)
                return;
            Rule.Pattern = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public GeneratorOption? Generator
    {
        get => Generators.FirstOrDefault(g => string.Equals(g.Name, Rule.Generator, StringComparison.OrdinalIgnoreCase))
               ?? (Rule.Generator.Length == 0 ? null : new GeneratorOption(Rule.Generator, "eigener Namensraum"));
        set
        {
            if (!CanEdit || value is null || Rule.Generator == value.Name)
                return;
            Rule.Generator = value.Name;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool IgnoreCase
    {
        get => Rule.IgnoreCase;
        set
        {
            if (!CanEdit || Rule.IgnoreCase == value)
                return;
            Rule.IgnoreCase = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string? Comment
    {
        get => Rule.Comment;
        set
        {
            if (!CanEdit || Rule.Comment == value)
                return;
            Rule.Comment = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            _onChanged();
        }
    }
}
