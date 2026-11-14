using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>Ersetzt Echtwerte durch Pseudonyme.</summary>
public sealed class ObfuscateTransformer : IRecordTransformer
{
    private readonly Profile _profile;
    private readonly FieldRuleResolver _resolver;
    private readonly Pseudonymizer _pseudonymizer;
    private readonly TextRuleEngine _textEngine;
    private readonly RunReport _report;
    private readonly bool _persist;

    /// <summary>Datumsformat je Feld, aus dem Vorablauf (<see cref="OnFieldValues"/>).</summary>
    private readonly Dictionary<string, string> _dateFormats = new(StringComparer.Ordinal);

    public ObfuscateTransformer(
        Profile profile,
        FieldRuleResolver resolver,
        Pseudonymizer pseudonymizer,
        TextRuleEngine textEngine,
        RunReport report,
        bool persist)
    {
        _profile = profile;
        _resolver = resolver;
        _pseudonymizer = pseudonymizer;
        _textEngine = textEngine;
        _report = report;
        _persist = persist;
    }

    /// <summary>Wie viele Stellen die Textregeln insgesamt ersetzt haben.</summary>
    public int TextReplacementCount { get; private set; }

    public void OnFields(IReadOnlyList<FieldOccurrence> fields)
    {
        var unhandled = new List<string>();

        foreach (var field in fields)
        {
            var rule = _resolver.Resolve(field.Name, field.JsonPath);

            // Ein namenloser Wert ohne eigene Regel laeuft durch die
            // Textregeln, solange die Vorgabe nicht den Abbruch verlangt.
            var namelessAsText = field.IsNameless && rule.IsFromDefault && rule.Action != FieldAction.Error;
            _report.FieldActions[field.Name] = namelessAsText ? "scantext [namenlos]" : Describe(rule);

            // Ohne eigene Regel greift die Vorgabe — das ist berichtenswert,
            // auch wenn die Vorgabe den Lauf nicht abbricht.
            if (rule.IsFromDefault && !_report.UnhandledFields.Contains(field.Name))
                _report.UnhandledFields.Add(field.Name);

            // Abgebrochen wird bei jedem Feld ohne getroffene Entscheidung,
            // gleich ob die aus der Vorgabe stammt oder ausdruecklich als
            // 'error' in der Regel steht — nach 'init' ist das der Normalfall.
            if (rule.Action == FieldAction.Error && !unhandled.Contains(field.Name))
                unhandled.Add(field.Name);
        }

        // Der Abbruch erfolgt gesammelt und bevor der erste Wert verarbeitet
        // wurde: so entsteht keine halbe Ausgabedatei, und der Anwender sieht
        // auf einen Schlag alle Felder, fuer die noch eine Entscheidung fehlt.
        if (unhandled.Count > 0)
            throw new UnhandledFieldException(unhandled);
    }

    public bool WantsValues(FieldOccurrence field)
    {
        var rule = _resolver.Resolve(field.Name, field.JsonPath);
        return rule.Action == FieldAction.Pseudonymize
               && _pseudonymizer.DateFormatsOf(rule.Generator ?? "token") is not null;
    }

    public void OnFieldValues(FieldOccurrence field, IReadOnlyCollection<string> values)
    {
        var rule = _resolver.Resolve(field.Name, field.JsonPath);
        var generatorName = rule.Generator ?? "token";
        var formats = _pseudonymizer.DateFormatsOf(generatorName);
        if (formats is null)
            return;

        var relevant = values.Where(value => !_profile.Defaults.IsEffectivelyEmpty(value)).ToList();
        var format = DateValues.DetermineColumnFormat(relevant, formats, generatorName, field.Name, out _);
        if (format is not null)
            _dateFormats[field.Key] = format;
    }

    public bool ShouldDrop(string fieldName, string? jsonPath)
        => _resolver.Resolve(fieldName, jsonPath).Action == FieldAction.Drop;

    public string TransformField(string fieldName, string? jsonPath, string value, string location)
    {
        try
        {
            return TransformFieldCore(fieldName, jsonPath, value, location);
        }
        catch (GenerationException ex)
        {
            throw ex.WithLocation(location);
        }
    }

