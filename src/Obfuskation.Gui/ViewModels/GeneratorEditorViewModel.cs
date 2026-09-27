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

    private string _name;
    private string _selectedBaseTypeName;
    private string _sampleInput;
    private string? _previewText;
    private string? _previewError;

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

        _name = existingName ?? SuggestUniqueName(ProfileScaffolder.ToGeneratorKey(suggestedName ?? "generator"));
        _sampleInput = string.IsNullOrWhiteSpace(sampleValue) ? "Beispiel 4711" : sampleValue!;

        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());

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

    /// <summary>Die eingebauten Basistypen, aus denen ein eigener Generator seine Grundlage waehlt.</summary>
    public IReadOnlyList<GeneratorOption> BaseTypes { get; } = GeneratorOption.BuiltIn;

    public GeneratorOption SelectedBaseType
    {
        get => BaseTypes.FirstOrDefault(o => string.Equals(o.Name, _selectedBaseTypeName, StringComparison.OrdinalIgnoreCase))
               ?? BaseTypes.First(o => o.Name == "token");
        set
        {
            if (!CanChangeType || value is null
                || string.Equals(_selectedBaseTypeName, value.Name, StringComparison.OrdinalIgnoreCase))
                return;

            _selectedBaseTypeName = value.Name;
            OnPropertyChanged();
            RaiseOptionVisibilityChanged();
            RefreshPreview();
            RefreshErrors();
        }
    }

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

    public bool HasPreviewError => _previewError is not null;

    private void RefreshPreview()
    {
        if (GeneratorPreview.TryExample(NameForPreview, BuildResult(), _sampleInput, _deriver, out var beispiel, out var fehler))
        {
            PreviewText = beispiel;
            PreviewError = null;
        }
        else
        {
            PreviewText = null;
            PreviewError = fehler;
        }

        OnPropertyChanged(nameof(HasPreviewError));
    }

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
        ApplyCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Baut das Ergebnis aus dem Entwurf: die gewaehlte Art, dazu nur die
    /// Optionen, die zu dieser Art gehoeren (<see cref="ProfileValidator.OptionOwnership"/>)
    /// -- alles andere (etwa ein Praefix aus einer fruehreren Wahl von
    /// "token") wird verworfen. Felder ausserhalb dieser Zuordnung
    /// (<c>maxDays</c>, <c>formats</c>, <c>country</c>, <c>domain</c>) bleiben
    /// aus dem Entwurf erhalten, damit von Hand gepflegtes JSON beim
    /// Bearbeiten nicht verloren geht.
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
