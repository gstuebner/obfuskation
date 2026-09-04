using System.Collections.ObjectModel;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Der Bericht eines Laufs, aufbereitet fuer die Anzeige.
///
/// <see cref="RunReport"/> enthaelt per Bauart keine Klartexte und keine
/// Pseudonyme, nur Zaehler und Feldnamen — er darf deshalb ohne Weiteres
/// angezeigt und protokolliert werden.
/// </summary>
public sealed class RunResultViewModel
{
    private const int MaxFindingsShown = 50;

    public RunResultViewModel(RunReport report)
    {
        Command = report.Command;
        RowsProcessed = report.RowsProcessed;
        NewMappings = report.NewMappings;
        TotalMappings = report.TotalMappings;
        DurationMs = report.DurationMs;

        foreach (var (regel, anzahl) in report.RuleHits)
            RuleHits.Add(new NamedCount(regel, anzahl));

        foreach (var warnung in report.Warnings)
            Warnings.Add(warnung.Message);

        foreach (var befund in report.Findings.Take(MaxFindingsShown))
            Findings.Add(new FindingItem(befund.Location, befund.Rule, Describe(befund.Kind)));

        TotalFindings = report.Findings.Count;
        UnhandledFields = string.Join(", ", report.UnhandledFields);
    }

    public string Command { get; }
    public int RowsProcessed { get; }
    public int NewMappings { get; }
    public int TotalMappings { get; }
    public long DurationMs { get; }
    public int TotalFindings { get; }
    public string UnhandledFields { get; }

    public ObservableCollection<NamedCount> RuleHits { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<FindingItem> Findings { get; } = new();

    public bool HasRuleHits => RuleHits.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasFindings => TotalFindings > 0;
    public bool HasUnhandledFields => !string.IsNullOrEmpty(UnhandledFields);

    public string Headline => Command switch
    {
        "obfuscate" => "Ersetzt",
        "deobfuscate" => "Zurueckgeholt",
        "scan" => "Geprueft",
        _ => Command,
    };

    public string Summary
        => $"{RowsProcessed} Datensaetze · {DurationMs} ms"
           + (NewMappings > 0 ? $" · {NewMappings} neue Eintraege" : "")
           + (TotalMappings > 0 ? $" · {TotalMappings} in der Tabelle" : "");

    /// <summary>Hinweis, wenn nicht alle Befunde angezeigt werden.</summary>
    public string FindingsNote
        => TotalFindings > Findings.Count
            ? $"… und {TotalFindings - Findings.Count} weitere"
            : "";

    public bool HasFindingsNote => TotalFindings > Findings.Count;

    private static string Describe(string kind) => kind switch
    {
        "echtwertAusTabelle" => "Echtwert aus der Tabelle",
        "musterTreffer" => "Mustertreffer",
        "unbekanntesPseudonym" => "unbekanntes Pseudonym",
        "nichtWiederherstellbar" => "nicht wiederherstellbar",
        _ => kind,
    };
}

/// <param name="Name">Generator oder Textregel.</param>
/// <param name="Count">Wie oft gegriffen.</param>
public sealed record NamedCount(string Name, int Count);

/// <param name="Location">Fundstelle, etwa <c>Zeile 42, Spalte Bemerkung</c>.</param>
/// <param name="Rule">Regel oder Namensraum, der angeschlagen hat.</param>
/// <param name="Kind">Art des Befunds, in Worten.</param>
public sealed record FindingItem(string Location, string Rule, string Kind);
