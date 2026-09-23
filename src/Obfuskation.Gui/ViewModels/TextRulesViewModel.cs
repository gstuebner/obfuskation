using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Textregeln eines Bereichs (Projekt oder Erweiterung), mit einem
/// Erprobungsfeld -- der Inhalt von <c>TextRulesPanel</c>, seit die
/// Einstellungen (Plan Teil B) beide Bereiche nebeneinander zeigen, statt wie
/// zuvor nur die Profilregeln in einem eigenen Fenster.
///
/// Das Erproben ist hier kein Beiwerk: ein zu weit gefasstes Muster ersetzt
/// harmlose Werte und beschaedigt die Testdaten, ein zu enges laesst Echtdaten
/// stehen. Beides faellt beim Betrachten des Musters nicht auf, beim Erproben
/// an echtem Text sofort.
///
/// Die Regeln des jeweils anderen Bereichs erscheinen nur lesend darunter
/// (<see cref="TextRuleViewModel.IsOtherArea"/>) -- mit dem Knopf "Dort
/// bearbeiten", der in den anderen Reiter wechselt, und, sofern der Aufrufer
/// es erlaubt, mit einem Knopf zum Verschieben.
/// </summary>
public sealed class TextRulesViewModel : ObservableObject
{
    private readonly List<TextRule> _editableRules;
    private readonly Func<IReadOnlyList<TextRule>> _otherAreaRules;
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;
    private readonly Func<IReadOnlyList<TextRule>> _mergeForTrial;
    private readonly Action _onChanged;
    private readonly bool _isReadOnly;
    private readonly string _otherAreaLabel;
    private readonly string _moveLabel;
    private readonly Action<string>? _onSwitchToOtherArea;
    private readonly Action<TextRule>? _onMoveRequested;
    private readonly TextRuleEngine _engine = new();

    private TextRuleViewModel? _selected;
    private string _sampleText =
        "Herr Max Mustermann, IBAN DE02120300000000202051, erreichbar unter\n"
        + "max.mustermann@beispiel.de oder +49 30 12345678.\n"
        + "Rechnung 2024-0815 vom 15.03.2024, Betrag 1.234,56 EUR.";

    private string _matchSummary = "";

    /// <param name="editableRules">Die bearbeitbare Liste dieses Bereichs (Profil- oder Erweiterungsregeln).</param>
    /// <param name="otherAreaRules">
    /// Liest bei jedem Aufbau live die Regeln des jeweils anderen Bereichs --
    /// eine Funktion statt einer Momentaufnahme, damit <see cref="RefreshOtherArea"/>
    /// stets den aktuellen Stand zeigt, auch nachdem dort etwas verschoben
    /// oder geloescht wurde.
    /// </param>
    /// <param name="profile">Fuer die Generatorenliste (<see cref="GeneratorOption.For"/>), unabhaengig vom Bereich.</param>
    /// <param name="extensions">Wie <paramref name="profile"/>.</param>
    /// <param name="mergeForTrial">
    /// Liefert die fuer die Erprobung zusammengefuehrten Regeln (Profil- und
    /// Erweiterungsregeln nach <c>ExtensionLibrary.MergeTextRules</c>) --
    /// dieselbe Funktion fuer beide Bereiche, denn die Vereinigung ist immer
    /// dieselbe, unabhaengig davon, welcher Reiter gerade offen ist.
    /// </param>
    /// <param name="onChanged">Wird bei jeder Aenderung an einer eigenen Regel gerufen.</param>
    /// <param name="isReadOnly">
    /// Ob der ganze Bereich schreibgeschuetzt ist -- etwa der globale Reiter
    /// bei einer schreibgeschuetzten Erweiterungsdatei.
    /// </param>
    /// <param name="otherAreaLabel">Beschriftung der gespiegelten Zeilen, z. B. "gilt für alle Projekte".</param>
    /// <param name="moveLabel">Beschriftung des Verschieben-Knopfs, oder leer ohne diesen Knopf.</param>
    /// <param name="onSwitchToOtherArea">"Dort bearbeiten" bei einer gespiegelten Zeile -- <c>null</c> ohne diesen Knopf.</param>
    /// <param name="onMoveRequested">
    /// Verschiebt eine eigene Regel in den anderen Bereich -- <c>null</c>,
    /// wenn dort nicht geschrieben werden darf (kein Profil geladen bzw. die
    /// Erweiterungsdatei ist schreibgeschuetzt).
    /// </param>
    public TextRulesViewModel(
        List<TextRule> editableRules,
        Func<IReadOnlyList<TextRule>> otherAreaRules,
        Profile profile,
        ExtensionLibrary extensions,
        Func<IReadOnlyList<TextRule>> mergeForTrial,
        Action onChanged,
        bool isReadOnly = false,
        string otherAreaLabel = "",
        string moveLabel = "",
        Action<string>? onSwitchToOtherArea = null,
        Action<TextRule>? onMoveRequested = null)
    {
        _editableRules = editableRules;
        _otherAreaRules = otherAreaRules;
        _profile = profile;
        _extensions = extensions;
        _mergeForTrial = mergeForTrial;
        _onChanged = onChanged;
        _isReadOnly = isReadOnly;
        _otherAreaLabel = otherAreaLabel;
        _moveLabel = moveLabel;
        _onSwitchToOtherArea = onSwitchToOtherArea;
        _onMoveRequested = onMoveRequested;

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.For(profile, extensions));

