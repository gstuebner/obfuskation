using System.Diagnostics;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Formats;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core;

/// <summary>Welches Dateiformat verarbeitet wird.</summary>
public enum DataFormat
{
    /// <summary>Aus der Dateiendung bestimmen, sonst Freitext.</summary>
    Auto,
    Csv,
    Json,
    Text,
}

/// <summary>Einstellungen eines einzelnen Laufs.</summary>
public sealed class RunOptions
{
    /// <summary>Unbehandelte Felder fuehren zum Abbruch, unabhaengig vom Profil.</summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Nichts schreiben: weder Ausgabe noch neue Eintraege in der Tabelle. Der
    /// Bericht entsteht trotzdem vollstaendig.
    /// </summary>
    public bool DryRun { get; set; }

    public DataFormat Format { get; set; } = DataFormat.Auto;

    /// <summary>Die Sicherung gegen einen Mapping-Store im Git-Verzeichnis aufheben.</summary>
    public bool AllowUnsafeStore { get; set; }
}

/// <summary>Ergebnis eines Laufs: die erzeugten Daten und der Bericht.</summary>
/// <param name="Content">Die Ausgabedaten.</param>
/// <param name="Report">Der Bericht, frei von Klartexten.</param>
public sealed record RunResult(byte[] Content, RunReport Report);

/// <summary>Wie ein einzelnes Feld nach dem Regelwerk behandelt wuerde.</summary>
/// <param name="FieldName">Spaltenname oder JSON-Eigenschaft.</param>
/// <param name="Action">Die zugeordnete Behandlung.</param>
/// <param name="Generator">Generatorname, falls ersetzt wird.</param>
/// <param name="IsDecided">
/// Ob eine Entscheidung getroffen ist. <c>false</c> heisst: ein Lauf wuerde an
/// diesem Feld abbrechen.
/// </param>
/// <param name="IsFromDefault">Ob keine eigene Regel greift, sondern die Vorgabe.</param>
public sealed record FieldAnalysis(
    string FieldName,
    FieldAction Action,
    string? Generator,
    bool IsDecided,
    bool IsFromDefault);

/// <summary>Struktur einer Datei samt der Behandlung, die jedes Feld erfahren wuerde.</summary>
public sealed record AnalysisResult(InspectedFile File, IReadOnlyList<FieldAnalysis> Fields)
{
    /// <summary>Felder, fuer die noch keine Entscheidung feststeht.</summary>
    // "field" waere hier kein gueltiger Name: seit C# 14 ist es innerhalb eines
    // Eigenschaftsaccessors ein Schluesselwort.
    public IReadOnlyList<FieldAnalysis> Undecided
        => Fields.Where(analysis => !analysis.IsDecided).ToList();
}

/// <summary>
/// Der Einstiegspunkt fuer alle Vorgaenge. Sowohl das Kommandozeilenprogramm als
/// auch eine spaetere grafische Oberflaeche arbeiten ausschliesslich hierueber;
/// die Klasse selbst gibt nichts aus und kennt keine Konsole.
/// </summary>
public sealed class ObfuscationEngine
{
    private readonly Profile _profile;
    private readonly ExtensionLibrary _extensions;

    /// <summary>
    /// Das Profil mit hineingemischten Textregeln der Erweiterungsdatei: eine
    /// Erweiterungsregel gilt genauso wie eine Profilregel, ausser eine
    /// gleichnamige Profilregel ersetzt sie vollstaendig (siehe
    /// <see cref="MergeTextRules"/>). Alles, was Textregeln verarbeitet
    /// (Resolver, ObfuscateTransformer, ScanTransformer, ...) bekommt dieses
    /// Profil statt <see cref="_profile"/>, damit die Vereinigung an genau
    /// einer Stelle entsteht.
    /// </summary>
    private readonly Profile _effectiveProfile;

