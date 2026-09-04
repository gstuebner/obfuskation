using System.Text.Json.Serialization;

namespace Obfuskation.Core.Reporting;

/// <summary>
/// Ergebnisbericht eines Laufs.
///
/// Der Bericht enthaelt ausschliesslich Zaehler, Feld- und Regelnamen — niemals
/// Klartexte oder Pseudonyme. Nur so kann er gefahrlos protokolliert, in der
/// Oberflaeche angezeigt oder weitergereicht werden.
/// </summary>
public sealed class RunReport
{
    public string Command { get; set; } = "";

    public string? Input { get; set; }

    public string? Output { get; set; }

    public string Format { get; set; } = "";

    /// <summary>Erkannter Zeichensatz der Eingabe.</summary>
    public string? Encoding { get; set; }

    /// <summary>Erkanntes CSV-Trennzeichen.</summary>
    public string? Delimiter { get; set; }

    public int RowsProcessed { get; set; }

    /// <summary>Wie oft je Generator beziehungsweise Textregel ersetzt wurde.</summary>
    public SortedDictionary<string, int> RuleHits { get; } = new(StringComparer.Ordinal);

    /// <summary>Behandlung je Feld, damit nachvollziehbar bleibt, was womit geschah.</summary>
    public SortedDictionary<string, string> FieldActions { get; } = new(StringComparer.Ordinal);

    /// <summary>Felder ohne eigene Regel, die nach der Vorgabe behandelt wurden.</summary>
    public List<string> UnhandledFields { get; } = new();

    public int NewMappings { get; set; }

    public int TotalMappings { get; set; }

    /// <summary>Befunde von <c>scan</c> und <c>deobfuscate</c>.</summary>
    public List<ReportFinding> Findings { get; } = new();

    public List<ReportWarning> Warnings { get; } = new();

    public long DurationMs { get; set; }

    [JsonIgnore]
    public bool HasFindings => Findings.Count > 0;

    public void CountHit(string key)
    {
        RuleHits.TryGetValue(key, out var current);
        RuleHits[key] = current + 1;
    }

    public void Warn(string code, string message)
        => Warnings.Add(new ReportWarning(code, message));
}

/// <param name="Code">Kurzkennung, etwa <c>storePermissions</c>.</param>
public sealed record ReportWarning(string Code, string Message);

/// <summary>
/// Ein Verdachtsfall aus <c>scan</c>. Enthaelt bewusst keinen Fundwert, sondern
/// nur seine Position — sonst wuerde der Bericht genau die Daten transportieren,
/// die er meldet.
/// </summary>
/// <param name="Rule">Regel oder Namensraum, der angeschlagen hat.</param>
/// <param name="Location">Fundstelle, etwa <c>Zeile 42, Spalte Bemerkung</c>.</param>
/// <param name="Kind">Art des Befunds.</param>
public sealed record ReportFinding(string Rule, string Location, string Kind);
