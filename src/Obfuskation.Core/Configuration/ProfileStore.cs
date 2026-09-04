using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obfuskation.Core.Configuration;

/// <summary>Laedt und speichert Profile als JSON.</summary>
public static class ProfileStore
{
    public const string DefaultFileName = "obfuskation.json";

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
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, DefaultFileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
