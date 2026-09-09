using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obfuskation.Core.Configuration;

/// <summary>Laedt und speichert Profile als JSON.</summary>
public static class ProfileStore
{
    /// <summary>
    /// Neuer, qualifizierter Name der Projektdatei. Wird ab jetzt allein
    /// geschrieben -- <see cref="LegacyFileName"/> bleibt nur zum Lesen
    /// bestehender Bestaende erhalten.
    /// </summary>
    public const string DefaultFileName = "obfuskation-projekt.json";

    /// <summary>
    /// Der unqualifizierte Programmname als frueherer Dateiname der
    /// Projektdatei. Seit die Erweiterungsdatei (<see cref="ExtensionLibrary"/>)
    /// denselben Namen fuer ihre eigene Rolle beansprucht, wird er nur noch
    /// als Profil erkannt, wenn <see cref="LooksLikeProfile"/> zutrifft --
    /// siehe <see cref="Discover"/>.
    /// </summary>
    public const string LegacyFileName = "obfuskation.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // Ohne diesen Encoder erscheinen Apostroph und Pluszeichen in den
        // Mustern als \u-Folgen; die Konfiguration soll von Hand lesbar bleiben.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Profile Load(string path)
    {
        if (!File.Exists(path))
            throw new ConfigurationException($"Konfigurationsdatei nicht gefunden: {path}");

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Profile>(stream, JsonOptions)
                   ?? throw new ConfigurationException($"Konfigurationsdatei ist leer: {path}");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Konfigurationsdatei ist kein gültiges JSON: {path}", ex);
        }
    }

    public static void Save(Profile profile, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Erst in eine Nebendatei schreiben, dann umbenennen: ein Abbruch darf
        // keine halb geschriebene Konfiguration hinterlassen.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Sucht die Konfigurationsdatei ab <paramref name="startDirectory"/> aufwaerts.
    /// Gibt <c>null</c> zurueck, wenn keine gefunden wurde.
    ///
    /// Je Verzeichnisebene wird erst <see cref="DefaultFileName"/> geprueft,
    /// dann <see cref="LegacyFileName"/> -- erst danach geht die Suche eine
    /// Ebene hoeher. Eine gefundene <see cref="LegacyFileName"/> zaehlt nur,
    /// wenn <see cref="LooksLikeProfile"/> zutrifft: der unqualifizierte Name
    /// kann an derselben Stelle auch die Erweiterungsdatei sein (siehe
    /// <see cref="ExtensionLibrary.ResolvePath"/>), und die hat weder
    /// <c>profileName</c> noch <c>fields</c>. Ohne diese Pruefung wuerde die
    /// Suche an ihr haengenbleiben, statt weiter aufwaerts nach einem
    /// tatsaechlichen Profil zu suchen.
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            var neu = Path.Combine(directory.FullName, DefaultFileName);
            if (File.Exists(neu))
                return neu;

            var alt = Path.Combine(directory.FullName, LegacyFileName);
            if (File.Exists(alt) && LooksLikeProfile(alt))
                return alt;

            directory = directory.Parent;
        }
        return null;
    }

    /// <summary>
    /// Siebt fremde oder andersrollige JSON-Dateien aus, bevor ueberhaupt
    /// <see cref="Load"/> versucht wird: eine Datei zaehlt als Profil, wenn
    /// sie <c>profileName</c> oder <c>fields</c> traegt. Gebraucht an zwei
    /// Stellen, die sich nicht auseinanderentwickeln duerfen --
    /// <see cref="Discover"/> (unterscheidet ein Altprofil von der
    /// gleichnamigen Erweiterungsdatei) und <see cref="ProfileCatalog"/>
    /// (siebt fremde Dateien im zentralen Ordner aus).
    /// </summary>
    public static bool LooksLikeProfile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   (document.RootElement.TryGetProperty("profileName", out _) ||
                    document.RootElement.TryGetProperty("fields", out _));
        }
        catch (JsonException)
        {
            // Kaputtes JSON laesst sich hier nicht beurteilen -- durchlassen,
            // Load wirft gleich noch einmal und liefert dann den Fehler mit
            // brauchbarer Meldung.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
