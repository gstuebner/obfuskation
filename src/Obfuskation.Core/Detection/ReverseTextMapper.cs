using System.Text;
using System.Text.RegularExpressions;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Detection;

/// <summary>
/// Fuehrt Pseudonyme in beliebigem Text auf ihre Klartexte zurueck — der
/// Alltagsfall, wenn eine Antwort der KI mit generiertem Code zurueckuebersetzt
/// werden soll.
///
/// Zwei Fallen sind dabei zu umgehen:
/// <list type="bullet">
/// <item>Ein Pseudonym kann Teilzeichenfolge eines anderen sein. Deshalb werden
/// alle Pseudonyme nach Laenge absteigend sortiert und in einem einzigen
/// Durchgang gesucht — der laengere Treffer gewinnt.</item>
/// <item>Wortartige Pseudonyme duerfen nicht mitten in einem Wort greifen.
/// Sie bekommen deshalb Wortgrenzen.</item>
/// </list>
/// </summary>
public sealed class ReverseTextMapper
{
    /// <summary>
    /// Hoechstzahl an Alternativen je Ausdruck. Grosse Bestaende werden in
    /// mehrere Ausdruecke aufgeteilt, damit die Uebersetzung des Ausdrucks nicht
    /// unverhaeltnismaessig teuer wird.
    /// </summary>
    private const int MaxAlternativesPerRegex = 1000;

    private readonly List<(Regex Pattern, Dictionary<string, string> Lookup)> _blocks = new();

    public int EntryCount { get; }

    public ReverseTextMapper(MappingStore store, GeneratorRegistry generators)
    {
        // Nach Laenge absteigend: so wird bei zwei sich ueberlappenden Kandidaten
        // stets der laengere zuerst gefunden.
        var entries = store.AllReverseEntries()
            .Select(entry => new
            {
                entry.Pseudonym,
                entry.Plaintext,
                WordLike = generators.Contains(entry.Namespace) && generators.Get(entry.Namespace).IsWordLike,
            })
            .Where(entry => entry.Pseudonym.Length > 0)
            .OrderByDescending(entry => entry.Pseudonym.Length)
            .ThenBy(entry => entry.Pseudonym, StringComparer.Ordinal)
            .ToList();

        EntryCount = entries.Count;

        for (var offset = 0; offset < entries.Count; offset += MaxAlternativesPerRegex)
        {
            var block = entries.Skip(offset).Take(MaxAlternativesPerRegex).ToList();
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            var alternatives = new StringBuilder();

            foreach (var entry in block)
            {
                // Ein Pseudonym kann in mehreren Namensraeumen vorkommen. Der
                // erste Treffer gewinnt; die Sortierung macht das reproduzierbar.
                lookup.TryAdd(entry.Pseudonym, entry.Plaintext);

                if (alternatives.Length > 0)
                    alternatives.Append('|');

                var escaped = Regex.Escape(entry.Pseudonym);

                // Wortgrenzen nur dort, wo sie ueberhaupt greifen koennen: \b
                // wirkt zwischen Wortzeichen und Nichtwortzeichen und waere an
                // einem Wert, der mit einem Sonderzeichen beginnt, wirkungslos.
                var needsLeadingBoundary = entry.WordLike && char.IsLetterOrDigit(entry.Pseudonym[0]);
                var needsTrailingBoundary = entry.WordLike && char.IsLetterOrDigit(entry.Pseudonym[^1]);

                if (needsLeadingBoundary)
                    alternatives.Append("\\b");
                alternatives.Append(escaped);
                if (needsTrailingBoundary)
                    alternatives.Append("\\b");
            }

            if (alternatives.Length == 0)
                continue;

            var pattern = new Regex(alternatives.ToString(),
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));
            _blocks.Add((pattern, lookup));
        }
    }

    /// <summary>Ersetzt alle bekannten Pseudonyme durch ihre Klartexte.</summary>
    public string Restore(string text, out int replacementCount)
    {
        var total = 0;
        var current = text;

        // Die Bloecke laufen nacheinander. Innerhalb eines Blocks kann es keine
        // Kettenersetzung geben; zwischen Bloecken auch nicht, weil ein Klartext
        // niemals gleichzeitig ein Pseudonym ist — das schliesst der
        // Pseudonymizer beim Anlegen aus.
        foreach (var (pattern, lookup) in _blocks)
        {
            var count = 0;
            current = pattern.Replace(current, match =>
            {
                if (!lookup.TryGetValue(match.Value, out var plaintext))
                    return match.Value;
                count++;
                return plaintext;
            });
            total += count;
        }

        replacementCount = total;
        return current;
    }
}
