using System.Text.Json;

namespace Obfuskation.Core.Configuration;

/// <summary>Eine Zeile der Profiluebersicht.</summary>
/// <param name="Error">Gesetzt, wenn das Profil nicht gelesen werden konnte.</param>
public sealed record ProfileSummary(
    string Path,
    string Name,
    string? Description,
    int FieldCount,
    int TextRuleCount,
    string MappingStorePath,
    bool MappingStoreExists,
    DateTimeOffset ModifiedUtc,
    DateTimeOffset? LastUsedUtc,
    IReadOnlyList<DataFileUsage> DataFiles,
    string? Error);

/// <summary>
/// Sammelt alle bekannten Profile fuer die Uebersicht: den zentralen Ordner
/// plus Zusatzpfade (Zuletzt-Liste der Oberflaeche, Pfade aus dem Index).
/// Sortiert wird hier bewusst nicht — das ist Sache des Ansichtsmodells.
/// </summary>
public static class ProfileCatalog
{
    public static IReadOnlyList<ProfileSummary> Collect(ProfileIndex index, IEnumerable<string> additionalPaths)
    {
        ArgumentNullException.ThrowIfNull(index);

        var candidates = new List<string>();

        if (Directory.Exists(PathHelper.ProfileDirectory))
            candidates.AddRange(Directory.EnumerateFiles(PathHelper.ProfileDirectory, "*.json"));

        candidates.AddRange(additionalPaths);
        candidates.AddRange(index.Profiles.Select(p => p.Path));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var summaries = new List<ProfileSummary>();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                continue; // ungueltiger Pfad -- ueberspringen statt abzubrechen
            }

            if (!seen.Add(full))
                continue;

            var summary = CollectOne(full, index);
            if (summary is not null)
                summaries.Add(summary);
        }

        return summaries;
    }

    /// <returns><c>null</c> bedeutet: keine Profildatei, wird uebergangen.</returns>
    private static ProfileSummary? CollectOne(string path, ProfileIndex index)
    {
        var usage = index.Profiles.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.Ordinal));
        var files = (IReadOnlyList<DataFileUsage>?)usage?.Files ?? Array.Empty<DataFileUsage>();

        try
        {
            if (!File.Exists(path))
            {
                // Nur Pfade aus additionalPaths/Index koennen das ueberhaupt
                // erreichen (Verzeichnis-Enumeration listet nur Vorhandenes) --
                // z. B. eine Zuletzt-Liste, deren Datei geloescht wurde, oder
                // eine Datei, die zwischen Auflisten und Laden verschwunden
                // ist. Als Fehlereintrag melden, damit die Uebersicht "Aus
                // Liste entfernen" anbieten kann, statt den Eintrag
                // stillschweigend verschwinden zu lassen.
                return new ProfileSummary(
                    path, Path.GetFileName(path), null, 0, 0, "", false,
                    DateTimeOffset.MinValue, usage?.LastUsedUtc, files,
                    "Datei nicht gefunden.");
            }

            if (!LooksLikeProfile(path))
                return null;

            var profile = ProfileStore.Load(path);
            var mappingStorePath = PathHelper.ResolveMappingStore(profile);

            return new ProfileSummary(
                path, profile.ProfileName, profile.Description,
                profile.Fields.Count, profile.TextRules.Count,
                mappingStorePath, File.Exists(mappingStorePath),
                File.GetLastWriteTimeUtc(path), usage?.LastUsedUtc, files,
                Error: null);
        }
        catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException)
        {
            return new ProfileSummary(
                path, Path.GetFileName(path), null, 0, 0, "", false,
                File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTimeOffset.MinValue,
                usage?.LastUsedUtc, files,
                Error: ex.Message);
        }
    }

    /// <summary>
    /// Siebt fremde JSON-Dateien im zentralen Ordner aus, bevor ueberhaupt
    /// <see cref="ProfileStore.Load"/> versucht wird: ein versehentlich
    /// abgelegtes JSON soll keine Geisterzeile in der Uebersicht erzeugen.
    /// </summary>
    private static bool LooksLikeProfile(string path)
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
            // ProfileStore.Load wirft gleich noch einmal und liefert dann den
            // Fehlereintrag mit brauchbarer Meldung.
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
