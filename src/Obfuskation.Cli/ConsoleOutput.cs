using System.Text.Json;
using System.Text.Json.Serialization;
using Obfuskation.Core.Configuration;
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

    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
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

    /// <summary>
    /// Tabelle bekannter Profile auf die Standardfehlerausgabe, wie
    /// <see cref="WriteSummary"/>. Die Reihenfolge kommt bereits sortiert vom
    /// Aufrufer, hier wird nur ausgerichtet und ausgegeben.
    /// </summary>
    public static void WriteProfiles(IReadOnlyList<ProfileSummary> profiles)
    {
        var target = Console.Error;

        if (profiles.Count == 0)
        {
            target.WriteLine(
                "Keine Profile gefunden. 'obfuskation init --central' oder die Oberflaeche legen eines an.");
            return;
        }

        var rows = profiles.Select(profile => new
        {
            Profile = profile,
            Name = profile.Error is null ? profile.Name : $"{profile.Name} (nicht lesbar)",
            Files = profile.Error is not null
                ? "-"
                : profile.DataFiles.Count == 1 ? "1 Datei" : $"{profile.DataFiles.Count} Dateien",
            Used = profile.LastUsedUtc is { } lastUsed
                ? lastUsed.LocalDateTime.ToString("dd.MM.yyyy HH:mm")
                : "nie",
            Changed = profile.Error is null ? profile.ModifiedUtc.LocalDateTime.ToString("dd.MM.yyyy") : "-",
        }).ToList();

        var nameWidth = Math.Max("Name".Length, rows.Max(r => r.Name.Length));
        var filesWidth = Math.Max("Dateien".Length, rows.Max(r => r.Files.Length));
        var usedWidth = Math.Max("Zuletzt benutzt".Length, rows.Max(r => r.Used.Length));
        var changedWidth = Math.Max("Geaendert".Length, rows.Max(r => r.Changed.Length));

        target.WriteLine(
            $"{"Name".PadRight(nameWidth)}  {"Dateien".PadRight(filesWidth)}  " +
            $"{"Zuletzt benutzt".PadRight(usedWidth)}  {"Geaendert".PadRight(changedWidth)}  Pfad");

        foreach (var row in rows)
        {
            target.WriteLine(
                $"{row.Name.PadRight(nameWidth)}  {row.Files.PadRight(filesWidth)}  " +
                $"{row.Used.PadRight(usedWidth)}  {row.Changed.PadRight(changedWidth)}  {row.Profile.Path}");

            if (row.Profile.Error is not null)
                target.WriteLine($"  Fehler: {row.Profile.Error}");
        }
    }

    /// <summary>
    /// Dieselbe Liste als JSON auf die Standardausgabe. Enthaelt Pfade, aber
    /// wie <see cref="ProfileSummary"/> selbst keine Echtwerte aus den
    /// verarbeiteten Dateien.
    /// </summary>
    public static void WriteProfilesJson(IReadOnlyList<ProfileSummary> profiles)
        => Console.Out.WriteLine(JsonSerializer.Serialize(profiles, ProfileJsonOptions));

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
