using Obfuskation.Core;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Ein Feld der geoeffneten Datei und die Regel, die darauf greift.
///
/// Traegt die Regel aus dem Profil, sofern es eine gibt. Steht das Feld noch
/// ohne, wird beim ersten Setzen einer Behandlung eine Regel angelegt und ins
/// Profil aufgenommen — der Anwender soll nicht erst eine Regel erzeugen
/// muessen, um sie dann auszufuellen.
/// </summary>
public sealed class FieldRuleViewModel : ObservableObject
{
    private readonly Profile _profile;
    private readonly Action _onChanged;

    private FieldRule? _rule;
    private FieldAction _action;
    private string? _generator;
    private IReadOnlyList<string> _sampleValues;
    private string? _preview;
    private bool _previewIsExample;

    public FieldRuleViewModel(
        Profile profile, FieldAnalysis analysis, IReadOnlyList<string>? sampleValues, Action onChanged)
    {
        _profile = profile;
        _onChanged = onChanged;

        FieldName = analysis.FieldName;
        _action = analysis.Action;
        _sampleValues = sampleValues ?? Array.Empty<string>();

        // Nur eine eigene Regel wird uebernommen; greift bloss die Vorgabe,
        // bleibt das Feld regellos, bis der Anwender etwas festlegt.
        //
        // Der Generator ebenso: die Regelaufloesung liefert fuer ein Feld ohne
        // Regel "token" als Platzhalter mit. Wuerde der hier uebernommen, hielte
        // das Ansichtsmodell ihn faelschlich fuer eine getroffene Wahl und der
        // Vorschlag aus dem Feldnamen kaeme nie zum Zuge.
        if (!analysis.IsFromDefault)
        {
            _generator = analysis.Generator;
            _rule = FindRule(analysis.FieldName);
        }
    }

    public string FieldName { get; }

