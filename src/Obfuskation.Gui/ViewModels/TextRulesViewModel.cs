using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Textregeln eines Profils, mit einem Erprobungsfeld.
///
/// Das Erproben ist hier kein Beiwerk: ein zu weit gefasstes Muster ersetzt
/// harmlose Werte und beschaedigt die Testdaten, ein zu enges laesst Echtdaten
/// stehen. Beides faellt beim Betrachten des Musters nicht auf, beim Erproben
/// an echtem Text sofort.
/// </summary>
public sealed class TextRulesViewModel : ObservableObject
{
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;
    private readonly Action _onChanged;
    private readonly TextRuleEngine _engine = new();

    private TextRuleViewModel? _selected;
    private string _sampleText =
        "Herr Max Mustermann, IBAN DE02120300000000202051, erreichbar unter\n"
        + "max.mustermann@beispiel.de oder +49 30 12345678.\n"
        + "Rechnung 2024-0815 vom 15.03.2024, Betrag 1.234,56 EUR.";

    private string _matchSummary = "";

    public TextRulesViewModel(Profile profile, ExtensionLibrary extensions, Action onChanged)
    {
        _profile = profile;
        _extensions = extensions;
        _onChanged = onChanged;

        foreach (var rule in profile.TextRules)
            Rules.Add(new TextRuleViewModel(profile, extensions, rule, OnRuleChanged));

        // Erweiterungsregeln nur lesend anhaengen -- gepflegt wird die Datei im
        // Texteditor (siehe ExtensionLibrary). Eine gleichnamige Profilregel
        // ersetzt eine Erweiterungsregel vollstaendig, statt zusaetzlich zu
        // gelten (dieselbe Regel wie ObfuscationEngine.MergeTextRules); sie
        // erscheint dann nicht doppelt.
        var profilnamen = new HashSet<string>(
            profile.TextRules.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var rule in extensions.TextRules.Where(rule => !profilnamen.Contains(rule.Name)))
            Rules.Add(new TextRuleViewModel(profile, extensions, rule, OnRuleChanged, isExtensionRule: true));

        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove, () => _selected is { IsExtensionRule: false });

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.For(profile, extensions));

        // Erst jetzt: der Setter meldet dem Entfernen-Befehl seine
        // Verfuegbarkeit, den es vorher noch nicht gab.
        Selected = Rules.FirstOrDefault();

        Evaluate();
    }

    public ObservableCollection<TextRuleViewModel> Rules { get; } = new();
    public ObservableCollection<GeneratorOption> Generators { get; }
    public ObservableCollection<MatchPreview> Matches { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public TextRuleViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelected));
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelected => _selected is not null;

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

    private void Add()
    {
        var rule = new TextRule
        {
            Name = NextName(),
            Priority = 50,
            Pattern = "",
            Generator = "token",
        };

        _profile.TextRules.Add(rule);

        var viewModel = new TextRuleViewModel(_profile, _extensions, rule, OnRuleChanged);
        Rules.Add(viewModel);
        Selected = viewModel;

        OnRuleChanged();
    }

    private void Remove()
    {
        if (_selected is null)
            return;

        _profile.TextRules.Remove(_selected.Rule);
        Rules.Remove(_selected);
        Selected = Rules.FirstOrDefault();

        OnRuleChanged();
    }

    private string NextName()
    {
        var nummer = 1;
        while (_profile.TextRules.Any(r =>
                   string.Equals(r.Name, $"regel{nummer}", StringComparison.OrdinalIgnoreCase)))
        {
            nummer++;
        }
        return $"regel{nummer}";
    }

    private void OnRuleChanged()
    {
        _onChanged();
        Evaluate();
    }

    /// <summary>
    /// Wendet alle Regeln auf den Erprobungstext an und zeigt, was greift.
    /// Verwendet dieselbe Aufloesung ueberlappender Treffer wie der echte Lauf,
    /// damit hier nichts anderes herauskommt als dort.
    ///
    /// Geprueft werden Profil- und Erweiterungsregeln zusammen -- dieselbe
    /// Vereinigung wie <c>ObfuscationEngine.MergeTextRules</c>, denn beide
    /// nutzen <see cref="ExtensionLibrary.MergeTextRules"/>: eine
    /// Erweiterungsregel gilt, ausser eine gleichnamige Profilregel ersetzt sie
    /// vollstaendig. Das Erprobungsfeld soll pruefen koennen, was ein echter
    /// Lauf tatsaechlich findet, nicht nur den Ausschnitt im Profil.
    /// </summary>
    private void Evaluate()
    {
        Matches.Clear();

        var brauchbare = _extensions.MergeTextRules(_profile.TextRules)
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

/// <summary>
/// Eine einzelne Textregel im Formular.
///
/// Eine Erweiterungsregel (<see cref="IsExtensionRule"/>) ist nur lesend: die
/// Setter tun dann nichts. Gepflegt wird die Erweiterungsdatei im Texteditor,
/// nicht hier -- siehe <see cref="ExtensionLibrary"/>.
/// </summary>
public sealed class TextRuleViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;

    public TextRuleViewModel(
        Profile profile, ExtensionLibrary extensions, TextRule rule, Action onChanged, bool isExtensionRule = false)
    {
        _profile = profile;
        _extensions = extensions;
        Rule = rule;
        _onChanged = onChanged;
        IsExtensionRule = isExtensionRule;
    }

    public TextRule Rule { get; }

    /// <summary>
    /// Ob diese Regel aus der Erweiterungsdatei stammt statt aus dem Profil --
    /// steuert, ob das Formular sie bearbeitbar zeigt und ob sie sich
    /// entfernen laesst.
    /// </summary>
    public bool IsExtensionRule { get; }

    public string Name
    {
        get => Rule.Name;
        set
        {
            if (IsExtensionRule || Rule.Name == value)
                return;
            Rule.Name = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Pattern
    {
        get => Rule.Pattern;
        set
        {
            if (IsExtensionRule || Rule.Pattern == value)
                return;
            Rule.Pattern = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public GeneratorOption? Generator
    {
        get => GeneratorOption.Find(_profile, Rule.Generator, _extensions);
        set
        {
            if (IsExtensionRule || value is null || Rule.Generator == value.Name)
                return;
            Rule.Generator = value.Name;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public int Priority
    {
        get => Rule.Priority;
        set
        {
            if (IsExtensionRule || Rule.Priority == value)
                return;
            Rule.Priority = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool IgnoreCase
    {
        get => Rule.IgnoreCase;
        set
        {
            if (IsExtensionRule || Rule.IgnoreCase == value)
                return;
            Rule.IgnoreCase = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Display => IsExtensionRule
        ? $"{Rule.Name}  ·  Erweiterung"
        : $"{Rule.Name}  ·  {Rule.Priority}";
}

/// <param name="Rule">Regel, die gegriffen hat.</param>
/// <param name="Value">Der getroffene Text.</param>
/// <param name="Line">Zeile im Erprobungstext.</param>
public sealed record MatchPreview(string Rule, string Value, int Line);
