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
    private readonly Action<string>? _onRemoveRuleRequested;
    private readonly TimeSpan _debounceDelay;

    /// <summary>
    /// Die Namen der eingebauten Regeln. Ein Fund, dessen Regel nicht dazu
    /// gehoert, stammt aus einer selbst angelegten -- nur die laesst sich in
    /// der Fundliste wieder entfernen (siehe <see cref="TextMatchViewModel.IsUserRule"/>).
    /// </summary>
    private static readonly HashSet<string> EingebauteRegeln =
        new(ProfileScaffolder.DefaultTextRules().Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

    private string _inputText = "";
    private string _resultText = "";
    private string _matchSummary = "";
    private IReadOnlyList<TextSegment> _inputSegments = [];
    private IReadOnlyList<TextSegment> _resultSegments = [];
    private bool _isEditing = true;
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
        Action<string>? onRemoveRuleRequested = null,
        TimeSpan? debounceDelay = null)
    {
        _session = session;
        _direction = initialDirection;
        _onRealRunCompleted = onRealRunCompleted;
        _onAlwaysReplaceRequested = onAlwaysReplaceRequested;
        _onRemoveRuleRequested = onRemoveRuleRequested;
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
    /// der Fundzeile untergehen. Ueber <see cref="ReportViewError"/> landen
    /// hier auch Fehler aus der Ansicht selbst (Zwischenablage, Datei-IO).
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

    /// <summary>
    /// Meldet einen Fehler aus dem Codebehind (Zwischenablage, Datei-IO) in der
    /// Warnzeile der Ansicht. Der Weg ueber <see cref="StoreWarning"/> ist
    /// bewusst gewaehlt: sie ist die einzige dauerhaft sichtbare Meldungszeile.
    /// </summary>
    public void ReportViewError(string message) => StoreWarning = message;

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

    /// <summary>
    /// Der Eingabetext des letzten Laufs, zerlegt in hervorhebbare Abschnitte:
    /// die Prueffassung der linken Seite. Erst sie beantwortet die Frage, an
    /// der die ganze Ansicht haengt -- was wurde erkannt, und was eben nicht.
    /// Farblos heisst: von keiner Regel erfasst, ginge so hinaus.
    /// </summary>
    public IReadOnlyList<TextSegment> InputSegments
    {
        get => _inputSegments;
        private set => SetProperty(ref _inputSegments, value);
    }

    /// <summary><see cref="ResultText"/> in denselben Abschnitten, parallel gesetzt.</summary>
    public IReadOnlyList<TextSegment> ResultSegments
    {
        get => _resultSegments;
        private set => SetProperty(ref _resultSegments, value);
    }

    /// <summary>
    /// Setzt Ergebnistext und beide Abschnittslisten in einem Zug. Jeder Pfad,
    /// der <see cref="ResultText"/> aendert, laeuft hier durch -- sonst
    /// zeigten die Farben einen Stand, den es nicht mehr gibt.
    ///
    /// Ohne uebergebene Abschnitte entsteht je ein einziger unauffaelliger:
    /// der richtige Ausdruck fuer "hier wurde nichts erkannt oder nichts
    /// zugeordnet".
    /// </summary>
    private void SetResult(
        string text,
        IReadOnlyList<TextSegment>? inputSegments = null,
        IReadOnlyList<TextSegment>? resultSegments = null)
    {
        ResultText = text;
        InputSegments = inputSegments ?? Plain(_lastRunInput);
        ResultSegments = resultSegments ?? Plain(text);
    }

    private static IReadOnlyList<TextSegment> Plain(string text)
        => text.Length == 0 ? [] : [new TextSegment(text, TextSegmentKind.Normal)];

    /// <summary>
    /// Ob die linke Seite gerade das beschreibbare Feld zeigt statt der
    /// farbigen Prueffassung. Ein Eingabefeld kann in Avalonia keine
    /// Hintergruende je Textabschnitt tragen, ein <c>SelectableTextBlock</c>
    /// kein Tippen -- beides zugleich gibt es nicht, also gibt es zwei
    /// Zustaende.
    ///
    /// Solange nichts dasteht, gilt das Bearbeiten: vor einer leeren, nicht
    /// beschreibbaren Flaeche zu stehen waere das denkbar schlechteste
    /// Willkommen.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
                OnPropertyChanged(nameof(EditToggleLabel));
        }
    }

    /// <summary>Aufschrift des Umschalters -- sagt, wohin er fuehrt, nicht wo man ist.</summary>
    public string EditToggleLabel => IsEditing ? "Fertig" : "Bearbeiten";

    /// <summary>
    /// Wechselt zwischen Bearbeiten und Pruefen. Beim Verlassen des
    /// Bearbeitens wird eine ausstehende Entprellung eingeholt (wie in
    /// <see cref="RunReal"/>): sonst gehoerten die Farben zum Stand vor der
    /// letzten Aenderung, waehrend daneben schon der neue Text steht.
    /// </summary>
    public void ToggleEditing()
    {
        if (!IsEditing)
        {
            IsEditing = true;
            return;
        }

        _debounceCts?.Cancel();
        RefreshPreview();
        IsEditing = false;
    }

    /// <summary>
    /// Uebernimmt Text von aussen (Einfuegen, Datei, Ziehen) und zeigt ihn
    /// sofort geprueft an -- ohne Entprellung, denn hier hat niemand getippt,
    /// auf dessen naechsten Tastenschlag zu warten waere.
    /// </summary>
    public void SetInputFromOutside(string text)
    {
        InputText = text;

        _debounceCts?.Cancel();
        RefreshPreview();
        IsEditing = text.Length == 0;
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
            _lastRunInput = "";
            SetResult("");
            MatchSummary = "";
            return;
        }

        if (!_session.TryGetEngine(out var engine, out _) || engine is null)
        {
            Matches.Clear();
            _lastRunInput = text;
            SetResult(text);
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
            _lastRunInput = text;
            SetResult(text);
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
            _lastRunInput = text;
            SetResult(text);
            MatchSummary = ex.Message;
            return;
        }

        _lastRunResult = Encoding.UTF8.GetString(result.Content);

        // Die Rueckuebersetzung kennt keine Fundstellen zum Hervorheben (siehe
        // unten): beide Seiten bleiben einfarbig.
        _lastRunInput = text;
        SetResult(_lastRunResult);

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
                match.Value, replacement, match.Rule.Name, canToggle,
                !EingebauteRegeln.Contains(match.Rule.Name),
                RequestAlwaysReplace, _onRemoveRuleRequested);
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
            // Ohne zugeordnete Ersatzwerte laesst sich nicht sagen, welches
            // Stueck der Ausgabe zu welchem Fund gehoert -- dann lieber gar
            // keine Farbe als eine falsche.
            SetResult(_lastRunResult);
            return;
        }

        var text = _lastRunInput;
        var builder = new StringBuilder(text.Length);
        var eingabe = new List<TextSegment>();
        var ausgabe = new List<TextSegment>();
        var position = 0;

        for (var i = 0; i < _lastFound.Count; i++)
        {
            var match = _lastFound[i];
            var mitnehmen = Matches[i].IsIncluded;
            var ersatz = mitnehmen ? Matches[i].Replacement : match.Value;
            var art = mitnehmen ? TextSegmentKind.Replaced : TextSegmentKind.Excluded;

            if (match.Start > position)
            {
                var dazwischen = text[position..match.Start];
                eingabe.Add(new TextSegment(dazwischen, TextSegmentKind.Normal));
                ausgabe.Add(new TextSegment(dazwischen, TextSegmentKind.Normal));
            }

            // Links steht der Originalwert, rechts der Ersatzwert -- dieselbe
            // Farbe an beiden Stellen, damit sich die eine der anderen
            // zuordnen laesst.
            eingabe.Add(new TextSegment(match.Value, art));
            ausgabe.Add(new TextSegment(ersatz, art));

            builder.Append(text, position, match.Start - position);
            builder.Append(ersatz);
            position = match.End;
        }

        if (position < text.Length)
        {
            var rest = text[position..];
            eingabe.Add(new TextSegment(rest, TextSegmentKind.Normal));
            ausgabe.Add(new TextSegment(rest, TextSegmentKind.Normal));
        }

        builder.Append(text, position, text.Length - position);
        SetResult(builder.ToString(), eingabe, ausgabe);
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
                SetResult(text);
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
/// Wie ein Abschnitt der Textansicht hervorgehoben wird. Dieselbe Einteilung
/// gilt fuer beide Seiten: links am Originalwert, rechts am Ersatzwert.
/// </summary>
public enum TextSegmentKind
{
    /// <summary>
    /// Von keiner Regel erfasst -- also das, was ohne weiteres Zutun im
    /// Klartext hinausginge. Bleibt ohne Farbe: die Abwesenheit von Farbe ist
    /// hier die eigentliche Aussage.
    /// </summary>
    Normal,