    /// <summary>
    /// Bis zu drei Werte aus der Datei, fuer die Anzeige des Feldinhalts.
    /// </summary>
    public IReadOnlyList<string> SampleValues
    {
        get => _sampleValues;
        set
        {
            _sampleValues = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SampleValue));
            OnPropertyChanged(nameof(HasSampleValues));
        }
    }

    /// <summary>
    /// Der erste Beispielwert, an dem sich die Ersetzung zeigen laesst.
    ///
    /// Die Vorschau zeigt bewusst nur diesen einen Wert: mit drei Pseudonymen
    /// neben drei Klartexten waere die Karte kaum noch lesbar.
    /// </summary>
    public string? SampleValue => _sampleValues.Count > 0 ? _sampleValues[0] : null;

    /// <summary>
    /// Ob ueberhaupt ein Beispielwert vorliegt — unabhaengig davon, ob daraus
    /// auch eine Vorschau entsteht. Ein Feld, das noch auf "error" steht oder
    /// dessen Generator keine Vorschau liefert, braucht die Anzeige des
    /// Inhalts am dringendsten: genau dort ist die Entscheidung ja noch offen.
    /// </summary>
    public bool HasSampleValues => _sampleValues.Count > 0;

    public FieldAction Action
    {
        get => _action;
        set
        {
            if (!SetProperty(ref _action, value))
                return;

            EnsureRule();
            _rule!.Action = value;

            // Beim Wechsel auf "pseudonymize" braucht es einen Generator;
            // ohne ihn wuerde die Profilpruefung sofort anschlagen.
            if (value == FieldAction.Pseudonymize && string.IsNullOrWhiteSpace(_generator))
                Generator = SuggestGenerator();

            if (value != FieldAction.Pseudonymize)
                _rule.Generator = null;

            OnPropertyChanged(nameof(IsDecided));
            OnPropertyChanged(nameof(StatusSymbol));
            OnPropertyChanged(nameof(SelectedAction));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(NeedsGenerator));
            OnPropertyChanged(nameof(SelectedGenerator));
            OnPropertyChanged(nameof(ShowPrefix));
            OnPropertyChanged(nameof(Prefix));
            _onChanged();
        }
    }

    public string? Generator
    {
        get => _generator;
        set
        {
            if (!SetProperty(ref _generator, value))
                return;

            EnsureRule();
            _rule!.Generator = value;

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(SelectedGenerator));
            OnPropertyChanged(nameof(ShowPrefix));
            OnPropertyChanged(nameof(Prefix));
            OnPropertyChanged(nameof(PrefixIsShared));
            _onChanged();
        }
    }

    /// <summary>Ob eine Entscheidung feststeht. Sonst bricht ein Lauf hier ab.</summary>
    public bool IsDecided => _action != FieldAction.Error;

    public bool NeedsGenerator => _action == FieldAction.Pseudonymize;

    /// <summary>
    /// Ob die Kennzeichnung (das Praefix) angezeigt werden soll: nur bei
    /// "pseudonymize" mit einem Generator, der auf <c>token</c> beruht —
    /// eingebaut oder ein eigener Namensraum mit <c>Type == "token"</c>. Jeder
    /// andere Generator liefert das Format seines Wertes (IBAN, Datum, ...),
    /// ein Praefix wuerde das zerstoeren; das prueft schon der
    /// <see cref="ProfileValidator"/>, hier geht es nur um die Sichtbarkeit.
    /// </summary>
    public bool ShowPrefix => _action == FieldAction.Pseudonymize && IsTokenBasedGenerator();

    /// <summary>
    /// Die Kennzeichnung, die dem erzeugten Pseudonym vorangestellt wird.
    /// Leer bzw. <c>null</c> heisst: kein Praefix. Gelesen und geschrieben
    /// wird direkt am <see cref="GeneratorSettings"/>-Eintrag des aktuellen
    /// Generators, nicht an der Feldregel — das Praefix haengt am
    /// Namensraum (siehe A4), nicht am Feld.
    /// </summary>
    public string? Prefix
    {
        get => _generator is not null && _profile.Generators.TryGetValue(_generator, out var settings)
            ? settings.Prefix
            : null;
        set => SetPrefix(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>
    /// Ob der aktuelle Namensraum noch von einer anderen Feld- oder Textregel
    /// verwendet wird. Eine Aenderung der Kennzeichnung trifft dann auch
    /// jene Regeln mit — das muss sichtbar sein, bevor jemand versehentlich
    /// ein fremdes Pseudonymformat aendert.
    /// </summary>
    public bool PrefixIsShared
    {
        get
        {
            if (_generator is null || string.Equals(_generator, "token", StringComparison.OrdinalIgnoreCase))
                return false;

            var vonAnderenFeldern = _profile.Fields.Any(regel =>
                !ReferenceEquals(regel, _rule)
                && string.Equals(regel.Generator, _generator, StringComparison.OrdinalIgnoreCase));

            var vonTextregeln = _profile.TextRules.Any(regel =>
                string.Equals(regel.Generator, _generator, StringComparison.OrdinalIgnoreCase));

            return vonAnderenFeldern || vonTextregeln;
        }
    }

    /// <summary>
    /// Setzt oder loescht die Kennzeichnung. Steht die Regel noch auf dem
    /// eingebauten <c>token</c>, entsteht dabei ein neuer, eigener
    /// Namensraum — das Praefix am eingebauten Generator zu setzen traefe
    /// jedes andere Feld mit, das ebenfalls schlicht <c>token</c> verwendet.
    /// Zeigt die Regel schon auf einen eigenen Namensraum, wird dessen
    /// Praefix aktualisiert; ein leerer Wert entfernt nur das Praefix, nicht
    /// den Namensraum selbst, damit andere Regeln, die ihn referenzieren,
    /// nicht ins Leere laufen.
    /// </summary>
    private void SetPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(_generator) || !IsTokenBasedGenerator())
            return;

        if (string.Equals(_generator, "token", StringComparison.OrdinalIgnoreCase))
        {
            // Ohne Wert gibt es nichts einzurichten -- "token" ohne Praefix
            // ist bereits der Ausgangszustand, ein leerer Namensraum waere
            // nur unnoetiger Ballast im Profil.
            if (value is null)
                return;

            var key = ProfileScaffolder.ToGeneratorKey(FieldName);
            _profile.Generators[key] = new GeneratorSettings { Type = "token", Prefix = value };

            // Setzt zugleich die Regel um und loest ueber den Generator-Setter
            // bereits _onChanged() sowie die Benachrichtigungen fuer Prefix
            // und PrefixIsShared aus.
            Generator = key;
            return;
        }

        _profile.Generators[_generator].Prefix = value;

        OnPropertyChanged(nameof(Prefix));
        OnPropertyChanged(nameof(PrefixIsShared));
        _onChanged();
    }

    /// <summary>
    /// Ob der aktuelle Generator (eingebaut oder eigener Namensraum) auf dem
    /// Basistyp <c>token</c> beruht.
    /// </summary>
    private bool IsTokenBasedGenerator()
    {
        if (string.IsNullOrWhiteSpace(_generator))
            return false;

        if (string.Equals(_generator, "token", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!_profile.Generators.TryGetValue(_generator, out var settings))
            return false;

        var baseName = string.IsNullOrWhiteSpace(settings.Type) ? _generator : settings.Type;
        return string.Equals(baseName, "token", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Statuspunkt der Feldliste.</summary>
    public string StatusSymbol => IsDecided ? "●" : "○";

    /// <summary>Knappe Beschreibung fuer die Feldliste.</summary>
    public string Summary => _action switch
    {
        FieldAction.Pseudonymize => _generator ?? "?",
        FieldAction.Passthrough => "unverändert",
        FieldAction.Redact => "***",
        FieldAction.Drop => "entfernt",
        FieldAction.ScanText => "Freitext",
        _ => "offen",
    };

    /// <summary>
    /// Die gewaehlte Behandlung als Listeneintrag. Die Auswahlliste zeigt
    /// deutsche Bezeichnungen statt der englischen Namen aus der
    /// Konfigurationsdatei — dort bleiben sie, wie sie sind.
    /// </summary>
    public ActionOption SelectedAction
    {
        get => ActionOption.For(_action);
        set => Action = value.Action;
    }

    /// <summary>Der gewaehlte Generator als Listeneintrag mit Erklaerung.</summary>
    public GeneratorOption? SelectedGenerator
    {
        get => GeneratorOption.Find(_profile, _generator);
        set => Generator = value?.Name;
    }

    /// <summary>Das vorgeschaute Pseudonym, oder <c>null</c>.</summary>
    public string? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>
    /// Ob die Vorschau nur beispielhaft ist, weil es noch keine
    /// Ersetzungstabelle gibt. Der Anwender muss das wissen — sonst wundert er
    /// sich, dass der echte Lauf andere Werte liefert.
    /// </summary>
    public bool PreviewIsExample
    {
        get => _previewIsExample;
        private set => SetProperty(ref _previewIsExample, value);
    }

    public bool HasPreview => !string.IsNullOrEmpty(_preview);

    /// <summary>
    /// Kartentitel je nach Zustand: ohne Vorschau nur der Inhalt, mit Vorschau
    /// beides. Sagt dem Anwender vorab, ob er gleich ein Pseudonym sieht oder
    /// nur den Rohwert.
    /// </summary>
    public string SampleCardTitle => HasPreview ? "Feldinhalt und Vorschau" : "Feldinhalt";

    /// <summary>Erneuert die Vorschau. Fehler bleiben stumm — es ist nur eine Vorschau.</summary>
    public void RefreshPreview(ObfuscationEngine? engine)
    {
        if (engine is null || _action != FieldAction.Pseudonymize
            || string.IsNullOrWhiteSpace(_generator) || string.IsNullOrEmpty(SampleValue))
        {
            Preview = null;
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(SampleCardTitle));
            return;
        }

        try
        {
            Preview = engine.PreviewValue(_generator, SampleValue);
            PreviewIsExample = !engine.MappingStoreExists;
        }
        catch (Exception ex) when (ex is Core.Generation.GenerationException
                                       or Core.Mapping.MappingConflictException
                                       or Core.Mapping.MappingLockedException
                                       or ConfigurationException
                                       or IOException)
        {
            // Etwa ein Datumsfeld, dessen Beispielwert kein Datum ist. Der
            // eigentliche Lauf meldet das deutlich; hier genuegt es, keine
            // Vorschau zu zeigen.
            Preview = null;
        }

        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(SampleCardTitle));
    }

    /// <summary>Legt bei Bedarf eine Regel an und haengt sie ins Profil.</summary>
    private void EnsureRule()
    {
        if (_rule is not null)
            return;

        _rule = new FieldRule
        {
            Match = FieldName,
            MatchType = FieldMatchType.Exact,
            Action = _action,
            Generator = _generator,
        };

        // Vorn einfuegen: die erste passende Regel gewinnt, und eine eben vom
        // Anwender getroffene Entscheidung soll eine allgemeinere Regel
        // uebersteuern, nicht von ihr verdeckt werden.
        _profile.Fields.Insert(0, _rule);
    }

    private FieldRule? FindRule(string fieldName)
        => _profile.Fields.FirstOrDefault(rule =>
            rule.MatchType == FieldMatchType.Exact
            && string.Equals(rule.Match, fieldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ein Generator, der zum Feldnamen passt. Nutzt dieselbe Zuordnung wie
    /// <c>init</c>, damit Oberflaeche und Kommandozeile dasselbe vorschlagen.
    /// </summary>
    private string SuggestGenerator()
    {
        var vorschlag = ProfileScaffolder.Suggest(FieldName);

        // "scanText" ist eine Behandlung, kein Generator — als Vorschlag fuer
        // ein Auswahlfeld voller Generatoren taugt es nicht.
        return vorschlag is null or "scanText" ? "token" : vorschlag;
    }
}

/// <summary>Ein Generator, wie er in der Auswahlliste erscheint.</summary>
/// <param name="Name">Der Name, wie er in der Konfigurationsdatei steht.</param>
/// <param name="Description">Kurze deutsche Erklärung.</param>
public sealed record GeneratorOption(string Name, string Description)
{
    /// <summary>Die eingebauten Generatoren.</summary>
    public static IReadOnlyList<GeneratorOption> BuiltIn { get; } =
        Core.Generation.GeneratorRegistry.KnownNames
            .Select(name => new GeneratorOption(name, Core.Generation.GeneratorDescriptions.For(name)))
            .ToList();

    /// <summary>
    /// Die Generatoren eines Profils: die eingebauten und die im Profil selbst
    /// angelegten.
    ///
    /// Die eigenen sind wichtiger, als es aussieht: ein Eintrag unter
    /// <c>generators</c> schafft einen zweiten Namensraum. Erst damit lassen
    /// sich zwei Zahlenfelder trennen, die zufaellig denselben Wert fuehren —
    /// etwa Personennummer 4711 und Belegnummer 4711, die sonst dasselbe
    /// Pseudonym bekaemen und eine Verbindung vortaeuschten, die es nie gab.
    /// </summary>
    public static IReadOnlyList<GeneratorOption> For(Profile profile)
    {
        var eigene = profile.Generators.Keys
            .Where(name => !Core.Generation.GeneratorRegistry.KnownNames
                .Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name =>
            {
                var grundlage = profile.Generators[name].Type;
                var erklaerung = string.IsNullOrWhiteSpace(grundlage)
                    ? "eigener Namensraum"
                    : $"eigener Namensraum, wie {grundlage}";
                return new GeneratorOption(name, erklaerung);
            });

        return BuiltIn.Concat(eigene).ToList();
    }

    /// <summary>
    /// Der Listeneintrag zu einem Generatornamen.
    ///
    /// Gesucht wird in <see cref="For"/>, nicht bloss in <see cref="BuiltIn"/>:
    /// ein Eintrag fuer einen eigenen Namensraum traegt dort die Erklaerung
    /// "eigener Namensraum, wie numericId". Wuerde hier ersatzweise ein neuer
    /// Eintrag mit einer anderen Erklaerung gebaut, waere er — Datensatz mit
    /// Wertvergleich — nicht derselbe wie der in der Auswahlliste, und das
    /// Auswahlfeld bliebe leer, obwohl die Regel einen Generator traegt.
    /// </summary>
    public static GeneratorOption? Find(Profile profile, string? name)
        => name is null ? null : For(profile).FirstOrDefault(option =>
            string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? new GeneratorOption(name, "eigener Namensraum");

    public override string ToString() => Name;
}

/// <summary>Eine Behandlung, wie sie in der Auswahlliste erscheint.</summary>
/// <param name="Action">Der zugrundeliegende Wert.</param>
/// <param name="Label">Die Bezeichnung fuer den Menschen.</param>
/// <param name="Hint">Ein Satz dazu, was das bedeutet.</param>
public sealed record ActionOption(FieldAction Action, string Label, string Hint)
{
    public static IReadOnlyList<ActionOption> All { get; } =
    [
        new(FieldAction.Pseudonymize, "ersetzen",
            "Wert durch ein typgerechtes Pseudonym ersetzen. Umkehrbar."),
        new(FieldAction.Passthrough, "durchlassen",
            "Wert unverändert übernehmen. Nur für Felder ohne Personenbezug."),
        new(FieldAction.ScanText, "Freitext durchsuchen",
            "Textregeln auf den Inhalt anwenden. Findet nur, wofür ein Muster besteht."),
        new(FieldAction.Redact, "schwärzen",
            "Durch *** ersetzen. NICHT umkehrbar."),
        new(FieldAction.Drop, "Feld entfernen",
            "Spalte ganz aus der Ausgabe nehmen. NICHT umkehrbar."),
        new(FieldAction.Error, "offen — Entscheidung fehlt",
            "Solange das so steht, bricht jeder Lauf an diesem Feld ab."),
    ];

    public static ActionOption For(FieldAction action)
        => All.First(option => option.Action == action);

    public override string ToString() => Label;
}
