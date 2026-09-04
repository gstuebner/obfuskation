using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Durchsucht eine bereits pseudonymisierte Datei nach Restbestaenden und
/// veraendert dabei nichts.
///
/// Das ist das Sicherheitsnetz gegen die Schwaeche des Musteransatzes: was keine
/// Regel getroffen hat, steht noch drin. Geprueft wird gegen zwei Quellen:
/// <list type="number">
/// <item>Die Klartexte aus der Ersetzungstabelle. Das ist der harte Befund —
/// von diesen Werten ist erwiesen, dass sie echt sind.</item>
/// <item>Die Textregeln des Profils. Hier gilt ein Treffer nur dann als
/// Verdacht, wenn der Wert nicht selbst ein bekanntes Pseudonym ist: eine
/// erzeugte Test-IBAN passt zwangslaeufig auf das IBAN-Muster, und ohne diese
/// Einschraenkung meldete die Pruefung ausschliesslich sich selbst.</item>
/// </list>
/// </summary>
public sealed class ScanTransformer : IRecordTransformer
{
    /// <summary>
    /// Mindestlaenge fuer den Abgleich mit Klartexten. Kuerzere Werte erzeugen
    /// zu viele Zufallstreffer und machen den Bericht unbrauchbar.
    /// </summary>
    private const int MinimumPlaintextLength = 4;

    private readonly Profile _profile;
    private readonly FieldRuleResolver _resolver;
    private readonly TextRuleEngine _textEngine;
    private readonly RunReport _report;
    private readonly IReadOnlyList<(string Namespace, string Plaintext)> _knownPlaintexts;
    private readonly HashSet<string> _knownPseudonyms;

    public ScanTransformer(
        Profile profile,
        FieldRuleResolver resolver,
        TextRuleEngine textEngine,
        MappingStore store,
        RunReport report)
    {
        _profile = profile;
        _resolver = resolver;
        _textEngine = textEngine;
        _report = report;

        _knownPlaintexts = store.AllPlaintexts()
            .Where(entry => entry.Plaintext.Length >= MinimumPlaintextLength)
            .ToList();

        _knownPseudonyms = store.AllReverseEntries()
            .Select(entry => entry.Pseudonym)
            .ToHashSet(StringComparer.Ordinal);
    }

    public void OnFields(IReadOnlyList<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var rule = _resolver.Resolve(fieldName);
            _report.FieldActions[fieldName] = rule.Action.ToString().ToLowerInvariant();

            if (rule.IsFromDefault)
                _report.UnhandledFields.Add(fieldName);
        }
    }

    public bool ShouldDrop(string fieldName, string? jsonPath) => false;

    public string TransformField(string fieldName, string? jsonPath, string value, string location)
    {
        Inspect(value, location);
        return value;
    }

    public string TransformFreeText(string text, string location)
    {
        // Im Fliesstext wird zeilenweise gemeldet, damit die Fundstelle brauchbar ist.
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            Inspect(lines[index], $"Zeile {index + 1}");

        return text;
    }

    private void Inspect(string value, string location)
    {
        if (string.IsNullOrEmpty(value))
            return;

        // Ein Wert, der selbst ein vergebenes Pseudonym ist, ist geprueft in
        // Ordnung — auch wenn er zufaellig mit einem Klartext zusammenfaellt
        // oder auf ein Muster passt.
        if (_knownPseudonyms.Contains(value))
            return;

        // Der harte Befund zuerst: ein Wert, der als Klartext in der Tabelle steht.
        var reportedAsPlaintext = false;
        foreach (var (namespaceName, plaintext) in _knownPlaintexts)
        {
            if (value.Contains(plaintext, StringComparison.Ordinal))
            {
                _report.Findings.Add(new ReportFinding(namespaceName, location, "echtwertAusTabelle"));
                _report.CountHit("fund:echtwert");
                reportedAsPlaintext = true;
            }
        }

        // Musterverdacht ist die schwaechere Aussage und waere neben einem
        // belegten Echtwert nur Doppelmeldung.
        if (reportedAsPlaintext)
            return;

        foreach (var match in _textEngine.FindMatches(value, _profile.TextRules))
        {
            if (_knownPseudonyms.Contains(match.Value))
                continue;

            _report.Findings.Add(new ReportFinding(match.Rule.Name, location, "musterTreffer"));
            _report.CountHit("fund:" + match.Rule.Name);
        }
    }
}
