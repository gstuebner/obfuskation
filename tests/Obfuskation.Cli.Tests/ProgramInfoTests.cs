using System.CommandLine;
using System.CommandLine.Help;
using Obfuskation.Cli;

namespace Obfuskation.Cli.Tests;

/// <summary>
/// Befund D-7: <c>--help</c> mischt Deutsch und Englisch. Der einzige Teil,
/// der sich ueber die oeffentliche API von System.CommandLine 2.0.11
/// beheben laesst, ist der Text der Hilfeoption selbst; die Ueberschrift
/// "Description:" bleibt eine bekannte Einschraenkung (siehe
/// docs/entwicklerdokumentation.md, Abschnitt 11). Ohne Abfangen der
/// Konsolenausgabe pruefbar, weil <see cref="Symbol.Description"/> am
/// Objektmodell selbst steht.
/// </summary>
public sealed class ProgramInfoTests
{
    [Fact]
    public void AddHelpFooter_uebersetzt_die_Beschreibung_der_Hilfeoption_der_Wurzel()
    {
        var root = new RootCommand("Testbefehl.");

        ProgramInfo.AddHelpFooter(root);

        var help = Assert.Single(root.Options.OfType<HelpOption>());
        Assert.Equal("Zeigt Hilfe und Verwendungsinformationen an.", help.Description);
    }

    [Fact]
    public void Ein_Unterbefehl_bekommt_keine_eigene_Hilfeoption_und_braucht_deshalb_auch_keine()
    {
        // Gegenprobe zum vorigen Test: System.CommandLine haengt --help nur
        // einmal an die Wurzel und behandelt es dort als globale Option, die
        // fuer jeden Unterbefehl mitgilt (belegt am Programm selbst:
        // "obfuscate --help" zeigt densselben uebersetzten Text). Ein
        // Unterbefehl fuehrt also nie eine eigene HelpOption in seinen
        // Options -- die Rekursion in AddHelpFooter ist defensiv fuer den
        // Fall, dass sich das aendert, aendert hier aber nichts.
        var root = new RootCommand("Testbefehl.");
        var sub = new Command("unter", "Ein Unterbefehl.");
        root.Subcommands.Add(sub);

        ProgramInfo.AddHelpFooter(root);

        Assert.Empty(sub.Options.OfType<HelpOption>());
    }
}
