using System.Collections.ObjectModel;
using System.Text;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>Welche der beiden Richtungen die Textansicht gerade anwendet.</summary>
public enum TextDirection
{
    /// <summary>Echtwerte raus -- <see cref="ObfuscationEngine.Obfuscate"/>.</summary>
    Forward,

    /// <summary>Pseudonyme zurueck -- <see cref="ObfuscationEngine.Deobfuscate"/>.</summary>
    Reverse,
}

/// <summary>
/// Die Textansicht: Fliesstext einfuegen, Echtwerte raus (oder umgekehrt),
/// Ergebnis kopieren. Kein Feld, kein Generator, keine Regex ist hier
/// sichtbar -- die Regeln kommen aus dem Profil im Hintergrund.
///
/// <b>Verbindlichkeit der Vorschau:</b> der Konstruktor ruft
/// <see cref="ObfuscationEngine.EnsureMappingStore"/> einmal, bevor die erste
/// Vorschau entsteht. Danach liefert ein Probelauf exakt dieselben Werte wie
/// der echte Lauf (siehe <c>MappingStoreTests</c>) -- wer aus dieser Ansicht
/// kopiert, kopiert genau das, was er sieht.
///
/// Fensterfrei wie jedes Ansichtsmodell dieses Projekts: Zwischenablage und
/// Drag&amp;Drop gehoeren in die View-Codebehind-Datei, dieses Modell kennt
/// nur <see cref="InputText"/> als schlichten Zeichenkettenwert.
/// </summary>
public sealed class TextViewModel : ObservableObject
{
    private readonly ProfileSession _session;
    private readonly TextRuleEngine _ruleEngine = new();
    private readonly Action? _onRealRunCompleted;
    private readonly Action<string>? _onAlwaysReplaceRequested;
    private readonly TimeSpan _debounceDelay;

    private string _inputText = "";
    private string _resultText = "";
    private string _matchSummary = "";
    private TextDirection _direction;
    private CancellationTokenSource? _debounceCts;
    private string? _storeWarning;

    /// <summary>Die Funde des letzten Vorwaertslaufs, parallel zu <see cref="Matches"/>.</summary>
    private IReadOnlyList<TextMatch> _lastFound = Array.Empty<TextMatch>();

    /// <summary>
    /// Der Ersatzwert je Fund aus <see cref="_lastFound"/>, oder <c>null</c>,
    /// wenn sich die Zuordnung nicht herstellen liess (siehe <see cref="AlignReplacements"/>)
    /// -- dann bleibt <see cref="ResultText"/> beim Text des Laufs, nur ohne
    /// dass sich einzelne Funde abwaehlen liessen.
    /// </summary>
    private string[]? _alignment;

    /// <summary>Der vollstaendige Text des letzten Laufs (Vorwaerts oder Rueckwaerts).</summary>
    private string _lastRunResult = "";

    /// <summary>
    /// Der Eingabetext, auf dem der letzte Lauf beruht. <see cref="_lastFound"/>
    /// traegt Positionen in genau diesen Text -- <see cref="_inputText"/> kann
    /// inzwischen ein anderer sein, wenn waehrend der Entprellung weiter
    /// getippt wurde. Baut <see cref="RecomputeResultText"/> gegen den
    /// aktuellen statt gegen diesen Text, greifen die Positionen ins Leere.
    /// </summary>
    private string _lastRunInput = "";

