using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Fuehrt Pseudonyme auf die Echtwerte zurueck.
///
/// Bei strukturierten Dateien geschieht das feldweise ueber den Namensraum der
/// jeweiligen Regel — das ist der genaue Weg. In Freitext greift stattdessen die
/// Rueckabbildung ueber alle bekannten Pseudonyme, weil dort kein Feldbezug
/// vorliegt.
/// </summary>
public sealed class DeobfuscateTransformer : IRecordTransformer
{
    private readonly Profile _profile;
    private readonly FieldRuleResolver _resolver;
    private readonly Pseudonymizer _pseudonymizer;
    private readonly ReverseTextMapper _reverseMapper;
    private readonly RunReport _report;

    public DeobfuscateTransformer(
        Profile profile,
        FieldRuleResolver resolver,
        Pseudonymizer pseudonymizer,
        ReverseTextMapper reverseMapper,
        RunReport report)
    {
        _profile = profile;
        _resolver = resolver;
        _pseudonymizer = pseudonymizer;
        _reverseMapper = reverseMapper;
        _report = report;
    }

    public void OnFields(IReadOnlyList<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
            _report.FieldActions[fieldName] = _resolver.Resolve(fieldName).Action.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Bei der Rueckabbildung wird nichts entfernt: die Spalte fehlt in der
    /// Eingabe bereits, und was gestrichen wurde, ist ohnehin verloren.
    /// </summary>
    public bool ShouldDrop(string fieldName, string? jsonPath) => false;

    public string TransformField(string fieldName, string? jsonPath, string value, string location)
    {
        // Derselbe Helfer wie in ObfuscateTransformer und ScanTransformer,
        // sonst laufen Obfuskation, Pruefung und Rueckuebersetzung auseinander.
        if (_profile.Defaults.IsEffectivelyEmpty(value))
            return value;

        var rule = _resolver.Resolve(fieldName, jsonPath);

        switch (rule.Action)
        {
            case FieldAction.Pseudonymize:
            {
                var generatorName = rule.Generator ?? "token";
                if (_pseudonymizer.TryReverse(generatorName, value, out var plaintext))
                {
                    _report.CountHit(generatorName);
                    return plaintext;
                }

                // Nicht umkehrbare Generatoren (etwa partialMask oder
                // dateGeneralize) haben nie einen Tabelleneintrag angelegt.
                // Sie hier als "unbekanntes Pseudonym" zu melden legte den
                // falschen Schluss nahe, ein anderer Bestand koennte den Wert
                // noch hergeben -- es ist die Bauart des Generators.
                if (!_pseudonymizer.IsReversible(generatorName))
                {
                    _report.Findings.Add(new ReportFinding(generatorName, location, "nichtWiederherstellbar"));
                    return value;
                }

                // Unbekannter Wert: entweder war er nie ersetzt worden, oder er
                // stammt aus einem anderen Mapping-Bestand. Beides ist eine
                // Meldung wert, aber kein Grund abzubrechen.
                _report.Findings.Add(new ReportFinding(generatorName, location, "unbekanntesPseudonym"));
                return value;
            }

            case FieldAction.Redact:
                if (value == _profile.Defaults.RedactionPlaceholder)
                    _report.Findings.Add(new ReportFinding("redact", location, "nichtWiederherstellbar"));
                return value;

            case FieldAction.ScanText:
                // Im Freitextfeld ist unbekannt, welcher Teil ersetzt wurde;
                // deshalb greift hier dieselbe Rueckabbildung wie im Fliesstext.
                return RestoreFreeText(value);

            default:
                return value;
        }
    }

    public string TransformFreeText(string text, string location) => RestoreFreeText(text);

    private string RestoreFreeText(string text)
    {
        var restored = _reverseMapper.Restore(text, out var count);
        if (count > 0)
            _report.RuleHits["freitext"] = _report.RuleHits.TryGetValue("freitext", out var existing)
                ? existing + count
                : count;
        return restored;
    }
}
