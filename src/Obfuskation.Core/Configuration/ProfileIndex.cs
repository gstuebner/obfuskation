using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Merkt sich, welche Datendateien unter welchem Profil bearbeitet wurden, und
/// wann ein Profil zuletzt benutzt wurde.
///
/// Datenschutz: hier stehen ausschliesslich <b>Pfade</b> bearbeiteter Dateien,
/// nie Inhalte oder Werte. Ein Pfad kann trotzdem verraten, woran gearbeitet
/// wurde — deshalb muss die Oberflaeche einen Weg bieten, einen Eintrag ohne
/// Rueckfrage wieder zu entfernen ("Aus der Liste entfernen").
///
/// Bewusst getrennt von der Profildatei selbst: die Profildatei wird nie
/// ungefragt geschrieben, ein automatischer Eintrag hier wuerde sonst
/// unbestaetigte Regeln aus dem Speicher mitspeichern.
/// </summary>
public sealed class ProfileIndex
{
    public const int CurrentVersion = 1;

    /// <summary>Obergrenze je Profil — die Datei soll klein und lesbar bleiben.</summary>
    private const int MaxDataFilesPerProfile = 20;

    /// <summary>Obergrenze an Profilen insgesamt.</summary>
    private const int MaxProfiles = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Version { get; set; } = CurrentVersion;

    public List<ProfileUsage> Profiles { get; set; } = new();

    [JsonIgnore]
    public static string FilePath => Path.Combine(PathHelper.ConfigDirectory, "profil-index.json");

    public static ProfileIndex Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ProfileIndex();

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<ProfileIndex>(stream, JsonOptions) ?? new ProfileIndex();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Ein kaputter Index ist kein Grund, den Start zu verweigern; er
            // faengt einfach wieder leer an.
            return new ProfileIndex();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sich die Nutzung nicht merken zu koennen ist unschoen, aber kein
            // Fehler, der die eigentliche Arbeit unterbrechen sollte.
        }
    }

    public void RecordProfileUse(string profilePath)
    {
        var usage = FindOrCreate(Path.GetFullPath(profilePath));
        usage.LastUsedUtc = DateTimeOffset.UtcNow;
        EnforceProfileCap();
    }

    public void RecordDataFile(string profilePath, string dataFilePath)
    {
        var usage = FindOrCreate(Path.GetFullPath(profilePath));
        var fileFull = Path.GetFullPath(dataFilePath);

        usage.Files.RemoveAll(f => string.Equals(f.Path, fileFull, StringComparison.Ordinal));
        usage.Files.Add(new DataFileUsage { Path = fileFull, LastUsedUtc = DateTimeOffset.UtcNow });

        if (usage.Files.Count > MaxDataFilesPerProfile)
        {
            usage.Files = usage.Files
                .OrderByDescending(f => f.LastUsedUtc)
                .Take(MaxDataFilesPerProfile)
                .ToList();
        }

        EnforceProfileCap();
    }

    /// <summary>Nach Umbenennen oder Verschieben der Profildatei den Schluessel nachziehen.</summary>
    public void MoveProfile(string oldPath, string newPath)
    {
        var oldFull = Path.GetFullPath(oldPath);
        var newFull = Path.GetFullPath(newPath);

        var usage = Profiles.FirstOrDefault(p => string.Equals(p.Path, oldFull, StringComparison.Ordinal));
        if (usage is null)
            return;

        // Steht unter dem Zielpfad schon ein Eintrag (z. B. Umbenennen auf ein
        // bereits bekanntes Profil), gewinnt der bisherige — er wird ersetzt,
        // nicht dupliziert.
        Profiles.RemoveAll(p => string.Equals(p.Path, newFull, StringComparison.Ordinal));
        usage.Path = newFull;
    }

    public void Forget(string profilePath)
    {
        var full = Path.GetFullPath(profilePath);
        Profiles.RemoveAll(p => string.Equals(p.Path, full, StringComparison.Ordinal));
    }

    public void ForgetDataFile(string profilePath, string dataFilePath)
    {
        var full = Path.GetFullPath(profilePath);
        var fileFull = Path.GetFullPath(dataFilePath);

        var usage = Profiles.FirstOrDefault(p => string.Equals(p.Path, full, StringComparison.Ordinal));
        usage?.Files.RemoveAll(f => string.Equals(f.Path, fileFull, StringComparison.Ordinal));
    }

    /// <summary>Entfernt Eintraege, deren Profildatei nicht mehr existiert. Gibt die Anzahl zurueck.</summary>
    public int Prune()
    {
        var before = Profiles.Count;
        Profiles.RemoveAll(p => !File.Exists(p.Path));
        return before - Profiles.Count;
    }

    private ProfileUsage FindOrCreate(string fullPath)
    {
        var usage = Profiles.FirstOrDefault(p => string.Equals(p.Path, fullPath, StringComparison.Ordinal));
        if (usage is null)
        {
            usage = new ProfileUsage { Path = fullPath };
            Profiles.Add(usage);
        }
        return usage;
    }

    private void EnforceProfileCap()
    {
        if (Profiles.Count <= MaxProfiles)
            return;

        Profiles = Profiles
            .OrderByDescending(p => p.LastUsedUtc ?? DateTimeOffset.MinValue)
            .Take(MaxProfiles)
            .ToList();
    }
}

public sealed class ProfileUsage
{
    /// <summary>Vollpfad, Schluessel (Vergleich immer mit StringComparison.Ordinal).</summary>
    public string Path { get; set; } = "";

    public DateTimeOffset? LastUsedUtc { get; set; }

    public List<DataFileUsage> Files { get; set; } = new();
}

public sealed class DataFileUsage
{
    public string Path { get; set; } = "";

    public DateTimeOffset LastUsedUtc { get; set; }
}