    public ObfuscationEngine(Profile profile, ExtensionLibrary? extensions = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _extensions = extensions ?? ExtensionLibrary.Load();
        _effectiveProfile = MergeTextRules(_profile, _extensions);

        var issues = ProfileValidator.Validate(profile, _extensions);
        var errors = issues.Where(issue => issue.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new ConfigurationException(
                "Die Konfiguration ist fehlerhaft:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => $"  {e.Path}: {e.Message}")),
                issues);
    }

    /// <summary>Pfad des Mapping-Stores, wie er sich aus dem Profil ergibt.</summary>
    public string ResolveMappingStorePath()
        => PathHelper.ResolveMappingStore(_profile);

    /// <summary>
    /// Sieht sich eine Datei an und ordnet jedem Feld seine Behandlung zu.
    ///
    /// Anders als <see cref="Obfuscate"/> bricht dies bei einem Feld ohne
    /// Entscheidung <b>nicht</b> ab: die Oberflaeche muss den offenen Zustand
    /// gerade anzeigen koennen. Verarbeitet wird dabei nichts — gelesen wird
    /// nur die Struktur, was auch bei grossen Dateien sofort geht.
    /// </summary>
    public AnalysisResult Analyze(byte[] content, string? inputName, RunOptions? options = null)
    {
        options ??= new RunOptions();

        var file = FieldInspector.Inspect(content, inputName, _profile.Input);
        var resolver = CreateResolver(options);

        var fields = new List<FieldAnalysis>(file.FieldNames.Count);
        foreach (var fieldName in file.FieldNames)
        {
            var rule = resolver.Resolve(fieldName);

            fields.Add(new FieldAnalysis(
                fieldName,
                rule.Action,
                rule.Generator,
                IsDecided: rule.Action != FieldAction.Error,
                rule.IsFromDefault));
        }

        return new AnalysisResult(file, fields);
    }

    /// <summary>
    /// Zeigt, welches Pseudonym ein Wert bekaeme — ohne etwas festzuhalten.
    ///
    /// Achtung: existiert noch keine Ersetzungstabelle, wird ein Salt erzeugt
    /// und nicht gespeichert. Der gezeigte Wert ist dann nur beispielhaft und
    /// weicht vom spaeteren echten Lauf ab. <see cref="MappingStoreExists"/>
    /// sagt, welcher der beiden Faelle vorliegt.
    /// </summary>
    public string PreviewValue(string generatorName, string sampleValue)
    {
        if (string.IsNullOrEmpty(sampleValue))
            return sampleValue;

        using var store = MappingStore.Open(
            ResolveMappingStorePath(), _profile.ProfileName,
            readOnly: true, allowInsideGitWorkingTree: true);

        var deriver = new SeedDeriver(store.Salt);
        var generators = GeneratorRegistry.Build(_profile, deriver, _extensions);
        var pseudonymizer = new Pseudonymizer(deriver, generators, store);

        return pseudonymizer.Pseudonymize(generatorName, sampleValue, persist: false);
    }

    /// <summary>
    /// Ob bereits eine Ersetzungstabelle besteht. Ist sie es nicht, sind
    /// Vorschauwerte nur beispielhaft.
    /// </summary>
    public bool MappingStoreExists => File.Exists(ResolveMappingStorePath());

    /// <param name="progress">
    /// Empfaengt Zwischenstaende. Die Meldungen kommen aus dem Hintergrundfaden;
    /// wer sie anzeigt, muss selbst in seinen Faden zurueckwechseln — ein
    /// <c>Progress&lt;T&gt;</c> tut das von sich aus.
    /// </param>
    public Task<RunResult> ObfuscateAsync(byte[] content, string? inputName, RunOptions options,
        CancellationToken cancellationToken = default, IProgress<RunProgress>? progress = null)
        => Task.Run(() => Obfuscate(content, inputName, options, progress, cancellationToken),
            cancellationToken);

    public Task<RunResult> DeobfuscateAsync(byte[] content, string? inputName, RunOptions options,
        CancellationToken cancellationToken = default, IProgress<RunProgress>? progress = null)
        => Task.Run(() => Deobfuscate(content, inputName, options, progress, cancellationToken),
            cancellationToken);

    public Task<RunResult> ScanAsync(byte[] content, string? inputName, RunOptions options,
        CancellationToken cancellationToken = default, IProgress<RunProgress>? progress = null)
        => Task.Run(() => Scan(content, inputName, options, progress, cancellationToken),
            cancellationToken);

    public RunResult Obfuscate(
        byte[] content,
        string? inputName,
        RunOptions options,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = StartReport("obfuscate", inputName);
        var stopwatch = Stopwatch.StartNew();

        // Bei einem Probelauf wird der Store nur gelesen; die Sperre entfaellt,
        // damit ein Probelauf einen echten Lauf nicht behindert.
        using var store = OpenStore(options, readOnly: options.DryRun);
        TransferOpenWarnings(store, report);

        var deriver = new SeedDeriver(store.Salt);
        var generators = GeneratorRegistry.Build(_profile, deriver, _extensions);
        var pseudonymizer = new Pseudonymizer(deriver, generators, store);
        var resolver = CreateResolver(options);

        var transformer = new ObfuscateTransformer(
            _effectiveProfile, resolver, pseudonymizer, new TextRuleEngine(), report, persist: !options.DryRun);

        var result = RunProcessor(content, inputName, options, transformer, report, progress, cancellationToken);

        if (!options.DryRun)
            store.Save();

        report.NewMappings = store.NewEntries;
        report.TotalMappings = store.TotalEntries;
        report.DurationMs = stopwatch.ElapsedMilliseconds;

        return new RunResult(result, report);
    }

    public RunResult Deobfuscate(
        byte[] content,
        string? inputName,
        RunOptions options,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = StartReport("deobfuscate", inputName);
        var stopwatch = Stopwatch.StartNew();

        // Die Rueckabbildung veraendert die Tabelle nicht.
        using var store = OpenStore(options, readOnly: true);
        TransferOpenWarnings(store, report);

        var deriver = new SeedDeriver(store.Salt);
        var generators = GeneratorRegistry.Build(_profile, deriver, _extensions);
        var pseudonymizer = new Pseudonymizer(deriver, generators, store);
        var reverseMapper = new ReverseTextMapper(store, generators);
        var resolver = CreateResolver(options);

        var transformer = new DeobfuscateTransformer(_effectiveProfile, resolver, pseudonymizer, reverseMapper, report);
        var result = RunProcessor(content, inputName, options, transformer, report, progress, cancellationToken);

        // Generatoren mit eigener Umkehrung (heute nur dateShift) rechnen ohne
        // Tabelleneintrag ueber den profilweiten Offset zurueck. Mit einem
        // falschen Profil ist dieser Offset ein anderer Wert, das Ergebnis also
        // ein plausibles, aber falsches Datum -- waehrend alle anderen Spalten
        // korrekt als unbekanntesPseudonym auffallen. Der Bezug laeuft ueber
        // HasIntrinsicInverse statt ueber den Namen "dateShift": ein Profil
        // kann diesen Typ unter einem eigenen Namensraum fuehren, und
        // RuleHits zaehlt unter dem Namen aus der Feldregel, nicht unter dem
        // eingebauten Generatornamen.
        var intrinsicInverseNames = generators.All
            .Where(entry => entry.Value.HasIntrinsicInverse)
            .Select(entry => entry.Key);
        var dateShiftTraf = intrinsicInverseNames.Any(name => report.RuleHits.GetValueOrDefault(name) > 0);
        var unbekanntePseudonyme = report.Findings.Any(f => f.Kind == "unbekanntesPseudonym");
        if (dateShiftTraf && unbekanntePseudonyme)
            report.Warn("datumMitFremdemProfil",
                "Es wurden Datumswerte zurückgerechnet, während andere Werte unbekannt blieben. " +
                "Das deutet auf ein falsches Profil hin: Datumsspalten werden ohne Tabelleneintrag " +
                "über den profilweiten Versatz zurückgerechnet und ergeben dann plausible, aber " +
                "falsche Daten. Bei richtigem Profil muss die Befundzahl 0 sein (außer bei redact " +
                "und drop).");

        report.TotalMappings = store.TotalEntries;
        report.DurationMs = stopwatch.ElapsedMilliseconds;

        return new RunResult(result, report);
    }

    public RunResult Scan(
        byte[] content,
        string? inputName,
        RunOptions options,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = StartReport("scan", inputName);
        var stopwatch = Stopwatch.StartNew();

        using var store = OpenStore(options, readOnly: true);
        TransferOpenWarnings(store, report);

        var resolver = CreateResolver(options);
        var transformer = new ScanTransformer(_effectiveProfile, resolver, new TextRuleEngine(), store, report);

        // Das Ergebnis wird verworfen: geprueft wird, nicht veraendert.
        RunProcessor(content, inputName, options, transformer, report, progress, cancellationToken);

        report.TotalMappings = store.TotalEntries;
        report.DurationMs = stopwatch.ElapsedMilliseconds;

        return new RunResult(content, report);
    }

    private FieldRuleResolver CreateResolver(RunOptions options)
    {
        if (!options.Strict)
            return new FieldRuleResolver(_effectiveProfile);

        // Der strenge Modus wirkt nur auf eine Kopie, damit das geladene Profil
        // unveraendert bleibt und die Oberflaeche es weiter anzeigen kann.
        var strictProfile = CloneWithStrictDefaults(_effectiveProfile);
        return new FieldRuleResolver(strictProfile);
    }

    /// <summary>
    /// Fuehrt die Textregeln der Erweiterungsdatei und des Profils zusammen:
    /// eine Erweiterungsregel gilt, ausser eine gleichnamige Profilregel
    /// ersetzt sie vollstaendig — sie faellt dann ganz weg, statt zusaetzlich
    /// zu gelten. Prioritaeten bleiben unveraendert, die vorhandene
    /// Ueberlappungsaufloesung in <see cref="Detection.TextRuleEngine"/>
    /// braucht keine Anpassung.
    ///
    /// Ohne Erweiterungsregeln wird <paramref name="profile"/> unveraendert
    /// zurueckgegeben — der haeufige Fall bleibt damit ohne zusaetzliche Kopie.
    /// </summary>
    private static Profile MergeTextRules(Profile profile, ExtensionLibrary extensions)
    {
        if (extensions.TextRules.Count == 0)
            return profile;

        var profileNames = new HashSet<string>(
            profile.TextRules.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

        var merged = new List<TextRule>(extensions.TextRules.Count + profile.TextRules.Count);
        merged.AddRange(extensions.TextRules.Where(rule => !profileNames.Contains(rule.Name)));
        merged.AddRange(profile.TextRules);

        // Nur die Textregeln aendern sich; alles andere bleibt dieselbe
        // Referenz wie im Original, damit z. B. CloneWithStrictDefaults und
        // die Defaults-Zugriffe der Transformer unveraendert weiterarbeiten.
        return new Profile
        {
            Version = profile.Version,
            ProfileName = profile.ProfileName,
            Description = profile.Description,
            MappingStore = profile.MappingStore,
            Input = profile.Input,
            Defaults = profile.Defaults,
            Fields = profile.Fields,
            TextRules = merged,
            Generators = profile.Generators,
        };
    }

    private static Profile CloneWithStrictDefaults(Profile profile) => new()
    {
        Version = profile.Version,
        ProfileName = profile.ProfileName,
        MappingStore = profile.MappingStore,
        Input = profile.Input,
        Defaults = new ProfileDefaults
        {
            UnknownField = FieldAction.Error,
            RedactionPlaceholder = profile.Defaults.RedactionPlaceholder,
        },
        Fields = profile.Fields,
        TextRules = profile.TextRules,
        Generators = profile.Generators,
    };

    private MappingStore OpenStore(RunOptions options, bool readOnly)
        => MappingStore.Open(ResolveMappingStorePath(), _profile.ProfileName, readOnly, options.AllowUnsafeStore);

    private byte[] RunProcessor(
        byte[] content, string? inputName, RunOptions options, IRecordTransformer transformer,
        RunReport report, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        var format = ResolveFormat(options.Format, inputName);

        return format switch
        {
            DataFormat.Csv => new CsvProcessor(_profile)
                .Process(content, transformer, report, progress, cancellationToken),
            DataFormat.Json => new JsonProcessor(_profile)
                .Process(content, transformer, report, progress, cancellationToken),
            _ => new PlainTextProcessor(_profile)
                .Process(content, transformer, report, progress, cancellationToken),
        };
    }

    /// <summary>Bestimmt das Format aus der Vorgabe oder der Dateiendung.</summary>
    public static DataFormat ResolveFormat(DataFormat requested, string? inputName)
    {
        if (requested != DataFormat.Auto)
            return requested;

        var extension = string.IsNullOrEmpty(inputName) ? "" : Path.GetExtension(inputName).ToLowerInvariant();

        return extension switch
        {
            ".csv" or ".tsv" => DataFormat.Csv,
            ".json" => DataFormat.Json,
            _ => DataFormat.Text,
        };
    }

    private RunReport StartReport(string command, string? inputName) => new()
    {
        Command = command,
        Input = inputName,
    };

    private static void TransferOpenWarnings(MappingStore store, RunReport report)
    {
        foreach (var warning in store.OpenWarnings)
            report.Warn("mappingStore", warning);
    }
}
