using System.Text.RegularExpressions;
using Obfuskation.Core;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Erzeugt ein Regelgeruest aus einer vorhandenen Datei.
///
/// Bewusst bleibt jede Spalte auf <c>error</c> stehen, auch wenn der Name einen
/// Generator nahelegt: die Entscheidung, was mit einem Feld geschieht, soll ein
/// Mensch treffen. Der Vorschlag steht daneben und muss nur bestaetigt werden.
/// </summary>
public static class ProfileScaffolder
{
    /// <summary>
    /// Zeichenvorrat, den ein Token-Praefix nach <see cref="ProfileValidator"/>
    /// tragen darf. Dieselbe Menge wie dort, hier zum Ausduennen eines
    /// Feldnamens statt zum Pruefen.
    /// </summary>
    private static readonly Regex DisallowedPrefixChars =
        new("[^A-Za-z0-9ÄÖÜäöüß_-]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Profile Create(
        string profileName, string? sampleFilePath, string? description = null, ExtensionLibrary? extensions = null)
        => Create(profileName,
            string.IsNullOrWhiteSpace(sampleFilePath) ? Array.Empty<string>() : new[] { sampleFilePath },
            description, extensions);

    /// <summary>
    /// Wie die Einzelfassung, aber aus mehreren zusammengehoerenden Dateien auf
    /// einmal -- fuer "Neu aus Datei…" mit Mehrfachauswahl. Die Feldnamen aller
    /// Dateien werden in Lesereihenfolge vereinigt, ein doppelt vorkommender
    /// Name (etwa die gemeinsame Schluesselspalte zweier Tabellen) erscheint
    /// nur bei seinem ersten Auftreten -- sonst bekaeme er zwei widerspruechliche
    /// Vorschlaege.
    ///
    /// Ohne <paramref name="extensions"/> gilt <see cref="ExtensionLibrary.Load()"/>.
    /// Wertetreffer aus <see cref="ValueSuggester"/> schlagen dabei das
    /// Namensraten aus <see cref="FieldNameSuggester"/>: ein passender
    /// Beispielwert ist die haertere Aussage als ein Spaltenmuster.
    /// </summary>
    public static Profile Create(
        string profileName, IEnumerable<string> sampleFilePaths, string? description = null,
        ExtensionLibrary? extensions = null)
    {
        extensions ??= ExtensionLibrary.Load();

        var profile = new Profile
        {
            ProfileName = profileName,
            Description = description,
            MappingStore = PathHelper.DefaultMappingStorePath(profileName),
            TextRules = DefaultTextRules(),
        };

        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pfad in sampleFilePaths)
        {
            if (string.IsNullOrWhiteSpace(pfad))
                continue;

            var expanded = PathHelper.ExpandHome(pfad);
            if (!File.Exists(expanded))
                throw new FileNotFoundException($"Datei nicht gefunden: {expanded}", expanded);

            var content = File.ReadAllBytes(expanded);
            var inspected = FieldInspector.Inspect(content, pfad);

            // Je Datei einmal bestimmt, nicht je Feld.
            var samples = FieldSampler.Sample(content, pfad);
            var vorschlaege = ValueSuggester.Suggest(samples, extensions)
                .ToDictionary(v => v.FieldName, StringComparer.OrdinalIgnoreCase);

            foreach (var fieldName in inspected.FieldNames)
            {
                if (!gesehen.Add(fieldName))
                    continue;

                vorschlaege.TryGetValue(fieldName, out var vorschlag);
                profile.Fields.Add(CreateRule(profile, fieldName, vorschlag, extensions));
            }
        }

