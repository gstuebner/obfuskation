using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Obfuskation.Core.Mapping;

/// <summary>
/// Setzt die Zugriffsrechte des Mapping-Stores durch. Auf Unix ist das der
/// eigentliche Schutz der Datei; auf anderen Systemen bleibt es wirkungslos und
/// wird als Hinweis vermerkt.
/// </summary>
internal static class FilePermissions
{
    private const UnixFileMode FileMode600 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode DirectoryMode700 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // Das Guard-Attribut sagt dem Compiler zu, dass hinter dieser Pruefung
    // keine Windows-Plattform mehr liegt; die Unix-Aufrufe sind damit belegt.
    [UnsupportedOSPlatformGuard("windows")]
    private static bool IsUnix => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static void RestrictFile(string path)
    {
        if (!IsUnix)
            return;

        try
        {
            File.SetUnixFileMode(path, FileMode600);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Auf manchen Dateisystemen (etwa exFAT) gibt es keine Rechte zu setzen.
        }
    }

    public static void RestrictDirectory(string path, List<string> warnings)
    {
        if (!IsUnix)
        {
            warnings.Add(
                "Zugriffsrechte lassen sich auf diesem System nicht setzen. Der Mapping-Store " +
                "muss von Hand geschützt werden.");
            return;
        }

        try
        {
            var current = File.GetUnixFileMode(path);
            if ((current & ~DirectoryMode700) != 0)
            {
                warnings.Add(
                    $"Das Verzeichnis des Mapping-Stores war für andere zugänglich ({Describe(current)}); " +
                    "die Rechte wurden auf 0700 gesetzt.");
            }
            File.SetUnixFileMode(path, DirectoryMode700);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Die Rechte des Verzeichnisses {path} ließen sich nicht setzen: {ex.Message}");
        }
    }

    public static void CheckFile(string path, List<string> warnings)
    {
        if (!IsUnix)
            return;

        try
        {
            var current = File.GetUnixFileMode(path);
            if ((current & ~FileMode600) != 0)
            {
                warnings.Add(
                    $"Der Mapping-Store war für andere lesbar ({Describe(current)}); " +
                    "die Rechte wurden auf 0600 gesetzt. Die Datei enthält Echtdaten — " +
                    "prüfen, wer bisher Zugriff hatte.");
            }
            RestrictFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Die Rechte der Datei {path} ließen sich nicht prüfen: {ex.Message}");
        }
    }

    private static string Describe(UnixFileMode mode)
    {
        var owner = ((int)(mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) >> 6) & 7;
        var group = ((int)(mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute)) >> 3) & 7;
        var others = (int)(mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) & 7;
        return $"0{owner}{group}{others}";
    }
}
