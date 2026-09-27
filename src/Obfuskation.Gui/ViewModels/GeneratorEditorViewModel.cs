using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Anlegen und Bearbeiten eines eigenen Generators (Plan P3) -- an drei
/// Stellen erreichbar: dem Dialog "Immer ersetzen" (<see cref="AlwaysReplaceViewModel"/>),
/// dem Reiter "Eigene Generatoren" und dem Regelformular
/// (<see cref="TextRulesViewModel"/>). Alle drei binden dasselbe Ansichtsmodell
/// ein, statt drei eigene Formulare zu pflegen, die auseinanderlaufen koennten.
///
/// Schreibt selbst nichts: das Ergebnis liest der Aufrufer aus
/// <see cref="ResultName"/>, <see cref="ResultSettings"/> und
/// <see cref="ResultScope"/>, nachdem <see cref="ApplyCommand"/> gelaufen ist
/// und <see cref="Confirmed"/> wahr wurde -- dasselbe Muster wie
/// <see cref="AlwaysReplaceViewModel"/> und <see cref="ProfilesViewModel"/>.
/// Fensterfrei wie jedes Ansichtsmodell dieses Projekts: das Fenster entsteht
/// nur in <see cref="Services.DialogService"/>, angestossen ueber
/// <see cref="CloseRequested"/>.
/// </summary>
public sealed class GeneratorEditorViewModel : ObservableObject
{
    /// <summary>Zuordnung Option -&gt; Basistyp, wie in <see cref="FieldRuleViewModel"/> aus der Bibliothek gespiegelt.</summary>
    private static readonly Dictionary<string, string> OptionBaseTypes =
        ProfileValidator.OptionOwnership.ToDictionary(
            eintrag => eintrag.Option, eintrag => eintrag.BaseType, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex NamePattern =
        new(@"^[A-Za-z0-9ÄÖÜäöüß_-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Profile? _profile;
    private readonly ExtensionLibrary _extensions;
    private readonly bool _canEditGlobal;
    private readonly string? _globalLockReason;
    private readonly string? _existingName;
    private readonly RuleScope? _lockScopeTo;
    private readonly IReadOnlyList<string> _users;

    /// <summary>Der Entwurf, auf dem alle Optionsfelder direkt arbeiten -- Kopie eines bestehenden Eintrags, oder ein frischer.</summary>
    private readonly GeneratorSettings _draft;

    /// <summary>
    /// Ein Salt je Dialoglebenszeit: derselbe Seed bei jeder Neuberechnung der
    /// Vorschau, damit das Beispiel beim Tippen nicht bei jedem Tastenschlag
    /// springt (siehe <see cref="GeneratorPreview"/>).
    /// </summary>
    private readonly SeedDeriver _deriver = new(SeedDeriver.CreateSalt());

    private readonly List<GeneratorKindOption> _baseTypes;

    private string _name;
    private string _selectedBaseTypeName;
    private string _sampleInput;
    private string? _previewText;
    private string? _previewError;
    private IReadOnlyList<string> _missingTableNames = Array.Empty<string>();

    /// <summary>
    /// Ob <see cref="SampleInput"/> noch die Vorgabe traegt. Nur dann folgt
    /// das Beispiel der gewaehlten Art (ein Datum fuer die Datumsarten) --
    /// was jemand selbst eingetippt oder markiert hat, bleibt stehen.
    /// </summary>
    private bool _sampleIsDefault;

    /// <summary>
    /// Die Hoechstverschiebung eines schon bestehenden <c>dateShift</c>-Generators
    /// beim Oeffnen, sonst <c>null</c> -- Vergleichswert fuer
    /// <see cref="MaxDaysChangeWarning"/>.
    /// </summary>
    private readonly int? _originalMaxDays;

    public GeneratorEditorViewModel(
        Profile? profile,
        ExtensionLibrary extensions,
        bool canEditGlobal, string? globalLockReason,
        RuleScope initialScope,
        string? existingName = null,
        RuleScope? lockScopeTo = null,
        string? suggestedName = null,
        string? sampleValue = null,
        IReadOnlyList<string>? users = null)
    {
        _profile = profile;
        _extensions = extensions;
        _canEditGlobal = canEditGlobal;
        _globalLockReason = globalLockReason;
        _existingName = existingName;
        _lockScopeTo = lockScopeTo;
        _users = users ?? Array.Empty<string>();

        IsEditMode = existingName is not null;

        // Ohne Profil bleibt nur "alle Projekte" -- unabhaengig davon, was der
        // Aufrufer als initialScope mitgibt.
        Scope = profile is null ? RuleScope.Global : lockScopeTo ?? initialScope;

        CanChooseScope = !IsEditMode && lockScopeTo is null && profile is not null && canEditGlobal;

        var vorhanden = IsEditMode ? FindExisting(existingName!, Scope) : null;
        _draft = vorhanden is not null ? ProfileStore.DeepCopy(vorhanden) : new GeneratorSettings { Type = "token" };

        _selectedBaseTypeName = !string.IsNullOrWhiteSpace(_draft.Type) ? _draft.Type! : "token";
        if (!GeneratorRegistry.KnownNames.Contains(_selectedBaseTypeName, StringComparer.OrdinalIgnoreCase))
            _selectedBaseTypeName = "token";

        // Die Liste zeigt nur die einstellbaren Arten (Plan P2a). Eine per
        // JSON angelegte, hier nicht einstellbare Art (etwa "numericId")
        // bekommt trotzdem einen Eintrag angehaengt, damit das Bearbeiten
        // eines solchen Generators nicht still auf "token" zurueckfaellt.
        _baseTypes = new List<GeneratorKindOption>(GeneratorKindOption.Configurable);
        if (!_baseTypes.Any(o => string.Equals(o.Name, _selectedBaseTypeName, StringComparison.OrdinalIgnoreCase)))
            _baseTypes.Add(GeneratorKindOption.ForUnconfigurable(_selectedBaseTypeName));

        _name = existingName ?? SuggestUniqueName(ProfileScaffolder.ToGeneratorKey(suggestedName ?? "generator"));
        _sampleIsDefault = string.IsNullOrWhiteSpace(sampleValue);
        _sampleInput = _sampleIsDefault ? DefaultSampleFor(_selectedBaseTypeName) : sampleValue!;

        if (vorhanden is not null && IsBaseType("dateShift"))
            _originalMaxDays = EffectiveMaxDays(vorhanden.MaxDays);

        Tables = new ObservableCollection<ExpressionTableViewModel>();
        if (_draft.Tables is not null)
        {
            foreach (var (tableName, values) in _draft.Tables)
                Tables.Add(NewTableViewModel(tableName, values));
        }

        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        AddTableCommand = new RelayCommand(() => AddTable(SuggestTableName(), Array.Empty<string>()));
        ApplyTemplateCommand = new RelayCommand<string>(ApplyTemplate);

        RefreshTableUsage();
        RefreshPreview();
        RefreshErrors();
    }

    private GeneratorSettings? FindExisting(string name, RuleScope scope)
    {
        var dict = scope == RuleScope.Project ? _profile?.Generators : _extensions.Generators;
        return dict is not null && dict.TryGetValue(name, out var settings) ? settings : null;
    }

    private Dictionary<string, GeneratorSettings>? TargetDictionary()
        => Scope == RuleScope.Project ? _profile?.Generators : _extensions.Generators;

    private string SuggestUniqueName(string baseKey)
    {
        var vergeben = (TargetDictionary()?.Keys ?? Enumerable.Empty<string>())
            .Concat(GeneratorRegistry.KnownNames)
            .Append("scanText");
        return TextRuleNaming.MakeUnique(baseKey.Length == 0 ? "generator" : baseKey, vergeben);
    }

    // ------------------------------------------------------------ Allgemein

    public bool IsEditMode { get; }

    public string WindowTitle => IsEditMode ? $"Generator „{_existingName}“ bearbeiten" : "Neuer Generator";

    public string ApplyLabel => IsEditMode ? "Übernehmen" : "Anlegen";

    // ----------------------------------------------------------------- Name

    public string Name
    {
        get => _name;
        set
        {
            if (!CanRename)
                return;

            var neu = value ?? "";
            if (!SetProperty(ref _name, neu))
                return;

            RaiseNameChanged();
        }
    }

    /// <summary>
    /// Gesetzt, wenn der Name leer ist, unerlaubte Zeichen traegt, einem
    /// eingebauten Generator oder <c>scanText</c> entspricht, oder im
    /// gewaehlten Bereich schon vergeben ist (ausser an diesen Eintrag selbst
    /// im Bearbeiten-Modus). <c>null</c>, wenn der Name gueltig ist.
    /// </summary>
    public string? NameError
    {
        get
        {
            var name = _name.Trim();

            if (name.Length == 0)
                return "Der Name darf nicht leer sein.";

            if (!NamePattern.IsMatch(name))
                return "Erlaubt sind Buchstaben, Ziffern, „_“ und „-“.";

            if (GeneratorRegistry.KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                return $"„{name}“ ist ein eingebauter Generator.";

            if (string.Equals(name, "scanText", StringComparison.OrdinalIgnoreCase))
                return "„scanText“ ist eine Behandlung, kein Generatorname.";

            var ziel = TargetDictionary();
            var istEigenerName = IsEditMode && string.Equals(name, _existingName, StringComparison.OrdinalIgnoreCase);

            if (!istEigenerName && ziel is not null && ziel.ContainsKey(name))
                return $"„{name}“ ist im gewählten Bereich schon vergeben.";

            return null;
        }
    }

    public bool HasNameError => NameError is not null;

    /// <summary>
    /// Warnt ohne zu sperren, wenn derselbe Name im jeweils anderen Bereich
    /// bereits existiert: eine Projektregel mit diesem Namen wuerde die
    /// gleichnamige globale Fassung fuer dieses Projekt verdraengen (siehe
    /// <see cref="ProfileValidator"/>, Warnung "ueberschreibt den gleichnamigen
    /// Eintrag aus der Erweiterungsdatei").
    /// </summary>
    public string? NameHint
    {
        get
        {
            if (NameError is not null)
                return null;

            var name = _name.Trim();
            return Scope == RuleScope.Project && _extensions.Generators.ContainsKey(name)
                ? "In diesem Projekt gilt dann dieser Generator, nicht der für alle Projekte."
                : null;
        }
    }

    public bool HasNameHint => NameHint is not null;

    private void RaiseNameChanged()
    {
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(NameHint));
        OnPropertyChanged(nameof(HasNameHint));
        RefreshErrors();
    }

    // ------------------------------------------------------------------ Art

    /// <summary>
    /// Die einstellbaren Grundlagen, aus denen ein eigener Generator seine
    /// Art waehlt (Plan P2a) -- nicht alle eingebauten Generatoren, siehe
    /// <see cref="GeneratorKindOption"/>.
    /// </summary>
    public IReadOnlyList<GeneratorKindOption> BaseTypes => _baseTypes;

    public GeneratorKindOption SelectedBaseType
    {
        get => BaseTypes.FirstOrDefault(o => string.Equals(o.Name, _selectedBaseTypeName, StringComparison.OrdinalIgnoreCase))
               ?? BaseTypes.First(o => o.Name == "token");
        set
        {
            if (!CanChangeType || value is null
                || string.Equals(_selectedBaseTypeName, value.Name, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedBaseTypeName = value.Name;

            if (_sampleIsDefault && !string.Equals(_sampleInput, DefaultSampleFor(value.Name), StringComparison.Ordinal))
            {
                _sampleInput = DefaultSampleFor(value.Name);
                OnPropertyChanged(nameof(SampleInput));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(KindHint));
            OnPropertyChanged(nameof(HasKindHint));
            RaiseOptionVisibilityChanged();
            RefreshPreview();
            RefreshErrors();
        }
    }

    /// <summary>
    /// Hinweis fuer eine nicht einstellbare Art (etwa ein per JSON angelegtes
    /// "numericId"): sie hat hier keine Einstellungen, sondern trennt nur die
    /// Ersetzungstabelle vom eingebauten Generator gleichen Basistyps.
    /// </summary>
    public string? KindHint => SelectedBaseType.Example.Length == 0
        ? "Diese Art hat hier keine Einstellungen – sie trennt nur die Ersetzungstabelle."
        : null;

    public bool HasKindHint => KindHint is not null;

    // ------------------------------------------------- Sperren beim Bearbeiten

    /// <summary>Ob dieser Generator schon verwendet wird -- dann lassen sich Name und Art nicht mehr aendern.</summary>
    public bool HasUsers => _users.Count > 0;

    public bool CanRename => !HasUsers;

    public bool CanChangeType => !HasUsers;

    public string? UsersHint => HasUsers
        ? $"Wird verwendet von {string.Join(", ", _users)}: Name und Art lassen sich erst ändern, " +
          "wenn nichts mehr darauf verweist."
        : null;

    // ------------------------------------------------------------- Optionen

    private bool IsBaseType(string baseType) => string.Equals(_selectedBaseTypeName, baseType, StringComparison.OrdinalIgnoreCase);

    public bool ShowPrefix => IsBaseType(OptionBaseTypes["prefix"]);

    public string? Prefix
    {
        get => _draft.Prefix;
        set => SetOption(s => s.Prefix = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool ShowPlaceholder => IsBaseType(OptionBaseTypes["placeholder"]);

    public string? Placeholder
    {
        get => _draft.Placeholder;
        set => SetOption(s => s.Placeholder = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool ShowDateRange => IsBaseType(OptionBaseTypes["from"]);

    public string? From
    {
        get => _draft.From;
        set => SetOption(s => s.From = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string? To
    {
        get => _draft.To;
        set => SetOption(s => s.To = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool ShowGranularity => IsBaseType(OptionBaseTypes["granularity"]);

    public IReadOnlyList<GranularityOption> GranularityOptions { get; } = GranularityOption.All;

    public GranularityOption SelectedGranularity
    {
        get => GranularityOption.For(_draft.Granularity);
        set => SetOption(s => s.Granularity = value.Value == GranularityOption.Default ? null : value.Value);
    }

    public bool ShowPatternMask => IsBaseType(OptionBaseTypes["pattern"]);

    public string? Pattern
    {
        get => _draft.Pattern;
        set => SetOption(s => s.Pattern = string.IsNullOrEmpty(value) ? null : value);
    }

    public bool ShowWordlist => IsBaseType(OptionBaseTypes["values"]);

    public string ValuesText
    {
        get => _draft.Values is { } values ? string.Join('\n', values) : "";
        set => SetOption(s => s.Values = SplitValues(value));
    }

    public bool ShowPartialMask => IsBaseType(OptionBaseTypes["keepFirst"]);

    public int KeepFirst
    {
        get => _draft.KeepFirst;
        set => SetOption(s => s.KeepFirst = value);
    }

    public int KeepLast
    {
        get => _draft.KeepLast;
        set => SetOption(s => s.KeepLast = value);
    }

    public string MaskChar
    {
        get => _draft.MaskChar ?? "*";
        // Der angezeigte Stern ist die Vorgabe, kein gesetzter Wert -- wie in
        // FieldRuleViewModel wird er wieder auf "nicht gesetzt" zurueckgefuehrt.
        set => SetOption(s => s.MaskChar = string.IsNullOrEmpty(value) || value == "*" ? null : value);
    }

    // ------------------------------------------------------- Verschiebung

    public bool ShowMaxDays => IsBaseType(OptionBaseTypes["maxDays"]);

    /// <summary>
    /// Die Hoechstverschiebung in Tagen. Ein gespeichertes <c>0</c> wirkt im
    /// Generator wie die Vorgabe und wird darum auch so angezeigt.
    /// </summary>
    public int MaxDays
    {
        get => EffectiveMaxDays(_draft.MaxDays);
        set
        {
            SetOption(s => s.MaxDays = value);
            OnPropertyChanged(nameof(MaxDaysChangeWarning));
            OnPropertyChanged(nameof(HasMaxDaysChangeWarning));
        }
    }

    /// <summary>
    /// Warnt beim Bearbeiten, sobald die Hoechstverschiebung vom
    /// gespeicherten Wert abweicht. Die Datumsverschiebung hat keine
    /// Ersetzungstabelle, sie rechnet nur zurueck -- und die Verschiebung
    /// selbst wird aus diesem Wert abgeleitet. Nach einer Aenderung liessen
    /// sich frueher erzeugte Pseudodaten nicht mehr korrekt zurueckfuehren,
    /// und nichts im Lauf wuerde das bemerken.
    /// </summary>
    public string? MaxDaysChangeWarning
        => ShowMaxDays && _originalMaxDays is { } bisher && MaxDays != bisher
            ? $"Bisher {bisher} Tage. Eine Änderung ergibt eine andere Verschiebung: Pseudodateien, " +
              "die mit dem bisherigen Wert entstanden sind, lassen sich danach nicht mehr korrekt " +
              "zurückführen. Dieser Generator hat keine Ersetzungstabelle, er rechnet nur zurück."
            : null;

    public bool HasMaxDaysChangeWarning => MaxDaysChangeWarning is not null;

    private static int EffectiveMaxDays(int maxDays) => maxDays > 0 ? maxDays : GeneratorSettings.DefaultMaxDays;

    // ------------------------------------------------------------- Ausdruck

    public bool ShowExpression => IsBaseType(OptionBaseTypes["expression"]);

    public string? Expression
    {
        get => _draft.Expression;
        set
        {
            var neu = string.IsNullOrEmpty(value) ? null : value;
            if (_draft.Expression == neu)
                return;

            _draft.Expression = neu;
            OnPropertyChanged();
            RefreshTableUsage();
            RefreshPreview();
            RefreshErrors();
        }
    }

    /// <summary>Die Tabellen des Entwurfs, in der Reihenfolge, in der sie im Dialog stehen.</summary>
    public ObservableCollection<ExpressionTableViewModel> Tables { get; private set; }

    public RelayCommand AddTableCommand { get; private set; }

    /// <summary>Die drei Vorlagen fuer den Knopf "Beispiel einsetzen ▾".</summary>
    public IReadOnlyList<ExpressionTemplate> ExpressionTemplates => ExpressionTemplate.All;

    public RelayCommand<string> ApplyTemplateCommand { get; private set; }

    /// <summary>Im Ausdruck verwendete, aber unter <see cref="Tables"/> fehlende Tabellennamen.</summary>
    public IReadOnlyList<string> MissingTableNames
    {
        get => _missingTableNames;
        private set => SetProperty(ref _missingTableNames, value);
    }

    public bool HasMissingTables => MissingTableNames.Count > 0;

    /// <summary>
    /// Wie <see cref="MissingTableNames"/>, aber mit einem eigenen
    /// "Anlegen"-Befehl je Name -- fuer die Anzeige im Dialog (siehe
    /// <see cref="MissingTableHint"/>).
    /// </summary>
    public IReadOnlyList<MissingTableHint> MissingTableHints
        => MissingTableNames.Select(name => new MissingTableHint(name, () => AddTable(name, Array.Empty<string>()))).ToList();

    private ExpressionTableViewModel NewTableViewModel(string name, IReadOnlyList<string> values)
        => new(name, values, onChanged: SyncTablesToDraft, remove: RemoveTable);

    private void AddTable(string name, IReadOnlyList<string> values)
    {
        Tables.Add(NewTableViewModel(name, values));
        SyncTablesToDraft();
    }

    private void RemoveTable(ExpressionTableViewModel table)
    {
        Tables.Remove(table);
        SyncTablesToDraft();
    }

    /// <summary>
    /// Der Name fuer den naechsten Knopfdruck auf "+ Tabelle": zuerst ein im
    /// Ausdruck verwendeter, aber fehlender Tabellenname, sonst "tabelle",
    /// eindeutig gemacht.
    /// </summary>
    private string SuggestTableName()
    {
        var fehlend = MissingTableNames.FirstOrDefault();
        if (fehlend is not null)
            return fehlend;

        var vergeben = new HashSet<string>(Tables.Select(t => t.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        if (!vergeben.Contains("tabelle"))
            return "tabelle";

        var i = 2;
        while (vergeben.Contains($"tabelle{i}"))
            i++;
        return $"tabelle{i}";
    }

    /// <summary>
    /// Ersetzt Ausdruck und Tabellen durch eine Vorlage (Plan P2b, Knopf
    /// "Beispiel einsetzen ▾"). Unbekannte Schluessel bewirken nichts.
    /// </summary>
    private void ApplyTemplate(string? key)
    {
        var vorlage = ExpressionTemplate.Find(key);
        if (vorlage is null)
            return;

        _draft.Expression = vorlage.Expression;
        OnPropertyChanged(nameof(Expression));

        Tables.Clear();
        foreach (var tabelle in vorlage.Tables)
            Tables.Add(NewTableViewModel(tabelle.Name, tabelle.Values));

        SyncTablesToDraft();
    }

    /// <summary>
    /// Baut <see cref="GeneratorSettings.Tables"/> aus <see cref="Tables"/>
    /// neu auf (in deren Reihenfolge) und stoesst die davon abhaengigen
    /// Neuberechnungen an. Leere Namen werden ausgelassen -- sie meldet
    /// <see cref="RefreshErrors"/> als eigenen Befund.
    /// </summary>
    private void SyncTablesToDraft()
    {
        _draft.Tables = Tables.Count == 0 ? null : BuildTablesDictionary();

        RefreshTableUsage();
        RefreshPreview();
        RefreshErrors();
    }

    private Dictionary<string, List<string>> BuildTablesDictionary()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var tabelle in Tables)
        {
            var name = tabelle.Name.Trim();
            if (name.Length > 0)
                result[name] = tabelle.Values;
        }
        return result;
    }

    /// <summary>
    /// Markiert jede Tabelle, ob sie im aktuellen Ausdruck vorkommt, und
    /// ermittelt die verwendeten, aber fehlenden Tabellennamen. Ein
    /// fehlerhafter Ausdruck zaehlt dabei als "verweist auf nichts" -- der
    /// Parserfehler selbst erscheint ueber <see cref="RefreshErrors"/>.
    /// </summary>
    private void RefreshTableUsage()
    {
        var referenced = GeneratorExpression.TryParse(_draft.Expression, out var expression, out _)
            ? new HashSet<string>(expression!.TableReferences, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tabelle in Tables)
            tabelle.UpdateUsage(referenced.Contains(tabelle.Name.Trim()));

        MissingTableNames = referenced
            .Where(name => !Tables.Any(t => string.Equals(t.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        OnPropertyChanged(nameof(HasMissingTables));
        OnPropertyChanged(nameof(MissingTableHints));
    }

    private void SetOption(Action<GeneratorSettings> anwenden, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        anwenden(_draft);
        OnPropertyChanged(propertyName);
        RefreshPreview();
        RefreshErrors();
    }

    private void RaiseOptionVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowPrefix));
        OnPropertyChanged(nameof(Prefix));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(Placeholder));
        OnPropertyChanged(nameof(ShowDateRange));
        OnPropertyChanged(nameof(From));
        OnPropertyChanged(nameof(To));
        OnPropertyChanged(nameof(ShowGranularity));
        OnPropertyChanged(nameof(SelectedGranularity));
        OnPropertyChanged(nameof(ShowPatternMask));
        OnPropertyChanged(nameof(Pattern));
        OnPropertyChanged(nameof(ShowWordlist));
        OnPropertyChanged(nameof(ValuesText));
        OnPropertyChanged(nameof(ShowPartialMask));
        OnPropertyChanged(nameof(KeepFirst));
        OnPropertyChanged(nameof(KeepLast));
        OnPropertyChanged(nameof(MaskChar));
        OnPropertyChanged(nameof(ShowExpression));
        OnPropertyChanged(nameof(Expression));
        OnPropertyChanged(nameof(ShowMaxDays));
        OnPropertyChanged(nameof(MaxDays));
        OnPropertyChanged(nameof(MaxDaysChangeWarning));
        OnPropertyChanged(nameof(HasMaxDaysChangeWarning));
    }

    private static List<string> SplitValues(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(zeile => zeile.Length > 0)
            .ToList();

    // -------------------------------------------------------------- Bereich

    public RuleScope Scope { get; private set; }

    /// <summary>"Dieses Projekt"/"Alle Projekte" als Text -- fuer die Anzeige im Bearbeiten-Modus, der den Bereich nicht mehr wechseln laesst.</summary>
    public string ScopeLabel => Scope == RuleScope.Project ? "Dieses Projekt" : "Alle Projekte";

    public bool IsProjectScope
    {
        get => Scope == RuleScope.Project;
        set { if (value) SetScope(RuleScope.Project); }
    }

    public bool IsGlobalScope
    {
        get => Scope == RuleScope.Global;
        set { if (value) SetScope(RuleScope.Global); }
    }

    /// <summary>Ob sich der Bereich ueberhaupt wechseln laesst -- nur beim Anlegen, ohne feste Vorgabe, mit Profil und beschreibbarer Erweiterung.</summary>
    public bool CanChooseScope { get; }

    /// <summary>Erklaert, wenn <see cref="CanChooseScope"/> falsch ist -- sonst <c>null</c> (etwa im Bearbeiten-Modus, der den Bereich nur als Text zeigt).</summary>
    public string? ScopeHint
    {
        get
        {
            if (CanChooseScope)
                return null;

            if (_profile is null)
                return "Kein Projekt geöffnet.";

            if (_lockScopeTo == RuleScope.Global)
                return "Eine Regel für alle Projekte braucht einen Generator für alle Projekte.";

            if (!_canEditGlobal)
                return _globalLockReason;

            return null;
        }
    }

    public bool HasScopeHint => ScopeHint is not null;

    private void SetScope(RuleScope scope)
    {
        if (!CanChooseScope || Scope == scope)
            return;

        Scope = scope;
        OnPropertyChanged(nameof(IsProjectScope));
        OnPropertyChanged(nameof(IsGlobalScope));
        RaiseNameChanged();
    }

    // -------------------------------------------------------------- Vorschau

    /// <summary>Der Beispielwert, gegen den die Vorschau erzeugt wird.</summary>
    public string SampleInput
    {
        get => _sampleInput;
        set
        {
            var neu = value ?? "";
            if (!SetProperty(ref _sampleInput, neu))
                return;

            _sampleIsDefault = false;
            RefreshPreview();
        }
    }

    public string? PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public string? PreviewError
    {
        get => _previewError;
        private set => SetProperty(ref _previewError, value);
    }

    /// <summary>
    /// Nur wahr, wenn <see cref="Errors"/> leer ist: Ein Ausdrucksfehler etwa
    /// erscheint sonst gleich zweimal -- einmal hier, einmal in der
    /// Fehlerliste, mit derselben Meldung.
    /// </summary>
    public bool HasPreviewError => _previewError is not null && !HasErrors;

    /// <summary>Wie viele Beispiele die Vorschau auf einmal zeigt (Plan P2b) -- fuer alle Arten gleich.</summary>
    private const int PreviewExampleCount = 3;

    private void RefreshPreview()
    {
        if (GeneratorPreview.TryExamples(
                NameForPreview, BuildResult(), _sampleInput, _deriver, PreviewExampleCount,
                out var beispiele, out var fehler))
        {
            // Eine Zeile je Beispiel statt eines Trennzeichens: Werte wie
            // "HH-OB 255" tragen selbst Leerzeichen und wuerden sonst mitten im
            // Wert umbrechen, und ein "·" koennte selbst Teil eines Werts sein.
            PreviewText = string.Join('\n', beispiele);
            PreviewError = null;
        }
        else
        {
            PreviewText = null;
            PreviewError = fehler;
        }

        OnPropertyChanged(nameof(HasPreviewError));
    }

    /// <summary>
    /// Das vorgegebene Beispiel je Art: ein Datum fuer die drei Datumsarten,
    /// die mit "Beispiel 4711" nur eine Fehlermeldung zeigen koennten, sonst
    /// ein Wert mit Buchstaben und Ziffern.
    /// </summary>
    private static string DefaultSampleFor(string baseType)
        => baseType.ToLowerInvariant() switch
        {
            "dateshift" or "daterange" or "dategeneralize" => "15.03.2024",
            _ => "Beispiel 4711",
        };

    /// <summary>Ein Name fuer das Wegwerf-Profil der Vorschau -- auch brauchbar, solange <see cref="Name"/> (noch) ungueltig ist.</summary>
    private string NameForPreview => _name.Trim().Length > 0 ? _name.Trim() : "vorschau";

    // -------------------------------------------------------------- Pruefung

    public ObservableCollection<string> Errors { get; } = new();

    public bool HasErrors => Errors.Count > 0;

    /// <summary>Ob "Anlegen"/"Übernehmen" gerade etwas taete -- weder ein Namensfehler noch ein Pruefungsbefund.</summary>
    public bool CanApply => NameError is null && Errors.Count == 0;

    private void RefreshErrors()
    {
        Errors.Clear();

        // Doppelte oder leere Tabellennamen kann ein Woerterbuch nicht halten
        // -- die Pruefung dafuer uebernimmt dieses Ansichtsmodell selbst, vor
        // jedem Aufruf von BuildResult (siehe AddTableNameErrors).
        if (ShowExpression)
            AddTableNameErrors();

        var name = _name.Trim();
        if (name.Length > 0)
        {
            var profil = new Profile();
            profil.Generators[name] = BuildResult();

            var pathPrefix = $"generators.{name}";
            foreach (var issue in ProfileValidator.Validate(profil, ExtensionLibrary.Empty))
            {
                if (issue.Severity == ValidationSeverity.Error && issue.Path.StartsWith(pathPrefix, StringComparison.Ordinal))
                    Errors.Add(issue.Message);
            }
        }

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasPreviewError));
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private void AddTableNameErrors()
    {
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tabelle in Tables)
        {
            var name = tabelle.Name.Trim();
            if (name.Length == 0)
            {
                Errors.Add("Ein Tabellenname darf nicht leer sein.");
                continue;
            }

            if (!gesehen.Add(name))
                Errors.Add($"Der Tabellenname „{name}“ ist mehrfach vergeben.");
        }
    }

    /// <summary>
    /// Baut das Ergebnis aus dem Entwurf: die gewaehlte Art, dazu nur die
    /// Optionen, die zu dieser Art gehoeren (<see cref="ProfileValidator.OptionOwnership"/>)
    /// -- alles andere (etwa ein Praefix aus einer fruehreren Wahl von
    /// "token") wird verworfen; ein fremdes <c>maxDays</c> faellt auf die
    /// Vorgabe zurueck. Felder ausserhalb dieser Zuordnung (<c>formats</c>,
    /// <c>country</c>, <c>domain</c>) bleiben aus dem Entwurf erhalten, damit
    /// von Hand gepflegtes JSON beim Bearbeiten nicht verloren geht.
    /// </summary>
    private GeneratorSettings BuildResult()
    {
        var result = ProfileStore.DeepCopy(_draft);
        result.Type = _selectedBaseTypeName;

        foreach (var (option, baseType) in ProfileValidator.OptionOwnership)
        {
            if (!string.Equals(baseType, _selectedBaseTypeName, StringComparison.OrdinalIgnoreCase))
                ClearOption(result, option);
        }

        return result;
    }

    private static void ClearOption(GeneratorSettings settings, string option)
    {
        switch (option)
        {
            case "prefix": settings.Prefix = null; break;
            case "placeholder": settings.Placeholder = null; break;
            case "from": settings.From = null; break;
            case "to": settings.To = null; break;
            case "granularity": settings.Granularity = null; break;
            case "pattern": settings.Pattern = null; break;
            case "values": settings.Values = null; break;
            case "keepFirst": settings.KeepFirst = 0; break;
            case "keepLast": settings.KeepLast = 0; break;
            case "maskChar": settings.MaskChar = null; break;
            case "expression": settings.Expression = null; break;
            case "tables": settings.Tables = null; break;
            case "maxDays": settings.MaxDays = GeneratorSettings.DefaultMaxDays; break;
        }
    }

    // --------------------------------------------------- Befehle und Ergebnis

    public RelayCommand ApplyCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>Ob "Anlegen"/"Übernehmen" tatsaechlich ausgefuehrt wurde. Nur danach aussagekraeftig.</summary>
    public bool Confirmed { get; private set; }

    public string ResultName { get; private set; } = "";

    public GeneratorSettings ResultSettings { get; private set; } = new();

    public RuleScope ResultScope { get; private set; }

    public event Action? CloseRequested;

    private void Apply()
    {
        if (!CanApply)
            return;

        ResultName = _name.Trim();
        ResultSettings = BuildResult();
        ResultScope = Scope;
        Confirmed = true;

        CloseRequested?.Invoke();
    }
}
