using System.Text;
using System.Text.RegularExpressions;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Ein aus einem Beispielwert abgeleitetes Muster samt einer Beschreibung, die
/// ohne Kenntnis regulaerer Ausdruecke verstaendlich ist.
/// </summary>
/// <param name="Pattern">Das Muster, wie es in eine <see cref="TextRule"/> gehoert.</param>
/// <param name="Description">
/// Beschreibung in Anwendersprache, etwa "FW + 6 Ziffern". Die Oberflaeche
/// zeigt sie statt des Musters — wer das Muster sehen will, bekommt es
/// zusaetzlich, aber die Entscheidung faellt an dieser Zeile.
/// </param>
public sealed record SamplePattern(string Pattern, string Description);

/// <summary>
/// Leitet aus einem markierten Beispielwert ein Textregel-Muster ab.
///
/// Der Anlass: wiederkehrende hauseigene Werte (Hostnamen der Form
/// <c>FW123456</c>, Inventarnummern, Vorgangskennungen) sollen sich anlegen
/// lassen, ohne dass jemand einen regulaeren Ausdruck schreiben muss. Der
/// Anwender markiert einen Wert, das Programm bietet zwei Lesarten an: genau
/// dieses Wort, oder alles derselben Form.
///
/// Bewusst zurueckhaltend: verallgemeinert wird ausschliesslich ueber
/// Ziffernlaeufe. Buchstaben bleiben woertlich stehen, denn sie tragen im
/// Regelfall die Kennung (<c>FW</c>), und ein Muster, das auch sie ersetzt,
/// traefe halb Deutschland. Ein zu weit gefasstes Muster beschaedigt die
/// Testdaten, ohne dass es beim Betrachten auffiele — dieselbe Ueberlegung wie
/// bei <see cref="ProfileScaffolder.DefaultTextRules"/>.
/// </summary>
public static class PatternFromSample
{
    /// <summary>
    /// Muster fuer genau diesen Wert. Der Beispielwert wird vollstaendig
    /// maskiert, es bleibt also auch dann ein woertlicher Treffer, wenn er
    /// Sonderzeichen enthaelt.
    /// </summary>
    public static SamplePattern Literal(string sample)
    {
        var trimmed = sample.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Der Beispielwert ist leer.", nameof(sample));

        return new SamplePattern(
            WithBoundaries(Regex.Escape(trimmed), trimmed),
            $"genau „{trimmed}“");
    }

    /// <summary>
    /// Muster fuer alle Werte derselben Form: Ziffernlaeufe werden zu
    /// <c>\d{n}</c>, alles Uebrige bleibt woertlich.
    ///
    /// Liefert <c>null</c>, wenn der Beispielwert keinen Ziffernlauf enthaelt
    /// — dann waere die Form nichts anderes als <see cref="Literal"/>, und die
    /// Oberflaeche soll keine zweite Moeglichkeit anbieten, die dasselbe tut.
    /// </summary>
    public static SamplePattern? Shape(string sample)
    {
        var trimmed = sample.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Der Beispielwert ist leer.", nameof(sample));

        var runs = SplitIntoRuns(trimmed);
        if (!runs.Any(run => run.IsDigits))
            return null;

        var pattern = new StringBuilder();
        foreach (var run in runs)
        {
            pattern.Append(run.IsDigits
                ? $@"\d{{{run.Text.Length}}}"
                : Regex.Escape(run.Text));
        }

        return new SamplePattern(
            WithBoundaries(pattern.ToString(), trimmed),
            Describe(runs));
    }

    /// <summary>
    /// Ein Token-Praefix aus dem Beispielwert, etwa <c>FW~</c> aus
    /// <c>FW123456</c>. Damit bleibt in der Pseudodatei erkennbar, wofuer ein
    /// Pseudonym steht.
    ///
    /// Zeichenvorrat und Laenge richten sich nach <c>ProfileValidator</c>
    /// (Buchstaben, Ziffern, Unterstrich und Bindestrich, hoechstens 32
    /// Zeichen einschliesslich des abschliessenden <c>~</c>). Liefert
    /// <c>null</c>, wenn sich daraus nichts Brauchbares bilden laesst — dann
    /// bleibt es beim Generator ohne Praefix.
    /// </summary>
    public static string? SuggestPrefix(string sample)
    {
        var trimmed = sample.Trim();
        if (trimmed.Length == 0)
            return null;

        // Der erste Buchstabenlauf ist die Kennung: aus "FW123456" wird "FW",
        // aus "2024-0815" bleibt nichts uebrig, und das ist richtig so — eine
        // reine Nummer traegt keinen Namen, den man voranstellen koennte.
        var runs = SplitIntoRuns(trimmed);
        var stem = runs.Where(run => run.IsLetters).Select(run => run.Text).FirstOrDefault() ?? "";

        var body = DisallowedPrefixChars.Replace(stem, "");
        if (body.Length == 0)
            return null;

        // 31 statt 32: das abschliessende '~' zaehlt beim Validator mit —
        // dieselbe Rechnung wie in ProfileScaffolder.SuggestPrefixNamespace.
        if (body.Length > 31)
            body = body[..31];

        return body + "~";
    }