    private string TransformFieldCore(string fieldName, string? jsonPath, string value, string location)
    {
        var rule = _resolver.Resolve(fieldName, jsonPath);

        // Faktisch leere Werte bleiben unveraendert: ein Pseudonym waere eine
        // Information, die im Original gar nicht stand. Gilt nicht nur fuer
        // "", sondern auch reinen Leerraum und die in defaults.emptyValues
        // hinterlegten Platzhalter wie "-" oder "N/A" (ProfileDefaults.IsEffectivelyEmpty,
        // derselbe Helfer wie in ScanTransformer und DeobfuscateTransformer).
        if (_profile.Defaults.IsEffectivelyEmpty(value))
            return value;

        switch (rule.Action)
        {
            case FieldAction.Passthrough:
                return value;

            case FieldAction.Drop:
                return value; // Wird vom Prozessor gar nicht erst geschrieben.

            case FieldAction.Redact:
                _report.CountHit("redact");
                return _profile.Defaults.RedactionPlaceholder;

            case FieldAction.Pseudonymize:
            {
                var generatorName = rule.Generator ?? "token";
                _report.CountHit(generatorName);
                var key = jsonPath is null ? fieldName : JsonPathMatcher.Normalize(jsonPath);
                _dateFormats.TryGetValue(key, out var dateFormat);
                return _pseudonymizer.Pseudonymize(generatorName, value, _persist, dateFormat,
                    _preferJsonNumber ? JsonProcessor.IsJsonNumberLiteral : null);
            }

            case FieldAction.ScanText:
                return ApplyTextRules(value, rule.TextRules);

            case FieldAction.Error:
                // Die Vorgabe war "error", der Abbruch erfolgte bereits in OnFields.
                // Hierher gelangt nur ein Feld, das erst in einer spaeteren Zeile
                // auftaucht — etwa ein ueberzaehliges CSV-Feld.
                throw new UnhandledFieldException([fieldName], location);

            default:
                return value;
        }
    }

    public string TransformNameless(string jsonPath, string value, string location)
    {
        var normalized = JsonPathMatcher.Normalize(jsonPath);
        var rule = _resolver.Resolve(normalized, jsonPath);

        if (!rule.IsFromDefault)
            return TransformField(normalized, jsonPath, value, location);

        if (rule.Action == FieldAction.Error)
            throw new UnhandledFieldException([normalized], location);

        if (_profile.Defaults.IsEffectivelyEmpty(value))
            return value;

        try
        {
            return ApplyTextRules(value, _profile.TextRules);
        }
        catch (GenerationException ex)
        {
            throw ex.WithLocation(location);
        }
    }

    /// <summary>
    /// Ob das naechste Pseudonym eine gueltige JSON-Zahl sein soll, damit der
    /// Typ erhalten bleibt (Befund A3). Gesetzt nur waehrend
    /// <see cref="TransformNumber"/>.
    /// </summary>
    private bool _preferJsonNumber;

    public string TransformNumber(string fieldName, string? jsonPath, string value, string location)
    {
        _preferJsonNumber = true;
        try
        {
            return TransformField(fieldName, jsonPath, value, location);
        }
        finally
        {
            _preferJsonNumber = false;
        }
    }

    public string TransformFreeText(string text, string location)
    {
        try
        {
            return ApplyTextRules(text, _profile.TextRules);
        }
        catch (GenerationException ex)
        {
            throw ex.WithLocation(location);
        }
    }

    private string ApplyTextRules(string text, IReadOnlyList<TextRule> rules)
    {
        _displaced.Clear();
        var result = _textEngine.Replace(text, rules, match =>
        {
            _report.CountHit(match.Rule.Name);
            return _pseudonymizer.Pseudonymize(match.Rule.Generator, match.Value, _persist);
        }, out var count, _displaced);

        TextReplacementCount += count;

        // Eine Regel, deren Fund von einer hoeher priorisierten verdraengt
        // wurde, ohne dass deren Funde ihn ganz abdecken: ein Teil dessen, was
        // sie fuer ersetzenswert hielt, steht noch im Text. Einmal je Regel melden.
        foreach (var match in _displaced)
        {
            if (_displacedRules.Add(match.Rule.Name))
            {
                _report.Warn("textregelVerdraengt",
                    $"Ein Fund der Textregel '{match.Rule.Name}' überschnitt sich mit einer Regel höherer " +
                    "Priorität und wurde nur teilweise ersetzt; der übrige Teil steht unverändert im Text. " +
                    "Mit 'scan' prüfen oder die Muster so fassen, dass sie sich nicht überschneiden.");
            }
        }

        return result;
    }

    private readonly List<TextMatch> _displaced = new();
    private readonly HashSet<string> _displacedRules = new(StringComparer.Ordinal);

    private static string Describe(ResolvedFieldRule rule)
    {
        var action = rule.Action.ToString().ToLowerInvariant();
        var suffix = rule.Action == FieldAction.Pseudonymize && rule.Generator is not null
            ? $" ({rule.Generator})"
            : "";
        var origin = rule.IsFromDefault ? " [Vorgabe]" : "";
        return action + suffix + origin;
    }
}
