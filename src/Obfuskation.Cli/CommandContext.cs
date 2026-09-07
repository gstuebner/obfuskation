using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Cli;

/// <summary>
/// Gemeinsame Ablaeufe der Unterbefehle: Konfiguration laden, Ein- und Ausgabe
/// abwickeln und Ausnahmen auf Rueckgabewerte abbilden.
/// </summary>
public static class CommandContext
{
    /// <summary>
    /// Laedt das Profil. <paramref name="configPath"/> darf ein Dateipfad oder
    /// ein blosser Profilname sein (dann wird unter
    /// <see cref="PathHelper.DefaultProfilePath"/> gesucht); ohne Angabe wird
    /// ab dem aktuellen Verzeichnis aufwaerts nach <c>obfuskation.json</c>
    /// gesucht.
    /// </summary>
    public static Profile LoadProfile(string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var expanded = PathHelper.ExpandHome(configPath);

            // Sieht der Wert nicht wie ein Pfad aus (kein Trennzeichen, keine
            // .json-Endung) und existiert er auch nicht woertlich als Datei,
            // dann steckt vermutlich ein Profilname dahinter -- im zentralen
            // Ordner nachsehen, bevor aufgegeben wird.
            if (LooksLikeProfileName(configPath) && !File.Exists(expanded))
            {
                var central = PathHelper.DefaultProfilePath(configPath);
                if (File.Exists(central))
                    return ProfileStore.Load(central);

                throw new ConfigurationException(
                    $"Kein Profil gefunden fuer '{configPath}': weder als Datei ({expanded}) " +
                    $"noch im zentralen Ordner ({central}). Vorhandene Profile zeigt " +
                    "'obfuskation profile list'.");
            }

            return ProfileStore.Load(expanded);
        }

        var discovered = ProfileStore.Discover(Directory.GetCurrentDirectory());
        if (discovered is null)
        {
            throw new ConfigurationException(
                $"Keine {ProfileStore.DefaultFileName} gefunden. Mit 'obfuskation init --from <datei>' " +
                "ein Regelgeruest erzeugen oder den Pfad mit --config angeben. Vorhandene Profile " +
                "zeigt 'obfuskation profile list'.");
        }

        return ProfileStore.Load(discovered);
    }

    /// <summary>
    /// Ob <paramref name="value"/> eher ein blosser Profilname als ein Pfad
    /// ist: kein Verzeichnistrennzeichen, keine <c>.json</c>-Endung.
    /// </summary>
    private static bool LooksLikeProfileName(string value)
        => value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
           && !value.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static byte[] ReadInput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "-")
        {
            using var standardInput = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            standardInput.CopyTo(buffer);
            return buffer.ToArray();
        }

        var expanded = PathHelper.ExpandHome(path);
        if (!File.Exists(expanded))
            throw new FileNotFoundException($"Eingabedatei nicht gefunden: {expanded}", expanded);

        return File.ReadAllBytes(expanded);
    }

    /// <summary>
    /// Schreibt das Ergebnis. Ohne Zielpfad geht es auf die Standardausgabe —
    /// ausser bei JSON-Bericht, der dort bereits steht.
    /// </summary>
    public static void WriteOutput(byte[] content, string? path, bool jsonReport)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var expanded = PathHelper.ExpandHome(path);
            var directory = Path.GetDirectoryName(Path.GetFullPath(expanded));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(expanded, content);
            return;
        }

        if (jsonReport)
        {
            // Beides auf die Standardausgabe zu schreiben wuerde den Bericht
            // und die Nutzdaten ununterscheidbar vermengen.
            throw new InvalidOperationException(
                "Mit --json muss die Ausgabedatei ueber -o angegeben werden, sonst " +
                "vermischen sich Bericht und Daten auf der Standardausgabe.");
        }

        using var standardOutput = Console.OpenStandardOutput();
        standardOutput.Write(content, 0, content.Length);
        standardOutput.Flush();
    }

    /// <summary>
    /// Fuehrt einen Unterbefehl aus und bildet bekannte Ausnahmen auf
    /// Rueckgabewerte ab. Unerwartete Ausnahmen bleiben unbehandelt.
    /// </summary>
    public static int Run(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (UnhandledFieldException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            ConsoleOutput.WriteInfo(
                "  Fuer jedes genannte Feld in 'fields' eine action setzen: pseudonymize, " +
                "passthrough, redact oder drop. Solange das aussteht, wird bewusst nichts " +
                "geschrieben.");
            return ExitCodes.UnhandledField;
        }
        catch (ConfigurationException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            foreach (var issue in ex.Issues.Where(i => i.Severity == ValidationSeverity.Error))
                ConsoleOutput.WriteInfo($"  {issue.Path}: {issue.Message}");
            return ExitCodes.ConfigurationError;
        }
        catch (MappingLockedException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.MappingProblem;
        }
        catch (MappingConflictException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.MappingProblem;
        }
        catch (FileNotFoundException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.Failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.Failure;
        }
    }

    /// <summary>Gibt den Bericht in der gewuenschten Form aus.</summary>
    public static void Report(RunReport report, bool json)
    {
        if (json)
            ConsoleOutput.WriteJson(report);
        else
            ConsoleOutput.WriteSummary(report);
    }
}
