using System.Text.RegularExpressions;
using Obfuskation.Core.Detection;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Ein wertbasierter Vorschlag: die Beispielwerte eines Feldes passen
/// vollstaendig auf ein bekanntes Muster.
/// </summary>
/// <param name="FieldName">Spaltenname bzw. JSON-Eigenschaft.</param>
/// <param name="Generator">Der zum Muster gehoerende Generator.</param>
/// <param name="Evidence">Ein Beispielwert, der zum Vorschlag gefuehrt hat.</param>
/// <param name="MatchedSamples">
/// Anzahl der nicht leeren Beispielwerte, die passen — bei einem Vorschlag
/// immer gleich <see cref="TotalSamples"/> minus der ausgeschlossenen
/// Leerwerte, denn vorgeschlagen wird nur bei vollstaendiger Uebereinstimmung.
/// </param>
/// <param name="TotalSamples">Anzahl aller eingesammelten Beispielwerte, einschliesslich leerer.</param>
public sealed record ValueSuggestion(
    string FieldName, string Generator, string Evidence, int MatchedSamples, int TotalSamples);

/// <summary>
/// Schlaegt anhand von Beispielwerten einen Generator vor — die haertere
/// Aussage als ein Namensfragment (<see cref="ProfileScaffolder.Suggest"/>):
/// ein Wert, der auf ein Muster passt, ist Beweis, ein Feldname ist nur
/// Vermutung.
///
/// Bewusst streng gehalten, um Fehltreffer zu vermeiden: ein Muster muss den
/// gesamten (getrimmten) Wert treffen, nicht nur einen Ausschnitt, und es
/// braucht mindestens zwei uebereinstimmende Beispielwerte. Ein Feld mit nur
/// einem Beispielwert oder mit nur einem Teiltreffer bekommt keinen Vorschlag
/// — ein Teiltreffer ist ein Freitextfall und gehoert zu <c>scanText</c>,
/// nicht zu einem Feldgenerator.
///
/// Kandidatenmuster sind der Grundstock aus
/// <see cref="ProfileScaffolder.DefaultTextRules"/> zusammen mit den
/// Textregeln der <see cref="GeneratorLibrary"/> — dadurch schlaegt die
/// Erkennung auch ein hauseigenes Muster vor, sobald es in der Bibliothek
/// steht.
/// </summary>
public static class ValueSuggester
{
    /// <summary>
    /// Mindestens so viele passende Beispielwerte, sonst ist die Aussage zu
    /// duenn, um einen Vorschlag zu rechtfertigen.
    /// </summary>
    private const int MinimumMatchingSamples = 2;

    /// <summary>
    /// Fuer den Leerwert-Ausschluss ohne uebergebene Profilvorgaben: nur "" und
    /// reiner Leerraum gelten dann als leer.
    /// </summary>
    private static readonly ProfileDefaults EmptyValueDefaults = new();

    /// <param name="defaults">
    /// Die Vorgaben des Profils, deren <see cref="ProfileDefaults.EmptyValues"/>
    /// beim Aussieben gelten. Ohne Angabe zaehlen nur "" und reiner Leerraum als
    /// leer — dann wuerde ein faktischer Leerwert wie "N/A" als abweichender
    /// Beispielwert gelten und den Vorschlag verhindern, obwohl das Feld ihn
    /// selbst als leer behandelt.
    /// </param>
    public static IReadOnlyList<ValueSuggestion> Suggest(
        IReadOnlyDictionary<string, IReadOnlyList<string>> samplesByField,
        GeneratorLibrary? library = null,
        ProfileDefaults? defaults = null)
    {
        library ??= GeneratorLibrary.Load();
        var leerwerte = defaults ?? EmptyValueDefaults;
        var candidates = BuildCandidates(library);
        if (candidates.Count == 0)
            return Array.Empty<ValueSuggestion>();

        var result = new List<ValueSuggestion>();

        foreach (var (fieldName, rawSamples) in samplesByField)
        {
            var samples = rawSamples
                .Select(value => value.Trim())
                .Where(value => !leerwerte.IsEffectivelyEmpty(value))
                .ToList();

            if (samples.Count < MinimumMatchingSamples)
                continue;

            ValueSuggestion? best = null;
            var bestPriority = int.MinValue;

            foreach (var candidate in candidates)
            {
                if (candidate.Rule.Priority <= bestPriority)
                    continue;

                if (!samples.All(value => candidate.FullMatch.IsMatch(value)))
                    continue;

                bestPriority = candidate.Rule.Priority;
                best = new ValueSuggestion(
                    fieldName, candidate.Rule.Generator, samples[0], samples.Count, rawSamples.Count);
            }

            if (best is not null)
                result.Add(best);
        }

        return result;
    }

    private readonly record struct Candidate(TextRule Rule, Regex FullMatch);

    private static List<Candidate> BuildCandidates(GeneratorLibrary library)
    {
        var rules = ProfileScaffolder.DefaultTextRules().Concat(library.TextRules);
        var candidates = new List<Candidate>();

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;

            try
            {
                // Voller Treffer statt eines Fundes irgendwo im Wert: ein
                // Teiltreffer ist ein Freitextfall, kein Feldgenerator.
                // Dieselben Optionen und dasselbe Zeitlimit wie
                // TextRuleEngine — dieselbe Hilfsmethode, nicht neu gebaut.
                var fullMatch = TextRuleEngine.Compile($@"\A(?:{rule.Pattern})\z", rule.IgnoreCase, rule.Name);
                candidates.Add(new Candidate(rule, fullMatch));
            }
            catch (ConfigurationException)
            {
                // Eine kaputte Textregel ist Sache von ProfileValidator; hier
                // wird sie einfach kein Kandidat — ein Vorschlag ist nur eine
                // Erleichterung, kein Pruefschritt.
            }
        }

        return candidates;
    }
}
