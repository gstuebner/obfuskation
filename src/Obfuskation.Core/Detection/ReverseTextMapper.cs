using System.Text;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Detection;

/// <summary>
/// Fuehrt Pseudonyme in beliebigem Text auf ihre Klartexte zurueck — der
/// Alltagsfall, wenn eine Antwort der KI mit generiertem Code zurueckuebersetzt
/// werden soll.
///
/// Drei Fallen sind dabei zu umgehen:
/// <list type="bullet">
/// <item>Ein Pseudonym kann Teilzeichenfolge eines anderen sein. Beginnen zwei
/// Treffer an derselben Stelle, gewinnt deshalb der laengere.</item>
/// <item>Wortartige Pseudonyme duerfen nicht mitten in einem Wort greifen.
/// Sie verlangen deshalb Wortgrenzen.</item>
/// <item>Ein wiederhergestellter Klartext darf nie erneut durchsucht werden.
/// Bis 1.13.0 liefen grosse Bestaende in Bloecken zu 1000 Eintraegen
/// nacheinander ueber den jeweils schon veraenderten Text, und ein kuerzeres
/// Pseudonym eines spaeteren Blocks traf im eben eingesetzten Klartext
/// (Befund A4). Jetzt werden alle Treffer im unveraenderten Text gesammelt,
/// ueberschneidungsfrei ausgewaehlt und das Ergebnis in einem Durchgang
/// gebaut -- wie in <see cref="TextRuleEngine"/>.</item>
/// </list>
/// </summary>
public sealed class ReverseTextMapper
{
    private readonly AhoCorasick? _automaton;
    private readonly List<(string Pseudonym, string Plaintext, bool WordLike)> _entries = new();

    public int EntryCount { get; }

    /// <summary>
    /// Wie viele Pseudonyme in mehreren Namensraeumen auf verschiedene
    /// Klartexte verweisen. Fuer sie gilt der Eintrag des alphabetisch ersten
    /// Namensraums; die Rueckuebersetzung im Freitext ist dort mehrdeutig.
    /// </summary>
    public int AmbiguousCount { get; }

    public ReverseTextMapper(MappingStore store, GeneratorRegistry generators)
    {
        var grouped = store.AllReverseEntries()
            .Where(entry => entry.Pseudonym.Length > 0)
            .OrderBy(entry => entry.Namespace, StringComparer.Ordinal)
            .GroupBy(entry => entry.Pseudonym, StringComparer.Ordinal);

        var ambiguous = 0;
        foreach (var group in grouped)
        {
            var first = group.First();
            if (group.Select(entry => entry.Plaintext).Distinct(StringComparer.Ordinal).Count() > 1)
                ambiguous++;

            var wordLike = group.All(entry =>
                generators.Contains(entry.Namespace) && generators.Get(entry.Namespace).IsWordLike);

            _entries.Add((first.Pseudonym, first.Plaintext, wordLike));
        }

        EntryCount = _entries.Count;
        AmbiguousCount = ambiguous;

        if (_entries.Count > 0)
            _automaton = new AhoCorasick(_entries.Select(entry => entry.Pseudonym).ToList());
    }

    /// <summary>Ersetzt alle bekannten Pseudonyme durch ihre Klartexte.</summary>
    public string Restore(string text, out int replacementCount)
    {
        replacementCount = 0;
        if (_automaton is null || text.Length == 0)
            return text;

        var candidates = new List<(int Start, int Length, int Entry)>();
        foreach (var (start, length, entry) in _automaton.FindAll(text))
        {
            if (_entries[entry].WordLike && !AhoCorasick.HasWordBoundaries(text, start, length))
                continue;
            candidates.Add((start, length, entry));
        }

        if (candidates.Count == 0)
            return text;

        // Von links nach rechts, an derselben Stelle der laengere Treffer --
        // dieselbe Wahl, die frueher die nach Laenge sortierte Alternation traf.
        candidates.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            return byStart != 0 ? byStart : right.Length.CompareTo(left.Length);
        });

        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (var (start, length, entry) in candidates)
        {
            if (start < position)
                continue;

            builder.Append(text, position, start - position);
            builder.Append(_entries[entry].Plaintext);
            position = start + length;
            replacementCount++;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }
}
