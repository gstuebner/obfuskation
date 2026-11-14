using System.Text;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Detection;

/// <summary>Ein Fund einer Textregel.</summary>
/// <param name="Start">Beginn im Originaltext.</param>
/// <param name="Length">Laenge im Originaltext.</param>
/// <param name="Value">Der getroffene Text.</param>
/// <param name="Rule">Die Regel, die den Fund erzeugt hat.</param>
public sealed record TextMatch(int Start, int Length, string Value, TextRule Rule)
{
    public int End => Start + Length;
}

/// <summary>
/// Wendet Textregeln auf Freitext an.
///
/// Entscheidend ist, dass alle Regeln gegen den <b>unveraenderten</b> Originaltext
/// laufen und der Ergebnistext danach in einem Durchgang aus den ueberschneidungs-
/// frei ausgewaehlten Funden gebaut wird. Wuerde man stattdessen Regel fuer Regel
/// ersetzen, koennte eine spaetere Regel in dem greifen, was eine fruehere gerade
/// eingesetzt hat.
/// </summary>
public sealed class TextRuleEngine
{
    private readonly Dictionary<string, Regex> _compiled = new(StringComparer.Ordinal);

    /// <summary>Sammelt alle ueberschneidungsfreien Funde in Reihenfolge.</summary>
    public IReadOnlyList<TextMatch> FindMatches(string text, IReadOnlyList<TextRule> rules)
        => FindMatches(text, rules, displaced: null);

    /// <summary>
    /// Wie <see cref="FindMatches(string, IReadOnlyList{TextRule})"/>; zusaetzlich
    /// landen in <paramref name="displaced"/> die verdraengten Funde, deren
    /// Bereich die gewaehlten nicht vollstaendig abdecken -- dort bleibt ein
    /// Teil stehen, den eine Regel fuer ersetzenswert hielt.
    /// </summary>
    public IReadOnlyList<TextMatch> FindMatches(string text, IReadOnlyList<TextRule> rules, List<TextMatch>? displaced)
    {
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
            return Array.Empty<TextMatch>();

        var candidates = new List<TextMatch>();

        foreach (var rule in rules)
        {
            var regex = GetRegex(rule);
            foreach (Match match in regex.Matches(text))
            {
                if (!match.Success)
                    continue;

                var group = rule.CaptureGroup > 0 ? match.Groups[rule.CaptureGroup] : match.Groups[0];
                if (!group.Success || group.Length == 0)
                    continue;

                candidates.Add(new TextMatch(group.Index, group.Length, group.Value, rule));
            }
        }

        if (candidates.Count == 0)
            return Array.Empty<TextMatch>();

        // Auswahlreihenfolge: die hoehere Prioritaet zuerst, danach der
        // laengere Fund, danach der weiter links beginnende. Jeder Fund, der
        // sich mit keinem schon gewaehlten ueberschneidet, wird uebernommen.
        //
        // Bis 1.13.0 ging die Auswahl von links nach rechts, und die Prioritaet
        // entschied nur bei gleicher Startposition. Ein frueher beginnender
        // Fund niedriger Prioritaet verdraengte dann einen spaeter beginnenden
        // hoher Prioritaet vollstaendig, und dessen nicht ueberdeckter Teil
        // blieb im Klartext stehen (Befund A5).
        candidates.Sort(static (left, right) =>
        {
            var byPriority = right.Rule.Priority.CompareTo(left.Rule.Priority);
            if (byPriority != 0)
                return byPriority;

            var byLength = right.Length.CompareTo(left.Length);
            if (byLength != 0)
                return byLength;

            return left.Start.CompareTo(right.Start);
        });

        // Gewaehlte Funde nach Start geordnet, fuer die Ueberschneidungspruefung
        // per Binaersuche.
        var selected = new List<TextMatch>(candidates.Count);
        var losers = displaced is null ? null : new List<TextMatch>();
        foreach (var candidate in candidates)
        {
            var index = selected.BinarySearch(candidate, StartComparer.Instance);
            if (index < 0)
                index = ~index;

            var overlapsLeft = index > 0 && selected[index - 1].End > candidate.Start;
            var overlapsRight = index < selected.Count && selected[index].Start < candidate.End;
            if (overlapsLeft || overlapsRight)
            {
                losers?.Add(candidate);
                continue;
            }

            selected.Insert(index, candidate);
        }

        if (losers is not null)
        {
            foreach (var loser in losers)
            {
                if (!IsCovered(loser, selected))
                    displaced!.Add(loser);
            }
        }

        return selected;
    }

    /// <summary>Ob die gewaehlten Funde den Bereich von <paramref name="match"/> lueckenlos abdecken.</summary>
    private static bool IsCovered(TextMatch match, List<TextMatch> selected)
    {
        var position = match.Start;
        foreach (var chosen in selected)
        {
            if (chosen.End <= position)
                continue;
            if (chosen.Start > position)
                return false;
            position = chosen.End;
            if (position >= match.End)
                return true;
        }
        return position >= match.End;
    }

    private sealed class StartComparer : IComparer<TextMatch>
    {
        public static readonly StartComparer Instance = new();
        public int Compare(TextMatch? x, TextMatch? y) => x!.Start.CompareTo(y!.Start);
    }

    /// <summary>
    /// Ersetzt alle Funde in einem Durchgang. <paramref name="replace"/> liefert
    /// den Ersatzwert zu einem Fund.
    /// </summary>
    public string Replace(string text, IReadOnlyList<TextRule> rules, Func<TextMatch, string> replace,
        out int replacementCount)
        => Replace(text, rules, replace, out replacementCount, displaced: null);

    /// <summary>Wie oben; verdraengte, nicht ueberdeckte Funde landen in <paramref name="displaced"/>.</summary>
    public string Replace(string text, IReadOnlyList<TextRule> rules, Func<TextMatch, string> replace,
        out int replacementCount, List<TextMatch>? displaced)
    {
        var matches = FindMatches(text, rules, displaced);
        replacementCount = matches.Count;

        if (matches.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var position = 0;

        foreach (var match in matches)
        {
            builder.Append(text, position, match.Start - position);
            builder.Append(replace(match));
            position = match.End;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    private Regex GetRegex(TextRule rule)
    {
        var key = rule.Name + " " + rule.Pattern + " " + rule.IgnoreCase;
        if (_compiled.TryGetValue(key, out var existing))
            return existing;

        var regex = Compile(rule.Pattern, rule.IgnoreCase, rule.Name);
        _compiled[key] = regex;
        return regex;
    }

    /// <summary>
    /// Uebersetzt ein Regex-Muster mit denselben Optionen und demselben
    /// Zeitlimit wie diese Engine selbst. Oeffentlich, damit
    /// <see cref="Configuration.ValueSuggester"/> beim Pruefen von
    /// Beispielwerten dieselben Grundeinstellungen verwendet, statt sie ein
    /// zweites Mal zu pflegen.
    /// </summary>
    /// <param name="ruleName">Nur fuer die Fehlermeldung; leer ist erlaubt.</param>
    public static Regex Compile(string pattern, bool ignoreCase, string ruleName = "")
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;

        try
        {
            return new Regex(pattern, options, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            var context = ruleName.Length > 0 ? $" in der Textregel '{ruleName}'" : "";
            throw new ConfigurationException($"Ungültiger regulärer Ausdruck{context}: {ex.Message}", ex);
        }
    }
}
