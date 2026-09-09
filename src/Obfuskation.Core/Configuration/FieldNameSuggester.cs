using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Obfuskation.Core.Detection;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Schlaegt anhand des Feldnamens einen Generator vor -- die Spaltenmuster der
/// Erweiterungsdatei (<see cref="ExtensionLibrary.FieldRules"/>) statt eines
/// fest einkompilierten Namensfragments. Ohne Erweiterungsdatei (oder ohne
/// hinterlegte Spaltenmuster) gibt es keinen Vorschlag mehr -- das ist eine
/// bewusste Verhaltensaenderung gegenueber dem frueheren, immer aktiven Raten.
///
/// Trefferregel: ganzer Feldname. Jedes Muster wird wie bei
/// <see cref="ValueSuggester"/> mit <c>\A(?:…)\z</c> umschlossen, ein
/// Teiltreffer zaehlt also nicht -- genau das behebt den Fehler der frueheren
/// Liste, in der ein Fragment wie <c>"nr"</c> mitten in einem laengeren
/// Feldnamen traf. Die erste passende Regel gewinnt, Reihenfolge in der Datei
/// entscheidet.
/// </summary>
public static class FieldNameSuggester
{
    /// <summary>Der Generatorname, <c>"scanText"</c>, oder <c>null</c>.</summary>
    public static string? Suggest(string fieldName, ExtensionLibrary? extensions = null)
    {
        extensions ??= ExtensionLibrary.Load();

        foreach (var candidate in BuildCandidates(extensions))
        {
            if (candidate.Pattern.IsMatch(fieldName))
                return candidate.Rule.Generator;
        }

        return null;
    }

    private readonly record struct Candidate(FieldNameRule Rule, Regex Pattern);

    // Je ExtensionLibrary-Instanz zwischengespeichert (typischerweise einmal
    // pro Lauf geladen und fuer mehrere Felder wiederverwendet) -- ohne
    // erneutes Uebersetzen bei jedem einzelnen Feldnamen.
    private static readonly ConditionalWeakTable<ExtensionLibrary, Candidate[]> Cache = new();

    private static Candidate[] BuildCandidates(ExtensionLibrary extensions)
    {
        if (Cache.TryGetValue(extensions, out var cached))
            return cached;

        var candidates = new List<Candidate>();

        foreach (var rule in extensions.FieldRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            try
            {
                // Voller Treffer auf den Feldnamen, dieselben Optionen und
                // dasselbe Zeitlimit wie TextRuleEngine -- dieselbe
                // Hilfsmethode, nicht neu gebaut.
                var pattern = TextRuleEngine.Compile($@"\A(?:{rule.Pattern})\z", rule.IgnoreCase, rule.Generator);
                candidates.Add(new Candidate(rule, pattern));
            }
            catch (ConfigurationException)
            {
                // Ein ungueltiges Muster ist Sache von ProfileValidator; hier
                // wird es einfach kein Kandidat -- ein Vorschlag ist nur eine
                // Erleichterung, kein Pruefschritt.
            }
        }

        var result = candidates.ToArray();
        Cache.AddOrUpdate(extensions, result);
        return result;
    }
}
