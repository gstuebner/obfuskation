using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Der Dialog "Immer ersetzen…": legt aus einem Beispielwert eine
/// Ersetzungsregel an, ohne dass der Anwender einen regulaeren Ausdruck sehen
/// oder schreiben muss. Er fragt drei Dinge in Anwendersprache -- was, wie
/// weit, wo -- und schreibt bei "Uebernehmen" unmittelbar in das Profil oder
/// die Erweiterungsdatei, die beide als Referenz hereinkommen.
///
/// Wie <see cref="TextRulesViewModel"/> traegt dieses Ansichtsmodell direkten
/// Zugriff auf <see cref="Profile"/> und <see cref="ExtensionLibrary"/> und
/// mutiert sie selbst -- eine Rueckgabe an den Aufrufer braucht es nur fuer
/// die Nacharbeit (Vorschau neu rechnen, Statuszeile). Fensterfrei: Fenster
/// entstehen ausschliesslich in <see cref="Services.DialogService"/>, angestossen
/// ueber <see cref="CloseRequested"/> und <see cref="EditManuallyRequested"/>.
/// </summary>
public sealed class AlwaysReplaceViewModel : ObservableObject
{
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;
    private readonly string _contextText;
    private readonly Action<bool> _onApplied;
    private readonly Func<TextRulesViewModel> _createTextRulesViewModel;
    private readonly TextRuleEngine _engine = new();

    private string _sample;
    private bool _useShape = true;
    private bool _useExtension;
    private GeneratorOption? _selectedGenerator;
    private string _matchSummary = "";

    /// <param name="profile">Das offene Profil -- Ziel, wenn "nur in diesem Projekt" gilt.</param>
    /// <param name="extensions">
    /// Die Erweiterung der laufenden Sitzung -- dasselbe Objekt, das
    /// <see cref="Services.ProfileSession.Extensions"/> und die Textansicht
    /// verwenden. Ein Schreiben hierhinein ist damit sofort ueberall sichtbar,
    /// ohne dass irgendwer neu laden muesste.
    /// </param>
    /// <param name="initialSample">Vorbelegung des "Was?"-Feldes, aus der Auswahl.</param>
    /// <param name="contextText">
    /// Der Text, an dem der Vorschaustreifen seine Trefferzahl zeigt -- der
    /// Eingabetext der Textansicht, oder in der Dateiansicht der Rohinhalt der
    /// geoeffneten Datei.
    /// </param>
    /// <param name="onApplied">
    /// Wird nach erfolgreichem "Uebernehmen" gerufen, mit <c>true</c>, wenn die
    /// Regel ins Profil geschrieben wurde (dann muss der Aufrufer das Profil
    /// als geaendert markieren), <c>false</c> bei der Erweiterungsdatei (die
    /// speichert sich selbst, siehe <see cref="Apply"/>).
    /// </param>
    /// <param name="createTextRulesViewModel">
    /// Baut das Ansichtsmodell der Fachansicht fuer "Muster von Hand
    /// bearbeiten…" -- dieselbe Stelle, die auch <c>MainViewModel.CreateTextRulesViewModel</c>
    /// verwendet, damit es nur eine einzige Verdrahtung von Profil, Erweiterung
    /// und Aenderungsmeldung gibt.
    /// </param>
    public AlwaysReplaceViewModel(
        Profile profile,
        ExtensionLibrary extensions,
        string initialSample,
        string contextText,
        Action<bool> onApplied,
        Func<TextRulesViewModel> createTextRulesViewModel)
    {
        _profile = profile;
        _extensions = extensions;
        _contextText = contextText;
        _onApplied = onApplied;
        _createTextRulesViewModel = createTextRulesViewModel;
        _sample = initialSample.Trim();

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.For(profile, extensions));
        _selectedGenerator = Generators.FirstOrDefault(
            g => string.Equals(g.Name, "token", StringComparison.OrdinalIgnoreCase));

