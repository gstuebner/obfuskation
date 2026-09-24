using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Der Dialog "Immer ersetzen…": legt aus einem Beispielwert eine
/// Ersetzungsregel an, ohne dass der Anwender einen regulaeren Ausdruck sehen
/// oder schreiben muss.
/// Er fragt drei Dinge in Anwendersprache -- was, wie weit, wo -- und schreibt
/// bei "Uebernehmen" unmittelbar in das Profil oder die Erweiterungsdatei, die
/// beide als Referenz hereinkommen. "Uebernehmen und weiter" tut dasselbe und
/// bleibt offen, fuer mehrere Begriffe hintereinander.
///
/// Wie <see cref="TextRulesViewModel"/> traegt dieses Ansichtsmodell direkten
/// Zugriff auf <see cref="Profile"/> und <see cref="ExtensionLibrary"/> und
/// mutiert sie selbst -- eine Rueckgabe an den Aufrufer braucht es nur fuer
/// die Nacharbeit (Vorschau neu rechnen, Statuszeile). Fensterfrei: Fenster
/// entstehen ausschliesslich in <see cref="Services.DialogService"/>, angestossen
/// ueber <see cref="CloseRequested"/> und <see cref="EditRulesRequested"/>.
/// </summary>
public sealed class AlwaysReplaceViewModel : ObservableObject
{
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;
    private readonly string _contextText;
    private readonly Action<bool> _onApplied;
    private readonly string? _extensionBlockedReason;
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
    /// <param name="windowTitle">
    /// Fenstertitel. Alle heutigen Einstiege lassen es bei der Vorgabe: der
    /// Menueeintrag der Textansicht nennt bereits den markierten Wert, und
    /// zwei Namen fuer dasselbe Fenster stifteten nur Verwirrung. Der
    /// Parameter bleibt fuer einen Aufrufer, der es einmal anders braucht.
    /// </param>
    /// <param name="introText">
    /// Einleitender Satz ueber den Fragen, oder <c>null</c> fuer keinen.
    /// </param>
    /// <param name="extensionBlockedReason">
    /// Gesetzt, wenn die Erweiterungsdatei kaputt geladen wurde (Fehler 1) --
    /// dann steht "Immer, in allen Projekten" nicht zur Wahl, unabhaengig von
    /// den Dateirechten. <c>null</c> im ueblichen Fall.
    /// </param>
    public AlwaysReplaceViewModel(
        Profile profile,
        ExtensionLibrary extensions,
        string initialSample,
        string contextText,
        Action<bool> onApplied,
        string windowTitle = "Immer ersetzen",
        string? introText = null,
        string? extensionBlockedReason = null)
    {
        _profile = profile;
        _extensions = extensions;
        _contextText = contextText;
        _onApplied = onApplied;
        _extensionBlockedReason = extensionBlockedReason;
        _sample = initialSample?.Trim() ?? "";

        WindowTitle = windowTitle;
        IntroText = introText;

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.For(profile, extensions));
        _selectedGenerator = Generators.FirstOrDefault(
            g => string.Equals(g.Name, "token", StringComparison.OrdinalIgnoreCase));

        ApplyCommand = new RelayCommand(Apply, () => HasSample);
        ApplyAndContinueCommand = new RelayCommand(ApplyAndContinue, () => HasSample);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        EditManuallyCommand = new RelayCommand(EditManually);

