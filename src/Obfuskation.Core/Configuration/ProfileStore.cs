using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

        // Blendet Eigenschaften mit OmitFromJsonWhenAttribute aus, sobald ihr
        // Wert dem dort hinterlegten Vorgabewert entspricht -- siehe
        // OmitConfiguredDefaults.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitConfiguredDefaults } },
    };

    /// <summary>
    /// Modifier fuer <see cref="JsonOptions"/>: traegt eine Eigenschaft ein
    /// <see cref="OmitFromJsonWhenAttribute"/>, wird sie beim Schreiben
    /// uebersprungen, sobald ihr Wert dem dort hinterlegten Vorgabewert
    /// entspricht. Das greift auch beim Laden und Wiederspeichern einer alten
    /// Datei -- eine dort stehende "maxDays": 400 verschwindet dadurch beim
    /// naechsten Speichern von selbst, ohne dass die Datei eigens dafuer
    /// migriert werden muesste. Beim Einlesen aendert sich nichts: eine
    /// fehlende Eigenschaft behaelt ihre Vorgabe, wie zuvor.
    /// </summary>
    private static void OmitConfiguredDefaults(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            var attribute = property.AttributeProvider?
                .GetCustomAttributes(typeof(OmitFromJsonWhenAttribute), inherit: true)
                .OfType<OmitFromJsonWhenAttribute>()
                .FirstOrDefault();

            if (attribute is null)
                continue;

            property.ShouldSerialize = (_, value) => !Equals(value, attribute.Value);
        }
    }

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

    /// <summary>
    /// Eine unabhaengige Kopie per JSON-Rundreise -- fuer das Einstellungsfenster
    /// der Oberflaeche, das auf Kopien von Profil und Erweiterung arbeitet und
    /// sie erst bei "Übernehmen" zurueckschreibt. Taugt fuer <see cref="Profile"/>
    /// selbst genauso wie fuer einzelne Listen (<see cref="TextRule"/>,
    /// <see cref="GeneratorSettings"/>, <see cref="FieldNameRule"/>).
    /// </summary>
    public static T DeepCopy<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
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
    /// Ergebnis von <see cref="Classify"/>: was eine JSON-Datei an einem der
    /// beiden gemeinsamen Fundorte (Profil oder Erweiterungsdatei) ist.
    /// </summary>
    public enum JsonFileKind
    {
        /// <summary>Traegt <c>profileName</c> oder <c>fields</c> -- ein Profil.</summary>
        Profile,

        /// <summary>Gueltiges JSON, aber kein Profil -- etwa eine Erweiterungsdatei.</summary>
        Other,

        /// <summary>Kein gueltiges JSON. <see cref="Load"/> liefert dazu die eigentliche Fehlermeldung.</summary>
        Unreadable,
    }

    /// <summary>
    /// Optionen fuer <see cref="JsonDocument.Parse(Stream, JsonDocumentOptions)"/>
    /// in <see cref="Classify"/> -- wie <see cref="JsonOptions"/>, damit eine von
    /// Hand kommentierte Erweiterungsdatei hier nicht faelschlich als
    /// <see cref="JsonFileKind.Unreadable"/> durchfaellt.
    /// </summary>
    private static readonly JsonDocumentOptions ClassifyOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Stellt fest, was fuer eine JSON-Datei an <paramref name="path"/> liegt,
    /// ohne sie schon vollstaendig einzulesen. Gebraucht an zwei Stellen, die
    /// sich nicht auseinanderentwickeln duerfen -- <see cref="Discover"/>
    /// (unterscheidet ein Altprofil von der gleichnamigen Erweiterungsdatei)
    /// und <see cref="ExtensionLibrary.ResolvePath"/> (uebersieht eine dort
    /// liegende Profildatei).
    /// </summary>
    public static JsonFileKind Classify(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, ClassifyOptions);
            var istProfil = document.RootElement.ValueKind == JsonValueKind.Object &&
                            (document.RootElement.TryGetProperty("profileName", out _) ||
                             document.RootElement.TryGetProperty("fields", out _));

            return istProfil ? JsonFileKind.Profile : JsonFileKind.Other;
        }
        catch (JsonException)
        {
            // Echtes kaputtes JSON laesst sich von einer Profildatei hier
            // nicht unterscheiden -- Load soll den Fehler mit brauchbarer
            // Meldung melden, nicht diese Klassifikation.
            return JsonFileKind.Unreadable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return JsonFileKind.Other;
        }
    }

    /// <summary>
    /// Siebt fremde oder andersrollige JSON-Dateien aus, bevor ueberhaupt
    /// <see cref="Load"/> versucht wird: eine Datei zaehlt als Profil, wenn
    /// <see cref="Classify"/> <see cref="JsonFileKind.Profile"/> ergibt, oder
    /// wenn sie sich gar nicht erst parsen laesst (<see cref="JsonFileKind.Unreadable"/>)
    /// -- dann soll <see cref="Load"/> gleich noch einmal ansetzen und den
    /// eigentlichen Fehler melden, statt dass die Datei hier schon
    /// stillschweigend als "keine Profildatei" durchfaellt.
    /// </summary>
    public static bool LooksLikeProfile(string path) => Classify(path) is JsonFileKind.Profile or JsonFileKind.Unreadable;
}
