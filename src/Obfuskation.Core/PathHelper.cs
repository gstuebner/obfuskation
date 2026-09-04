namespace Obfuskation.Core;

/// <summary>Aufloesung von Pfadangaben aus der Konfiguration.</summary>
public static class PathHelper
{
    /// <summary>Ersetzt ein fuehrendes <c>~</c> durch das Benutzerverzeichnis.</summary>
    public static string ExpandHome(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '~')
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
            return home;
        if (path[1] == '/' || path[1] == Path.DirectorySeparatorChar)
            return Path.Combine(home, path[2..]);

        // "~user/..." wird nicht unterstuetzt und bleibt unveraendert.
        return path;
    }

    /// <summary>
    /// Standardablage des Mapping-Stores: ausserhalb des Projekts, damit die
    /// Datei nicht versehentlich mit hochgeladen oder eingecheckt wird.
    /// </summary>
    public static string DefaultMappingStorePath(string profileName)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");

        return Path.Combine(dataHome, "obfuskation", SanitizeName(profileName), "mapping.json");
    }

    /// <summary>Entschaerft einen Profilnamen zur Verwendung als Verzeichnisname.</summary>
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "default";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return cleaned.Trim('.', '_') is { Length: > 0 } result ? result : "default";
    }

    /// <summary>
    /// Sucht ab <paramref name="path"/> aufwaerts nach einem <c>.git</c>-Eintrag.
    /// Der Mapping-Store darf nicht in einem Git-Arbeitsverzeichnis liegen.
    /// </summary>
    public static bool IsInsideGitWorkingTree(string path)
    {
        var directory = Directory.Exists(path)
            ? new DirectoryInfo(Path.GetFullPath(path))
            : new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

        while (directory is not null)
        {
            var git = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return true;
            directory = directory.Parent;
        }
        return false;
    }
}