    /// <summary>Erkannt und ersetzt (Haekchen an).</summary>
    Replaced,

    /// <summary>Erkannt, aber bewusst im Klartext behalten (Haekchen aus).</summary>
    Excluded,
}

/// <summary>
/// Ein Abschnitt einer der beiden Textseiten. Die Verkettung aller
/// <see cref="Text"/> ergibt wieder genau die jeweilige Seite -- darauf
/// verlaesst sich die Anzeige, und ein Test haelt es fest.
/// </summary>
public sealed record TextSegment(string Text, TextSegmentKind Kind);

/// <summary>
/// Ein einzelner Fund in der Textansicht: Original, Ersatzwert, die Regel,
/// die gegriffen hat, und ob er fuer diesen Durchgang mitgilt.
/// </summary>
public sealed class TextMatchViewModel : ObservableObject
{
    private bool _isIncluded = true;

    public TextMatchViewModel(
        string original, string replacement, string ruleName, bool canToggle,
        bool isUserRule = false,
        Action<string>? onAlwaysReplace = null,
        Action<string>? onRemoveRule = null)
    {
        Original = original;
        Replacement = replacement;
        RuleName = ruleName;
        CanToggle = canToggle;
        IsUserRule = isUserRule;

        // Eigene Kommandos statt gebundener Aufrufe auf das aeussere
        // Ansichtsmodell: die DataTemplate der Fundstellenliste hat als
        // DataContext genau diesen Fund, nicht die TextViewModel-Instanz --
        // ohne sie gaebe es aus der Liste heraus keinen Weg zu
        // "Immer ersetzen…" und keinen zurueck.
        AlwaysReplaceCommand = new RelayCommand(() => onAlwaysReplace?.Invoke(Original));
        RemoveRuleCommand = new RelayCommand(() => onRemoveRule?.Invoke(RuleName));
    }

