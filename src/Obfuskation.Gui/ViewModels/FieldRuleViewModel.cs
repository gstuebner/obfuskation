using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly ExtensionLibrary _extensions;
    private readonly Action _onChanged;

    private FieldRule? _rule;
    private FieldAction _action;
    private string? _generator;
    private IReadOnlyList<string> _sampleValues;
    private string? _preview;
    private bool _previewIsExample;

    public FieldRuleViewModel(
        Profile profile, ExtensionLibrary extensions, FieldAnalysis analysis,
        IReadOnlyList<string>? sampleValues, Action onChanged)
    {
        _profile = profile;
        _extensions = extensions;
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
            RaiseOptionVisibilityChanged();
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
            RaiseOptionVisibilityChanged();
            _onChanged();
        }
    }

    /// <summary>
    /// Meldet alle Eigenschaften, die von Generator oder Namensraum abhaengen
    /// -- Sichtbarkeit der Optionen, ihre Werte, die Freigabe-Warnung. Ein
    /// Wechsel des Generators (auch ueber <see cref="EnsureNamespace"/>) kann
    /// jede von ihnen veraendern, darum werden sie gemeinsam an einer Stelle
    /// gemeldet statt an jedem einzelnen Aufrufer wiederholt aufgezaehlt.
    /// </summary>
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
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(SharedNamespaceCount));
        OnPropertyChanged(nameof(NamespaceIsShared));
        OnPropertyChanged(nameof(NamespaceSharedWarning));
    }

    /// <summary>Ob eine Entscheidung feststeht. Sonst bricht ein Lauf hier ab.</summary>
    public bool IsDecided => _action != FieldAction.Error;

    public bool NeedsGenerator => _action == FieldAction.Pseudonymize;

    /// <summary>
    /// Ordnet jede Option ihrem einzig zulaessigen Basistyp zu -- dieselbe
    /// Zuordnung wie <c>ProfileValidator.OptionOwnership</c> in der
    /// Bibliothek. Core-Code ist fuer diese Etappe gesperrt, darum steht die
    /// Zuordnung hier gespiegelt statt von dort gelesen; sie ist aber genau
    /// einmal im GUI-Code hinterlegt und bestimmt von hier aus, wann
    /// Schaltflaeche, Optionsdialog und die inline Kennzeichnung etwas
    /// anzeigen -- nirgends sonst wird diese Zuordnung nachgebaut.
    /// </summary>
    private static readonly Dictionary<string, string> OptionBaseTypes =
        ProfileValidator.OptionOwnership.ToDictionary(
            eintrag => eintrag.Option, eintrag => eintrag.BaseType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Der Basistyp des aktuellen Generators: bei einem eigenen Namensraum
    /// (im Profil oder in der Erweiterung) dessen <c>Type</c> (oder, falls
    /// leer, der Schluessel selbst), sonst der eingebaute Name unmittelbar.
    ///
    /// Das Profil gewinnt bei gleichem Schluessel -- dieselbe Reihenfolge wie
    /// bei <see cref="Core.Generation.GeneratorRegistry.Build"/>. Ohne diesen
    /// Blick in die Erweiterung wuerde ein Feld, dessen Regel auf einen
    /// Erweiterungseintrag wie "assetTag" (Basistyp <c>pattern</c>) zeigt, hier
    /// faelschlich "assetTag" selbst als Basistyp erhalten, und die
    /// Optionsanzeige (etwa die Maske) faende ihn nie.
    /// </summary>
    private string? BaseType
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_generator))
                return null;

            if (_profile.Generators.TryGetValue(_generator, out var settings))
                return string.IsNullOrWhiteSpace(settings.Type) ? _generator : settings.Type;

            if (_extensions.Generators.TryGetValue(_generator, out var erweiterungsSettings))
                return string.IsNullOrWhiteSpace(erweiterungsSettings.Type) ? _generator : erweiterungsSettings.Type;

            return _generator;
        }
    }

    /// <summary>Ob der aktuelle Generator bei "pseudonymize" auf dem genannten Basistyp beruht.</summary>
    private bool IsBaseType(string baseType)
        => _action == FieldAction.Pseudonymize
           && string.Equals(BaseType, baseType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Die Einstellungen des aktuellen Generators, sofern er schon einen
    /// eigenen Namensraum im Profil hat. Bei einem noch unveraenderten
    /// eingebauten Generator gibt es keine -- die Optionsgetter liefern dann
    /// ihre Vorgabe.
    /// </summary>
    private GeneratorSettings? CurrentSettings
        => _generator is not null && _profile.Generators.TryGetValue(_generator, out var settings)
            ? settings
            : null;

    /// <summary>
    /// Ob die Kennzeichnung (das Praefix) angezeigt werden soll: nur bei
    /// "pseudonymize" mit einem Generator, der auf <c>token</c> beruht —
    /// eingebaut oder ein eigener Namensraum mit <c>Type == "token"</c>. Jeder
    /// andere Generator liefert das Format seines Wertes (IBAN, Datum, ...),
    /// ein Praefix wuerde das zerstoeren; das prueft schon der
    /// <see cref="ProfileValidator"/>, hier geht es nur um die Sichtbarkeit.
    /// </summary>
    public bool ShowPrefix => IsBaseType(OptionBaseTypes["prefix"]);

    /// <summary>
    /// Die Kennzeichnung, die dem erzeugten Pseudonym vorangestellt wird.
    /// Leer bzw. <c>null</c> heisst: kein Praefix. Gelesen und geschrieben
    /// wird direkt am <see cref="GeneratorSettings"/>-Eintrag des aktuellen
    /// Generators, nicht an der Feldregel — das Praefix haengt am
    /// Namensraum (siehe A4), nicht am Feld.
    /// </summary>
    public string? Prefix
    {
        get => CurrentSettings?.Prefix;
        set => SetPrefix(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Ob "placeholder" (nur <c>redact</c>) angezeigt werden soll.</summary>
    public bool ShowPlaceholder => IsBaseType(OptionBaseTypes["placeholder"]);

    /// <summary>Eigener Platzhalter fuer <c>redact</c>. Ohne Angabe gilt die Profilvorgabe.</summary>
    public string? Placeholder
    {
        get => CurrentSettings?.Placeholder;
        set => SetOption(s => s.Placeholder = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Ob "from"/"to" (nur <c>dateRange</c>) angezeigt werden sollen.</summary>
    public bool ShowDateRange => IsBaseType(OptionBaseTypes["from"]);

    /// <summary>Untere Grenze des Zeitraums, ISO-Datum. Ohne Angabe gilt das Kalenderjahr des Originals.</summary>
    public string? From
    {
        get => CurrentSettings?.From;
        set => SetOption(s => s.From = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Obere Grenze des Zeitraums, ISO-Datum.</summary>
    public string? To
    {
        get => CurrentSettings?.To;
        set => SetOption(s => s.To = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Ob "granularity" (nur <c>dateGeneralize</c>) angezeigt werden soll.</summary>
    public bool ShowGranularity => IsBaseType(OptionBaseTypes["granularity"]);

    /// <summary>Die Rundungsstufe fuer <c>dateGeneralize</c>, als Listeneintrag. Ohne Angabe "month".</summary>
    public GranularityOption SelectedGranularity
    {
        get => GranularityOption.For(CurrentSettings?.Granularity);
        // Wie beim Maskenzeichen: "month" ist die Vorgabe und wird nicht als
        // gesetzter Wert zurueckgeschrieben.
        set => SetOption(s => s.Granularity =
            value.Value == GranularityOption.Default ? null : value.Value);
    }

    /// <summary>Ob "pattern" (nur <c>pattern</c>) angezeigt werden soll.</summary>
    public bool ShowPatternMask => IsBaseType(OptionBaseTypes["pattern"]);

    /// <summary>Die Zeichenmaske. Ohne Angabe wird sie aus dem Original abgeleitet.</summary>
    public string? Pattern
    {
        get => CurrentSettings?.Pattern;
        set => SetOption(s => s.Pattern = string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>Ob "values" (nur <c>wordlist</c>) angezeigt werden soll.</summary>
    public bool ShowWordlist => IsBaseType(OptionBaseTypes["values"]);

    /// <summary>
    /// Die Werteliste als mehrzeiliger Text, ein Wert je Zeile -- fuer ein
    /// einfaches Textfeld im Dialog statt einer eigenen Listenbearbeitung.
    /// </summary>
    public string ValuesText
    {
        get => CurrentSettings?.Values is { } values ? string.Join('\n', values) : "";
        set => SetOption(s => s.Values = SplitValues(value));
    }

    /// <summary>Ob "keepFirst"/"keepLast"/"maskChar" (nur <c>partialMask</c>) angezeigt werden sollen.</summary>
    public bool ShowPartialMask => IsBaseType(OptionBaseTypes["keepFirst"]);

    /// <summary>Anzahl der am Anfang sichtbar bleibenden Zeichen.</summary>
    public int KeepFirst
    {
        get => CurrentSettings?.KeepFirst ?? 0;
        set => SetOption(s => s.KeepFirst = value);
    }

    /// <summary>
    /// Anzahl der am Ende sichtbar bleibenden Zeichen. Solange beide,
    /// <see cref="KeepFirst"/> und <see cref="KeepLast"/>, auf 0 stehen, gilt
    /// die Vorgabe von vier sichtbaren Endstellen; sobald eine der beiden
    /// gesetzt wird, zaehlt nur noch das tatsaechlich Eingestellte (siehe
    /// Hinweistext im Dialog).
    /// </summary>
    public int KeepLast
    {
        get => CurrentSettings?.KeepLast ?? 0;
        set => SetOption(s => s.KeepLast = value);
    }

    /// <summary>Maskierungszeichen, genau ein Zeichen. Ohne Angabe '*'.</summary>
    public string MaskChar
    {
        get => CurrentSettings?.MaskChar ?? "*";
        // Der angezeigte Stern ist die Vorgabe, kein gesetzter Wert. Er wird
        // deshalb wieder auf "nicht gesetzt" zurueckgefuehrt -- sonst schriebe
        // schon das blosse Anzeigen ihn als echte Einstellung ins Profil.
        set => SetOption(s => s.MaskChar = string.IsNullOrEmpty(value) || value == "*" ? null : value);
    }

    /// <summary>
    /// Ob der gewaehlte Generator ueberhaupt Optionen kennt -- steuert, ob
    /// die Schaltflaeche zum Optionsdialog neben der Generator-Auswahl
    /// erscheint.
    /// </summary>
    public bool HasOptions
        => ShowPrefix || ShowPlaceholder || ShowDateRange || ShowGranularity
           || ShowPatternMask || ShowWordlist || ShowPartialMask;

    /// <summary>
    /// Anzahl anderer Feld- oder Textregeln, die denselben Namensraum
    /// verwenden. Eine Optionsaenderung (Praefix, Maske, Werteliste, ...)
    /// trifft diese Regeln mit -- das muss sichtbar sein, bevor jemand
    /// versehentlich ein fremdes Pseudonymformat aendert. Ein noch nicht
    /// angelegter eigener Namensraum zaehlt nicht: der eingebaute Generator
    /// (schlicht "token" etc.) gilt nicht als "geteilter Namensraum" im Sinn
    /// dieser Warnung.
    /// </summary>
    public int SharedNamespaceCount
    {
        get
        {
            if (_generator is null || CurrentSettings is null)
                return 0;

            var vonAnderenFeldern = _profile.Fields.Count(regel =>
                !ReferenceEquals(regel, _rule)
                && string.Equals(regel.Generator, _generator, StringComparison.OrdinalIgnoreCase));

            var vonTextregeln = _profile.TextRules.Count(regel =>
                string.Equals(regel.Generator, _generator, StringComparison.OrdinalIgnoreCase));

            return vonAnderenFeldern + vonTextregeln;
        }
    }

    /// <summary>Ob <see cref="SharedNamespaceCount"/> groesser als 0 ist.</summary>
    public bool NamespaceIsShared => SharedNamespaceCount > 0;

    /// <summary>Anwendersichtbarer Text zur Namensraum-Freigabe, fuer den Optionsdialog und die Karte.</summary>
    public string NamespaceSharedWarning => SharedNamespaceCount switch
    {
        1 => "Dieser Namensraum wird noch von 1 weiteren Regel verwendet.",
        var n => $"Dieser Namensraum wird noch von {n} weiteren Regeln verwendet.",
    };

    /// <summary>
    /// Setzt oder loescht die Kennzeichnung. Steht die Regel noch auf dem
    /// eingebauten <c>token</c>, entsteht dabei ein neuer, eigener
    /// Namensraum — das Praefix am eingebauten Generator zu setzen traefe
    /// jedes andere Feld mit, das ebenfalls schlicht <c>token</c> verwendet.
    /// Zeigt die Regel schon auf einen eigenen Namensraum, wird dessen
    /// Praefix aktualisiert; ein leerer Wert entfernt nur das Praefix, nicht
    /// den Namensraum selbst, damit andere Regeln, die ihn referenzieren,
    /// nicht ins Leere laufen. Diese Sonderbehandlung -- kein Namensraum fuer
    /// einen leeren Wert -- gilt nur fuer das Praefix; jede andere Option
    /// legt beim Setzen immer einen Namensraum an (siehe <see cref="SetOption"/>).
    /// </summary>
    private void SetPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(_generator) || !ShowPrefix)
            return;

        // Ohne Wert gibt es nichts einzurichten -- "token" ohne Praefix ist
        // bereits der Ausgangszustand, ein leerer Namensraum waere nur
        // unnoetiger Ballast im Profil.
        if (value is null && string.Equals(_generator, "token", StringComparison.OrdinalIgnoreCase))
            return;

        var settings = EnsureNamespace();
        settings.Prefix = value;

        OnPropertyChanged(nameof(Prefix));
        OnPropertyChanged(nameof(SharedNamespaceCount));
        OnPropertyChanged(nameof(NamespaceIsShared));
        OnPropertyChanged(nameof(NamespaceSharedWarning));
        _onChanged();
    }

    /// <summary>
    /// Setzt eine Option und meldet die zugehoerigen Aenderungen. Anders als
    /// <see cref="SetPrefix"/> legt jeder Aufruf bei Bedarf sofort einen
    /// eigenen Namensraum an, auch fuer einen leeren Wert -- der Dialog zeigt
    /// die Option ohnehin nur fuer einen Generator, der sie kennt, ein
    /// versehentlich angelegter leerer Namensraum waere hier kein
    /// realistischer Fall.
    /// </summary>
    private void SetOption(Action<GeneratorSettings> anwenden, [CallerMemberName] string? propertyName = null)
    {
        // Ein Setzer, der nichts aendert, darf keinen Namensraum anlegen und
        // das Profil nicht als geaendert markieren. Avalonia schreibt bei
        // NumericUpDown und ComboBox schon beim Oeffnen des Dialogs den
        // geltenden Wert zurueck; ohne diese Pruefung entstuende dabei ein
        // Namensraum, den niemand angelegt hat -- mitsamt der Rueckfrage nach
        // ungespeicherten Aenderungen beim Schliessen.
        var bisher = CurrentSettings ?? NeuerNamensraum();
        var probe = Kopie(bisher);
        anwenden(probe);

        if (Fingerabdruck(bisher) == Fingerabdruck(probe))
            return;

        var settings = EnsureNamespace();
        anwenden(settings);

        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SharedNamespaceCount));
        OnPropertyChanged(nameof(NamespaceIsShared));
        OnPropertyChanged(nameof(NamespaceSharedWarning));
        _onChanged();
    }

    /// <summary>
    /// Sorgt dafuer, dass der aktuelle Generator einen eigenen Namensraum im
    /// Profil hat, und liefert dessen Einstellungen.
    ///
    /// Steht die Regel noch auf einem eingebauten Generator (schlicht
    /// "token", "dateRange", "pattern", ...), wird ueber
    /// <see cref="ProfileScaffolder.ToGeneratorKey"/> ein eigener Namensraum
    /// mit demselben Basistyp angelegt und die Regel darauf umgesetzt. Zeigt
    /// die Regel schon auf einen eigenen Namensraum, wird dessen Eintrag
    /// unveraendert zurueckgegeben.
    ///
    /// Der Namensraum haengt damit immer am <c>generators</c>-Eintrag, nie am
    /// Feldnamen selbst: wuerde eine Option stattdessen aus dem Spaltennamen
    /// abgeleitet, bekaeme derselbe Klartext in zwei Dateien mit
    /// abweichenden Spaltennamen zwei verschiedene Pseudonyme, und die
    /// dateiuebergreifende Verknuepfung braeche (siehe
    /// <c>MultiFileTests.Auch_bei_verschiedenen_Spaltennamen_bleibt_die_Verknuepfung</c>).
    /// </summary>
    /// <summary>
    /// Der Eintrag, den <see cref="EnsureNamespace"/> anlegen wuerde -- als
    /// Vergleichsmassstab dafuer, ob eine Option ueberhaupt etwas aendert.
    /// </summary>
    private GeneratorSettings NeuerNamensraum() => new() { Type = _generator ?? "token" };

    private static GeneratorSettings Kopie(GeneratorSettings settings)
        => JsonSerializer.Deserialize<GeneratorSettings>(JsonSerializer.Serialize(settings))!;

    private static string Fingerabdruck(GeneratorSettings settings) => JsonSerializer.Serialize(settings);

    private GeneratorSettings EnsureNamespace()
    {
        if (_generator is not null && _profile.Generators.TryGetValue(_generator, out var eigene))
            return eigene;

        var key = ProfileScaffolder.ToGeneratorKey(FieldName);
        var settings = NeuerNamensraum();
        _profile.Generators[key] = settings;

        // Setzt zugleich die Regel um und loest ueber den Generator-Setter
        // bereits _onChanged() sowie RaiseOptionVisibilityChanged() aus.
        Generator = key;
        return settings;
    }

    private static List<string> SplitValues(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(zeile => zeile.Length > 0)
            .ToList();

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
        get => GeneratorOption.Find(_profile, _generator, _extensions);
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
    /// Ein Generator, der zum Feldnamen passt. Nutzt denselben
    /// <see cref="FieldNameSuggester"/> wie <c>init</c>, damit Oberflaeche und
    /// Kommandozeile dasselbe vorschlagen -- aus den Spaltenmustern der
    /// Erweiterungsdatei. Ohne Erweiterungsdatei (oder ohne dort hinterlegte
    /// Spaltenmuster) gibt es keinen Vorschlag, und es bleibt beim Ausweichwert.
    /// </summary>
    private string SuggestGenerator()
    {
        var vorschlag = FieldNameSuggester.Suggest(FieldName, _extensions);

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
    /// Die Generatoren eines Profils: die eingebauten, die im Profil selbst
    /// angelegten, und -- sofern eine <paramref name="extensions"/> uebergeben
    /// wird -- die der Erweiterungsdatei, die das Profil nicht selbst
    /// ueberschreibt.
    ///
    /// Die eigenen sind wichtiger, als es aussieht: ein Eintrag unter
    /// <c>generators</c> schafft einen zweiten Namensraum. Erst damit lassen
    /// sich zwei Zahlenfelder trennen, die zufaellig denselben Wert fuehren —
    /// etwa Personennummer 4711 und Belegnummer 4711, die sonst dasselbe
    /// Pseudonym bekaemen und eine Verbindung vortaeuschten, die es nie gab.
    ///
    /// Erweiterungseintraege erscheinen mit einem Herkunftszusatz ("aus der
    /// Erweiterung") statt "eigener Namensraum" -- sie sind nicht Teil dieses
    /// Profils, sondern werden nur zur Laufzeit hineingemischt (siehe
    /// <see cref="ExtensionLibrary"/>). Ueberschreibt das Profil einen
    /// Erweiterungseintrag mit gleichem Schluessel, gewinnt der Profileintrag —
    /// dieselbe Reihenfolge wie in <see cref="Core.Generation.GeneratorRegistry.Build"/>.
    /// </summary>
    public static IReadOnlyList<GeneratorOption> For(Profile profile, ExtensionLibrary? extensions = null)
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

        var erweiterung = (extensions?.Generators.Keys ?? Enumerable.Empty<string>())
            .Where(name => !profile.Generators.ContainsKey(name)
                && !Core.Generation.GeneratorRegistry.KnownNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name =>
            {
                var grundlage = extensions!.Generators[name].Type;
                var erklaerung = string.IsNullOrWhiteSpace(grundlage)
                    ? "aus der Erweiterung"
                    : $"aus der Erweiterung, wie {grundlage}";
                return new GeneratorOption(name, erklaerung);
            });

        return BuiltIn.Concat(eigene).Concat(erweiterung).ToList();
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
    public static GeneratorOption? Find(Profile profile, string? name, ExtensionLibrary? extensions = null)
        => name is null ? null : For(profile, extensions).FirstOrDefault(option =>
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

/// <summary>Eine Rundungsstufe fuer <c>dateGeneralize</c>, wie sie im Optionsdialog erscheint.</summary>
/// <param name="Value">Der Wert, wie er in der Konfigurationsdatei steht.</param>
/// <param name="Label">Die Bezeichnung fuer den Menschen.</param>
public sealed record GranularityOption(string Value, string Label)
{
    public static IReadOnlyList<GranularityOption> All { get; } =
    [
        new("month", "Monat"),
        new("quarter", "Quartal"),
        new("year", "Jahr"),
    ];

    /// <summary>Die Rundungsstufe, die ohne eigene Angabe gilt.</summary>
    public const string Default = "month";

    /// <summary>Der Listeneintrag zu einem Wert. Ohne Angabe (<c>null</c>) gilt "month".</summary>
    public static GranularityOption For(string? value)
        => All.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
           ?? All[0];

    public override string ToString() => Label;
}
