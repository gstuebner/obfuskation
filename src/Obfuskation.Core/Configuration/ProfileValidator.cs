using System.Text.RegularExpressions;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Prueft ein Profil auf Widersprueche. Liefert eine Liste einzelner Befunde
/// statt einer Sammelmeldung, damit die Oberflaeche jeden Hinweis am
/// zugehoerigen Eingabefeld anzeigen kann.
/// </summary>
public static class ProfileValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(Profile profile)
    {
        var issues = new List<ValidationIssue>();

        if (profile.Version > Profile.CurrentVersion)
        {
            issues.Add(new ValidationIssue("version", ValidationSeverity.Error,
                $"Die Konfiguration hat Version {profile.Version}, dieses Programm kennt höchstens " +
                $"Version {Profile.CurrentVersion}."));
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            issues.Add(new ValidationIssue("profileName", ValidationSeverity.Error,
                "Der Profilname darf nicht leer sein; er bestimmt die Ablage der Ersetzungstabelle."));
        }

        ValidateGenerators(profile, issues);
        ValidateTextRules(profile, issues);
        ValidateFields(profile, issues);

        if (profile.Fields.Count == 0 && profile.TextRules.Count == 0)
        {
            issues.Add(new ValidationIssue("fields", ValidationSeverity.Warning,
                "Weder Feld- noch Textregeln sind hinterlegt. Der Lauf würde nichts ersetzen."));
        }

        if (profile.Defaults.UnknownField == FieldAction.Passthrough)
        {
            issues.Add(new ValidationIssue("defaults.unknownField", ValidationSeverity.Warning,
                "Felder ohne Regel werden unverändert durchgereicht. Damit können Echtdaten in die " +
                "Ausgabe gelangen, ohne dass es auffällt. 'error' ist die sichere Vorgabe."));
        }

        return issues;
    }

    private static void ValidateGenerators(Profile profile, List<ValidationIssue> issues)
    {
        foreach (var (key, settings) in profile.Generators)
        {
            var baseName = string.IsNullOrWhiteSpace(settings.Type) ? key : settings.Type;

            if (!GeneratorRegistry.KnownNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue($"generators.{key}.type", ValidationSeverity.Error,
                    $"Unbekannter Generatortyp '{baseName}'. Verfügbar: " +
                    string.Join(", ", GeneratorRegistry.KnownNames)));
            }

            if (settings.MaxDays < 0)
            {
                issues.Add(new ValidationIssue($"generators.{key}.maxDays", ValidationSeverity.Error,
                    "maxDays darf nicht negativ sein."));
            }
        }
    }

    private static void ValidateTextRules(Profile profile, List<ValidationIssue> issues)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < profile.TextRules.Count; index++)
        {
            var rule = profile.TextRules[index];
            var path = $"textRules[{index}]";

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                issues.Add(new ValidationIssue($"{path}.name", ValidationSeverity.Error,
                    "Jede Textregel braucht einen Namen; Feldregeln verweisen darauf."));
            }
            else if (!seenNames.Add(rule.Name))
            {
                issues.Add(new ValidationIssue($"{path}.name", ValidationSeverity.Error,
                    $"Der Name '{rule.Name}' ist mehrfach vergeben."));
            }

            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                issues.Add(new ValidationIssue($"{path}.pattern", ValidationSeverity.Error,
                    "Das Muster darf nicht leer sein."));
            }
            else
            {
                try
                {
                    _ = new Regex(rule.Pattern);
                }
                catch (ArgumentException ex)
                {
                    issues.Add(new ValidationIssue($"{path}.pattern", ValidationSeverity.Error,
                        $"Ungültiger regulärer Ausdruck: {ex.Message}"));
                }
            }

            ValidateGeneratorReference(profile, rule.Generator, $"{path}.generator", issues);

            if (rule.CaptureGroup < 0)
            {
                issues.Add(new ValidationIssue($"{path}.captureGroup", ValidationSeverity.Error,
                    "Die Gruppennummer darf nicht negativ sein."));
            }
        }
    }

    private static void ValidateFields(Profile profile, List<ValidationIssue> issues)
    {
        var textRuleNames = new HashSet<string>(
            profile.TextRules.Select(rule => rule.Name), StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < profile.Fields.Count; index++)
        {
            var rule = profile.Fields[index];
            var path = $"fields[{index}]";

            if (string.IsNullOrWhiteSpace(rule.Match))
            {
                issues.Add(new ValidationIssue($"{path}.match", ValidationSeverity.Error,
                    "Das Muster für den Feldnamen darf nicht leer sein."));
            }
            else if (rule.MatchType == FieldMatchType.Regex)
            {
                try
                {
                    _ = new Regex(rule.Match);
                }
                catch (ArgumentException ex)
                {
                    issues.Add(new ValidationIssue($"{path}.match", ValidationSeverity.Error,
                        $"Ungültiger regulärer Ausdruck: {ex.Message}"));
                }
            }

            switch (rule.Action)
            {
                case FieldAction.Pseudonymize:
                    if (string.IsNullOrWhiteSpace(rule.Generator))
                    {
                        issues.Add(new ValidationIssue($"{path}.generator", ValidationSeverity.Error,
                            "Bei 'pseudonymize' muss ein Generator angegeben sein."));
                    }
                    else
                    {
                        ValidateGeneratorReference(profile, rule.Generator, $"{path}.generator", issues);
                    }
                    break;

                case FieldAction.ScanText:
                    if (rule.TextRules is { Count: > 0 })
                    {
                        foreach (var name in rule.TextRules)
                        {
                            if (!textRuleNames.Contains(name))
                            {
                                issues.Add(new ValidationIssue($"{path}.textRules", ValidationSeverity.Error,
                                    $"Die Textregel '{name}' ist nicht definiert."));
                            }
                        }
                    }
                    else if (profile.TextRules.Count == 0)
                    {
                        issues.Add(new ValidationIssue($"{path}.textRules", ValidationSeverity.Warning,
                            "'scanText' ohne hinterlegte Textregeln bewirkt nichts."));
                    }
                    break;

                case FieldAction.Error:
                    issues.Add(new ValidationIssue($"{path}.action", ValidationSeverity.Warning,
                        $"Für '{rule.Match}' steht noch keine Entscheidung fest; der Lauf wird " +
                        "abbrechen, sobald das Feld auftaucht."));
                    break;
            }
        }
    }

    private static void ValidateGeneratorReference(
        Profile profile, string? generatorName, string path, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(generatorName))
            return;

        var known = GeneratorRegistry.KnownNames.Contains(generatorName, StringComparer.OrdinalIgnoreCase)
                    || profile.Generators.ContainsKey(generatorName);

        if (!known)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Error,
                $"Unbekannter Generator '{generatorName}'. Verfügbar: " +
                string.Join(", ", GeneratorRegistry.KnownNames)));
        }
    }
}
