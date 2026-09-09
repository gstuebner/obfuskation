using System.Text.Json;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Hauseigene Generatoren und Textregeln an einem festen, privaten Ort
/// (<see cref="DefaultPath"/>, typischerweise
/// <c>~/.config/obfuskation/generators.json</c>), die in jedes Profil
/// einfliessen, ohne je in eine Profildatei geschrieben zu werden.
///
/// Der Anlass: wiederkehrende hauseigene Muster (etwa Inventarnummern der Form
/// <c>INV123456</c>) sind Betriebswissen und duerfen nicht in einer Profildatei
/// stehen, die geteilt oder eingecheckt werden koennte. Die Bibliothek liegt
/// deshalb ausserhalb jedes Profils und wird nur zur Laufzeit dazugemischt —
/// siehe <see cref="Generation.GeneratorRegistry.Build"/>,
/// <see cref="ObfuscationEngine"/> und <see cref="ProfileValidator"/>. Eine
/// Profildatei kennt die Bibliothek nicht und darf sie auch nie enthalten:
/// wuerde die Oberflaeche die Bibliothekseintraege beim Speichern in das
/// Profil mischen, landeten die Hausmuster in jeder Profildatei — genau das,
/// was diese Trennung verhindern soll.
///
/// Bewusst dieselben Typen wie im Profil (<see cref="GeneratorSettings"/>,
/// <see cref="TextRule"/>): kein zweites Schema, keine zweite Validierung.
///
/// Nur lesend: die Datei wird von Hand im Texteditor gepflegt, diese Klasse
/// bietet deshalb kein <c>Save</c>.
/// </summary>
public sealed class GeneratorLibrary
{
    /// <summary>Dateiname unter <see cref="PathHelper.ConfigDirectory"/>.</summary>
    public const string FileName = "generators.json";

    public int Version { get; set; } = 1;

    /// <summary>Generatoren, adressiert ueber ihren Namen — wie <see cref="Profile.Generators"/>.</summary>
    public Dictionary<string, GeneratorSettings> Generators { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textregeln — wie <see cref="Profile.TextRules"/>.</summary>
    public List<TextRule> TextRules { get; set; } = [];

    /// <summary>Eine leere Bibliothek, fuer den Fall ohne oder mit abgeschalteter Datei.</summary>
    public static GeneratorLibrary Empty => new();

    /// <summary>
    /// Vorgabepfad der Bibliotheksdatei. Eine Eigenschaft statt eines
    /// zwischengespeicherten Feldes — wie <see cref="PathHelper.ConfigDirectory"/>
    /// und <see cref="ProfileIndex.FilePath"/> —, damit eine Testumgebung, die
    /// <c>XDG_CONFIG_HOME</c> vor dem ersten Zugriff umleitet, garantiert den
    /// umgeleiteten Pfad bekommt und niemals den echten Bestand des Rechners.
    /// </summary>
    public static string DefaultPath => Path.Combine(PathHelper.ConfigDirectory, FileName);

    /// <summary>Ob weder Generatoren noch Textregeln hinterlegt sind.</summary>
    public bool IsEmpty => Generators.Count == 0 && TextRules.Count == 0;

    /// <summary>
    /// Laedt die Bibliothek von <paramref name="path"/>, ohne Angabe von
    /// <see cref="DefaultPath"/>. Eine fehlende Datei ist kein Fehler — die
    /// Bibliothek ist optional — und ergibt <see cref="Empty"/>. Eine
    /// vorhandene, aber kaputte Datei wirft <see cref="ConfigurationException"/>
    /// mit dem Pfad in der Meldung.
    /// </summary>
    public static GeneratorLibrary Load(string? path = null)
    {
        var resolved = path ?? DefaultPath;
        if (!File.Exists(resolved))
            return Empty;

        try
        {
            using var stream = File.OpenRead(resolved);
            return JsonSerializer.Deserialize<GeneratorLibrary>(stream, ProfileStore.JsonOptions)
                   ?? throw new ConfigurationException($"Generator-Bibliothek ist leer: {resolved}");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Generator-Bibliothek ist kein gültiges JSON: {resolved}", ex);
        }
    }
}
