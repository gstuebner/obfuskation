using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Obfuskation.Cli;

/// <summary>
/// Fassung und Ersteller des Programms, wie sie unter jeder Hilfe stehen.
/// </summary>
public static class ProgramInfo
{
    /// <summary>
    /// Die Fassung ohne den angehaengten Commit
    /// (<c>1.0.2+abcdef…</c> wird zu <c>1.0.2</c>). Ohne gesetzte
    /// Informationsfassung — etwa beim Bauen ohne Linker-Angabe — bleibt
    /// <c>dev</c> uebrig.
    /// </summary>
    public static string Version
    {
        get
        {
            var informational = typeof(ProgramInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational))
                return "dev";

            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }
    }

    /// <summary>
    /// Die einzeilige Fusszeile, in allen Anwendungen dieselbe Form.
    /// </summary>
    public static string Footer => $"obfuskation {Version} · Gregor Stübner & Claude (Anthropic)";

    /// <summary>
    /// Deutscher Text der eingebauten <c>-h</c>/<c>--help</c>-Option (Befund
    /// D-7). Die englische Vorgabe "Show help and usage information" stammt
    /// aus <c>System.CommandLine.Properties.Resources</c>; per Reflection
    /// gegen Fassung 2.0.11 geprueft, fehlt genau dieser Schluessel
    /// (<c>HelpOptionDescription</c>) im deutschen Resourcenset, waehrend
    /// andere Ueberschriften wie "Usage:"/"Options:" dort sehr wohl uebersetzt
    /// sind ("Nutzung:"/"Optionen:") — eine Luecke im Paket, keine bewusste
    /// Entscheidung. <see cref="Symbol.Description"/> ist eine ganz normale
    /// oeffentliche Eigenschaft (wie bei jeder selbst angelegten Option),
    /// ihr Ueberschreiben ist deshalb keine Ersatzkonstruktion, sondern der
    /// vorgesehene Weg.
    /// </summary>
    private const string HelpOptionDescriptionDe = "Zeigt Hilfe und Verwendungsinformationen an.";

    /// <summary>
    /// Haengt die Fusszeile an jede Hilfeausgabe an — an die des Programms
    /// selbst wie an die jedes Unterbefehls — und uebersetzt dabei den Text
    /// der Hilfeoption selbst (D-7). Die Ueberschrift "Description:" bleibt
    /// davon unberuehrt: sie kommt aus dem internen, nicht ableitbaren
    /// <c>HelpBuilder</c> von System.CommandLine, fuer den es keinen
    /// oeffentlichen Ueberschreibungspunkt gibt (siehe Befund D-7 in
    /// docs/entwicklerdokumentation.md, Abschnitt 11). System.CommandLine
    /// kennt auch keinen Platz fuer eigenen Text unter der Hilfe, deshalb
    /// wird die vorhandene Hilfeaktion fuer die Fusszeile umschlossen statt
    /// ersetzt.
    /// </summary>
    public static void AddHelpFooter(Command command)
    {
        foreach (var option in command.Options)
        {
            if (option is HelpOption { Action: SynchronousCommandLineAction inner } help)
            {
                help.Action = new FooterHelpAction(inner);
                help.Description = HelpOptionDescriptionDe;
            }
        }

        foreach (var subcommand in command.Subcommands)
            AddHelpFooter(subcommand);
    }

    private sealed class FooterHelpAction(SynchronousCommandLineAction inner) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            var result = inner.Invoke(parseResult);
            parseResult.InvocationConfiguration.Output.WriteLine();
            parseResult.InvocationConfiguration.Output.WriteLine(Footer);
            return result;
        }
    }
}
