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
            var bytes = File.ReadAllBytes(path);
            var profile = JsonSerializer.Deserialize<Profile>(bytes, JsonOptions)
                          ?? throw new ConfigurationException($"Konfigurationsdatei ist leer: {path}");

            using var document = JsonDocument.Parse(bytes, ClassifyOptions);
            profile.UnknownProperties = FindUnknownProperties(document.RootElement, typeof(Profile));
            return profile;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Konfigurationsdatei ist kein gültiges JSON: {path}", ex);
        }
    }

    /// <summary>
    /// Sucht in <paramref name="element"/> Eigenschaften, die
    /// <paramref name="type"/> nicht kennt (Befund C5). Grundlage sind dieselben
    /// Typmetadaten, mit denen <see cref="JsonOptions"/> liest -- was hier
    /// bekannt ist, wird beim Laden auch uebernommen, und umgekehrt. Ein
    /// Fehler waere unvertraeglicher als diese Warnung: aeltere Profile koennen
    /// abgeloeste Felder enthalten.
    /// </summary>
    public static IReadOnlyList<UnknownProperty> FindUnknownProperties(JsonElement element, Type type)
    {
        var unknown = new List<UnknownProperty>();
        CollectUnknownProperties(element, type, "", unknown);
        return unknown;
    }

    private static void CollectUnknownProperties(JsonElement element, Type type, string path, List<UnknownProperty> unknown)
    {
        var info = JsonOptions.GetTypeInfo(type);
        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object when element.ValueKind == JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    var known = info.Properties.FirstOrDefault(
                        p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));

                    if (known is null)
                        unknown.Add(new UnknownProperty(childPath, Closest(property.Name, info.Properties.Select(p => p.Name))));
                    else
                        CollectUnknownProperties(property.Value, known.PropertyType, childPath, unknown);
                }
                break;

            case JsonTypeInfoKind.Enumerable when element.ValueKind == JsonValueKind.Array
                                                   && GenericArgument(type, typeof(IEnumerable<>), 0) is { } itemType:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    CollectUnknownProperties(item, itemType, $"{path}[{index++}]", unknown);
                break;

            case JsonTypeInfoKind.Dictionary when element.ValueKind == JsonValueKind.Object
                                                   && GenericArgument(type, typeof(IDictionary<,>), 1) is { } valueType:
                foreach (var property in element.EnumerateObject())
                    CollectUnknownProperties(property.Value, valueType, $"{path}.{property.Name}", unknown);
                break;
        }
    }

    /// <summary>
    /// Typargument einer generischen Schnittstelle, die <paramref name="type"/>
    /// umsetzt -- der Elementtyp einer Liste, der Werttyp eines Woerterbuchs.
    /// (<c>JsonTypeInfo.ElementType</c> gibt es erst ab .NET 9.)
    /// </summary>
    private static Type? GenericArgument(Type type, Type genericInterface, int position)
        => type.GetInterfaces().Append(type)
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == genericInterface)
            ?.GetGenericArguments()[position];

    /// <summary>Der bekannte Name mit dem kleinsten Editierabstand, hoechstens zwei.</summary>
    private static string? Closest(string name, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = 3;
        foreach (var candidate in candidates)
        {
            var distance = EditDistance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Editierabstand nach Damerau-Levenshtein (vertauschte Nachbarn zaehlen einfach).</summary>
    private static int EditDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[a.Length, b.Length];
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

        // Geschrieben wird nur, was das Programm kennt; die Warnung ist damit erledigt.
        profile.UnknownProperties = [];
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
