using Obfuskation.Core.Generation;

namespace Obfuskation.Cli.Tests;

/// <summary>
/// Regressionstests zu den Befunden A10 und B2 der Codepruefung von 1.13.0
/// (_dev/features+bugs/befunde-codepruefung-1.13.0.md).
/// </summary>
public sealed class Codepruefung1130CliTests : IDisposable
{
    private readonly string _verzeichnis;

    public Codepruefung1130CliTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);
    }

    [Fact]
    public void A10_Ausgabe_gleich_Eingabe_wird_vor_dem_Lauf_abgelehnt()
    {
        var datei = Path.Combine(_verzeichnis, "t.csv");
        File.WriteAllText(datei, "a\n1\n");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CommandContext.CheckOutput(datei, Path.Combine(_verzeichnis, ".", "t.csv"), jsonReport: false));

        Assert.Contains("Eingabedatei", ex.Message);
    }

    [Fact]
    public void A10_Unter_Windows_zaehlt_die_Schreibweise_nicht()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var datei = Path.Combine(_verzeichnis, "t.csv");
        File.WriteAllText(datei, "a\n1\n");

        Assert.Throws<InvalidOperationException>(
            () => CommandContext.CheckOutput(datei, Path.Combine(_verzeichnis, "T.CSV"), jsonReport: false));
    }

    [Fact]
    public void A10_Json_ohne_Ausgabedatei_wird_vor_dem_Lauf_abgelehnt()
    {
        Assert.Throws<InvalidOperationException>(
            () => CommandContext.CheckOutput("t.csv", null, jsonReport: true));
    }

    [Fact]
    public void A10_Verschiedene_Dateien_und_Standardein_und_ausgabe_sind_erlaubt()
    {
        CommandContext.CheckOutput(Path.Combine(_verzeichnis, "a.csv"), Path.Combine(_verzeichnis, "b.csv"), false);
        CommandContext.CheckOutput("-", null, false);
        CommandContext.CheckOutput(null, Path.Combine(_verzeichnis, "b.csv"), true);
    }

    [Fact]
    public void A10_Die_Ausgabe_wird_ueber_eine_Nebendatei_geschrieben()
    {
        var ziel = Path.Combine(_verzeichnis, "aus", "b.csv");

        CommandContext.WriteOutput([1, 2, 3], ziel, jsonReport: false);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(ziel));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ziel)!));
    }

    [Fact]
    public void B2_GenerationException_ergibt_eine_Fehlerzeile_und_einen_eigenen_Exitcode()
    {
        var code = CommandContext.Run(() => throw new GenerationException("dateShift", "nicht lesbar"));

        Assert.Equal(ExitCodes.GenerationError, code);
    }

    [Fact]
    public void B2_Unerwartete_Ausnahmen_fuehren_zu_Failure_statt_zum_Stacktrace()
    {
        Assert.Equal(ExitCodes.Failure, CommandContext.Run(() => throw new ArgumentException("x")));
        Assert.Equal(ExitCodes.Failure, CommandContext.Run(() => throw new FormatException("x")));
        Assert.Equal(ExitCodes.Failure, CommandContext.Run(
            () => throw new System.Text.RegularExpressions.RegexMatchTimeoutException("a", "b", TimeSpan.FromSeconds(1))));
    }

    public void Dispose()
    {
        try { Directory.Delete(_verzeichnis, recursive: true); }
        catch (IOException) { }
    }
}