        return profile;
    }

    private static FieldRule CreateRule(
        Profile profile, string fieldName, ValueSuggestion? valueSuggestion, ExtensionLibrary extensions)
    {
        if (valueSuggestion is not null)
        {
            return new FieldRule
            {
                Match = fieldName,
                MatchType = FieldMatchType.Exact,
                Action = FieldAction.Error,
                Generator = valueSuggestion.Generator,
                Comment = $"Vorschlag: pseudonymize mit '{valueSuggestion.Generator}' -- " +
                          $"{valueSuggestion.MatchedSamples} von {valueSuggestion.TotalSamples} Beispielwerten " +
                          $"passen, etwa '{valueSuggestion.Evidence}'. Zum Uebernehmen action auf " +
                          "pseudonymize setzen.",
            };
        }

        var suggestion = FieldNameSuggester.Suggest(fieldName, extensions);

        if (suggestion == "scanText")
        {
            return new FieldRule
            {
                Match = fieldName,
                MatchType = FieldMatchType.Exact,
                Action = FieldAction.Error,
                Comment = "Vorschlag: scanText (Freitext). Sicherer waere redact oder drop — " +
                          "Muster finden in Freitext nicht alles. action anpassen.",
            };
        }

        if (suggestion is null)
        {
            // Fuer ein Feld, dem kein eingebauter Generator zugeordnet werden
            // kann, lohnt trotzdem ein Vorschlag: ein eigener Token-Namensraum
            // mit einem aus dem Feldnamen abgeleiteten Praefix macht die
            // Pseudodatei lesbar, auch wenn niemand weiss, wofuer die Spalte
            // eigentlich steht. Die Entscheidung bleibt trotzdem beim Menschen
            // — action steht weiter auf "error".
            var namespaceKey = SuggestPrefixNamespace(profile, fieldName);
            if (namespaceKey is not null)
            {
                return new FieldRule
                {
                    Match = fieldName,
                    MatchType = FieldMatchType.Exact,
                    Action = FieldAction.Error,
                    Generator = namespaceKey,
                    Comment = $"Kein eingebauter Generator passt. Vorschlag: pseudonymize mit " +
                              $"eigenem Namensraum '{namespaceKey}' — das Praefix " +
                              $"'{profile.Generators[namespaceKey].Prefix}' stellt sich jedem " +
                              "Pseudonym voran und macht es lesbar. action anpassen.",
                };
            }
        }

        return new FieldRule
        {
            Match = fieldName,
            MatchType = FieldMatchType.Exact,
            Action = FieldAction.Error,
            Generator = suggestion,
            Comment = suggestion is null
                ? "Kein Vorschlag ableitbar. action auf pseudonymize, passthrough, redact oder drop setzen."
                : $"Vorschlag: pseudonymize mit '{suggestion}'. Zum Uebernehmen action auf pseudonymize setzen.",
        };
    }

    /// <summary>
    /// Schlaegt fuer ein Feld ohne eingebauten Generator einen eigenen
    /// Token-Namensraum vor und traegt ihn bereits in
    /// <see cref="Profile.Generators"/> ein. Liefert <c>null</c>, wenn sich
    /// kein brauchbares Praefix bilden laesst oder der Schluessel kollidiert
    /// — dann bleibt es beim bisherigen Verhalten ohne Vorschlag.
    /// </summary>
    private static string? SuggestPrefixNamespace(Profile profile, string fieldName)
    {
        var prefixBody = DisallowedPrefixChars.Replace(fieldName, "");
        if (prefixBody.Length == 0)
            return null;

        // 31 statt 32 Zeichen: das abschliessende '~' zaehlt beim Validator mit.
        if (prefixBody.Length > 31)
            prefixBody = prefixBody[..31];

        var key = ToGeneratorKey(fieldName);

        // Ein Vorschlag, der einen eingebauten Generator ueberschreibt oder
        // einen schon angelegten Namensraum kapert, waere eine boese
        // Ueberraschung — dann lieber gar keinen Vorschlag machen.
        if (GeneratorRegistry.KnownNames.Contains(key, StringComparer.OrdinalIgnoreCase)
            || profile.Generators.ContainsKey(key))
            return null;

        profile.Generators[key] = new GeneratorSettings { Type = "token", Prefix = prefixBody + "~" };
        return key;
    }

    /// <summary>
    /// Wandelt einen Feldnamen in einen camelCase-Schluessel fuer
    /// <see cref="Profile.Generators"/>: nur der erste Buchstabe wird
    /// kleingeschrieben, der Rest bleibt stehen. Oeffentlich, damit die
    /// Oberflaeche denselben Schluessel bildet, wenn sie ueber das
    /// Praefix-Feld einen neuen Namensraum anlegt — zwei getrennte
    /// Ableitungen wuerden auseinanderlaufen.
    /// </summary>
    public static string ToGeneratorKey(string fieldName)
        => fieldName.Length == 0
            ? fieldName
            : char.ToLowerInvariant(fieldName[0]) + fieldName[1..];

    /// <summary>
    /// Liest die Feldnamen einer Datei. Duenne Huelle um
    /// <see cref="FieldInspector"/> — dieselbe Erkennung, die auch die
    /// Oberflaeche verwendet.
    /// </summary>
    public static IReadOnlyList<string> ReadFieldNames(string path)
        => FieldInspector.InspectFile(path).FieldNames;

    /// <summary>
    /// Ein brauchbarer Grundstock an Mustern. Bewusst zurueckhaltend gehalten:
    /// zu weit gefasste Muster erzeugen Fehltreffer und untergraben das
    /// Vertrauen in die Ausgabe.
    ///
    /// Die Muster sind ein Ausgangspunkt, keine Gewaehr. Sie gehoeren an den
    /// eigenen Datenbestand angepasst und mit "scan" nachgeprueft.
    /// </summary>
    public static List<TextRule> DefaultTextRules() =>
    [
        new TextRule
        {
            Name = "iban",
            Priority = 100,
            Generator = "iban",
            Pattern = @"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b",
        },
        new TextRule
        {
            Name = "email",
            Priority = 90,
            Generator = "email",
            Pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        },
        new TextRule
        {
            Name = "bic",
            Priority = 85,
            Generator = "bic",
            Pattern = @"\b[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}(?:[A-Z0-9]{3})?\b",
        },
        new TextRule
        {
            Name = "phone",
            Priority = 80,
            Generator = "phone",

            // Der Blick nach hinten ist hier wesentlich: ohne ihn faengt das
            // Muster mitten in einer Rechnungsnummer wie "2024-0815" an und
            // erklaert deren Rest zur Telefonnummer.
            Pattern = @"(?<![\w\-/])\(?(?:\+49|0)[ \-/)]?\d[\d \-/()]{5,20}\d",
        },
    ];
}