    public string Original { get; }
    public string Replacement { get; }
    public string RuleName { get; }

    /// <summary>
    /// Ob dieser Fund aus einer selbst angelegten Regel stammt statt aus einer
    /// der eingebauten. Nur dann laesst sie sich hier wieder entfernen -- eine
    /// zu weit geratene eigene Regel (etwa "alles dieser Form" auf einem Datum)
    /// braucht einen Rueckweg, der nicht ueber den Regex-Editor fuehrt.
    /// </summary>
    public bool IsUserRule { get; }

    /// <summary>
    /// Ob sich dieser Fund einzeln abwaehlen laesst. Ist er es nicht (die
    /// Ersatzwert-Zuordnung liess sich fuer diesen Lauf nicht herstellen),
    /// bleibt das Haekchen ohne Wirkung auf den Ergebnistext.
    /// </summary>
    public bool CanToggle { get; }

    /// <summary>Legt aus diesem Fund eine dauerhafte Regel an (siehe <see cref="AlwaysReplaceViewModel"/>).</summary>
    public RelayCommand AlwaysReplaceCommand { get; }

    /// <summary>Loescht die selbst angelegte Regel wieder; nur sinnvoll bei <see cref="IsUserRule"/>.</summary>
    public RelayCommand RemoveRuleCommand { get; }

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
