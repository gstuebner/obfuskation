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
    /// Haengt die Fusszeile an jede Hilfeausgabe an — an die des Programms
    /// selbst wie an die jedes Unterbefehls. System.CommandLine kennt keinen
    /// Platz fuer eigenen Text unter der Hilfe, deshalb wird die vorhandene
    /// Hilfeaktion umschlossen statt ersetzt.
    /// </summary>
    public static void AddHelpFooter(Command command)
    {
        foreach (var option in command.Options)
        {
            if (option is HelpOption { Action: SynchronousCommandLineAction inner } help)
                help.Action = new FooterHelpAction(inner);
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
