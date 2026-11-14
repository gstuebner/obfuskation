using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;
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

    /// <summary>Datumsformat je Feld, aus dem Vorablauf (<see cref="OnFieldValues"/>).</summary>
    private readonly Dictionary<string, string> _dateFormats = new(StringComparer.Ordinal);

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

    public void OnFields(IReadOnlyList<FieldOccurrence> fields)
    {
        foreach (var field in fields)
            _report.FieldActions[field.Name] = _resolver.Resolve(field.Name, field.JsonPath).Action.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Nur Generatoren mit eigener Umkehrung brauchen das Spaltenformat: alle
    /// anderen schlagen den Wert woertlich in der Tabelle nach.
    /// </summary>
    public bool WantsValues(FieldOccurrence field)
    {
        var rule = _resolver.Resolve(field.Name, field.JsonPath);
        var generatorName = rule.Generator ?? "token";
        return rule.Action == FieldAction.Pseudonymize
               && _pseudonymizer.HasIntrinsicInverse(generatorName)
               && _pseudonymizer.DateFormatsOf(generatorName) is not null;
    }

    public void OnFieldValues(FieldOccurrence field, IReadOnlyCollection<string> values)
    {
        var rule = _resolver.Resolve(field.Name, field.JsonPath);
        var generatorName = rule.Generator ?? "token";
        var formats = _pseudonymizer.DateFormatsOf(generatorName);
        if (formats is null)
            return;

        var relevant = values.Where(value => !_profile.Defaults.IsEffectivelyEmpty(value)).ToList();
        var format = DateValues.DetermineColumnFormat(relevant, formats, generatorName, field.Name, out var candidates);
        if (format is null)
            return;

        _dateFormats[field.Key] = format;

        // Passen mehrere Formate auf die ganze Spalte (etwa "dd.MM.yyyy" und
        // "d.M.yyyy", wenn kein Tag und kein Monat einstellig ist), steht die
        // Schreibweise des Originals nicht fest. Das Datum stimmt in jedem
        // Fall; nur die fuehrenden Nullen koennten abweichen.
        foreach (var other in candidates.Skip(1))
        {
            foreach (var value in relevant)
            {
                if (_pseudonymizer.TryReverse(generatorName, value, out var a, format)
                    && _pseudonymizer.TryReverse(generatorName, value, out var b, other)
                    && a != b)
                {
                    _report.Warn("datumsschreibweiseUnklar",
                        $"Im Feld '{field.Name}' passen die Datumswerte auf '{format}' und auf '{other}'. " +
                        $"Zurückgerechnet wird in '{format}'; die Daten stimmen, führende Nullen können " +
                        $"aber vom Original abweichen. Abhilfe: generators.{generatorName}.formats setzen.");
                    return;
                }
            }
        }
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
                var key = jsonPath is null ? fieldName : JsonPathMatcher.Normalize(jsonPath);
                _dateFormats.TryGetValue(key, out var dateFormat);

                bool found;
                string plaintext;
                try
                {
                    found = _pseudonymizer.TryReverse(generatorName, value, out plaintext, dateFormat);
                }
                catch (GenerationException ex)
                {
                    throw ex.WithLocation(location);
                }

                if (found)
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

    public string TransformNameless(string jsonPath, string value, string location)
    {
        var normalized = JsonPathMatcher.Normalize(jsonPath);
        var rule = _resolver.Resolve(normalized, jsonPath);

        // Ohne eigene Regel liefen namenlose Werte beim Verschleiern durch die
        // Textregeln; zurueck geht es entsprechend ueber den Freitext.
        return rule.IsFromDefault
            ? (_profile.Defaults.IsEffectivelyEmpty(value) ? value : RestoreFreeText(value))
            : TransformField(normalized, jsonPath, value, location);
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
