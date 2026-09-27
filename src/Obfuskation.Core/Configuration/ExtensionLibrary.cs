using System.Text.Json;
using System.Text.Json.Serialization;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Configuration;

/// <summary>Woher eine geprueft Erweiterungsdatei stammen wuerde.</summary>
public enum ExtensionOrigin
{
    /// <summary>Verzeichnis der laufenden Programmdatei.</summary>
    ProgramDirectory,

    /// <summary><see cref="PathHelper.ConfigDirectory"/>.</summary>
    ConfigDirectory,
}

/// <summary>
/// Ein Fundort, den <see cref="ExtensionLibrary.ResolvePath"/> geprueft hat,
/// samt dem, was dort vorgefunden wurde.
/// </summary>
/// <param name="Path">Der geprueft Pfad, unabhaengig davon, ob dort etwas liegt.</param>
/// <param name="Origin">Welcher der beiden Fundorte das ist.</param>
/// <param name="Exists">Ob dort ueberhaupt eine Datei liegt.</param>
/// <param name="SkippedAsProfile">
/// Ob die Datei zwar existiert, aber uebergangen wurde, weil
/// <see cref="ProfileStore.LooksLikeProfile"/> zutraf -- dort liegt eine
/// Projektdatei, keine Erweiterungsdatei.
/// </param>
public sealed record ExtensionCandidate(string Path, ExtensionOrigin Origin, bool Exists, bool SkippedAsProfile);

/// <summary>
/// Ergebnis von <see cref="ExtensionLibrary.ResolvePath"/>: der Pfad, der
/// gelten wuerde (<c>null</c>, wenn keiner der Fundorte eine Erweiterungsdatei
/// hergibt), und alle geprueften Orte in Suchreihenfolge -- fuer
/// <c>extensions path</c> und <c>extensions list</c>, die beide begruenden
/// sollen, was sie zeigen.
/// </summary>
public sealed record ExtensionResolution(string? Path, ExtensionOrigin? Origin, IReadOnlyList<ExtensionCandidate> Candidates);

/// <summary>
/// Ob und wohin sich die Erweiterungsdatei gerade schreiben liesse, siehe
/// <see cref="ExtensionLibrary.GetWriteState"/>.
/// </summary>
/// <param name="Path">
/// Der Pfad, der gelten wuerde: entweder die gefundene Datei, oder --
/// existiert keine -- <see cref="PathHelper.ConfigDirectory"/>.
/// </param>
/// <param name="CanWrite">Ob sich unter <see cref="Path"/> tatsaechlich schreiben liesse.</param>
/// <param name="Reason">
/// Erklaerung fuer den Anwender, wenn <see cref="CanWrite"/> falsch ist --
/// sonst <c>null</c>.
/// </param>
public sealed record ExtensionWriteState(string Path, bool CanWrite, string? Reason);

/// <summary>
/// Hauseigene Generatoren, Textregeln und Spaltenmuster an einem von zwei
/// festen Fundorten (siehe <see cref="ResolvePath"/>), die in jedes Profil
/// einfliessen, ohne je in eine Profildatei geschrieben zu werden.
///
/// Der Anlass: wiederkehrende hauseigene Muster (etwa Inventarnummern der Form
/// <c>INV123456</c>) sind Betriebswissen und duerfen nicht in einer Profildatei
/// stehen, die geteilt oder eingecheckt werden koennte. Die Erweiterungsdatei
/// liegt deshalb ausserhalb jedes Profils und wird nur zur Laufzeit
/// dazugemischt — siehe <see cref="Generation.GeneratorRegistry.Build"/>,
/// <see cref="ObfuscationEngine"/>, <see cref="ProfileValidator"/> und
/// <see cref="FieldNameSuggester"/>. Eine Profildatei kennt die
/// Erweiterungsdatei nicht und darf sie auch nie enthalten: wuerde die
/// Oberflaeche die Erweiterungseintraege beim Speichern in das Profil
/// mischen, landeten die Hausmuster in jeder Profildatei — genau das, was
/// diese Trennung verhindern soll.
///
/// Bewusst dieselben Typen wie im Profil (<see cref="GeneratorSettings"/>,
/// <see cref="TextRule"/>): kein zweites Schema, keine zweite Validierung.
///
/// Die Datei laesst sich weiterhin von Hand im Texteditor pflegen; <see cref="Save"/>
/// gibt es allein fuer die Oberflaeche, damit ein hauseigenes Muster nicht nur
/// dem zugaenglich ist, der JSON schreibt. Am Verhaeltnis zum Profil aendert
/// das nichts: geschrieben wird ausschliesslich diese Datei, nie ein Profil,
/// und umgekehrt.
/// </summary>
public sealed class ExtensionLibrary
{
    /// <summary>
    /// Dateiname an beiden Fundorten (siehe <see cref="ResolvePath"/>). Der
    /// unqualifizierte Programmname bezeichnet damit das Hauseigene, wie man
    /// es von ihm erwartet -- das Projektbezogene traegt seit der Umbenennung
    /// den Zusatz "-projekt" (siehe <see cref="ProfileStore.DefaultFileName"/>).
    /// </summary>
    public const string FileName = "obfuskation.json";

