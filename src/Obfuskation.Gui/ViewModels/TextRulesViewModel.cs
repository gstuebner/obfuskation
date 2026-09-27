using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Wie eine Textregel gerade erfasst wird -- die drei Modi des Formulars
/// (Plan Teil B, "Anfaenger statt RegEx").
/// </summary>
public enum PatternMode
{
    /// <summary>"Genau dieser Wert" (<see cref="PatternFromSample.Literal"/>).</summary>
    Exact,

    /// <summary>"Alles dieser Form" (<see cref="PatternFromSample.Shape"/>).</summary>
    Shape,

    /// <summary>"Eigener Ausdruck (für Profis)": das Muster wird von Hand eingetragen.</summary>
    Custom,
}

/// <summary>
/// Eine gemeinsame Liste aller Textregeln -- Projektregeln und Regeln der
/// Erweiterungsdatei nebeneinander, statt wie vor Plan Teil B in zwei
/// getrennten Reiterinhalten. Jede Regel traegt ihren Bereich
/// (<see cref="RuleScope"/>) offen als Eigenschaft statt sich hinter zwei
/// Instanzen dieses Ansichtsmodells zu verstecken -- Verschieben zwischen den
/// Bereichen ist damit nur noch eine Eigenschaftsaenderung an der Regel
/// selbst (<see cref="TextRuleViewModel.IsProjectScope"/>/
/// <see cref="TextRuleViewModel.IsGlobalScope"/>), keine Bewegung zwischen
/// zwei Listen mehr.
///
/// Arbeitet wie das ganze Einstellungsfenster auf Kopien von Profil und
/// Erweiterung (siehe <see cref="SettingsViewModel"/>) und mutiert sie direkt --
/// <see cref="SettingsViewModel.Apply"/> uebernimmt die Kopien erst bei
/// "Übernehmen".
/// </summary>
public sealed class TextRulesViewModel : ObservableObject
{
    private readonly Profile? _profile;
    private readonly Profile _profileForGenerators;
    private readonly ExtensionLibrary _extensions;
    private readonly bool _canEditGlobal;
    private readonly string? _globalLockReason;
    private readonly Action<RuleScope> _onChanged;
    private readonly Action? _onGeneratorCopied;
    private readonly Func<RuleScope, string?, Task<string?>>? _createGenerator;
    private readonly TextRuleEngine _engine = new();
    private readonly List<TextRuleViewModel> _allRules = new();

    private TextRuleViewModel? _selected;
    private RuleFilterOption _selectedFilterOption;
    private string _sampleText =
        "Herr Max Mustermann, IBAN DE02120300000000202051, erreichbar unter\n"
        + "max.mustermann@beispiel.de oder +49 30 12345678.\n"
        + "Rechnung 2024-0815 vom 15.03.2024, Betrag 1.234,56 EUR.";

    private string _matchSummary = "";

    /// <param name="profile">Das Profil (Kopie), oder <c>null</c> ohne geladenes Profil.</param>
    /// <param name="extensions">Die Erweiterung (Kopie) -- immer vorhanden, unabhaengig von <paramref name="profile"/>.</param>
    /// <param name="canEditGlobal">Ob sich die Erweiterungsdatei gerade schreiben liesse.</param>
    /// <param name="globalLockReason">Erklaerung, wenn <paramref name="canEditGlobal"/> falsch ist -- sonst <c>null</c>.</param>
    /// <param name="onChanged">Wird bei jeder Aenderung gerufen, mit dem betroffenen Bereich.</param>
    /// <param name="onGeneratorCopied">
    /// Wird gerufen, wenn ein Bereichswechsel (<see cref="ChangeScope"/>)
    /// einen eigenen Generator in den Zielbereich mitkopiert hat -- der
    /// Aufrufer (<see cref="SettingsViewModel"/>) haelt die Listen "Eigene
    /// Generatoren" separat und muss sie dann neu aufbauen, sonst zeigte die
    /// dortige Seite den frisch kopierten Generator erst nach einem
    /// Neustart des Fensters.
    /// </param>
    /// <param name="createGenerator">
    /// Rueckruf fuer "Neuer Generator…" neben "Ersetzen durch" (Plan P3d):
    /// bekommt den Bereich der ausgewaehlten Regel und ihren Namen als
    /// Namensvorschlag, oeffnet den Generator-Dialog und liefert den neuen
    /// Namen -- oder <c>null</c> bei Abbruch. <c>null</c>, wenn der Aufrufer
    /// (etwa ein Test) das Anlegen nicht anbietet.
    /// </param>
    public TextRulesViewModel(
        Profile? profile, ExtensionLibrary extensions, bool canEditGlobal, string? globalLockReason,
        Action<RuleScope> onChanged, Action? onGeneratorCopied = null,
        Func<RuleScope, string?, Task<string?>>? createGenerator = null)
    {
        _profile = profile;
        _profileForGenerators = profile ?? new Profile();
        _extensions = extensions;
        _canEditGlobal = canEditGlobal;
        _globalLockReason = globalLockReason;
        _onChanged = onChanged;
        _onGeneratorCopied = onGeneratorCopied;
        _createGenerator = createGenerator;

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.For(_profileForGenerators, extensions));

