using System.Text.Json;
using System.Text.Json.Serialization;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Cli;

/// <summary>
/// Ausgabe auf der Konsole. Saemtliche Textausgabe des Programms laeuft hier
/// zusammen; die Bibliothek selbst gibt nie etwas aus.
/// </summary>
public static class ConsoleOutput
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Gibt den Bericht als JSON aus. Der Bericht enthaelt keine Klartexte, er
    /// darf deshalb protokolliert und weitergereicht werden.
    /// </summary>
    public static void WriteJson(RunReport report)
        => Console.Out.WriteLine(JsonSerializer.Serialize(report, ReportJsonOptions));

    public static void WriteSummary(RunReport report)
    {
        var target = Console.Error;

        target.WriteLine($"{report.Command}: {report.Input ?? "(Standardeingabe)"} [{report.Format}]");

        if (report.Encoding is not null)
        {
            var delimiter = report.Delimiter is null ? "" : $", Trennzeichen '{report.Delimiter}'";
            target.WriteLine($"  Zeichensatz {report.Encoding}{delimiter}");
        }

        target.WriteLine($"  Datensaetze: {report.RowsProcessed}");

        if (report.RuleHits.Count > 0)
        {
            var hits = report.RuleHits.Select(entry => $"{entry.Key}={entry.Value}");
            target.WriteLine($"  Ersetzungen: {string.Join(", ", hits)}");
        }

        if (report.UnhandledFields.Count > 0)
            target.WriteLine($"  Ohne eigene Regel: {string.Join(", ", report.UnhandledFields)}");

        if (report.NewMappings > 0 || report.TotalMappings > 0)
            target.WriteLine($"  Tabelle: {report.NewMappings} neu, {report.TotalMappings} gesamt");

        foreach (var warning in report.Warnings)
            target.WriteLine($"  Hinweis [{warning.Code}]: {warning.Message}");

        if (report.Findings.Count > 0)
        {
            target.WriteLine($"  BEFUNDE: {report.Findings.Count}");

            // Nur die ersten Fundstellen einzeln nennen, sonst wird die Ausgabe
            // unlesbar. Der vollstaendige Satz steht in der JSON-Ausgabe.
            foreach (var finding in report.Findings.Take(20))
                target.WriteLine($"    {finding.Location}: {finding.Rule} ({finding.Kind})");

            if (report.Findings.Count > 20)
                target.WriteLine($"    ... und {report.Findings.Count - 20} weitere (siehe --json)");
        }

        target.WriteLine($"  Dauer: {report.DurationMs} ms");
    }

    public static void WriteError(string message)
    {
        Console.Error.WriteLine("Fehler: " + message);
    }

    public static void WriteWarning(string message)
    {
        Console.Error.WriteLine("Hinweis: " + message);
    }

    public static void WriteInfo(string message)
    {
        Console.Error.WriteLine(message);
    }
}