        BuildRules();

        AddCommand = new RelayCommand(Add, () => !_isReadOnly);
        RemoveCommand = new RelayCommand(Remove, () => !_isReadOnly && _selected is { IsOtherArea: false });

        // Erst jetzt: der Setter meldet dem Entfernen-Befehl seine
        // Verfuegbarkeit, den es vorher noch nicht gab.
        Selected = Rules.FirstOrDefault(r => !r.IsOtherArea) ?? Rules.FirstOrDefault();

        Evaluate();
    }

    public ObservableCollection<TextRuleViewModel> Rules { get; } = new();
    public ObservableCollection<GeneratorOption> Generators { get; }
    public ObservableCollection<MatchPreview> Matches { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }

    /// <summary>Ob dieser Bereich schreibgeschuetzt ist (siehe Konstruktor).</summary>
    public bool IsReadOnly => _isReadOnly;

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

    /// <summary>Waehlt die Regel mit diesem Namen, sofern sie in diesem Bereich (auch gespiegelt) erscheint.</summary>
    public void SelectByName(string ruleName)
    {
        var treffer = Rules.FirstOrDefault(r => string.Equals(r.Name, ruleName, StringComparison.OrdinalIgnoreCase));
        if (treffer is not null)
            Selected = treffer;
    }

    private void BuildRules()
    {
        Rules.Clear();

        foreach (var rule in _editableRules)
            Rules.Add(MakeEditableEntry(rule));

        AppendOtherArea();
    }

    private TextRuleViewModel MakeEditableEntry(TextRule rule)
        => new(_profile, _extensions, rule, OnRuleChanged, isReadOnly: _isReadOnly,
            moveLabel: _moveLabel,
            onMove: _isReadOnly || _onMoveRequested is null ? null : () => _onMoveRequested(rule));

    private void AppendOtherArea()
    {
        // Eine gleichnamige eigene Regel ersetzt eine des anderen Bereichs
        // vollstaendig, statt zusaetzlich zu gelten -- dieselbe Regel wie
        // ExtensionLibrary.MergeTextRules; sie erscheint dann nicht doppelt.
        var eigeneNamen = new HashSet<string>(_editableRules.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var rule in _otherAreaRules().Where(r => !eigeneNamen.Contains(r.Name)))
        {
            Rules.Add(new TextRuleViewModel(
                _profile, _extensions, rule, OnRuleChanged, isReadOnly: true, areaLabel: _otherAreaLabel,
                onSwitchToOtherArea: _onSwitchToOtherArea is null ? null : () => _onSwitchToOtherArea(rule.Name)));
        }
    }

    /// <summary>
    /// Baut nur die gespiegelten (schreibgeschuetzten) Zeilen des anderen
    /// Bereichs neu auf -- gerufen, wenn sich dort etwas geaendert hat.
    /// Die eigene, gerade bearbeitete Liste bleibt dabei unangetastet, damit
    /// eine laufende Eingabe (Fokus, Cursorposition) nicht verlorengeht.
    /// </summary>
    public void RefreshOtherArea()
    {
        for (var i = Rules.Count - 1; i >= 0; i--)
        {
            if (Rules[i].IsOtherArea)
                Rules.RemoveAt(i);
        }

        AppendOtherArea();
        Evaluate();
    }

    /// <summary>Baut die Generatorenliste neu auf -- nach einer Aenderung an den eigenen Generatoren.</summary>
    public void RefreshGenerators()
    {
        var ziel = GeneratorOption.For(_profile, _extensions);

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

        _editableRules.Add(rule);

        var viewModel = MakeEditableEntry(rule);

        // Vor den gespiegelten Zeilen des anderen Bereichs einfuegen: die
        // eigenen Regeln stehen immer zuerst.
        var einfuegeIndex = Rules.TakeWhile(r => !r.IsOtherArea).Count();
        Rules.Insert(einfuegeIndex, viewModel);
        Selected = viewModel;

        OnRuleChanged();
    }

    private void Remove()
    {
        if (_selected is null || _selected.IsOtherArea)
            return;

        _editableRules.Remove(_selected.Rule);
        Rules.Remove(_selected);
        Selected = Rules.FirstOrDefault(r => !r.IsOtherArea) ?? Rules.FirstOrDefault();

        OnRuleChanged();
    }

    private string NextName()
    {
        var vergeben = new HashSet<string>(
            _editableRules.Select(r => r.Name).Concat(_otherAreaRules().Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);

        var nummer = 1;
        while (vergeben.Contains($"regel{nummer}"))
            nummer++;
        return $"regel{nummer}";
    }

    private void OnRuleChanged()
    {
        _onChanged();
        Evaluate();
    }

    /// <summary>
    /// Wendet alle zusammengefuehrten Regeln auf den Erprobungstext an und
    /// zeigt, was greift -- ueber <see cref="_mergeForTrial"/>, damit die
    /// Erprobung immer das prueft, was ein echter Lauf tatsaechlich findet,
    /// nicht nur den Ausschnitt dieses Bereichs.
    /// </summary>
    private void Evaluate()
    {
        Matches.Clear();

        var brauchbare = _mergeForTrial()
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
/// Eine gespiegelte Regel des anderen Bereichs (<see cref="IsOtherArea"/>)
/// oder eine Regel in einem schreibgeschuetzten Bereich
/// (<see cref="IsReadOnly"/>) ist nur lesend: die Setter tun dann nichts.
/// </summary>
public sealed class TextRuleViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;

    public TextRuleViewModel(
        Profile profile, ExtensionLibrary extensions, TextRule rule, Action onChanged,
        bool isReadOnly = false,
        string? areaLabel = null,
        Action? onSwitchToOtherArea = null,
        string moveLabel = "",
        Action? onMove = null)
    {
        _profile = profile;
        _extensions = extensions;
        Rule = rule;
        _onChanged = onChanged;
        IsReadOnly = isReadOnly;
        AreaLabel = areaLabel;
        IsOtherArea = areaLabel is not null;
        MoveLabel = moveLabel;

        SwitchToOtherAreaCommand = new RelayCommand(() => onSwitchToOtherArea?.Invoke(), () => onSwitchToOtherArea is not null);
        MoveCommand = new RelayCommand(() => onMove?.Invoke(), () => onMove is not null);
    }

    public TextRule Rule { get; }

    /// <summary>Ob diese Zeile ueberhaupt bearbeitet werden darf.</summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Ob der eigene Bereich (nicht eine gespiegelte Zeile des anderen
    /// Bereichs) schreibgeschuetzt ist -- fuer den Warnhinweis am Formular,
    /// der nur in diesem Fall erscheint, nicht bei jeder gespiegelten Zeile
    /// (die traegt bereits <see cref="AreaLabel"/>).
    /// </summary>
    public bool IsReadOnlyOwnArea => IsReadOnly && !IsOtherArea;

    /// <summary>
    /// Ob diese Regel aus dem jeweils anderen Bereich gespiegelt ist statt aus
    /// dem gerade bearbeiteten -- steuert Beschriftung und "Dort bearbeiten".
    /// </summary>
    public bool IsOtherArea { get; }

    /// <summary>Beschriftung der Herkunft, z. B. "gilt für alle Projekte". Nur bei <see cref="IsOtherArea"/> gesetzt.</summary>
    public string? AreaLabel { get; }

    /// <summary>Wechselt in den anderen Reiter und waehlt diese Regel dort aus. Nur bei <see cref="IsOtherArea"/> verfuegbar.</summary>
    public RelayCommand SwitchToOtherAreaCommand { get; }

    /// <summary>Beschriftung des Verschieben-Knopfs, oder leer ohne diesen Knopf.</summary>
    public string MoveLabel { get; }

    public bool HasMoveLabel => MoveLabel.Length > 0;

    /// <summary>Verschiebt diese Regel (samt eigenem Generator) in den anderen Bereich.</summary>
    public RelayCommand MoveCommand { get; }

    public string Name
    {
        get => Rule.Name;
        set
        {
            if (IsReadOnly || Rule.Name == value)
                return;
            Rule.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            _onChanged();
        }
    }

    public string Pattern
    {
        get => Rule.Pattern;
        set
        {
            if (IsReadOnly || Rule.Pattern == value)
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
            if (IsReadOnly || value is null || Rule.Generator == value.Name)
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
            if (IsReadOnly || Rule.Priority == value)
                return;
            Rule.Priority = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            _onChanged();
        }
    }

    public bool IgnoreCase
    {
        get => Rule.IgnoreCase;
        set
        {
            if (IsReadOnly || Rule.IgnoreCase == value)
                return;
            Rule.IgnoreCase = value;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Display => IsOtherArea
        ? $"{Rule.Name}  ·  {AreaLabel}"
        : $"{Rule.Name}  ·  {Rule.Priority}";
}

/// <param name="Rule">Regel, die gegriffen hat.</param>
/// <param name="Value">Der getroffene Text.</param>
/// <param name="Line">Zeile im Erprobungstext.</param>
public sealed record MatchPreview(string Rule, string Value, int Line);
