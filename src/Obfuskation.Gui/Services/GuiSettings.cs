using System.Text.Json;
using System.Text.Json.Serialization;
using Obfuskation.Core;

namespace Obfuskation.Gui.Services;

/// <summary>Welche Ansicht die Oberflaeche verwendet.</summary>
public enum AppTheme
{
    /// <summary>Der Einstellung des Betriebssystems folgen.</summary>
    System,
    Dark,
    Light,
}

/// <summary>Wonach die Profiluebersicht sortiert.</summary>
public enum ProfileSortKey
{
    Name,
    LastUsed,
    Modified,
}

/// <summary>
/// Was sich die Oberflaeche zwischen zwei Starts merkt.
///
/// Bewusst nur Bequemlichkeiten: zuletzt geoeffnete Profile und die gewaehlte
/// Ansicht. <b>Keine Dateiinhalte, keine Werte aus verarbeiteten Dateien</b> —
/// diese Datei liegt unverschluesselt im Benutzerverzeichnis.
/// </summary>
public sealed class GuiSettings
{
    private const int MaxRecentProfiles = 8;

    /// <summary>Obergrenze der ausgeblendeten Profile — wie bei <see cref="MaxRecentProfiles"/>.</summary>
    private const int MaxHiddenProfiles = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Zuletzt geoeffnete Konfigurationsdateien, jüngste zuerst.</summary>
    public List<string> RecentProfiles { get; set; } = new();

    /// <summary>Verzeichnis, in dem zuletzt eine Datendatei geoeffnet wurde.</summary>
    public string? LastDataDirectory { get; set; }

    /// <summary>
    /// Profile, die "Aus Liste entfernen" aus der Uebersicht ausgeblendet hat,
    /// Vollpfade. <see cref="Obfuskation.Core.Configuration.ProfileCatalog.Collect"/> zaehlt den
    /// zentralen Profilordner immer mit auf; ohne diese Liste kaeme ein
    /// entferntes Profil dort sofort wieder herein, nur ohne Nutzungsdaten. Die
    /// Profildatei selbst bleibt unangetastet -- "Aus Datei waehlen…" holt den
    /// Eintrag jederzeit zurueck.
    /// </summary>
    public List<string> HiddenProfiles { get; set; } = new();

    public double WindowWidth { get; set; } = 1040;

    public double WindowHeight { get; set; } = 720;

    /// <summary>Sortierung der Profiluebersicht, ueber einen Neustart hinweg gemerkt.</summary>
    public ProfileSortKey ProfileSortKey { get; set; } = ProfileSortKey.LastUsed;

    public bool ProfileSortDescending { get; set; } = true;

    [JsonIgnore]
    public static string FilePath => Path.Combine(PathHelper.ConfigDirectory, "gui.json");

    public static GuiSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new GuiSettings();

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<GuiSettings>(stream, JsonOptions) ?? new GuiSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Beschaedigte Einstellungen sind kein Grund, den Start zu
            // verweigern; die Vorgaben tun es auch.
            return new GuiSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sich nichts merken zu koennen ist unschoen, aber kein Fehler,
            // der den Anwender bei der Arbeit unterbrechen sollte.
        }
    }

    /// <summary>Traegt ein Profil vorn ein und haelt die Liste kurz.</summary>
    public void RememberProfile(string path)
    {
        var full = Path.GetFullPath(path);
        RecentProfiles.RemoveAll(entry =>
            string.Equals(Path.GetFullPath(entry), full, StringComparison.Ordinal));
        RecentProfiles.Insert(0, full);

        if (RecentProfiles.Count > MaxRecentProfiles)
            RecentProfiles.RemoveRange(MaxRecentProfiles, RecentProfiles.Count - MaxRecentProfiles);
    }

    /// <summary>Blendet ein Profil dauerhaft aus der Uebersicht aus.</summary>
    public void HideProfile(string path)
    {
        var full = Path.GetFullPath(path);
        if (HiddenProfiles.Any(entry => string.Equals(Path.GetFullPath(entry), full, StringComparison.Ordinal)))
            return;

        HiddenProfiles.Insert(0, full);

        if (HiddenProfiles.Count > MaxHiddenProfiles)
            HiddenProfiles.RemoveRange(MaxHiddenProfiles, HiddenProfiles.Count - MaxHiddenProfiles);
    }

    /// <summary>Holt ein ausgeblendetes Profil zurueck -- etwa ueber "Aus Datei waehlen…".</summary>
    public void UnhideProfile(string path)
    {
        var full = Path.GetFullPath(path);
        HiddenProfiles.RemoveAll(entry =>
            string.Equals(Path.GetFullPath(entry), full, StringComparison.Ordinal));
    }

    public bool IsHidden(string path)
    {
        var full = Path.GetFullPath(path);
        return HiddenProfiles.Any(entry => string.Equals(Path.GetFullPath(entry), full, StringComparison.Ordinal));
    }
}