    public TextViewModel(
        ProfileSession session,
        TextDirection initialDirection = TextDirection.Forward,
        Action? onRealRunCompleted = null,
        Action<string>? onAlwaysReplaceRequested = null,
        TimeSpan? debounceDelay = null)
    {
        _session = session;
        _direction = initialDirection;
        _onRealRunCompleted = onRealRunCompleted;
        _onAlwaysReplaceRequested = onAlwaysReplaceRequested;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(300);

        // Eine Sammelbenachrichtigung statt an jeder einzelnen Clear()/Add()-
        // Stelle: HasMatches soll unabhaengig davon stimmen, auf welchem Weg
        // sich die Liste gerade aendert.
        Matches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMatches));

        // Ohne diesen Aufruf entstuende bei jedem Probelauf ein fluechtiges
        // Salt (siehe MappingStore.EnsureCreated) -- der Anwender kopierte
        // dann etwas anderes, als die Vorschau gezeigt hat. Ein fehlerhaftes
        // Profil (etwa der Git-Schutz greift) darf das Betreten der Ansicht
        // nicht verhindern; der Hinweis bleibt stattdessen als StoreWarning
        // stehen. Bewusst nicht in MatchSummary: die ueberschreibt schon die
        // erste Vorschau, und dann waere die Warnung weg, kaum dass jemand
        // Text eingefuegt hat.
        try
        {
            if (_session.TryGetEngine(out var engine, out _) && engine is not null)
                engine.EnsureMappingStore();
        }
        catch (Exception ex) when (ex is ConfigurationException or MappingConflictException
                                       or MappingLockedException or IOException or UnauthorizedAccessException)
        {
            StoreWarning = "Die Ersetzungstabelle liess sich nicht anlegen: " + ex.Message
                + " Die Vorschau ist deshalb nur beispielhaft — es gilt, was „Kopieren“ ausgibt.";
        }
    }

    /// <summary>
    /// Warnung zur Ersetzungstabelle, die stehen bleibt, bis sie behoben ist:
    /// entweder liess sie sich beim Betreten nicht anlegen, oder Vorschau und
    /// echter Lauf sind auseinandergelaufen (siehe <see cref="RunReal"/>).
    /// Beides betrifft die Verlaesslichkeit des Gezeigten und darf nicht in
    /// der Fundzeile untergehen.
    /// </summary>
    public string? StoreWarning
    {
        get => _storeWarning;
        private set
        {
            if (SetProperty(ref _storeWarning, value))
                OnPropertyChanged(nameof(HasStoreWarning));
        }
    }

    public bool HasStoreWarning => _storeWarning is not null;

    /// <summary>
    /// Stoesst den Dialog "Immer ersetzen…" fuer <paramref name="sample"/> an --
    /// aus der Textmarkierung (View-Codebehind liest sie aus dem TextBox) oder
    /// aus einem Fund in <see cref="Matches"/> heraus. Dieses Ansichtsmodell
    /// oeffnet dabei kein Fenster selbst: es reicht die Anfrage an
    /// <see cref="MainViewModel"/> weiter, die einzige Stelle mit Zugriff auf
    /// <see cref="Services.IDialogService"/> (siehe Klassenkopf).
    /// </summary>
    public void RequestAlwaysReplace(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample))
            return;

        _onAlwaysReplaceRequested?.Invoke(sample);
    }

    public ObservableCollection<TextMatchViewModel> Matches { get; } = new();

    public bool HasMatches => Matches.Count > 0;

    /// <summary>Der eingegebene oder eingefuegte Text.</summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (!SetProperty(ref _inputText, value))
                return;

            ScheduleRefresh();
        }
    }

    /// <summary>Der gesaeuberte bzw. zurueckuebersetzte Text.</summary>
    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>Kurzfassung der Funde, etwa "Gefunden: 3× email · 1× iban".</summary>
    public string MatchSummary
    {
        get => _matchSummary;
        private set => SetProperty(ref _matchSummary, value);
    }

    /// <summary>Ob gerade zurueckuebersetzt statt gesaeubert wird.</summary>
    public bool IsReverse
    {
        get => _direction == TextDirection.Reverse;
        set
        {
            var ziel = value ? TextDirection.Reverse : TextDirection.Forward;
            if (_direction == ziel)
                return;

            _direction = ziel;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsForward));

            // Richtungswechsel ist eine bewusste, seltene Handlung -- anders
            // als beim Tippen darf die Vorschau hier sofort erscheinen statt
            // erst nach der Entprellung.
            RefreshPreview();
        }
    }

    /// <summary>
    /// Das Gegenstueck zu <see cref="IsReverse"/> -- mit eigenem Setter, nicht
    /// nur berechnet: die beiden Richtungs-RadioButtons der Ansicht sind
    /// zweiseitig gebunden, und Avalonia schaltet beim Ankreuzen des einen
    /// den anderen der Gruppe direkt (ohne Umweg ueber dessen eigene
    /// Bindungslogik) auf <c>false</c> -- das schriebe ohne Setter hier ins
    /// Leere.
    /// </summary>
    public bool IsForward
    {
        get => !IsReverse;
        set => IsReverse = !value;
    }

    private void ScheduleRefresh()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebounceThenRefreshAsync(_debounceCts.Token);
    }

    private async Task DebounceThenRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounceDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
            RefreshPreview();
    }

    /// <summary>
    /// Fuehrt die Vorschau sofort aus, ohne auf die Entprellung zu warten --
    /// fuer Tests, und fuer den sofortigen Richtungswechsel.
    /// </summary>
    public void RefreshPreview()
    {
        var text = _inputText;

        if (string.IsNullOrEmpty(text))
        {
            Matches.Clear();
            _lastFound = Array.Empty<TextMatch>();
            _alignment = null;
            _lastRunResult = "";
            ResultText = "";
            MatchSummary = "";
            return;
        }

        if (!_session.TryGetEngine(out var engine, out _) || engine is null)
        {
            Matches.Clear();
            ResultText = text;
            MatchSummary = "Die Konfiguration ist fehlerhaft — siehe Profil.";
            return;
        }

        if (IsReverse)
            RefreshReverse(engine, text);
        else
            RefreshForward(engine, text);
    }

    private void RefreshForward(ObfuscationEngine engine, string text)
    {
        var rules = _session.Extensions.MergeTextRules(_session.Profile.TextRules)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();

        var found = _ruleEngine.FindMatches(text, rules);

        RunResult result;
        try
        {
            result = engine.Obfuscate(Encoding.UTF8.GetBytes(text), "eingabe.txt", new RunOptions { DryRun = true });
        }
        catch (Exception ex) when (ex is ConfigurationException or MappingConflictException
                                       or MappingLockedException or GenerationException)
        {
            Matches.Clear();
            ResultText = text;
            MatchSummary = ex.Message;
            return;
        }

        _lastRunResult = Encoding.UTF8.GetString(result.Content);
        _lastRunInput = text;
        _lastFound = found;

        // Die Ersatzwerte je Fund kommen aus genau diesem einen Lauf, nicht
        // aus je einem eigenen PreviewValue-Aufruf: die unveraenderten
        // Textstuecke zwischen den Funden dienen dabei als Anker, denn sie
        // stehen unveraendert auch im Ergebnistext.
        _alignment = found.Count == 0 ? [] : AlignReplacements(text, _lastRunResult, found);

        RebuildMatches();
        RecomputeResultText();
    }

    private void RefreshReverse(ObfuscationEngine engine, string text)
    {
        Matches.Clear();
        _lastFound = Array.Empty<TextMatch>();
        _alignment = null;

        RunResult result;
        try
        {
            result = engine.Deobfuscate(Encoding.UTF8.GetBytes(text), "eingabe.txt", new RunOptions());
        }
        catch (Exception ex) when (ex is ConfigurationException or MappingConflictException or MappingLockedException)
        {
            ResultText = text;
            MatchSummary = ex.Message;
            return;
        }

        _lastRunResult = Encoding.UTF8.GetString(result.Content);
        ResultText = _lastRunResult;

        // Die Rueckuebersetzung findet ueber die Ersetzungstabelle statt,
        // nicht ueber Textregeln -- es gibt darum keine Fundstellenliste zum
        // Abwaehlen, nur die Anzahl der zurueckgefuehrten Pseudonyme.
        var restored = result.Report.RuleHits.GetValueOrDefault("freitext");
        MatchSummary = restored switch
        {
            0 => "Keine bekannten Pseudonyme gefunden.",
            1 => "1 Pseudonym zurückübersetzt.",
            var n => $"{n} Pseudonyme zurückübersetzt.",
        };
    }

    private void RebuildMatches()
    {
        Matches.Clear();

        for (var i = 0; i < _lastFound.Count; i++)
        {
            var match = _lastFound[i];
            var canToggle = _alignment is not null;
            var replacement = canToggle ? _alignment![i] : match.Value;

            var eintrag = new TextMatchViewModel(
                match.Value, replacement, match.Rule.Name, canToggle, RequestAlwaysReplace);
            eintrag.IncludedChanged += RecomputeResultText;
            Matches.Add(eintrag);
        }

        MatchSummary = BuildSummary(_lastFound);
    }

    private static string BuildSummary(IReadOnlyList<TextMatch> found)
    {
        if (found.Count == 0)
            return "Keine Funde im Text.";

        var gruppen = found
            .GroupBy(match => match.Rule.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(gruppe => gruppe.Count())
            .Select(gruppe => $"{gruppe.Count()}× {gruppe.Key}");

        return "Gefunden: " + string.Join(" · ", gruppen);
    }

    /// <summary>
    /// Baut den angezeigten Ergebnistext aus dem letzten Lauf und dem
    /// Haekchenstand zusammen -- ohne die Engine erneut zu bemuehen. Ein
    /// abgewaehlter Fund erscheint darin wieder im Original.
    ///
    /// Gebaut wird gegen <see cref="_lastRunInput"/>, nicht gegen
    /// <see cref="_inputText"/>: die Funde tragen Positionen im Text des
    /// Laufs. Wird waehrend der Entprellung weiter getippt und in derselben
    /// Zeitspanne ein Haekchen umgeschaltet, zeigen die Positionen sonst in
    /// einen Text, den es so nicht mehr gibt -- bei gekuerztem Text hinter
    /// dessen Ende.
    /// </summary>
    private void RecomputeResultText()
    {
        if (_lastFound.Count == 0 || _alignment is null)
        {
            ResultText = _lastRunResult;
            return;
        }

        var text = _lastRunInput;
        var builder = new StringBuilder(text.Length);
        var position = 0;

        for (var i = 0; i < _lastFound.Count; i++)
        {
            var match = _lastFound[i];
            var ersatz = Matches[i].IsIncluded ? Matches[i].Replacement : match.Value;

            builder.Append(text, position, match.Start - position);
            builder.Append(ersatz);
            position = match.End;
        }

        builder.Append(text, position, text.Length - position);
        ResultText = builder.ToString();
    }

    /// <summary>
    /// Liest zu jedem Fund seinen Ersatzwert aus dem einen Lauf heraus. Die
    /// unveraenderten Textstuecke zwischen zwei Funden stehen unveraendert
    /// auch im Ergebnistext und dienen als Anker: der Ersatzwert ist das, was
    /// im Ergebnistext zwischen zwei aufeinanderfolgenden Ankern steht.
    ///
    /// Liefert <c>null</c>, wenn sich ein Anker nicht wiederfindet -- etwa
    /// weil ein erzeugtes Pseudonym zufaellig denselben Text enthaelt wie das
    /// unveraenderte Stueck danach. Der angezeigte Ergebnistext bleibt davon
    /// unberuehrt (er stammt unveraendert aus dem Lauf selbst); nur die
    /// Haekchen je Fund liessen sich dann nicht auswerten.
    /// </summary>
    /// <remarks>
    /// Oeffentlich und ohne Zustand, damit sich die Randfaelle einzeln pruefen
    /// lassen -- ueber die Oberflaeche waeren sie nur schwer herbeizufuehren.
    /// </remarks>
    public static string[]? AlignReplacements(
        string original, string transformed, IReadOnlyList<TextMatch> matches)
    {
        var ersatzwerte = new string[matches.Count];
        var originalPos = 0;
        var ausgabePos = 0;

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var praefixLaenge = match.Start - originalPos;
            if (praefixLaenge < 0 || ausgabePos + praefixLaenge > transformed.Length)
                return null;

            ausgabePos += praefixLaenge;

            var ankerEnde = i + 1 < matches.Count ? matches[i + 1].Start : original.Length;
            var anker = original[match.End..ankerEnde];

            int ersatzEnde;
            if (anker.Length == 0)
            {
                // Kein Anker vorhanden. Reicht der Fund bis zum Textende, ist
                // der Rest der Ausgabe sein Ersatzwert. Schliesst dagegen der
                // naechste Fund unmittelbar an -- FindMatches laesst das zu,
                // sobald ein Kandidat genau dort beginnt, wo der vorige endet
                // -- ist nicht zu entscheiden, wo der eine Ersatzwert aufhoert
                // und der naechste anfaengt. Dann lieber gar keine Zuordnung
                // als eine falsche: eine falsche zerlegte beim Abwaehlen den
                // Text, waehrend ein fehlendes Alignment nur die Haekchen
                // sperrt und den Ergebnistext unangetastet laesst.
                if (ankerEnde != original.Length)
                    return null;

                ersatzEnde = transformed.Length;
            }
            else
            {
                var gefunden = transformed.IndexOf(anker, ausgabePos, StringComparison.Ordinal);
                if (gefunden < 0)
                    return null;

                ersatzEnde = gefunden;
            }

            if (ersatzEnde < ausgabePos)
                return null;

            ersatzwerte[i] = transformed[ausgabePos..ersatzEnde];

            ausgabePos = ersatzEnde + anker.Length;
            originalPos = ankerEnde;
        }

        return ersatzwerte;
    }

    /// <summary>
    /// Loest den echten Lauf aus (persistiert neue Eintraege in der Tabelle)
    /// und liefert den Text, der in die Zwischenablage soll.
    ///
    /// Ausgegeben wird immer das Ergebnis <b>dieses</b> Laufs, nie der zuvor
    /// angezeigte Vorschautext. Im Regelfall sind beide identisch -- dafuer
    /// sorgt <see cref="ObfuscationEngine.EnsureMappingStore"/> im
    /// Konstruktor. Schlaegt der Aufruf jedoch fehl (die Tabelle ist von einem
    /// anderen Lauf gesperrt, siehe <see cref="StoreWarning"/>), entsteht in
    /// jedem Probelauf ein fluechtiges Salt: die Vorschau zeigte dann Werte,
    /// die in keiner Tabelle stehen. Wer sie kopierte, koennte die Antwort
    /// spaeter nicht mehr zurueckuebersetzen -- und nichts wuerde ihn warnen.
    /// Deshalb gilt das Ergebnis des echten Laufs, und eine Abweichung wird
    /// sichtbar gemacht, statt sie stillschweigend glattzubuegeln.
    /// </summary>
    public string RunReal()
    {
        // Eine noch ausstehende Entprellung einholen: sonst gehoert der
        // angezeigte -- und gleich kopierte -- Text zum Stand vor der letzten
        // Aenderung, waehrend der Lauf unten den aktuellen Text verarbeitet.
        _debounceCts?.Cancel();
        RefreshPreview();

        if (!_session.TryGetEngine(out var engine, out _) || engine is null)
            return ResultText;

        if (string.IsNullOrEmpty(_inputText))
            return ResultText;

        var bytes = Encoding.UTF8.GetBytes(_inputText);

        try
        {
            var result = IsReverse
                ? engine.Deobfuscate(bytes, "eingabe.txt", new RunOptions())
                : engine.Obfuscate(bytes, "eingabe.txt", new RunOptions());

            var text = Encoding.UTF8.GetString(result.Content);

            if (IsReverse)
            {
                _lastRunResult = text;
                ResultText = text;
            }
            else
            {
                if (text != _lastRunResult)
                {
                    // Der seltene Stoerfall. Die bisherigen Ersatzwerte sind
                    // hinfaellig, die Fundliste wird darum neu aufgebaut --
                    // dabei fallen abgewaehlte Haekchen auf "mitnehmen"
                    // zurueck, was die vorsichtige Richtung ist.
                    _lastRunResult = text;
                    _alignment = _lastFound.Count == 0
                        ? []
                        : AlignReplacements(_lastRunInput, text, _lastFound);
                    RebuildMatches();

                    StoreWarning = "Vorschau und tatsächlicher Lauf sind auseinandergelaufen; "
                                   + "es gilt das Ergebnis des Laufs. Bitte vor der Weitergabe prüfen.";
                }

                RecomputeResultText();
            }
        }
        catch (Exception ex) when (ex is ConfigurationException or MappingConflictException
                                       or MappingLockedException or GenerationException)
        {
            MatchSummary = ex.Message;
            return ResultText;
        }

        _onRealRunCompleted?.Invoke();
        return ResultText;
    }
}

