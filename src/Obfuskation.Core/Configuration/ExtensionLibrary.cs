using System.Text.Json;

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
/// Nur lesend: die Datei wird von Hand im Texteditor gepflegt, diese Klasse
/// bietet deshalb kein <c>Save</c>.
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

    /// <summary>Ob weder Generatoren noch Textregeln noch Spaltenmuster hinterlegt sind.</summary>
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
    /// Eine Datei, auf die <see cref="ProfileStore.LooksLikeProfile"/> zutrifft,
    /// wird uebersprungen -- an derselben Stelle kann eine gleichnamige
    /// Projektdatei liegen (siehe <see cref="ProfileStore.Discover"/>) -- und
    /// der naechste Ort geprueft.
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
            var skipped = exists && ProfileStore.LooksLikeProfile(path);
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
    /// mit dem Pfad in der Meldung.
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
    }
}