        RefreshPreview();
    }

    /// <summary>Fenstertitel, vom Aufrufer gesetzt (siehe Konstruktor).</summary>
    public string WindowTitle { get; }

    /// <summary>Einleitender Satz ueber den Fragen, oder <c>null</c>.</summary>
    public string? IntroText { get; }

    public bool HasIntroText => IntroText is not null;

    /// <summary>
    /// Steht anstelle der Fragen da, solange nichts eingetragen ist. Ohne ihn
    /// wirkte der Dialog leer und kaputt. Die Textansicht sperrt ihren
    /// Menueeintrag inzwischen ohne Markierung, der Fall kann aber weiterhin
    /// eintreten -- etwa wenn jemand das Feld von Hand leert.
    /// </summary>
    public string EmptyHint =>
        "Noch nichts markiert. Den Begriff hier eintippen — oder den Dialog schließen, "
        + "die Stelle im Text markieren und mit der rechten Maustaste „… immer ersetzen…“ wählen.";

    private string Trimmed => _sample?.Trim() ?? "";

    /// <summary>
    /// Das "Was?"-Feld, vorbelegt mit der Auswahl, aber frei aenderbar.
    ///
    /// Der Setter nimmt <c>null</c> an, obwohl der Typ es nicht zulaesst: die
    /// zweiseitige Bindung des Eingabefeldes schreibt beim Leeren einen Wert
    /// zurueck, ueber dessen Beschaffenheit die Oberflaeche entscheidet, nicht
    /// diese Klasse. Ein <c>null</c> hier riss zuvor jeden Lesezugriff auf
    /// <see cref="Trimmed"/> mit -- und eine Ausnahme im Aufbau eines modalen
    /// Fensters beendet den Prozess.
    /// </summary>
    public string Sample
    {
        get => _sample;
        set
        {
            if (!SetProperty(ref _sample, value ?? ""))
                return;

            OnPropertyChanged(nameof(HasSample));
            OnPropertyChanged(nameof(CanUseShape));
            OnPropertyChanged(nameof(LiteralDescription));
            OnPropertyChanged(nameof(ShapeDescription));
            OnPropertyChanged(nameof(HasPrefixHint));
            OnPropertyChanged(nameof(PrefixHint));
            OnPropertyChanged(nameof(SuggestedRuleName));
            if (!_ruleNameEdited)
                OnPropertyChanged(nameof(RuleNameInput));
            RaiseRuleNameHint();
            ApplyCommand.RaiseCanExecuteChanged();
            ApplyAndContinueCommand.RaiseCanExecuteChanged();
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

    // ---------------------------------------------------------- Bezeichnung

    private string _ruleNameInput = "";
    private bool _ruleNameEdited;

    /// <summary>Der aus dem Beispielwert abgeleitete Name (<see cref="PatternFromSample.SuggestRuleName"/>).</summary>
    public string SuggestedRuleName => HasSample ? PatternFromSample.SuggestRuleName(Trimmed) : "";

    /// <summary>
    /// Die Bezeichnung der neuen Regel -- unter ihr erscheint die Regel in
    /// der Fundliste der Textansicht. Folgt dem Vorschlag aus dem
    /// Beispielwert, bis der Anwender selbst etwas eintippt; ein wieder
    /// geleertes Feld faellt beim Anlegen auf den Vorschlag zurueck.
    /// </summary>
    public string RuleNameInput
    {
        get => _ruleNameEdited ? _ruleNameInput : SuggestedRuleName;
        set
        {
            var neu = value ?? "";
            if (neu == RuleNameInput)
                return;

            _ruleNameInput = neu;
            _ruleNameEdited = true;
            OnPropertyChanged();
            RaiseRuleNameHint();
        }
    }

    /// <summary>Der Name vor der Eindeutigmachung: die Eingabe, sonst der Vorschlag.</summary>
    private string RuleNameBase
    {
        get
        {
            var eingabe = _ruleNameEdited ? _ruleNameInput.Trim() : "";
            return eingabe.Length > 0 ? eingabe : SuggestedRuleName;
        }
    }

    /// <summary>
    /// Sagt vorher, wenn die gewuenschte Bezeichnung schon vergeben ist und
    /// die Regel deshalb eine Nummer angehaengt bekommt -- sonst stuende
    /// hinterher ueberraschend "Kreditkartennummer2" in der Fundliste.
    /// </summary>
    public string? RuleNameHint
    {
        get
        {
            var basis = RuleNameBase;
            if (basis.Length == 0)
                return null;

            var eindeutig = MakeUniqueRuleName(basis);
            return eindeutig == basis ? null : $"„{basis}“ gibt es schon – die Regel heißt dann „{eindeutig}“.";
        }
    }

    public bool HasRuleNameHint => RuleNameHint is not null;

    private void RaiseRuleNameHint()
    {
        OnPropertyChanged(nameof(RuleNameHint));
        OnPropertyChanged(nameof(HasRuleNameHint));
    }

    /// <summary>"Nur in diesem Projekt" (Profil) statt "immer, in allen Projekten" (Erweiterung).</summary>
    public bool UseExtension
    {
        get => _useExtension;
        set
        {
            // Gesperrt bleibt gesperrt: weder ein kaputt geladenes noch ein
            // schreibgeschuetztes Ziel nimmt eine neue Regel auf (Fehler 1).
            if (value && !CanUseExtension)
                return;

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
    public string ExtensionPath => WriteState.Path;

    /// <summary>
    /// Ob die Zieldatei von Hand gepflegte Kommentare traegt -- dann entsteht
    /// beim Schreiben eine Sicherungskopie (siehe <see cref="ExtensionLibrary.Save"/>),
    /// und der Dialog soll das vorher sagen, nicht erst hinterher.
    /// </summary>
    public bool ExtensionHasComments => ExtensionLibrary.HasComments(ExtensionPath);

    /// <summary>
    /// Ob und wohin sich die Erweiterungsdatei gerade schreiben liesse (siehe
    /// <see cref="ExtensionLibrary.GetWriteState"/>). Neu ermittelt bei jedem
    /// Zugriff statt einmal zwischengespeichert: der Dialog bleibt waehrend
    /// seiner Lebenszeit offen, und ein aeusserer Zustand (Dateirechte) kann
    /// sich in der Zwischenzeit geaendert haben.
    /// </summary>
    private ExtensionWriteState WriteState => ExtensionLibrary.GetWriteState();

    /// <summary>
    /// Ob "Immer, in allen Projekten" ueberhaupt zur Wahl steht: weder darf
    /// die Erweiterungsdatei kaputt geladen worden sein (Fehler 1, siehe
    /// <see cref="_extensionBlockedReason"/>), noch darf sie schreibgeschuetzt
    /// sein.
    /// </summary>
    public bool CanUseExtension => _extensionBlockedReason is null && WriteState.CanWrite;

    /// <summary>Erklaerung, warum <see cref="CanUseExtension"/> falsch ist -- sonst <c>null</c>.</summary>
    public string? ExtensionBlockedReason => _extensionBlockedReason ?? WriteState.Reason;

    public bool HasExtensionBlockedReason => !CanUseExtension;

    /// <summary>Die Fundstellen im Vorschaustreifen.</summary>
    public ObservableCollection<AlwaysReplaceMatch> Matches { get; } = new();

    public bool HasMatches => Matches.Count > 0;

    public string MatchSummary
    {
        get => _matchSummary;
        private set => SetProperty(ref _matchSummary, value);
    }

    public RelayCommand ApplyCommand { get; }

    /// <summary>Anlegen, ohne den Dialog zu schliessen (siehe <see cref="ApplyAndContinue"/>).</summary>
    public RelayCommand ApplyAndContinueCommand { get; }

    public RelayCommand CancelCommand { get; }
    public RelayCommand EditManuallyCommand { get; }

    /// <summary>
    /// Ob mindestens eine Regel entstanden ist. Bleibt auch dann <c>true</c>,
    /// wenn nach "Uebernehmen und weiter" mit "Abbrechen" geschlossen wird --
    /// sonst rechnete der Aufrufer die Vorschau nicht neu, und die angelegten
    /// Regeln blieben unsichtbar.
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <summary>Der Name der zuletzt angelegten Regel -- fuer die Statuszeile des Aufrufers.</summary>
    public string RuleName { get; private set; } = "";

    private readonly List<string> _createdRuleNames = new();

    /// <summary>Alle Regeln, die dieser Dialog angelegt hat, in der Reihenfolge ihrer Entstehung.</summary>
    public IReadOnlyList<string> CreatedRuleNames => _createdRuleNames;

    public bool HasCreatedRules => _createdRuleNames.Count > 0;

    /// <summary>Was in diesem Durchgang schon entstanden ist -- die Rueckmeldung, die der offene Dialog sonst nicht gaebe.</summary>
    public string CreatedSummary => "Angelegt: " + string.Join(", ", _createdRuleNames);

    private string? _errorText;

    /// <summary>
    /// Meldung, wenn das Schreiben der Erweiterungsdatei scheiterte (etwa
    /// Schreibrecht waehrenddessen entzogen) -- im Fenster als
    /// <c>warning small</c> unter den Knoepfen. <c>null</c> im ueblichen Fall.
    /// </summary>
    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasErrorText));
        }
    }

    public bool HasErrorText => _errorText is not null;

    public event Action? CloseRequested;

    /// <summary>
    /// "In den Einstellungen bearbeiten…" wurde gewaehlt: der Aufrufer
    /// (<see cref="MainViewModel"/>) oeffnet danach das Einstellungsfenster,
    /// mit dem Bereich vorbelegt, in den diese Regel laut
    /// <see cref="UseExtension"/> gerade ginge -- der Reiter selbst kennt seit
    /// Plan Teil B keine Ablageorte mehr, nur noch Themen.
    /// </summary>
    public event Action? EditRulesRequested;

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

    /// <summary>
    /// Legt die Regel an und meldet sie dem Aufrufer. Liefert <c>false</c>,
    /// wenn nichts einzutragen war -- die Schaltflaechen sind dann ohnehin
    /// gesperrt, der Fall bleibt als Zusicherung stehen.
    /// </summary>
    private bool CreateRule()
    {
        if (!HasSample)
            return false;

        var pattern = EffectivePattern().Pattern;
        var ruleName = MakeUniqueRuleName(RuleNameBase);

        // Vor ResolveGeneratorName merken, welche Namensraeume es im Ziel schon
        // gibt -- nur so laesst sich ein dabei frisch angelegter eigener
        // Generator im Fehlerfall gezielt wieder zurücknehmen, ohne einen
        // schon vorher bestehenden gleichen Namens zu verlieren.
        var zielGeneratoren = UseExtension ? _extensions.Generators : _profile.Generators;
        var vorherVorhanden = new HashSet<string>(zielGeneratoren.Keys, StringComparer.OrdinalIgnoreCase);

        var generatorName = ResolveGeneratorName(ruleName);
        var neuAngelegterGenerator = !vorherVorhanden.Contains(generatorName) && zielGeneratoren.ContainsKey(generatorName)
            ? generatorName
            : null;

        var rule = new TextRule { Name = ruleName, Priority = 60, Generator = generatorName, Pattern = pattern };
        var zielRegeln = UseExtension ? _extensions.TextRules : _profile.TextRules;
        zielRegeln.Add(rule);

        if (UseExtension)
        {
            try
            {
                _extensions.Save(ExtensionPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Zurücknehmen statt mit einem Stand weiterzuarbeiten, der auf
                // der Platte gar nicht existiert: sonst zeigte der Dialog eine
                // Regel (und moeglicherweise einen frisch benannten
                // Namensraum), die nach dem naechsten Neustart wieder weg
                // waere, ohne dass hier je ein Fehler zu sehen war.
                zielRegeln.Remove(rule);
                if (neuAngelegterGenerator is not null)
                    zielGeneratoren.Remove(neuAngelegterGenerator);

                ErrorText = $"Die Regel konnte nicht gespeichert werden: {ex.Message}";
                return false;
            }
        }

        ErrorText = null;
        RuleName = ruleName;
        _createdRuleNames.Add(ruleName);
        Confirmed = true;
        _onApplied(!UseExtension);
        return true;
    }

    private void Apply()
    {
        if (CreateRule())
            CloseRequested?.Invoke();
    }

    /// <summary>
    /// Legt die Regel an und laesst den Dialog offen, mit geleertem
    /// "Was?"-Feld: mehrere verschiedene Begriffe hintereinander, ohne ihn je
    /// neu zu oeffnen. Markieren im Text geht so nicht -- der Dialog ist
    /// modal --, wohl aber Eintippen, und wer seine eigenen Kennungen kennt,
    /// ist damit schneller als ueber fuenfmaliges Oeffnen.
    ///
    /// Die Eindeutigkeit der Namen ueber mehrere Durchgaenge stellt sich von
    /// selbst her: <see cref="MakeUniqueRuleName"/> liest Profil und
    /// Erweiterung jedes Mal neu, und die eben angelegte Regel steht bereits
    /// darin.
    /// </summary>
    private void ApplyAndContinue()
    {
        if (!CreateRule())
            return;

        // Ueber den Setter, nicht ueber das Feld: er zieht Vorschau,
        // Beschreibungen und die Ausfuehrbarkeit der Schaltflaechen nach.
        // Die naechste Regel bekommt wieder den Vorschlag aus ihrem eigenen
        // Beispielwert, nicht die Bezeichnung der eben angelegten.
        _ruleNameEdited = false;
        _ruleNameInput = "";
        Sample = "";
        OnPropertyChanged(nameof(RuleNameInput));
        RaiseRuleNameHint();

        OnPropertyChanged(nameof(HasCreatedRules));
        OnPropertyChanged(nameof(CreatedSummary));
    }

    private void EditManually()
    {
        EditRulesRequested?.Invoke();
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
        => TextRuleNaming.MakeUnique(
            basisName, _profile.TextRules.Select(r => r.Name).Concat(_extensions.TextRules.Select(r => r.Name)));

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
