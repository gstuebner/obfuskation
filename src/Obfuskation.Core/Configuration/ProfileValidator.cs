using System.Globalization;
using System.Text.RegularExpressions;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Prueft ein Profil auf Widersprueche. Liefert eine Liste einzelner Befunde
/// statt einer Sammelmeldung, damit die Oberflaeche jeden Hinweis am
/// zugehoerigen Eingabefeld anzeigen kann.
///
/// Seit der Erweiterungsdatei (<see cref="ExtensionLibrary"/>) prueft diese
/// Klasse zwei Quellen: das Profil selbst und die Erweiterung, die neben jedem
/// Profil in dieselbe Generatorenmenge einfliesst. Ein Verweis auf einen
/// Erweiterungseintrag gilt als bekannt; ein fehlerhafter Erweiterungseintrag
/// wird mit demselben Massstab gemeldet wie ein fehlerhafter Profileintrag,
/// nur unter dem Pfad <c>extensions.generators.&lt;key&gt;</c> bzw.
/// <c>extensions.textRules[i]</c> bzw. <c>extensions.fieldRules[i]</c>.
/// </summary>
public static class ProfileValidator
{
    /// <summary>
    /// Erlaubter Zeichenvorrat eines Token-Praefixes: keine CSV-Trennzeichen,
    /// keine Anfuehrungszeichen, keine Steuerzeichen, und ein Abschluss mit
    /// '~' oder '_', damit das Praefix im fertigen Wert als solches erkennbar
    /// bleibt und nicht mit dem folgenden "TOK_" verschmilzt.
    /// </summary>
    private static readonly Regex PrefixPattern =
        new(@"^[A-Za-z0-9ÄÖÜäöüß_-]+[~_]$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Erlaubte Rundungsstufen fuer <c>dateGeneralize</c>.</summary>
    private static readonly string[] AllowedGranularities = ["month", "quarter", "year"];

    /// <summary>
    /// Ab wie vielen Werten der Wertevorrat von <c>wordlist</c> nicht mehr als
    /// zu knapp gilt. Kein hartes Limit -- der Pseudonymizer verwirft nur
    /// Kollisionen -- aber ein kleiner Vorrat lässt die 100 Ausweichversuche
    /// schnell aufbrauchen (siehe <c>Pseudonymizer.MaxCollisionRetries</c>).
    /// </summary>
    private const int MinimumWordlistValues = 5;

    /// <summary>Dieselbe Schwelle, angewandt auf die Grosse des Wertevorrats einer <c>pattern</c>-Maske.</summary>
    private const long MinimumPatternValuePool = 1000;

    /// <summary>
    /// Ordnet jede generatorspezifische Option ihrem einzig zulaessigen
    /// Basistyp zu. Ersetzt die fruehere Sonderbehandlung einzelner Optionen:
    /// alle gesetzten Optionen eines Generator-Eintrags werden gegen diese
    /// Tabelle geprueft, statt fuer jede Option einen eigenen Codepfad zu pflegen.
    /// </summary>
    /// <remarks>
    /// Oeffentlich, weil die Oberflaeche dieselbe Zuordnung braucht, um zu
    /// entscheiden, welche Optionen sie zu einem Generator anzeigt. Zwei
    /// Kopien liefen bei jedem neuen Generator auseinander -- die Oberflaeche
    /// zeigte dann ein Feld, das der Validator bemaengelt, oder verbaerge
    /// eines, das gebraucht wird.
    /// </remarks>
    public static readonly (string Option, string BaseType)[] OptionOwnership =
    [
        ("prefix", "token"),
        ("placeholder", "redact"),
        ("from", "dateRange"),
        ("to", "dateRange"),
        ("granularity", "dateGeneralize"),
        ("pattern", "pattern"),
        ("values", "wordlist"),
        ("keepFirst", "partialMask"),
        ("keepLast", "partialMask"),
        ("maskChar", "partialMask"),
    ];

    public static IReadOnlyList<ValidationIssue> Validate(Profile profile, ExtensionLibrary? extensions = null)
    {
        extensions ??= ExtensionLibrary.Load();
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

        ValidateGenerators(profile, extensions, issues);
        ValidateTextRules(profile, extensions, issues);
        ValidateFields(profile, extensions, issues);
        ValidateFieldRules(profile, extensions, issues);

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

    private static void ValidateGenerators(Profile profile, ExtensionLibrary extensions, List<ValidationIssue> issues)
    {
        foreach (var (key, settings) in extensions.Generators)
            ValidateGeneratorEntry("extensions.generators", key, settings, issues);

        foreach (var (key, settings) in profile.Generators)
        {
            ValidateGeneratorEntry("generators", key, settings, issues);

            // Ein gleichnamiger Profileintrag gewinnt (siehe
            // GeneratorRegistry.Build) — das ist gewollt moeglich, aber ein
            // wortlos ueberschriebener Erweiterungseintrag ist eine leichte
            // Ueberraschung wert.
            if (extensions.Generators.ContainsKey(key))
            {
                issues.Add(new ValidationIssue($"generators.{key}", ValidationSeverity.Warning,
                    $"'{key}' überschreibt den gleichnamigen Eintrag aus der Erweiterungsdatei für " +
                    "dieses Profil; die Erweiterungsfassung greift hier nicht."));
            }
        }
    }

    private static void ValidateGeneratorEntry(
        string pathPrefix, string key, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        var baseName = string.IsNullOrWhiteSpace(settings.Type) ? key : settings.Type;

        if (!GeneratorRegistry.KnownNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.type", ValidationSeverity.Error,
                $"Unbekannter Generatortyp '{baseName}'. Verfügbar: " +
                string.Join(", ", GeneratorRegistry.KnownNames)));
        }

        if (settings.MaxDays < 0)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.maxDays", ValidationSeverity.Error,
                "maxDays darf nicht negativ sein."));
        }

        ValidateOptionOwnership(pathPrefix, key, baseName, settings, issues);

        if (string.Equals(baseName, "token", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(settings.Prefix))
        {
            if (!PrefixPattern.IsMatch(settings.Prefix))
            {
                issues.Add(new ValidationIssue($"{pathPrefix}.{key}.prefix", ValidationSeverity.Error,
                    $"Das Präfix '{settings.Prefix}' ist ungültig. Erlaubt sind Buchstaben, " +
                    "Ziffern, '_' und '-', abgeschlossen mit '~' oder '_' " +
                    "(Muster: ^[A-Za-z0-9ÄÖÜäöüß_-]+[~_]$)."));
            }

            // Unabhaengig vom Zeichenvorrat geprueft: das Praefix steht in
            // jedem einzelnen Wert der Spalte, laenger macht die Pseudodatei
            // unleserlicher, statt sie lesbar zu machen.
            if (settings.Prefix.Length > 32)
            {
                issues.Add(new ValidationIssue($"{pathPrefix}.{key}.prefix", ValidationSeverity.Error,
                    "Das Präfix darf höchstens 32 Zeichen lang sein."));
            }
        }

        if (string.Equals(baseName, "dateRange", StringComparison.OrdinalIgnoreCase))
            ValidateDateRange(pathPrefix, key, settings, issues);

        if (string.Equals(baseName, "dateGeneralize", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.Granularity)
            && !AllowedGranularities.Contains(settings.Granularity, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.granularity", ValidationSeverity.Error,
                $"'{settings.Granularity}' ist keine gültige Granularität. Erlaubt: " +
                string.Join(", ", AllowedGranularities) + "."));
        }

        if (string.Equals(baseName, "pattern", StringComparison.OrdinalIgnoreCase))
            ValidatePattern(pathPrefix, key, settings, issues);

        if (string.Equals(baseName, "wordlist", StringComparison.OrdinalIgnoreCase))
            ValidateWordlist(pathPrefix, key, settings, issues);

        if (string.Equals(baseName, "partialMask", StringComparison.OrdinalIgnoreCase))
            ValidatePartialMask(pathPrefix, key, settings, issues);
    }

    /// <summary>
    /// Prueft jede gesetzte Option gegen <see cref="OptionOwnership"/>: eine
    /// Option, die an einem anderen Basistyp haengt als dem, fuer den sie
    /// gedacht ist, hat dort keine Wirkung und ist ein Konfigurationsfehler.
    /// </summary>
    private static void ValidateOptionOwnership(
        string pathPrefix, string key, string baseName, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        foreach (var (option, allowedBaseType) in OptionOwnership)
        {
            if (!IsOptionSet(settings, option))
                continue;

            if (string.Equals(baseName, allowedBaseType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (option == "prefix")
            {
                // Alle anderen Generatoren liefern das Format ihres Wertes (eine
                // gueltige IBAN, eine Zahlenkennung mit erhaltener Stellenzahl,
                // ein verschobenes Datum) — ein vorangestelltes Praefix zerstoert
                // genau das und macht z. B. aus einer IBAN keine IBAN mehr.
                issues.Add(new ValidationIssue($"{pathPrefix}.{key}.prefix", ValidationSeverity.Error,
                    $"'prefix' gilt nur für den Generatortyp 'token'. Generator '{key}' erzeugt Werte " +
                    $"vom Typ '{baseName}', die ihr eigenes Format tragen; ein Präfix würde dieses " +
                    "Format zerstören."));
                continue;
            }

            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.{option}", ValidationSeverity.Error,
                $"'{option}' gilt nur für den Generatortyp '{allowedBaseType}'. Generator '{key}' " +
                $"erzeugt Werte vom Typ '{baseName}' und wertet '{option}' nicht aus."));
        }
    }

    private static bool IsOptionSet(GeneratorSettings settings, string option) => option switch
    {
        "prefix" => !string.IsNullOrEmpty(settings.Prefix),
        "placeholder" => !string.IsNullOrEmpty(settings.Placeholder),
        "from" => !string.IsNullOrWhiteSpace(settings.From),
        "to" => !string.IsNullOrWhiteSpace(settings.To),
        "granularity" => !string.IsNullOrWhiteSpace(settings.Granularity),
        "pattern" => !string.IsNullOrEmpty(settings.Pattern),
        "values" => settings.Values is { Count: > 0 },
        "keepFirst" => settings.KeepFirst > 0,
        "keepLast" => settings.KeepLast > 0,
        "maskChar" => !string.IsNullOrEmpty(settings.MaskChar),
        _ => false,
    };

    private static void ValidateDateRange(
        string pathPrefix, string key, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(settings.From) && string.IsNullOrWhiteSpace(settings.To))
            return; // Ohne Angabe gilt das Kalenderjahr des Originals, siehe DateRangeGenerator.

        var fromOk = DateTime.TryParseExact(settings.From, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var from);
        var toOk = DateTime.TryParseExact(settings.To, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var to);

        if (!fromOk || !toOk)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.from", ValidationSeverity.Error,
                "'from' und 'to' müssen beide als ISO-Datum gesetzt sein (z. B. '1950-01-01')."));
            return;
        }

        if (from > to)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.from", ValidationSeverity.Error,
                "'from' darf nicht nach 'to' liegen."));
        }
    }

    private static void ValidatePattern(
        string pathPrefix, string key, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        if (settings.Pattern is { Length: 0 })
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.pattern", ValidationSeverity.Error,
                "Die Maske darf nicht leer sein. Nicht gesetzt ist erlaubt -- dann wird sie aus dem " +
                "Original abgeleitet."));
            return;
        }

        if (settings.Pattern is not { Length: > 0 } mask)
            return;

        var pool = PatternValuePool(mask);
        if (pool < MinimumPatternValuePool)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.pattern", ValidationSeverity.Warning,
                $"Die Maske '{mask}' lässt nur {pool} verschiedene Werte zu. Bei vielen Klartexten kann " +
                "der Generator keinen freien Wert mehr finden und der Lauf mit einer " +
                "MappingConflictException abbrechen; eine längere Maske schafft mehr Spielraum."));
        }
    }

    /// <summary>
    /// Groesse des Wertevorrats einer Maske: 'A'/'a' 26 Moeglichkeiten, '9' 10,
    /// 'X' 62, alles andere (auch ein escaptes Zeichen) genau eine. Bricht
    /// frueh ab, sobald die Warnschwelle erreicht ist -- die genaue Groesse
    /// jenseits davon interessiert fuer die Warnung nicht mehr.
    /// </summary>
    private static long PatternValuePool(string mask)
    {
        long pool = 1;
        for (var i = 0; i < mask.Length; i++)
        {
            var symbol = mask[i];
            if (symbol == '\\' && i + 1 < mask.Length)
            {
                i++;
                continue;
            }

            pool *= symbol switch
            {
                'A' or 'a' => 26,
                '9' => 10,
                'X' => 62,
                _ => 1,
            };

            if (pool >= MinimumPatternValuePool)
                return pool;
        }

        return pool;
    }

    private static void ValidateWordlist(
        string pathPrefix, string key, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        if (settings.Values is not { Count: > 0 })
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.values", ValidationSeverity.Error,
                $"Generator '{key}' vom Typ 'wordlist' braucht mindestens einen Wert unter 'values'."));
            return;
        }

        if (settings.Values.Count < MinimumWordlistValues)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.values", ValidationSeverity.Warning,
                $"Nur {settings.Values.Count} Werte unter 'values'. Bei vielen Klartexten kann der " +
                "Generator keinen freien Wert mehr finden und der Lauf mit einer " +
                "MappingConflictException abbrechen; mehr Werte schaffen mehr Spielraum."));
        }
    }

    private static void ValidatePartialMask(
        string pathPrefix, string key, GeneratorSettings settings, List<ValidationIssue> issues)
    {
        if (settings.KeepFirst < 0)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.keepFirst", ValidationSeverity.Error,
                "'keepFirst' darf nicht negativ sein."));
        }

        if (settings.KeepLast < 0)
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.keepLast", ValidationSeverity.Error,
                "'keepLast' darf nicht negativ sein."));
        }

        if (settings.MaskChar is { Length: not 1 })
        {
            issues.Add(new ValidationIssue($"{pathPrefix}.{key}.maskChar", ValidationSeverity.Error,
                "'maskChar' muss genau ein Zeichen lang sein."));
        }
    }

    private static void ValidateTextRules(Profile profile, ExtensionLibrary extensions, List<ValidationIssue> issues)
    {
        ValidateTextRuleList(extensions.TextRules, "extensions.textRules", profile, extensions, issues);
        ValidateTextRuleList(profile.TextRules, "textRules", profile, extensions, issues);
    }

    private static void ValidateTextRuleList(
        IReadOnlyList<TextRule> rules, string pathPrefix, Profile profile, ExtensionLibrary extensions,
        List<ValidationIssue> issues)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var path = $"{pathPrefix}[{index}]";

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

            ValidateGeneratorReference(profile, extensions, rule.Generator, $"{path}.generator", issues);

            if (rule.CaptureGroup < 0)
            {
                issues.Add(new ValidationIssue($"{path}.captureGroup", ValidationSeverity.Error,
                    "Die Gruppennummer darf nicht negativ sein."));
            }
        }
    }

    private static void ValidateFields(Profile profile, ExtensionLibrary extensions, List<ValidationIssue> issues)
    {
        var textRuleNames = new HashSet<string>(
            extensions.TextRules.Select(rule => rule.Name).Concat(profile.TextRules.Select(rule => rule.Name)),
            StringComparer.OrdinalIgnoreCase);

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
                        ValidateGeneratorReference(profile, extensions, rule.Generator, $"{path}.generator", issues);
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
                    else if (profile.TextRules.Count == 0 && extensions.TextRules.Count == 0)
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
        Profile profile, ExtensionLibrary extensions, string? generatorName, string path,
        List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(generatorName))
            return;

        var known = GeneratorRegistry.KnownNames.Contains(generatorName, StringComparer.OrdinalIgnoreCase)
                    || profile.Generators.ContainsKey(generatorName)
                    || extensions.Generators.ContainsKey(generatorName);

        if (!known)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Error,
                $"Unbekannter Generator '{generatorName}'. Verfügbar: " +
                string.Join(", ", GeneratorRegistry.KnownNames)));
        }
    }

    /// <summary>
    /// Prueft die Spaltenmuster der Erweiterungsdatei
    /// (<see cref="ExtensionLibrary.FieldRules"/>): Muster gesetzt und
    /// uebersetzbar, Generator gesetzt und bekannt -- eingebaut, aus
    /// <see cref="ExtensionLibrary.Generators"/>, aus den Generatoren des
    /// Profils, oder der Sonderwert <c>scanText</c>.
    /// </summary>
    private static void ValidateFieldRules(Profile profile, ExtensionLibrary extensions, List<ValidationIssue> issues)
    {
        for (var index = 0; index < extensions.FieldRules.Count; index++)
        {
            var rule = extensions.FieldRules[index];
            var path = $"extensions.fieldRules[{index}]";

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

            if (string.IsNullOrWhiteSpace(rule.Generator))
            {
                issues.Add(new ValidationIssue($"{path}.generator", ValidationSeverity.Error,
                    "Der Generator darf nicht leer sein."));
            }
            else if (!string.Equals(rule.Generator, "scanText", StringComparison.OrdinalIgnoreCase))
            {
                ValidateGeneratorReference(profile, extensions, rule.Generator, $"{path}.generator", issues);
            }
        }
    }
}