/// <summary>
/// Ein einzelner Fund in der Textansicht: Original, Ersatzwert, die Regel,
/// die gegriffen hat, und ob er fuer diesen Durchgang mitgilt.
/// </summary>
public sealed class TextMatchViewModel : ObservableObject
{
    private bool _isIncluded = true;

    public TextMatchViewModel(
        string original, string replacement, string ruleName, bool canToggle,
        Action<string>? onAlwaysReplace = null)
    {
        Original = original;
        Replacement = replacement;
        RuleName = ruleName;
        CanToggle = canToggle;

        // Eigenes Kommando statt eines gebundenen Aufrufs auf das aeussere
        // Ansichtsmodell: die DataTemplate der Fundstellenliste hat als
        // DataContext genau diesen Fund, nicht die TextViewModel-Instanz --
        // ohne dieses Kommando gaebe es aus der Liste heraus keinen Weg zu
        // "Immer ersetzen…".
        AlwaysReplaceCommand = new RelayCommand(() => onAlwaysReplace?.Invoke(Original));
    }

    public string Original { get; }
    public string Replacement { get; }
    public string RuleName { get; }

    /// <summary>
    /// Ob sich dieser Fund einzeln abwaehlen laesst. Ist er es nicht (die
    /// Ersatzwert-Zuordnung liess sich fuer diesen Lauf nicht herstellen),
    /// bleibt das Haekchen ohne Wirkung auf den Ergebnistext.
    /// </summary>
    public bool CanToggle { get; }

    /// <summary>Legt aus diesem Fund eine dauerhafte Regel an (siehe <see cref="AlwaysReplaceViewModel"/>).</summary>
    public RelayCommand AlwaysReplaceCommand { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value))
                IncludedChanged?.Invoke();
        }
    }

    internal event Action? IncludedChanged;
}
