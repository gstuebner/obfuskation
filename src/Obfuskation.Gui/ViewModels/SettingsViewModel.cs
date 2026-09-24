using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
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

        BuildProjectGenerators();
        BuildGlobalGenerators();
        BuildFieldRules();

        TextRules = new TextRulesViewModel(
            _profileCopy, _extensionsCopy, CanEditGlobal, GlobalLockReason, OnTextRuleChanged,
            onGeneratorCopied: OnGeneratorCopiedByScopeChange);

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
                    onFieldChanged: OnProjectGeneratorsChanged,
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
                onFieldChanged: OnGlobalGeneratorsChanged,
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
    private readonly Action _onFieldChanged;
    private readonly Func<string, IReadOnlyList<string>> _findUsers;
    private readonly bool _canEdit;
    private string _name;

    public GeneratorEntryViewModel(
        Dictionary<string, GeneratorSettings> owner, string name,
        Action onFieldChanged, Action onRemoved,
        Func<string, IReadOnlyList<string>> findUsers, bool canEdit = true)
    {
        _owner = owner;
        _name = name;
        _onFieldChanged = onFieldChanged;
        _findUsers = findUsers;
        _canEdit = canEdit;

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

    public string Name
    {
        get => _name;
        set
        {
            var neu = (value ?? "").Trim();
            if (!CanEdit || neu.Length == 0 || string.Equals(neu, _name, StringComparison.OrdinalIgnoreCase))
                return;

            // Waehrend der Generator irgendwo verwendet wird, bliebe eine
            // Umbenennung hier ohne Wirkung auf die Regeln, die ihn beim alten
            // Namen ansprechen -- dieselbe Sperre wie beim Entfernen.
            if (Users.Count > 0 || _owner.ContainsKey(neu))
                return;

            var settings = _owner[_name];
            _owner.Remove(_name);
            _owner[neu] = settings;
            _name = neu;

            OnPropertyChanged();
            _onFieldChanged();
        }
    }

    public string TypeLabel => string.IsNullOrWhiteSpace(Settings.Type) ? "token" : Settings.Type!;

    public string? Prefix
    {
        get => Settings.Prefix;
        set
        {
            if (!CanEdit || Settings.Prefix == value)
                return;

            Settings.Prefix = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            _onFieldChanged();
        }
    }

    /// <summary>Wer diesen Generator gerade nutzt -- gesperrt, solange die Liste nicht leer ist.</summary>
    public IReadOnlyList<string> Users => _findUsers(_name);

    public bool IsInUse => Users.Count > 0;

    public string RemoveTooltip => IsInUse
        ? "Wird verwendet von: " + string.Join(", ", Users)
        : "";

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