        FilterOptions =
        [
            new RuleFilterOption("alle", null),
            new RuleFilterOption("dieses Projekt", RuleScope.Project),
            new RuleFilterOption("alle Projekte", RuleScope.Global),
        ];
        _selectedFilterOption = FilterOptions[0];

        AddCommand = new RelayCommand(AddRule, () => CanAddRule);
        RemoveCommand = new RelayCommand(RemoveSelected, () => _selected is { IsEditable: true });
        NewGeneratorCommand = new AsyncRelayCommand(NewGeneratorAsync, () => HasSelected);

        BuildRules();
    }

    // -------------------------------------------------------------- Filter

    public bool HasProfile => _profile is not null;

    /// <summary>Ohne Profil bleibt der Filter ausgeblendet -- es gibt kein "dieses Projekt".</summary>
    public bool ShowFilter => HasProfile;

    public IReadOnlyList<RuleFilterOption> FilterOptions { get; }

    public RuleFilterOption SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (!SetProperty(ref _selectedFilterOption, value))
                return;

            ApplyFilter();
        }
    }

    // --------------------------------------------------------------- Liste

    public ObservableCollection<TextRuleViewModel> Rules { get; } = new();
    public ObservableCollection<GeneratorOption> Generators { get; }
    public ObservableCollection<MatchPreview> Matches { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }

    /// <summary>"Neuer Generator…" neben "Ersetzen durch" (Plan P3d).</summary>
    public AsyncRelayCommand NewGeneratorCommand { get; }

    public TextRuleViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelected));
                RemoveCommand.RaiseCanExecuteChanged();
                NewGeneratorCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelected => _selected is not null;

    /// <summary>Ob "+ Neue Regel" ueberhaupt etwas anlegen koennte -- sonst Grund fuer den Tooltip.</summary>
    public bool CanAddRule => _profile is not null || _canEditGlobal;

    public string? AddRuleLockReason => CanAddRule
        ? null
        : _globalLockReason ?? "Kein Profil geladen und die Erweiterungsdatei ist nicht beschreibbar.";

    /// <summary>Der Text, an dem die Muster erprobt werden.</summary>
    public string SampleText
    {
        get => _sampleText;
        set
        {
            if (SetProperty(ref _sampleText, value))
                Evaluate();
        }
    }

    public string MatchSummary
    {
        get => _matchSummary;
        private set => SetProperty(ref _matchSummary, value);
    }

    /// <summary>Waehlt die Regel mit diesem Namen und Bereich aus, sofern vorhanden -- passt den Filter bei Bedarf an.</summary>
    public void SelectByName(string ruleName, RuleScope? scope = null)
    {
        var treffer = _allRules.FirstOrDefault(r =>
            string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase) && (scope is null || r.Scope == scope));

        if (treffer is null)
            return;

        if (!Rules.Contains(treffer))
            SelectedFilterOption = FilterOptions[0]; // "alle" -- macht die Regel sicher sichtbar.

        Selected = treffer;
    }

    private void BuildRules()
    {
        _allRules.Clear();

        var projektNamen = new HashSet<string>(
            _profile?.TextRules.Select(r => r.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (_profile is not null)
        {
            foreach (var rule in _profile.TextRules)
                _allRules.Add(MakeEntry(rule, RuleScope.Project, isOverridden: false));
        }

        foreach (var rule in _extensions.TextRules)
            _allRules.Add(MakeEntry(rule, RuleScope.Global, isOverridden: projektNamen.Contains(rule.Name)));

        // Galt fuer genau diesen einen Aufbau -- ein spaeterer BuildRules-Lauf
        // (etwa nach dem naechsten Verschieben) soll keine andere Regel
        // ungewollt in den Namensautomatismus versetzen.
        _pendingAutoNameRule = null;

        ApplyFilter();
    }

    /// <summary>
    /// Die eben erst per <see cref="AddRule"/> angelegte Regel, deren
    /// Formular-Instanz beim naechsten <see cref="BuildRules"/> mit
    /// <c>nameIsAuto: true</c> entstehen soll -- ohne dieses Merken wuerde der
    /// vollstaendige Wiederaufbau der Liste den Automatismus sofort wieder
    /// verlieren, noch bevor der Anwender einen Beispielwert eintippen konnte.
    /// </summary>
    private TextRule? _pendingAutoNameRule;

    private TextRuleViewModel MakeEntry(TextRule rule, RuleScope scope, bool isOverridden)
    {
        var isEditable = scope == RuleScope.Project || _canEditGlobal;

        return new TextRuleViewModel(
            rule, scope, isOverridden, isEditable,
            canChangeScope: _profile is not null && _canEditGlobal && isEditable,
            scopeLockReason: ScopeLockReason,
            canAdjustForProject: scope == RuleScope.Global && !isEditable && !isOverridden && _profile is not null,
            profileForGenerators: _profileForGenerators,
            extensions: _extensions,
            makeUniqueName: kandidat => TextRuleNaming.MakeUnique(kandidat, AllNamesExcept(rule)),
            onChanged: () => OnRuleChanged(scope),
            onChangeScope: neuerBereich => ChangeScope(rule, neuerBereich),
            onAdjustForProject: () => AdjustForProject(rule),
            nameIsAuto: ReferenceEquals(rule, _pendingAutoNameRule),
            nameHint: () => DescribeNameConflict(rule, scope));
    }

    /// <summary>
    /// Hinweis unter der Bezeichnung, solange sie mit einer anderen Regel
    /// zusammenfaellt -- im selben Bereich blockiert das "Übernehmen"
    /// (<see cref="ProfileValidator"/>: "mehrfach vergeben"), ueber die
    /// Bereichsgrenze hinweg ersetzt die Projektregel die globale
    /// (<see cref="ExtensionLibrary.MergeTextRules"/>). Beides soll beim
    /// Tippen auffallen, nicht erst beim Übernehmen oder gar nicht.
    /// </summary>
    private string? DescribeNameConflict(TextRule rule, RuleScope scope)
    {
        bool Gleich(TextRule andere)
            => !ReferenceEquals(andere, rule) && string.Equals(andere.Name, rule.Name, StringComparison.OrdinalIgnoreCase);

        var projektRegeln = _profile?.TextRules ?? new List<TextRule>();
        var eigene = scope == RuleScope.Project ? projektRegeln : _extensions.TextRules;
        var fremde = scope == RuleScope.Project ? _extensions.TextRules : projektRegeln;

        if (eigene.Any(Gleich))
            return "Diese Bezeichnung ist schon vergeben. Bitte eine andere wählen.";

        if (fremde.Any(Gleich))
        {
            return scope == RuleScope.Project
                ? "Eine Regel für alle Projekte heißt genauso. In diesem Projekt gilt dann nur diese hier."
                : "Eine Projektregel heißt genauso. In diesem Projekt gilt dann nur die Projektregel.";
        }

        return null;
    }

    private string? ScopeLockReason
    {
        get
        {
            if (_profile is null)
                return "Kein Profil geladen.";
            if (!_canEditGlobal)
                return _globalLockReason ?? "Regeln für alle Projekte sind nicht bearbeitbar.";
            return null;
        }
    }

    private void ApplyFilter()
    {
        var vorherigeAuswahl = _selected;

        Rules.Clear();
        foreach (var eintrag in _allRules.Where(
                     r => _selectedFilterOption.Scope is null || r.Scope == _selectedFilterOption.Scope))
        {
            Rules.Add(eintrag);
        }

        Selected = vorherigeAuswahl is not null && Rules.Contains(vorherigeAuswahl)
            ? vorherigeAuswahl
            : Rules.FirstOrDefault();

        Evaluate();
    }

    /// <summary>Alle vergebenen Namen ueber beide Bereiche -- fuer Eindeutigkeit.</summary>
    private IEnumerable<string> AllNames()
        => (_profile?.TextRules ?? Enumerable.Empty<TextRule>()).Concat(_extensions.TextRules).Select(r => r.Name);

    /// <summary>Wie <see cref="AllNames"/>, ohne <paramref name="ausser"/> selbst -- damit eine Regel ihrem eigenen Namen nicht ausweicht.</summary>
    private IEnumerable<string> AllNamesExcept(TextRule ausser)
        => (_profile?.TextRules ?? Enumerable.Empty<TextRule>())
            .Concat(_extensions.TextRules)
            .Where(r => r != ausser)
            .Select(r => r.Name);

    // ----------------------------------------------------- Anlegen/Entfernen

    private void AddRule()
    {
        if (!CanAddRule)
            return;

        var scope = _profile is not null ? RuleScope.Project : RuleScope.Global;
        var name = TextRuleNaming.MakeUnique("regel", AllNames());
        var rule = new TextRule { Name = name, Priority = 60, Generator = "token", Pattern = "" };

        if (scope == RuleScope.Project)
            _profile!.TextRules.Add(rule);
        else
            _extensions.TextRules.Add(rule);

        // Der Name folgt dem Beispielwert, bis er von Hand geaendert wird
        // (siehe TextRuleViewModel.Sample) -- nur bei einer eben erst
        // angelegten Regel, nicht bei einer bestehenden.
        _pendingAutoNameRule = rule;

        BuildRules();
        SelectByName(name, scope);

        _onChanged(scope);
    }

    private void RemoveSelected()
    {
        if (_selected is not { IsEditable: true } regel)
            return;

        var owner = regel.Scope == RuleScope.Project ? _profile!.TextRules : _extensions.TextRules;
        owner.Remove(regel.Rule);

        var scope = regel.Scope;
        BuildRules();

        _onChanged(scope);
    }

    /// <summary>
    /// "Neuer Generator…" neben "Ersetzen durch" (Plan P3d): oeffnet den
    /// Generator-Dialog ueber <see cref="_createGenerator"/> und waehlt bei
    /// Erfolg den neuen Generator fuer die ausgewaehlte Regel aus. Der
    /// Rueckruf selbst schreibt den Generator schon in die richtige Kopie und
    /// baut die Generatorenlisten neu (<see cref="RefreshGeneratorsList"/>
    /// laeuft dabei ueber <c>SettingsViewModel.OnProject/GlobalGeneratorsChanged</c>) --
    /// hier bleibt nur noch die Auswahl an der Regel selbst.
    /// </summary>
    private async Task NewGeneratorAsync()
    {
        if (_createGenerator is null || _selected is not { } regel)
            return;

        var name = await _createGenerator(regel.Scope, regel.Name);
        if (name is null)
            return;

        regel.Generator = Generators.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------- Bereich

    /// <summary>
    /// Wechselt eine Regel in den anderen Bereich (ehemals
    /// <c>SettingsViewModel.MoveRule</c>). Ein eigener Generator, den die
    /// Regel nutzt und den es im Ziel noch nicht gibt, wird mitkopiert statt
    /// verschoben -- die Quelle soll weiter funktionieren, falls dort noch
    /// etwas anderes ihn braucht.
    /// </summary>
    private void ChangeScope(TextRule rule, RuleScope zielBereich)
    {
        if (_profile is null)
            return;

        var quellBereich = zielBereich == RuleScope.Global ? RuleScope.Project : RuleScope.Global;
        var quellRegeln = quellBereich == RuleScope.Project ? _profile.TextRules : _extensions.TextRules;
        var zielRegeln = zielBereich == RuleScope.Project ? _profile.TextRules : _extensions.TextRules;
        var quellGeneratoren = quellBereich == RuleScope.Project ? _profile.Generators : _extensions.Generators;
        var zielGeneratoren = zielBereich == RuleScope.Project ? _profile.Generators : _extensions.Generators;

        if (!quellRegeln.Contains(rule))
            return;

        var neuerName = TextRuleNaming.MakeUnique(
            rule.Name, zielRegeln.Select(r => r.Name).Concat(quellRegeln.Where(r => r != rule).Select(r => r.Name)));

        var generatorKopiert = false;
        if (!Core.Generation.GeneratorRegistry.KnownNames.Contains(rule.Generator, StringComparer.OrdinalIgnoreCase)
            && quellGeneratoren.TryGetValue(rule.Generator, out var generatorSettings)
            && !zielGeneratoren.ContainsKey(rule.Generator))
        {
            zielGeneratoren[rule.Generator] = ProfileStore.DeepCopy(generatorSettings);
            generatorKopiert = true;
        }

        quellRegeln.Remove(rule);
        rule.Name = neuerName;
        zielRegeln.Add(rule);

        BuildRules();
        SelectByName(neuerName, zielBereich);

        if (generatorKopiert)
            _onGeneratorCopied?.Invoke();

        // Beide Bereiche haben sich geaendert -- Quelle verliert, Ziel bekommt
        // die Regel.
        _onChanged(quellBereich);
        _onChanged(zielBereich);
        RefreshGeneratorsList();
    }

    /// <summary>
    /// "Für dieses Projekt anpassen" bei einer gesperrten globalen Regel: eine
    /// gleichnamige Kopie entsteht im Profil und ersetzt die globale Regel
    /// damit fuer dieses Projekt (<see cref="TextRuleViewModel.IsOverridden"/>),
    /// ohne die Erweiterungsdatei anzufassen.
    /// </summary>
    private void AdjustForProject(TextRule globalRule)
    {
        if (_profile is null)
            return;

        var kopie = ProfileStore.DeepCopy(globalRule);
        _profile.TextRules.Add(kopie);

        BuildRules();
        SelectByName(kopie.Name, RuleScope.Project);

        _onChanged(RuleScope.Project);
    }

    // --------------------------------------------------------- Generatoren

    /// <summary>Baut die Generatorenliste neu auf -- nach einer Aenderung an den eigenen Generatoren.</summary>
    public void RefreshGeneratorsList()
    {
        var ziel = GeneratorOption.For(_profileForGenerators, _extensions);

        for (var i = 0; i < ziel.Count; i++)
        {
            if (i < Generators.Count)
            {
                if (!Generators[i].Equals(ziel[i]))
                    Generators[i] = ziel[i];
            }
            else
            {
                Generators.Add(ziel[i]);
            }
        }

        while (Generators.Count > ziel.Count)
            Generators.RemoveAt(Generators.Count - 1);

        foreach (var eintrag in _allRules)
            eintrag.NotifyGeneratorChanged();
    }

    private void OnRuleChanged(RuleScope scope)
    {
        _onChanged(scope);
        Evaluate();
    }

    /// <summary>
    /// Wendet alle zusammengefuehrten Regeln auf den Erprobungstext an und
    /// zeigt, was greift (<see cref="ExtensionLibrary.MergeTextRules"/>) --
    /// dieselbe Vereinigung, die auch ein echter Lauf anwendet.
    /// </summary>
    private void Evaluate()
    {
        Matches.Clear();

        var brauchbare = _extensions.MergeTextRules(_profile?.TextRules ?? new List<TextRule>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();

        if (brauchbare.Count == 0 || string.IsNullOrEmpty(_sampleText))
        {
            MatchSummary = "Keine Muster hinterlegt.";
            return;
        }

        try
        {
            var treffer = _engine.FindMatches(_sampleText, brauchbare);

            foreach (var t in treffer)
                Matches.Add(new MatchPreview(t.Rule.Name, t.Value, ZeileVon(t.Start)));

            MatchSummary = treffer.Count switch
            {
                0 => "Kein Treffer im Erprobungstext.",
                1 => "1 Treffer",
                var n => $"{n} Treffer",
            };
        }
        catch (ConfigurationException ex)
        {
            // Ein halbfertiger Ausdruck waehrend des Tippens ist normal.
            MatchSummary = ex.Message;
        }
    }

    private int ZeileVon(int position)
    {
        var zeile = 1;
        for (var i = 0; i < position && i < _sampleText.Length; i++)
            if (_sampleText[i] == '\n')
                zeile++;
        return zeile;
    }
}

/// <summary>Ein Eintrag der Filter-ComboBox ("Zeigen: …").</summary>
/// <param name="Label">Beschriftung.</param>
/// <param name="Scope"><c>null</c> fuer "alle", sonst der gezeigte Bereich.</param>
public sealed record RuleFilterOption(string Label, RuleScope? Scope);

/// <summary>
/// Eine einzelne Textregel im Formular -- traegt jetzt ihren Bereich
/// (<see cref="Scope"/>) offen, statt in zwei getrennten Listen zu leben
/// (siehe Klassenkopf von <see cref="TextRulesViewModel"/>).
/// </summary>
public sealed class TextRuleViewModel : ObservableObject
{
    private readonly bool _canChangeScope;
    private readonly bool _canAdjustForProject;
    private readonly Profile _profileForGenerators;
    private readonly ExtensionLibrary _extensions;
    private readonly Func<string, string> _makeUniqueName;
    private readonly Action _onChanged;
    private readonly Action<RuleScope> _onChangeScope;
    private readonly Action _onAdjustForProject;
    private readonly Func<string?> _nameHint;

    private PatternMode _mode;
    private string _sample;
    private bool _nameIsAuto;

    public TextRuleViewModel(
        TextRule rule, RuleScope scope, bool isOverridden, bool isEditable,
        bool canChangeScope, string? scopeLockReason, bool canAdjustForProject,
        Profile profileForGenerators, ExtensionLibrary extensions,
        Func<string, string> makeUniqueName, Action onChanged, Action<RuleScope> onChangeScope,
        Action onAdjustForProject, bool nameIsAuto = false, Func<string?>? nameHint = null)
    {
        Rule = rule;
        Scope = scope;
        IsOverridden = isOverridden;
        IsEditable = isEditable;
        _canChangeScope = canChangeScope;
        ScopeLockReason = scopeLockReason;
        _canAdjustForProject = canAdjustForProject;
        _profileForGenerators = profileForGenerators;
        _extensions = extensions;
        _makeUniqueName = makeUniqueName;
        _onChanged = onChanged;
        _onChangeScope = onChangeScope;
        _onAdjustForProject = onAdjustForProject;
        _nameIsAuto = nameIsAuto;
        _nameHint = nameHint ?? (() => null);

        var (mode, sample) = DetermineInitialState(rule.Pattern);
        _mode = mode;
        _sample = sample;

        AdjustForProjectCommand = new RelayCommand(() => _onAdjustForProject(), () => _canAdjustForProject);
    }

    public TextRule Rule { get; }

    /// <summary>Ob diese Regel im Profil oder in der Erweiterungsdatei liegt.</summary>
    public RuleScope Scope { get; }

    public string ScopeLabel => Scope == RuleScope.Project ? "Dieses Projekt" : "Alle Projekte";

    /// <summary>
    /// Ob diese Regel ueberhaupt bearbeitet werden darf: eine Projektregel
    /// immer, eine globale nur, wenn die Erweiterungsdatei beschreibbar ist.
    /// </summary>
    public bool IsEditable { get; }

    /// <summary>Global und nicht beschreibbar -- fuer das Schloss-Symbol in der Liste.</summary>
    public bool IsLocked => Scope == RuleScope.Global && !IsEditable;

    /// <summary>
    /// Eine globale Regel mit gleichem Namen wie eine Projektregel: die
    /// Projektregel gewinnt (<see cref="ExtensionLibrary.MergeTextRules"/>),
    /// diese hier greift in diesem Projekt nicht.
    /// </summary>
    public bool IsOverridden { get; }

    // ------------------------------------------------------------- Bereich

    public bool IsProjectScope
    {
        get => Scope == RuleScope.Project;
        set { if (value && _canChangeScope) _onChangeScope(RuleScope.Project); }
    }

    public bool IsGlobalScope
    {
        get => Scope == RuleScope.Global;
        set { if (value && _canChangeScope) _onChangeScope(RuleScope.Global); }
    }

    /// <summary>Ob sich der Bereich ueberhaupt wechseln laesst: Profil vorhanden, Erweiterung beschreibbar, Regel bearbeitbar.</summary>
    public bool CanChangeScope => _canChangeScope;

    public string? ScopeLockReason { get; }

    /// <summary>"Für dieses Projekt anpassen" bei einer gesperrten, noch nicht ueberschriebenen globalen Regel.</summary>
    public bool CanAdjustForProject => _canAdjustForProject;

    public RelayCommand AdjustForProjectCommand { get; }

    // ------------------------------------------------------------- Felder

    public string Name
    {
        get => Rule.Name;
        set
        {
            if (!IsEditable)
                return;

            var neu = (value ?? "").Trim();
            if (Rule.Name == neu || neu.Length == 0)
                return;

            // Jede Handaenderung beendet den Automatismus -- ab hier folgt der
            // Name nicht mehr dem Beispielwert.
            _nameIsAuto = false;

            Rule.Name = neu;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PatternDescription));
            OnPropertyChanged(nameof(NameHint));
            OnPropertyChanged(nameof(HasNameHint));
            _onChanged();
        }
    }

    /// <summary>Warnung unter der Bezeichnung bei einem Namenszusammenfall, sonst <c>null</c>.</summary>
    public string? NameHint => _nameHint();

    public bool HasNameHint => NameHint is not null;

    /// <summary>Setzt den Namen, ohne <see cref="_nameIsAuto"/> zu beenden -- fuer die automatische Umbenennung ueber <see cref="Sample"/>.</summary>
    private void SetAutoName(string neu)
    {
        if (Rule.Name == neu)
            return;

        Rule.Name = neu;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(NameHint));
        OnPropertyChanged(nameof(HasNameHint));
    }

    public int Priority
    {
        get => Rule.Priority;
        set
        {
            if (!IsEditable || Rule.Priority == value)
                return;
            Rule.Priority = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    /// <summary>
    /// Nummer der Gruppe, deren Inhalt ersetzt wird (Plan P4). 0 bedeutet den
    /// gesamten Treffer, wie schon <see cref="TextRule.CaptureGroup"/> selbst
    /// vorgibt. Aenderungen stossen wie bei <see cref="Priority"/> Erprobung
    /// und Pruefung erneut an -- eine ungueltige Gruppe soll sofort auffallen,
    /// nicht erst nach dem Speichern.
    /// </summary>
    public int CaptureGroup
    {
        get => Rule.CaptureGroup;
        set
        {
            if (!IsEditable || Rule.CaptureGroup == value)
                return;
            Rule.CaptureGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptureGroupWarning));
            OnPropertyChanged(nameof(HasCaptureGroupWarning));
            _onChanged();
        }
    }

    /// <summary>
    /// Warnt, wenn das (gueltige) Muster keine Gruppe mit der Nummer
    /// <see cref="CaptureGroup"/> hat -- die Regel faende dann nie etwas,
    /// weil <c>TextRuleEngine</c> die fehlende Gruppe als "kein Treffer"
    /// wertet, ohne das irgendwo zu melden (siehe <see cref="ProfileValidator"/>,
    /// dieselbe Pruefung). Bei <c>0</c> oder einem ungueltigen Muster ist sie
    /// <c>null</c> -- ein ungueltiges Muster meldet schon <see cref="PatternError"/>.
    /// </summary>
    public string? CaptureGroupWarning
    {
        get
        {
            if (Rule.CaptureGroup <= 0)
                return null;

            Regex regex;
            try
            {
                regex = new Regex(Rule.Pattern);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return Rule.CaptureGroup > regex.GetGroupNumbers().Max()
                ? $"Das Muster hat keine Gruppe {Rule.CaptureGroup} – die Regel fände nie etwas."
                : null;
        }
    }

    public bool HasCaptureGroupWarning => CaptureGroupWarning is not null;

    public bool IgnoreCase
    {
        get => Rule.IgnoreCase;
        set
        {
            if (!IsEditable || Rule.IgnoreCase == value)
                return;
            Rule.IgnoreCase = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public GeneratorOption? Generator
    {
        get => GeneratorOption.Find(_profileForGenerators, Rule.Generator, _extensions);
        set
        {
            if (!IsEditable || value is null || Rule.Generator == value.Name)
                return;
            Rule.Generator = value.Name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PatternDescription));
            RaiseGeneratorScopeHintChanged();
            _onChanged();
        }
    }

    /// <summary>Meldet, dass sich die Generatorenliste geaendert haben koennte (siehe <see cref="TextRulesViewModel.RefreshGeneratorsList"/>).</summary>
    public void NotifyGeneratorChanged()
    {
        OnPropertyChanged(nameof(Generator));
        RaiseGeneratorScopeHintChanged();
    }

    private void RaiseGeneratorScopeHintChanged()
    {
        OnPropertyChanged(nameof(GeneratorScopeHint));
        OnPropertyChanged(nameof(HasGeneratorScopeHint));
    }

    /// <summary>
    /// Hinweis unter der Generator-Auswahl (Plan P4): eine Regel fuer alle
    /// Projekte, deren Generator es bislang nur im Profil gibt, bekommt ihn
    /// erst beim Übernehmen mitkopiert (siehe
    /// <see cref="ExtensionLibrary.AdoptProjectGenerators"/>) -- bis dahin
    /// liefe sie in jedem anderen Projekt ins Leere. <c>null</c> in jedem
    /// anderen Fall (Projektregel, eingebauter oder schon globaler Generator).
    /// </summary>
    public string? GeneratorScopeHint
    {
        get
        {
            if (Scope != RuleScope.Global)
                return null;

            var generatorName = Rule.Generator;
            if (string.IsNullOrWhiteSpace(generatorName))
                return null;

            if (Core.Generation.GeneratorRegistry.KnownNames.Contains(generatorName, StringComparer.OrdinalIgnoreCase))
                return null;

            if (string.Equals(generatorName, "scanText", StringComparison.OrdinalIgnoreCase))
                return null;

            // Schon ein globaler Eintrag (egal ob passend) -- nichts mehr zu
            // kopieren, derselbe Massstab wie AdoptProjectGenerators selbst.
            if (_extensions.Generators.ContainsKey(generatorName))
                return null;

            if (!_profileForGenerators.Generators.ContainsKey(generatorName))
                return null;

            return "Dieser Generator gilt bisher nur in diesem Projekt – beim Übernehmen wird er für alle Projekte mitkopiert.";
        }
    }

    public bool HasGeneratorScopeHint => GeneratorScopeHint is not null;

    // -------------------------------------------------------- Erfassungsmodus

    /// <summary>
    /// "Genau dieser Wert" -- auch dann angezeigt, wenn eigentlich die Form
    /// gewaehlt ist, der Beispielwert aber (noch) keine Ziffern hat: dann
    /// erzeugt <see cref="RegeneratePatternFromSample"/> ohnehin das woertliche
    /// Muster, und ohne diese Lesart stuende gar kein Knopf gewaehlt da, weil
    /// "Alles dieser Form" ausgeblendet ist. Wie <c>AlwaysReplaceViewModel.UseLiteral</c>.
    /// </summary>
    public bool IsExactMode
    {
        get => _mode == PatternMode.Exact || (_mode == PatternMode.Shape && !CanUseShape);
        set { if (value) SetMode(PatternMode.Exact); }
    }

    public bool IsShapeMode
    {
        get => _mode == PatternMode.Shape && CanUseShape;
        set { if (value) SetMode(PatternMode.Shape); }
    }

    public bool IsCustomMode
    {
        get => _mode == PatternMode.Custom;
        set { if (value) SetMode(PatternMode.Custom); }
    }

    private void SetMode(PatternMode mode)
    {
        if (!IsEditable || _mode == mode)
            return;

        _mode = mode;
        OnPropertyChanged(nameof(IsExactMode));
        OnPropertyChanged(nameof(IsShapeMode));
        OnPropertyChanged(nameof(IsCustomMode));

        // Wechsel zu Custom behaelt das Muster unangetastet und macht das
        // Feld bearbeitbar -- nur beim Wechsel zurueck zu Exact/Shape wird es
        // aus dem Beispielwert neu gebildet, und nur, wenn es schon einen
        // gibt: ein eigener Ausdruck hat kein Beispiel, und ein neugieriger
        // Klick auf "Genau dieser Wert" loeschte ihn sonst stillschweigend.
        // Er bleibt stehen, bis tatsaechlich ein Beispielwert getippt wird.
        if (mode != PatternMode.Custom && _sample.Trim().Length > 0)
            RegeneratePatternFromSample();

        RaisePatternChanged();
        _onChanged();
    }

    /// <summary>
    /// Der Beispielwert des Formulars. Ausserhalb des Custom-Modus wird daraus
    /// bei jeder Aenderung das Muster neu erzeugt (Form nur, wenn
    /// <see cref="CanUseShape"/> zutrifft, sonst woertlich); im Custom-Modus
    /// bleibt der Setter wirkungslos -- dort bestimmt das Musterfeld direkt.
    /// </summary>
    public string Sample
    {
        get => _sample;
        set
        {
            if (!IsEditable || _mode == PatternMode.Custom)
                return;

            var neu = value ?? "";
            if (_sample == neu)
                return;

            _sample = neu;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUseShape));
            OnPropertyChanged(nameof(IsExactMode));
            OnPropertyChanged(nameof(IsShapeMode));
            OnPropertyChanged(nameof(ExactDescription));
            OnPropertyChanged(nameof(ShapeDescription));

            RegeneratePatternFromSample();
            RaisePatternChanged();

            // Solange der Name automatisch ist, folgt er dem Beispielwert --
            // eindeutig gemacht ueber den Aufrufer, der beide Bereiche kennt.
            if (_nameIsAuto && neu.Trim().Length > 0)
                SetAutoName(_makeUniqueName(PatternFromSample.SuggestRuleName(neu)));

            _onChanged();
        }
    }

    private void RegeneratePatternFromSample()
    {
        var trimmed = _sample.Trim();
        if (trimmed.Length == 0)
        {
            Rule.Pattern = "";
            return;
        }

        Rule.Pattern = _mode == PatternMode.Shape && CanUseShape
            ? PatternFromSample.Shape(trimmed)!.Pattern
            : PatternFromSample.Literal(trimmed).Pattern;
    }

    /// <summary>Ob der Beispielwert einen Ziffernlauf enthaelt -- sonst waere "Alles dieser Form" nur die woertliche Lesart.</summary>
    public bool CanUseShape => _sample.Trim().Length > 0 && PatternFromSample.Shape(_sample.Trim()) is not null;

    public string ExactDescription => _sample.Trim().Length == 0 ? "" : PatternFromSample.Literal(_sample.Trim()).Description;

    public string ShapeDescription => CanUseShape ? PatternFromSample.Shape(_sample.Trim())!.Description : "";

    /// <summary>Das Musterfeld im Custom-Modus; sonst nur lesend ueber <see cref="Pattern"/>Description-Zeile.</summary>
    public string Pattern
    {
        get => Rule.Pattern;
        set
        {
            if (!IsEditable)
                return;

            var neu = value ?? "";
            if (Rule.Pattern == neu)
                return;

            Rule.Pattern = neu;
            RaisePatternChanged();
            _onChanged();
        }
    }

    private void RaisePatternChanged()
    {
        OnPropertyChanged(nameof(Pattern));
        OnPropertyChanged(nameof(PatternDescription));
        OnPropertyChanged(nameof(PatternError));
        OnPropertyChanged(nameof(HasPatternError));
        OnPropertyChanged(nameof(IsPatternEmpty));
        OnPropertyChanged(nameof(PatternWarning));
        OnPropertyChanged(nameof(HasPatternWarning));
        OnPropertyChanged(nameof(CaptureGroupWarning));
        OnPropertyChanged(nameof(HasCaptureGroupWarning));
    }

    /// <summary>Zeile 2 der Liste: erkannte Beschreibung, sonst das gekuerzte Muster, sonst "noch kein Muster", dazu der Generator.</summary>
    public string PatternDescription
    {
        get
        {
            string beschreibung;
            if (Rule.Pattern.Length == 0)
            {
                beschreibung = "noch kein Muster";
            }
            else
            {
                var erkannt = PatternFromSample.TryRecognize(Rule.Pattern);
                beschreibung = erkannt?.Description
                    ?? (Rule.Pattern.Length > 40 ? Rule.Pattern[..40] + "…" : Rule.Pattern);
            }

            return $"{beschreibung} → {Rule.Generator}";
        }
    }

    /// <summary>Leeres Muster ergibt einen neutralen Hinweis, ein ungueltiger Ausdruck die Meldung von <see cref="Regex"/>.</summary>
    public string? PatternError
    {
        get
        {
            if (Rule.Pattern.Length == 0)
                return "Noch kein Muster angegeben.";

            try
            {
                _ = new Regex(Rule.Pattern);
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }
    }

    public bool HasPatternError => PatternError is not null;

    /// <summary>Ob <see cref="PatternError"/> nur der neutrale "leer"-Hinweis ist -- fuer die Stilklasse (muted statt error) in der Ansicht.</summary>
    public bool IsPatternEmpty => Rule.Pattern.Length == 0;

    /// <summary>Trifft das Muster auch den leeren Text, ist es vermutlich zu weit gefasst.</summary>
    public string? PatternWarning
    {
        get
        {
            if (Rule.Pattern.Length == 0)
                return null;

            try
            {
                return Regex.IsMatch("", Rule.Pattern)
                    ? "Trifft auch leeren Text – vermutlich zu weit gefasst."
                    : null;
            }
            catch (ArgumentException)
            {
                return null; // ungueltiger Ausdruck steht schon in PatternError
            }
        }
    }

    public bool HasPatternWarning => PatternWarning is not null;

    /// <summary>
    /// Ermittelt beim Aufbau den Erfassungsmodus aus dem vorhandenen Muster:
    /// <see cref="PatternFromSample.TryRecognize"/> ergibt Form oder woertlich
    /// samt Beispielwert, ein leeres Muster startet im Formmodus mit leerem
    /// Beispiel, alles andere gilt als eigener Ausdruck (Custom).
    /// </summary>
    private static (PatternMode Mode, string Sample) DetermineInitialState(string pattern)
    {
        if (pattern.Length == 0)
            return (PatternMode.Shape, "");

        var erkannt = PatternFromSample.TryRecognize(pattern);
        if (erkannt is null)
            return (PatternMode.Custom, "");

        return (erkannt.IsShape ? PatternMode.Shape : PatternMode.Exact, erkannt.Sample);
    }
}

/// <param name="Rule">Regel, die gegriffen hat.</param>
/// <param name="Value">Der getroffene Text.</param>
/// <param name="Line">Zeile im Erprobungstext.</param>
public sealed record MatchPreview(string Rule, string Value, int Line);