    public int Version { get; set; } = 1;

    /// <summary>Generatoren, adressiert ueber ihren Namen — wie <see cref="Profile.Generators"/>.</summary>
    public Dictionary<string, GeneratorSettings> Generators { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textregeln — wie <see cref="Profile.TextRules"/>.</summary>
    public List<TextRule> TextRules { get; set; } = [];

    /// <summary>
    /// Spaltenmuster fuer das Namensraten von <see cref="FieldNameSuggester"/>.
    /// Die erste passende Regel gewinnt, Reihenfolge in der Datei entscheidet.
    /// </summary>
    public List<FieldNameRule> FieldRules { get; set; } = [];

    /// <summary>Eine leere Erweiterung, fuer den Fall ohne oder mit abgeschalteter Datei.</summary>
    public static ExtensionLibrary Empty => new();

    /// <summary>
    /// Fuehrt diese Erweiterungsregeln mit den Textregeln eines Profils
    /// zusammen: eine Erweiterungsregel gilt, ausser eine gleichnamige
    /// Profilregel ersetzt sie vollstaendig -- sie faellt dann ganz weg,
    /// statt zusaetzlich zu gelten.
    ///
    /// Eine Stelle fuer diese Vereinigung, die <see cref="ObfuscationEngine"/>
    /// (fuer den echten Lauf), <c>TextRulesViewModel</c> (fuer die Erprobung
    /// im Textregel-Formular) und <c>TextViewModel</c> (fuer die Fundstellen
    /// der Textansicht) gleichermassen nutzen. Eine zweite, unabhaengig
    /// gepflegte Fassung koennte unbemerkt auseinanderlaufen und liesse die
    /// Textansicht dann etwas anderes finden, als ein echter Lauf ersetzt.
    ///
    /// Ohne eigene Textregeln wird <paramref name="profileRules"/>
    /// unveraendert zurueckgegeben -- der haeufige Fall bleibt damit ohne
    /// zusaetzliche Kopie.
    /// </summary>
    public IReadOnlyList<TextRule> MergeTextRules(IReadOnlyList<TextRule> profileRules)
    {
        if (TextRules.Count == 0)
            return profileRules;

        var profileNames = new HashSet<string>(
            profileRules.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

        var merged = new List<TextRule>(TextRules.Count + profileRules.Count);
        merged.AddRange(TextRules.Where(rule => !profileNames.Contains(rule.Name)));
        merged.AddRange(profileRules);
        return merged;
    }

    /// <summary>
    /// Ob weder Generatoren noch Textregeln noch Spaltenmuster hinterlegt sind.
    /// Nicht Teil des Dateiformats: <see cref="Save"/> soll die Datei so
    /// schreiben, wie sie von Hand aussaehe.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Generators.Count == 0 && TextRules.Count == 0 && FieldRules.Count == 0;

    /// <summary>
    /// Sucht die Erweiterungsdatei an ihren beiden moeglichen Fundorten, in
    /// dieser Reihenfolge; die zuerst gefundene Datei gilt vollstaendig, es
    /// wird nichts zusammengemischt:
    ///
    /// 1. Verzeichnis der laufenden Programmdatei — <see cref="Environment.ProcessPath"/>,
    ///    nicht <c>AppContext.BaseDirectory</c>: bei der Einzeldatei-Veroeffentlichung
    ///    (<c>PublishSingleFile</c>, siehe <c>build-release.sh</c>) zeigt
    ///    letzteres in das Auspackverzeichnis unter <c>/tmp</c> und waere nutzlos.
    /// 2. <see cref="PathHelper.ConfigDirectory"/> (<c>~/.config/obfuskation</c>).
    ///
    /// Eine Datei, die laut <see cref="ProfileStore.Classify"/> ein Profil ist
    /// (<see cref="ProfileStore.JsonFileKind.Profile"/>), wird uebersprungen --
    /// an derselben Stelle kann eine gleichnamige Projektdatei liegen (siehe
    /// <see cref="ProfileStore.Discover"/>) -- und der naechste Ort geprueft.
    /// Eine kaputte Datei (<see cref="ProfileStore.JsonFileKind.Unreadable"/>)
    /// wird dagegen ausdruecklich <b>nicht</b> uebersprungen: sie wird gewaehlt,
    /// und <see cref="Load"/> wirft dafuer <see cref="ConfigurationException"/>
    /// mit brauchbarer Meldung, statt dass die Datei still als "dort liegt ein
    /// Profil" durchgeht (siehe Plan Teil A1).
    /// </summary>
    /// <param name="programDirectory">
    /// Verzeichnis der laufenden Programmdatei. Ohne Angabe gilt das
    /// Verzeichnis von <see cref="Environment.ProcessPath"/>. Ausdruecklich
    /// uebergeben ist ausschliesslich fuer Tests gedacht, die den echten
    /// Testlaeufer-Pfad nicht ansprechen duerfen.
    /// </param>
    public static ExtensionResolution ResolvePath(string? programDirectory = null)
    {
        programDirectory ??= Path.GetDirectoryName(Environment.ProcessPath);

        var candidates = new List<ExtensionCandidate>();
        string? selectedPath = null;
        ExtensionOrigin? selectedOrigin = null;

        void Pruefen(string? directory, ExtensionOrigin origin)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            var path = Path.Combine(directory, FileName);
            var exists = File.Exists(path);
            var skipped = exists && ProfileStore.Classify(path) == ProfileStore.JsonFileKind.Profile;
            candidates.Add(new ExtensionCandidate(path, origin, exists, skipped));

            if (selectedPath is null && exists && !skipped)
            {
                selectedPath = path;
                selectedOrigin = origin;
            }
        }

        Pruefen(programDirectory, ExtensionOrigin.ProgramDirectory);
        Pruefen(PathHelper.ConfigDirectory, ExtensionOrigin.ConfigDirectory);

        return new ExtensionResolution(selectedPath, selectedOrigin, candidates);
    }

    /// <summary>
    /// Laedt die Erweiterung von <paramref name="path"/>, ohne Angabe ueber
    /// <see cref="ResolvePath"/>. Eine fehlende Datei ist kein Fehler — die
    /// Erweiterung ist optional — und ergibt <see cref="Empty"/>. Eine
    /// vorhandene, aber kaputte Datei wirft <see cref="ConfigurationException"/>
    /// mit dem Pfad in der Meldung -- ebenso eine, die sich zwar parsen liesse,
    /// aber nicht mehr geoeffnet werden kann (Zugriffsrechte, Laufwerk weg).
    /// </summary>
    public static ExtensionLibrary Load(string? path = null)
    {
        var resolved = path ?? ResolvePath().Path;
        if (resolved is null || !File.Exists(resolved))
            return Empty;

        try
        {
            using var stream = File.OpenRead(resolved);
            return JsonSerializer.Deserialize<ExtensionLibrary>(stream, ProfileStore.JsonOptions)
                   ?? throw new ConfigurationException($"Erweiterungsdatei ist leer: {resolved}");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Erweiterungsdatei ist kein gültiges JSON: {resolved}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"Erweiterungsdatei nicht lesbar: {resolved} – {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ob und wohin sich die Erweiterungsdatei gerade schreiben liesse.
    ///
    /// Anders als eine fruehere Fassung (<c>ResolveWritePath</c>) faellt das
    /// Ergebnis bei einer schreibgeschuetzten, aber vorhandenen Datei nicht
    /// mehr stillschweigend auf <see cref="PathHelper.ConfigDirectory"/>
    /// zurueck: eine Programmverzeichnis-Datei, die den Vorrang gegen
    /// <see cref="ResolvePath"/> haelt, wuerde sonst weiter gelesen, waehrend
    /// jede Aenderung unbemerkt in eine zweite, nie gelesene Datei liefe --
    /// nach dem naechsten Neustart waere sie wirkungslos. Stattdessen meldet
    /// diese Auskunft den Schreibschutz, und die Oberflaeche sperrt die
    /// betroffenen Bereiche, statt in die falsche Datei zu schreiben.
    ///
    /// Drei Faelle:
    /// - Keine Datei gefunden: Ziel ist <see cref="PathHelper.ConfigDirectory"/>,
    ///   <see cref="ExtensionWriteState.CanWrite"/> ist wahr.
    /// - Eine Datei gefunden und beschreibbar: dieser Pfad, wahr.
    /// - Eine Datei gefunden, aber nicht beschreibbar: dieser Pfad, falsch,
    ///   mit einer erklaerenden <see cref="ExtensionWriteState.Reason"/>.
    /// </summary>
    public static ExtensionWriteState GetWriteState(string? programDirectory = null)
    {
        var resolution = ResolvePath(programDirectory);
        var configPath = Path.Combine(PathHelper.ConfigDirectory, FileName);

        if (resolution.Path is null)
            return new ExtensionWriteState(configPath, CanWrite: true, Reason: null);

        if (!IsWritable(resolution.Path))
        {
            var reason = resolution.Origin == ExtensionOrigin.ProgramDirectory
                ? "Die geltende Datei liegt im Programmverzeichnis und ist schreibgeschützt. "
                  + "Änderungen nimmt dort der Administrator vor."
                : "Die geltende Datei ist schreibgeschützt.";

            return new ExtensionWriteState(resolution.Path, CanWrite: false, reason);
        }

        // Die Datei selbst ist beschreibbar -- fuer das sichere Speichern
        // (Nebendatei, ggf. .bak, dann Move) braucht Save aber auch
        // Schreibrecht auf ihren Ordner. Ohne diese Pruefung wuerde ein
        // schreibgeschuetzter Ordner erst beim tatsaechlichen Speichern
        // auffallen, mitten im Einstellungsfenster.
        var directory = Path.GetDirectoryName(resolution.Path);
        if (!string.IsNullOrEmpty(directory) && !IsDirectoryWritable(directory))
        {
            return new ExtensionWriteState(resolution.Path, CanWrite: false,
                "Die Datei ist beschreibbar, ihr Ordner aber nicht. Zum sicheren Speichern "
                + "(Zwischendatei, Sicherungskopie) braucht das Programm Schreibrecht auf den Ordner.");
        }

        return new ExtensionWriteState(resolution.Path, CanWrite: true, Reason: null);
    }

    /// <summary>
    /// Ob die Datei an <paramref name="path"/> von Hand gepflegte Kommentare
    /// enthaelt. <see cref="Save"/> schreibt reines JSON und wuerde sie
    /// verlieren; wer eine solche Datei ueberschreibt, soll das vorher wissen
    /// und bekommt ueber <see cref="Save"/> eine Sicherungskopie.
    ///
    /// Gesucht wird nur ausserhalb von Zeichenketten, damit ein
    /// <c>"https://…"</c> in einem Muster nicht als Kommentar zaehlt.
    /// </summary>
    public static bool HasComments(string path)
    {
        if (!File.Exists(path))
            return false;

        var text = File.ReadAllText(path);
        var inString = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (character == '\\')
                    index++;
                else if (character == '"')
                    inString = false;
                continue;
            }

            if (character == '"')
                inString = true;
            else if (character == '/' && index + 1 < text.Length && (text[index + 1] == '/' || text[index + 1] == '*'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Schreibt die Erweiterung. Wie <see cref="ProfileStore.Save"/> erst in
    /// eine Nebendatei, dann umbenennen — ein Abbruch darf keine halbe Datei
    /// hinterlassen.
    ///
    /// Enthaelt die Zieldatei Kommentare, entsteht vorher eine Sicherungskopie
    /// mit der Endung <c>.bak</c>: <see cref="JsonSerializer"/> schreibt
    /// Kommentare nicht zurueck, und die Datei wird von Hand gepflegt (siehe
    /// die Beispieldatei unter <c>docs/beispiel</c>). Wer sie kommentiert hat,
    /// soll seine Notizen wiederfinden.
    /// </summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(PathHelper.ExpandHome(path));

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (HasComments(full))
            File.Copy(full, full + ".bak", overwrite: true);

        var temporary = full + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, ProfileStore.JsonOptions));
        File.Move(temporary, full, overwrite: true);
    }

    /// <summary>
    /// Nimmt Generatoren mit auf, die eine Regel dieser Erweiterung verwendet,
    /// aber die es bislang nur im uebergebenen Profil gibt (Plan P4).
    ///
    /// Der Anlass: eine Regel fuer alle Projekte (<see cref="TextRules"/> oder
    /// <see cref="FieldRules"/>) kann im Formular oder im AlwaysReplace-Dialog
    /// einen Generator waehlen, den es nur im gerade offenen Profil gibt. In
    /// diesem Projekt laeuft das, weil <see cref="Generation.GeneratorRegistry.Build"/>
    /// zuerst die Erweiterung, dann das Profil einmischt -- in jedem anderen
    /// Projekt fehlt der Generator dann ganz.
    ///
    /// Ein Generator gilt als "nur im Projekt", wenn er weder eingebaut
    /// (<see cref="GeneratorRegistry.KnownNames"/>) noch <c>scanText</c> ist,
    /// nicht schon in <see cref="Generators"/> steht (ein vorhandener globaler
    /// Eintrag wird nie ueberschrieben) und tatsaechlich in
    /// <paramref name="profile"/> existiert. Kopiert wird per
    /// <see cref="ProfileStore.DeepCopy{T}"/>, damit spaetere Aenderungen an der
    /// einen Fassung die andere nicht beruehren.
    /// </summary>
    /// <param name="profile">Das Profil, aus dem ein fehlender Generator kopiert wird.</param>
    /// <returns>Die Namen der tatsaechlich kopierten Generatoren.</returns>
    public IReadOnlyList<string> AdoptProjectGenerators(Profile profile)
    {
        var kopiert = new List<string>();

        void UebernehmenFallsNoetig(string? generatorName)
        {
            if (string.IsNullOrWhiteSpace(generatorName))
                return;

            if (GeneratorRegistry.KnownNames.Contains(generatorName, StringComparer.OrdinalIgnoreCase))
                return;

            if (string.Equals(generatorName, "scanText", StringComparison.OrdinalIgnoreCase))
                return;

            if (Generators.ContainsKey(generatorName))
                return;

            if (!profile.Generators.TryGetValue(generatorName, out var settings))
                return;

            Generators[generatorName] = ProfileStore.DeepCopy(settings);
            kopiert.Add(generatorName);
        }

        foreach (var regel in TextRules)
            UebernehmenFallsNoetig(regel.Generator);

        foreach (var regel in FieldRules)
            UebernehmenFallsNoetig(regel.Generator);

        return kopiert;
    }

    /// <summary>
    /// Eine unabhaengige Kopie per JSON-Rundreise -- fuer das Einstellungsfenster,
    /// das auf einer Kopie arbeitet und sie erst bei "Übernehmen" in das
    /// geteilte Original zurueckspielt (siehe <see cref="ReplaceWith"/>).
    /// </summary>
    public ExtensionLibrary Clone()
    {
        var json = JsonSerializer.Serialize(this, ProfileStore.JsonOptions);
        return JsonSerializer.Deserialize<ExtensionLibrary>(json, ProfileStore.JsonOptions)!;
    }

    /// <summary>
    /// Uebernimmt Generatoren, Textregeln und Spaltenmuster von
    /// <paramref name="other"/> in dieselbe Instanz, statt eine neue
    /// zurueckzugeben. Das ist wichtig: <see cref="Services.ProfileSession"/>
    /// (in der Oberflaeche), <c>TextViewModel</c> und <c>MainViewModel</c>
    /// teilen sich dieselbe <see cref="ExtensionLibrary"/>-Instanz -- eine
    /// neu zugewiesene Kopie wuerde nur eine der Stellen erreichen, waehrend
    /// die anderen weiter die alte saehen.
    /// </summary>
    public void ReplaceWith(ExtensionLibrary other)
    {
        Version = other.Version;

        Generators.Clear();
        foreach (var (name, settings) in other.Generators)
            Generators[name] = settings;

        TextRules.Clear();
        TextRules.AddRange(other.TextRules);

        FieldRules.Clear();
        FieldRules.AddRange(other.FieldRules);
    }

    private static bool IsWritable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Prueft das Schreibrecht auf einen Ordner ueber eine Probedatei, die sich
    /// beim Schliessen selbst wieder entfernt (<see cref="FileOptions.DeleteOnClose"/>) --
    /// es gibt keine verlaessliche API, die Ordnerrechte plattformuebergreifend
    /// ohne einen solchen Versuch beantwortet.
    /// </summary>
    private static bool IsDirectoryWritable(string directory)
    {
        var probe = Path.Combine(directory, $".obfuskation-schreibprobe-{Guid.NewGuid():N}");

        try
        {
            using var stream = new FileStream(
                probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