    /// <summary>
    /// Ein Regelname aus dem Beispielwert, kleingeschrieben und auf den
    /// Zeichenvorrat eines Bezeichners eingedampft. Ohne brauchbaren Stamm
    /// bleibt es bei <c>begriff</c>; fuer die Eindeutigkeit innerhalb eines
    /// Profils sorgt der Aufrufer.
    /// </summary>
    public static string SuggestRuleName(string sample)
    {
        var stem = SplitIntoRuns(sample.Trim())
            .Where(run => run.IsLetters).Select(run => run.Text).FirstOrDefault() ?? "";
        var cleaned = DisallowedPrefixChars.Replace(stem, "").ToLowerInvariant();

        return cleaned.Length == 0 ? "begriff" : cleaned;
    }

    /// <summary>
    /// Zeichenvorrat, den ein Token-Praefix nach <c>ProfileValidator</c>
    /// tragen darf. Wie in <see cref="ProfileScaffolder"/> — dort zum
    /// Ausduennen eines Feldnamens, hier eines Beispielwertes.
    /// </summary>
    private static readonly Regex DisallowedPrefixChars =
        new("[^A-Za-z0-9ÄÖÜäöüß_-]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly record struct Run(string Text, bool IsDigits, bool IsLetters);

    /// <summary>
    /// Zerlegt den Wert in Laeufe gleicher Zeichenart: Ziffern, Buchstaben,
    /// Uebriges. Aus "AB-12/34" werden "AB", "-", "12", "/", "34".
    /// </summary>
    private static List<Run> SplitIntoRuns(string value)
    {
        var runs = new List<Run>();
        var index = 0;

        while (index < value.Length)
        {
            var isDigit = char.IsDigit(value[index]);
            var isLetter = char.IsLetter(value[index]);

            var start = index;
            while (index < value.Length
                   && char.IsDigit(value[index]) == isDigit
                   && char.IsLetter(value[index]) == isLetter)
            {
                index++;
            }

            runs.Add(new Run(value[start..index], isDigit, isLetter));
        }

        return runs;
    }

    /// <summary>
    /// Beschreibt die Laeufe in Anwendersprache. Benachbarte woertliche Laeufe
    /// werden zusammengefasst, damit aus "AB", "-" nicht zwei Punkte werden,
    /// wo einer gemeint ist.
    /// </summary>
    private static string Describe(List<Run> runs)
    {
        var parts = new List<string>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;

            parts.Add($"„{literal}“");
            literal.Clear();
        }

        foreach (var run in runs)
        {
            if (run.IsDigits)
            {
                FlushLiteral();
                parts.Add(run.Text.Length == 1 ? "1 Ziffer" : $"{run.Text.Length} Ziffern");
            }
            else
            {
                literal.Append(run.Text);
            }
        }

        FlushLiteral();
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Setzt Wortgrenzen, aber nur dort, wo sie auch greifen: <c>\b</c> vor
    /// einem Nicht-Wortzeichen bedeutet etwas anderes, als man erwartet, und
    /// wuerde das Muster stillschweigend unbrauchbar machen. Ein Wert wie
    /// "+49 30 123456" bekommt vorne deshalb keine Grenze.
    /// </summary>
    private static string WithBoundaries(string pattern, string sample)
    {
        var leading = IsWordCharacter(sample[0]) ? @"\b" : "";
        var trailing = IsWordCharacter(sample[^1]) ? @"\b" : "";

        return leading + pattern + trailing;
    }

    private static bool IsWordCharacter(char character)
        => char.IsLetterOrDigit(character) || character == '_';
}
