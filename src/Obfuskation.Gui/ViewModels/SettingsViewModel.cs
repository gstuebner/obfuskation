using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>Welcher der beiden Reiter des Einstellungsfensters gemeint ist.</summary>
public enum SettingsTab
{
    /// <summary>„Dieses Projekt – ‹Profilname›“.</summary>
    Project,

    /// <summary>„Alle Projekte (hauseigen)“ -- die Erweiterungsdatei.</summary>
    Global,
}

/// <summary>
/// Das Einstellungsfenster: Textregeln, eigene Generatoren und (im globalen
/// Reiter) Spaltenmuster, fuer das Projekt und fuer die Erweiterungsdatei
/// nebeneinander. Ersetzt das fruehere Fenster "Textregeln" und "Hauseigene
/// Muster…" (rein lesend).
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
    private readonly string? _extensionLoadError;

    private SettingsTab _selectedTab;
    private bool _projectDirty;
    private bool _globalDirty;
    private readonly HashSet<string> _baselineErrors;

    public SettingsViewModel(
        ProfileSession? session,
        ExtensionLibrary extensions,
        ExtensionWriteState writeState,
        string? extensionLoadError,
        SettingsTab initialTab = SettingsTab.Project,
        string? selectRuleName = null)
    {
        _session = session;
        _extensions = extensions;
        _writeState = writeState;
        _extensionLoadError = extensionLoadError;

        _extensionsCopy = extensions.Clone();
        _profileCopy = session is null ? null : ProfileStore.DeepCopy(session.Profile);

        // Was schon vor dem Oeffnen falsch war (etwa ein halb eingerichtetes
        // Feld der Dateiansicht), soll "Übernehmen" nicht blockieren -- das
        // hat dieses Fenster weder verursacht noch kann es dort behoben werden.
        _baselineErrors = ErrorKeys(ProfileValidator.Validate(_profileCopy ?? new Profile(), _extensionsCopy));

        // Ohne Profil ist der Projektreiter nicht erreichbar (siehe
        // ProjectTabHint) -- der globale Reiter bleibt trotzdem sinnvoll, denn
        // die Erweiterungsdatei existiert unabhaengig von jedem Profil.
        _selectedTab = _profileCopy is null ? SettingsTab.Global : initialTab;

        ApplyCommand = new RelayCommand(Apply);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        OpenFolderCommand = new RelayCommand(
            () => OpenFolderRequested?.Invoke(Path.GetDirectoryName(_writeState.Path) ?? _writeState.Path));
        OpenEditorCommand = new RelayCommand(() => OpenEditorRequested?.Invoke(_writeState.Path));

        BuildProjectGenerators();
        BuildGlobalGenerators();
        BuildFieldRules();
        BuildTextRulePanels();

        if (selectRuleName is not null)
        {
            if (_selectedTab == SettingsTab.Project)
                ProjectRules?.SelectByName(selectRuleName);
            else
                GlobalRules.SelectByName(selectRuleName);
        }
    }

    // ----------------------------------------------------------- Reiter

    public SettingsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value))
                return;

            OnPropertyChanged(nameof(IsProjectTab));
            OnPropertyChanged(nameof(IsGlobalTab));
            OnPropertyChanged(nameof(SelectedTabIndex));
        }
    }

    public bool IsProjectTab
    {
        get => _selectedTab == SettingsTab.Project;
        set { if (value) SelectedTab = SettingsTab.Project; }
    }

    public bool IsGlobalTab
    {
        get => _selectedTab == SettingsTab.Global;
        set { if (value) SelectedTab = SettingsTab.Global; }
    }

    /// <summary>
    /// Wie <see cref="SelectedTab"/>, als Zahl fuer die Bindung an
    /// <c>TabControl.SelectedIndex</c> -- schlichter als ein eigener
    /// Konverter fuer genau diese eine Stelle.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTab == SettingsTab.Global ? 1 : 0;
        set => SelectedTab = value == 1 ? SettingsTab.Global : SettingsTab.Project;
    }

    /// <summary>Ob ueberhaupt ein Profil geladen ist -- sonst zeigt der Projektreiter nur einen Hinweis.</summary>
    public bool HasProfile => _profileCopy is not null;

    public string ProjectTabTitle => _profileCopy is null
        ? "Dieses Projekt"
        : $"Dieses Projekt – {_session!.DisplayName}";

    // ----------------------------------------------------------- Global

    public string GlobalPath => _writeState.Path;

    /// <summary>Ob der globale Reiter bearbeitet werden darf.</summary>
    public bool CanEditGlobal => _extensionLoadError is null && _writeState.CanWrite;

    /// <summary>
    /// Zustandstext der Kopfzeile: "bearbeitbar", oder die Ursache der Sperre
    /// -- entweder der Ladefehler (Fehler 1) oder <see cref="ExtensionWriteState.Reason"/>
    /// (Fehler 2).
    /// </summary>
    public string GlobalStateText
        => _extensionLoadError ?? _writeState.Reason ?? "bearbeitbar";

    public bool HasGlobalWarning => !CanEditGlobal;

    public bool HasGlobalComments => File.Exists(_writeState.Path) && ExtensionLibrary.HasComments(_writeState.Path);

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenEditorCommand { get; }

    /// <summary>Bittet den Aufrufer, den Ordner der Erweiterungsdatei zu oeffnen (Pfad im Argument).</summary>
    public event Action<string>? OpenFolderRequested;

    /// <summary>Bittet den Aufrufer, die Erweiterungsdatei im Editor zu oeffnen.</summary>
    public event Action<string>? OpenEditorRequested;

    // ------------------------------------------------------- Textregeln

    /// <summary>Nur mit geladenem Profil vorhanden.</summary>
    public TextRulesViewModel? ProjectRules { get; private set; }

    public TextRulesViewModel GlobalRules { get; private set; } = null!;

    private void BuildTextRulePanels()
    {
        IReadOnlyList<TextRule> MergeForTrial()
            => _extensionsCopy.MergeTextRules(_profileCopy?.TextRules ?? new List<TextRule>());

        if (_profileCopy is not null)
        {
            ProjectRules = new TextRulesViewModel(
                _profileCopy.TextRules,
                () => _extensionsCopy.TextRules,
                _profileCopy,
                _extensionsCopy,
                MergeForTrial,
                OnProjectChanged,
                isReadOnly: false,
                otherAreaLabel: "gilt für alle Projekte",
                moveLabel: "In alle Projekte verschieben",
                onSwitchToOtherArea: name => SwitchTo(SettingsTab.Global, name),
                onMoveRequested: CanEditGlobal ? rule => MoveRule(rule, fromProject: true) : null);
        }

        GlobalRules = new TextRulesViewModel(
            _extensionsCopy.TextRules,
            () => _profileCopy?.TextRules ?? new List<TextRule>(),
            _profileCopy ?? new Profile(),
            _extensionsCopy,
            MergeForTrial,
            OnGlobalChanged,
            isReadOnly: !CanEditGlobal,
            otherAreaLabel: "gilt nur in diesem Projekt",
            moveLabel: "Nur in dieses Projekt verschieben",
            onSwitchToOtherArea: name => SwitchTo(SettingsTab.Project, name),
            onMoveRequested: _profileCopy is not null ? rule => MoveRule(rule, fromProject: false) : null);

        OnPropertyChanged(nameof(ProjectRules));
        OnPropertyChanged(nameof(GlobalRules));
    }

    private void SwitchTo(SettingsTab tab, string ruleName)
    {
        SelectedTab = tab;
        (tab == SettingsTab.Project ? ProjectRules : GlobalRules)?.SelectByName(ruleName);
    }

    private void OnProjectChanged()
    {
        _projectDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        GlobalRules.RefreshOtherArea();
        RefreshGeneratorUsage();
    }

    private void OnGlobalChanged()
    {
        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ProjectRules?.RefreshOtherArea();
        RefreshGeneratorUsage();
    }

    /// <summary>
    /// Meldet allen Eintraegen der Generatorenlisten, dass sich anderswo etwas
    /// geaendert haben koennte, das sie betrifft (eine Regel, die ihren
    /// Generator gewechselt hat) -- Sperre und Tooltip von "Entfernen" lesen
    /// sonst einen veralteten Stand.
    /// </summary>
    private void RefreshGeneratorUsage()
    {
        foreach (var eintrag in ProjectGenerators)
            eintrag.NotifyUsageChanged();
        foreach (var eintrag in GlobalGenerators)
            eintrag.NotifyUsageChanged();
    }

    /// <summary>
    /// Verschiebt eine eigene Regel in den jeweils anderen Bereich. Einen
    /// eigenen Generator, den die Regel nutzt und den es im Ziel noch nicht
    /// gibt, kopiert das mit, statt ihn zu verschieben -- die Quelle soll
    /// weiter funktionieren, falls dort noch etwas anderes ihn braucht.
    /// </summary>
    private void MoveRule(TextRule rule, bool fromProject)
    {
        if (_profileCopy is null)
            return;

        var quellRegeln = fromProject ? _profileCopy.TextRules : _extensionsCopy.TextRules;
        var zielRegeln = fromProject ? _extensionsCopy.TextRules : _profileCopy.TextRules;
        var quellGeneratoren = fromProject ? _profileCopy.Generators : _extensionsCopy.Generators;
        var zielGeneratoren = fromProject ? _extensionsCopy.Generators : _profileCopy.Generators;

        var neuerName = TextRuleNaming.MakeUnique(
            rule.Name, zielRegeln.Select(r => r.Name).Concat(quellRegeln.Where(r => r != rule).Select(r => r.Name)));

        if (!Core.Generation.GeneratorRegistry.KnownNames.Contains(rule.Generator, StringComparer.OrdinalIgnoreCase)
            && quellGeneratoren.TryGetValue(rule.Generator, out var generatorSettings)
            && !zielGeneratoren.ContainsKey(rule.Generator))
        {
            zielGeneratoren[rule.Generator] = ProfileStore.DeepCopy(generatorSettings);
        }

        quellRegeln.Remove(rule);
        rule.Name = neuerName;
        zielRegeln.Add(rule);

        _projectDirty = true;
        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));

        BuildProjectGenerators();
        BuildGlobalGenerators();
        BuildTextRulePanels();

        SwitchTo(fromProject ? SettingsTab.Global : SettingsTab.Project, neuerName);
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
        ProjectRules?.RefreshGenerators();
        GlobalRules.RefreshGenerators();
    }

    private void OnGlobalGeneratorsChanged()
    {
        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ProjectRules?.RefreshGenerators();
        GlobalRules.RefreshGenerators();
    }

    // -------------------------------------------------------- Spaltenmuster

    public ObservableCollection<FieldNameRuleViewModel> FieldRules { get; } = new();

    public RelayCommand AddFieldRuleCommand { get; private set; } = null!;
    public RelayCommand RemoveFieldRuleCommand { get; private set; } = null!;

    private FieldNameRuleViewModel? _selectedFieldRule;

    public FieldNameRuleViewModel? SelectedFieldRule
    {
        get => _selectedFieldRule;
        set
        {
            if (SetProperty(ref _selectedFieldRule, value))
                RemoveFieldRuleCommand.RaiseCanExecuteChanged();
        }
    }

    private void BuildFieldRules()
    {
        FieldRules.Clear();
        foreach (var rule in _extensionsCopy.FieldRules)
            FieldRules.Add(new FieldNameRuleViewModel(rule, _profileCopy, _extensionsCopy, OnFieldRulesChanged, CanEditGlobal));

        AddFieldRuleCommand = new RelayCommand(AddFieldRule, () => CanEditGlobal);
        RemoveFieldRuleCommand = new RelayCommand(RemoveFieldRule, () => CanEditGlobal && _selectedFieldRule is not null);

        OnPropertyChanged(nameof(AddFieldRuleCommand));
        OnPropertyChanged(nameof(RemoveFieldRuleCommand));
    }

    private void AddFieldRule()
    {
        var rule = new FieldNameRule { Pattern = "", Generator = "token" };
        _extensionsCopy.FieldRules.Add(rule);

        var viewModel = new FieldNameRuleViewModel(rule, _profileCopy, _extensionsCopy, OnFieldRulesChanged, CanEditGlobal);
        FieldRules.Add(viewModel);
        SelectedFieldRule = viewModel;

        OnFieldRulesChanged();
    }

    private void RemoveFieldRule()
    {
        if (_selectedFieldRule is null)
            return;

        _extensionsCopy.FieldRules.Remove(_selectedFieldRule.Rule);
        FieldRules.Remove(_selectedFieldRule);
        SelectedFieldRule = FieldRules.FirstOrDefault();

        OnFieldRulesChanged();
    }

    private void OnFieldRulesChanged()
    {
        _globalDirty = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        RefreshGeneratorUsage();
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

        var issues = ProfileValidator.Validate(_profileCopy ?? new Profile(), _extensionsCopy);
        var fehler = issues
            .Where(i => i.Severity == ValidationSeverity.Error && !_baselineErrors.Contains(ErrorKey(i)))
            .ToList();

        if (fehler.Count > 0)
        {
            foreach (var issue in fehler)
                ValidationErrors.Add($"{issue.Path}: {issue.Message}");

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

            _session.MarkChanged();
        }

        Applied = true;
        OnPropertyChanged(nameof(HasValidationErrors));
        _projectDirty = false;
        _globalDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));

        CloseRequested?.Invoke();
    }

    private static string ErrorKey(ValidationIssue issue) => issue.Path + "\n" + issue.Message;

    private static HashSet<string> ErrorKeys(IEnumerable<ValidationIssue> issues)
        => issues.Where(i => i.Severity == ValidationSeverity.Error).Select(ErrorKey).ToHashSet(StringComparer.Ordinal);
}

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

    public FieldNameRuleViewModel(
        FieldNameRule rule, Profile? profile, ExtensionLibrary extensions, Action onChanged, bool canEdit)
    {
        Rule = rule;
        _onChanged = onChanged;
        _canEdit = canEdit;

        // Der Sonderwert "scanText" (Freitextfeld durchsuchen) steht neben den
        // eingebauten und eigenen Generatoren zur Wahl -- ein Spaltenmuster
        // kennt keinen Profilbezug, darum eine leere Vorgabe fuer die
        // Profilseite der Generatorenliste.
        var eintraege = new List<GeneratorOption> { new("scanText", "Freitextfeld durchsuchen") };
        eintraege.AddRange(GeneratorOption.For(profile ?? new Profile(), extensions));
        Generators = new ObservableCollection<GeneratorOption>(eintraege);
    }

    public FieldNameRule Rule { get; }

    public ObservableCollection<GeneratorOption> Generators { get; }

    public bool CanEdit => _canEdit;

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