        ApplyCommand = new RelayCommand(Apply, () => HasSample);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        EditManuallyCommand = new RelayCommand(EditManually);

        RefreshPreview();
    }

    private string Trimmed => _sample.Trim();

    /// <summary>Das "Was?"-Feld, vorbelegt mit der Auswahl, aber frei aenderbar.</summary>
    public string Sample
    {
        get => _sample;
        set
        {
            if (!SetProperty(ref _sample, value))
                return;

            OnPropertyChanged(nameof(HasSample));
            OnPropertyChanged(nameof(CanUseShape));
            OnPropertyChanged(nameof(LiteralDescription));
            OnPropertyChanged(nameof(ShapeDescription));
            OnPropertyChanged(nameof(HasPrefixHint));
            OnPropertyChanged(nameof(PrefixHint));
            ApplyCommand.RaiseCanExecuteChanged();
            RefreshPreview();
        }
    }

    public bool HasSample => Trimmed.Length > 0;

    /// <summary>
    /// Die Form-Lesart, oder <c>null</c>, wenn der Wert keinen Ziffernlauf
    /// enthaelt -- dann waere sie nichts anderes als die woertliche und wird
    /// nicht angeboten (siehe <see cref="PatternFromSample.Shape"/>).
    /// </summary>
    private SamplePattern? ShapeOption => HasSample ? PatternFromSample.Shape(Trimmed) : null;

    public bool CanUseShape => ShapeOption is not null;

    public string LiteralDescription => HasSample ? PatternFromSample.Literal(Trimmed).Description : "";

    public string ShapeDescription => ShapeOption?.Description ?? "";

    /// <summary>
    /// "Alles dieser Form" statt "nur genau dieses Wort". Faellt automatisch
    /// auf die woertliche Lesart zurueck, sobald <see cref="CanUseShape"/>
    /// nicht mehr zutrifft -- etwa weil der Anwender das "Was?"-Feld auf einen
    /// Wert ohne Ziffern geaendert hat.
    /// </summary>
    public bool UseShape
    {
        get => _useShape && CanUseShape;
        set
        {
            if (!SetProperty(ref _useShape, value))
                return;

            OnPropertyChanged(nameof(UseLiteral));
            RefreshPreview();
        }
    }

    /// <summary>
    /// Das Gegenstueck zu <see cref="UseShape"/> mit eigenem Setter -- wie bei
    /// <c>TextViewModel.IsForward</c>/<c>IsReverse</c> schaltet Avalonia beim
    /// Ankreuzen des einen RadioButtons den anderen der Gruppe direkt um, das
    /// liefe ohne Setter hier ins Leere.
    /// </summary>
    public bool UseLiteral
    {
        get => !UseShape;
        set => UseShape = !value;
    }

    /// <summary>Die Menge der zur Wahl stehenden Generatoren -- eingebaute plus eigene Namensraeume.</summary>
    public ObservableCollection<GeneratorOption> Generators { get; }

    public GeneratorOption? SelectedGenerator
    {
        get => _selectedGenerator;
        set
        {
            if (!SetProperty(ref _selectedGenerator, value))
                return;

            OnPropertyChanged(nameof(HasPrefixHint));
            OnPropertyChanged(nameof(PrefixHint));
        }
    }

    /// <summary>
    /// Das Praefix, das entstuende, bliebe es beim vorgeschlagenen Generator
    /// "token" -- nur dann bekommt die Regel automatisch einen eigenen
    /// Namensraum mit dieser Kennzeichnung (siehe <see cref="ResolveGeneratorName"/>).
    /// Waehlt der Anwender stattdessen einen bestehenden Generator, bleibt
    /// dessen Format unangetastet.
    /// </summary>
    public string? PrefixHint
        => HasSample && IsPlainTokenSelected() ? PatternFromSample.SuggestPrefix(Trimmed) : null;

    public bool HasPrefixHint => PrefixHint is not null;

    private bool IsPlainTokenSelected()
        => _selectedGenerator is not null
           && string.Equals(_selectedGenerator.Name, "token", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Nur in diesem Projekt" (Profil) statt "immer, in allen Projekten" (Erweiterung).</summary>
    public bool UseExtension
    {
        get => _useExtension;
        set
        {
            if (!SetProperty(ref _useExtension, value))
                return;

            OnPropertyChanged(nameof(UseProfile));
            OnPropertyChanged(nameof(ExtensionPath));
            OnPropertyChanged(nameof(ExtensionHasComments));
        }
    }

    public bool UseProfile
    {
        get => !UseExtension;
        set => UseExtension = !value;
    }

    /// <summary>
    /// Wohin geschrieben wuerde, wenn <see cref="UseExtension"/> gilt --
    /// nennt der Dialog im Klartext, bevor er schreibt.
    /// </summary>
    public string ExtensionPath => ExtensionLibrary.ResolveWritePath();

    /// <summary>
    /// Ob die Zieldatei von Hand gepflegte Kommentare traegt -- dann entsteht
    /// beim Schreiben eine Sicherungskopie (siehe <see cref="ExtensionLibrary.Save"/>),
    /// und der Dialog soll das vorher sagen, nicht erst hinterher.
    /// </summary>
    public bool ExtensionHasComments => ExtensionLibrary.HasComments(ExtensionPath);

    /// <summary>Die Fundstellen im Vorschaustreifen.</summary>
    public ObservableCollection<AlwaysReplaceMatch> Matches { get; } = new();

    public bool HasMatches => Matches.Count > 0;

    public string MatchSummary
    {
        get => _matchSummary;
        private set => SetProperty(ref _matchSummary, value);
    }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand EditManuallyCommand { get; }

    /// <summary>Ob "Uebernehmen" gewaehlt wurde, statt abzubrechen.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Der Name der angelegten Regel -- fuer die Statuszeile des Aufrufers.</summary>
    public string RuleName { get; private set; } = "";

    public event Action? CloseRequested;

    /// <summary>
    /// "Muster von Hand bearbeiten…" wurde gewaehlt: das Ansichtsmodell der
    /// vollstaendigen Fachansicht (<see cref="TextRulesViewModel"/>) entsteht
    /// bereits hier, der Empfaenger (die Dialogumsetzung) muss es nur noch in
    /// einem Fenster zeigen.
    /// </summary>
    public event Action<TextRulesViewModel>? EditManuallyRequested;

    /// <summary>
    /// Rechnet das gewaehlte Muster gegen <see cref="_contextText"/> aus und
    /// fuellt <see cref="Matches"/>. Ein halbfertiger Zwischenstand (etwa ein
    /// gerade erst begonnenes "Was?") ist normal und wird nicht als Fehler
    /// gemeldet.
    /// </summary>
    private void RefreshPreview()
    {
        Matches.Clear();

        if (!HasSample)
        {
            MatchSummary = "";
            OnPropertyChanged(nameof(HasMatches));
            return;
        }

        try
        {
            var pattern = EffectivePattern().Pattern;
            var probe = new TextRule { Name = "vorschau", Pattern = pattern, Priority = 1000 };
            var treffer = _engine.FindMatches(_contextText, new[] { probe });

            foreach (var t in treffer)
                Matches.Add(new AlwaysReplaceMatch(t.Value, ZeileVon(t.Start)));

            MatchSummary = treffer.Count switch
            {
                0 => "Trifft im aktuellen Text nicht zu.",
                1 => "Trifft im aktuellen Text 1× zu.",
                var n => $"Trifft im aktuellen Text {n}× zu.",
            };
        }
        catch (ConfigurationException ex)
        {
            MatchSummary = ex.Message;
        }

        OnPropertyChanged(nameof(HasMatches));
    }

    private int ZeileVon(int position)
    {
        var zeile = 1;
        for (var i = 0; i < position && i < _contextText.Length; i++)
            if (_contextText[i] == '\n')
                zeile++;
        return zeile;
    }

    private SamplePattern EffectivePattern()
        => UseShape && ShapeOption is not null ? ShapeOption : PatternFromSample.Literal(Trimmed);

    private void Apply()
    {
        if (!HasSample)
            return;

        var pattern = EffectivePattern().Pattern;
        var ruleName = MakeUniqueRuleName(PatternFromSample.SuggestRuleName(Trimmed));
        var generatorName = ResolveGeneratorName(ruleName);

        var rule = new TextRule { Name = ruleName, Priority = 60, Generator = generatorName, Pattern = pattern };

        if (UseExtension)
        {
            _extensions.TextRules.Add(rule);
            _extensions.Save(ExtensionPath);
        }
        else
        {
            _profile.TextRules.Add(rule);
        }

        RuleName = ruleName;
        Confirmed = true;
        _onApplied(!UseExtension);
        CloseRequested?.Invoke();
    }

    private void EditManually()
    {
        EditManuallyRequested?.Invoke(_createTextRulesViewModel());
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Stellt Eindeutigkeit ueber Profil <b>und</b> Erweiterung hinweg her,
    /// unabhaengig davon, wohin die neue Regel tatsaechlich geschrieben wird:
    /// eine gleichnamige Regel im jeweils anderen Ort wuerde sich sonst beim
    /// naechsten Zusammenfuehren gegenseitig verdraengen (siehe
    /// <see cref="ExtensionLibrary.MergeTextRules"/>).
    /// </summary>
    private string MakeUniqueRuleName(string basisName)
    {
        var vergeben = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        vergeben.UnionWith(_profile.TextRules.Select(r => r.Name));
        vergeben.UnionWith(_extensions.TextRules.Select(r => r.Name));

        if (!vergeben.Contains(basisName))
            return basisName;

        var zaehler = 2;
        while (vergeben.Contains(basisName + zaehler))
            zaehler++;

        return basisName + zaehler;
    }

    /// <summary>
    /// Der Generatorname fuer die neue Regel. Bleibt es beim vorgeschlagenen
    /// eingebauten "token" und laesst sich aus dem Beispielwert ein Praefix
    /// bilden, entsteht dafuer ein eigener Namensraum -- im selben Ort wie die
    /// Regel selbst, denn eine Erweiterungsregel, deren Generator nur im
    /// Profil existierte, waere in einem anderen Projekt ohne dieses Profil
    /// nicht lauffaehig. Waehlt der Anwender stattdessen einen bestehenden
    /// Generator (eingebaut oder ein schon vorhandener eigener Namensraum),
    /// wird dessen Name unveraendert uebernommen.
    /// </summary>
    private string ResolveGeneratorName(string ruleName)
    {
        var gewaehlt = _selectedGenerator?.Name ?? "token";
        var praefix = PatternFromSample.SuggestPrefix(Trimmed);

        if (!string.Equals(gewaehlt, "token", StringComparison.OrdinalIgnoreCase) || praefix is null)
            return gewaehlt;

        var key = ProfileScaffolder.ToGeneratorKey(ruleName);
        var ziel = UseExtension ? _extensions.Generators : _profile.Generators;

        // Eine Kollision waere eine boese Ueberraschung -- dann lieber beim
        // eingebauten "token" ohne eigenes Praefix bleiben, statt einen
        // fremden Namensraum zu kapern.
        if (GeneratorRegistry.KnownNames.Contains(key, StringComparer.OrdinalIgnoreCase) || ziel.ContainsKey(key))
            return gewaehlt;

        ziel[key] = new GeneratorSettings { Type = "token", Prefix = praefix };
        return key;
    }
}

/// <summary>Eine Fundstelle im Vorschaustreifen des "Immer ersetzen"-Dialogs.</summary>
/// <param name="Value">Der getroffene Text.</param>
/// <param name="Line">Zeile im Kontexttext.</param>
public sealed record AlwaysReplaceMatch(string Value, int Line);
