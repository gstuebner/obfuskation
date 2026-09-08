using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
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

    public void OnFields(IReadOnlyList<string> fieldNames)
    {
        var unhandled = new List<string>();

        foreach (var fieldName in fieldNames)
        {
            var rule = _resolver.Resolve(fieldName);
            _report.FieldActions[fieldName] = Describe(rule);

            // Ohne eigene Regel greift die Vorgabe — das ist berichtenswert,
            // auch wenn die Vorgabe den Lauf nicht abbricht.
            if (rule.IsFromDefault)
                _report.UnhandledFields.Add(fieldName);

            // Abgebrochen wird bei jedem Feld ohne getroffene Entscheidung,
            // gleich ob die aus der Vorgabe stammt oder ausdruecklich als
            // 'error' in der Regel steht — nach 'init' ist das der Normalfall.
            if (rule.Action == FieldAction.Error)
                unhandled.Add(fieldName);
        }

        // Der Abbruch erfolgt gesammelt und bevor der erste Wert verarbeitet
        // wurde: so entsteht keine halbe Ausgabedatei, und der Anwender sieht
        // auf einen Schlag alle Felder, fuer die noch eine Entscheidung fehlt.
        if (unhandled.Count > 0)
            throw new UnhandledFieldException(unhandled);
    }

    public bool ShouldDrop(string fieldName, string? jsonPath)
        => _resolver.Resolve(fieldName, jsonPath).Action == FieldAction.Drop;

    public string TransformField(string fieldName, string? jsonPath, string value, string location)
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
                return _pseudonymizer.Pseudonymize(generatorName, value, _persist);
            }

            case FieldAction.ScanText:
                return ApplyTextRules(value, rule.TextRules);

            case FieldAction.Error:
                // Die Vorgabe war "error", der Abbruch erfolgte bereits in OnFields.
                // Hierher gelangt nur ein Feld, das erst in einer spaeteren Zeile
                // auftaucht — etwa eine zusaetzliche JSON-Eigenschaft.
                throw new UnhandledFieldException([fieldName]);

            default:
                return value;
        }
    }

    public string TransformFreeText(string text, string location)
        => ApplyTextRules(text, _profile.TextRules);

    private string ApplyTextRules(string text, IReadOnlyList<TextRule> rules)
    {
        var result = _textEngine.Replace(text, rules, match =>
        {
            _report.CountHit(match.Rule.Name);
            return _pseudonymizer.Pseudonymize(match.Rule.Generator, match.Value, _persist);
        }, out _);

        return result;
    }

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
