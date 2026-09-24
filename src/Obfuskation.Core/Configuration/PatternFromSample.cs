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
/// Ergebnis von <see cref="PatternFromSample.TryRecognize"/>: ein Muster, das
/// sich als "alles dieser Form" oder als "genau dieser Wert" wiedererkennen
/// liess -- fuer eine bestehende Regel, die das Einstellungsfenster wieder im
/// Erfassungsmodus statt im freien Ausdruck oeffnen soll.
/// </summary>
/// <param name="IsShape">
/// <c>true</c> fuer "alles dieser Form" (<see cref="PatternFromSample.Shape"/>),
/// <c>false</c> fuer "genau dieser Wert" (<see cref="PatternFromSample.Literal"/>).
/// </param>
/// <param name="Sample">
/// Ein zum Muster passender Beispielwert -- bei einer Form nicht notwendig der
/// urspruengliche (Ziffernlaeufe werden durch "1234567890…" ersetzt), aber
/// einer, der dieselbe Beschreibung und dasselbe Muster ergibt.
/// </param>
/// <param name="Description">Wie <see cref="SamplePattern.Description"/>.</param>
public sealed record RecognizedPattern(bool IsShape, string Sample, string Description);

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
    /// Zeichenfolge fuer <see cref="DigitSample"/> -- willkuerlich, aber
    /// erkennbar keine echten Daten.
    /// </summary>
    private const string DigitSampleChars = "1234567890";

    /// <summary>Laengster Ziffernlauf, den <see cref="TryRecognize"/> noch als Beispielwert nachbildet.</summary>
    private const int MaxDigitRun = 1000;

    /// <summary>Erkennt ein Muster, das <see cref="Shape"/> oder <see cref="Literal"/> erzeugt haette.</summary>
    private static readonly Regex DigitRunToken = new(@"\\d\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Versucht, ein bestehendes Muster als "alles dieser Form" oder "genau
    /// dieser Wert" wiederzuerkennen, statt es als freien Ausdruck zu
    /// behandeln -- fuer eine schon vorhandene Regel, deren Formular sich
    /// wieder im vertrauten Erfassungsmodus statt im Profi-Modus oeffnen soll.
    ///
    /// Vorgehen: fuehrendes und abschliessendes <c>\b</c> abtrennen, den Rumpf
    /// an <c>\d{n}</c>-Stuecken zerlegen -- ein Ziffernlauf wird zu <c>n</c>
    /// Ziffern aus <see cref="DigitSampleChars"/>, alles Uebrige per
    /// <see cref="Regex.Unescape"/> entschluesselt. Scheitert das Entschluesseln
    /// (ein echter regulaerer Ausdruck, keine blosse Maskierung), ist das
    /// Ergebnis <c>null</c>.
    ///
    /// <b>Rundreise als Beweis:</b> der so gebildete Beispielwert wird selbst
    /// wieder durch <see cref="Shape"/> bzw. -- ohne <c>\d{</c>-Stuecke --
    /// <see cref="Literal"/> geschickt. Ergibt das exakt wieder
    /// <paramref name="pattern"/>, war die Zerlegung richtig; sonst war das
    /// Muster ein eigener, freier Ausdruck, und das Ergebnis ist <c>null</c>.
    /// </summary>
    public static RecognizedPattern? TryRecognize(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return null;

        var body = pattern;
        if (body.StartsWith(@"\b", StringComparison.Ordinal))
            body = body[2..];
        if (body.EndsWith(@"\b", StringComparison.Ordinal))
            body = body[..^2];

        if (body.Length == 0)
            return null;

        var sample = new StringBuilder();
        var hasDigitRun = false;
        var position = 0;

        while (position < body.Length)
        {
            var treffer = DigitRunToken.Match(body, position);

            if (treffer.Success && treffer.Index == position)
            {
                // Ein von Hand geschriebenes "\d{9999999999}" liefe bei
                // int.Parse ueber, ein grosses, aber gueltiges n baute eine
                // riesige Zeichenkette -- und das beim blossen Anzeigen der
                // Regelliste. Kein Beispielwert erreicht diese Laenge, also
                // ist es ohnehin ein eigener Ausdruck.
                if (!int.TryParse(treffer.Groups[1].Value, out var laenge) || laenge > MaxDigitRun)
                    return null;

                hasDigitRun = true;
                sample.Append(DigitSample(laenge));
                position += treffer.Length;
                continue;
            }

            var ende = treffer.Success ? treffer.Index : body.Length;
            string entschluesselt;
            try
            {
                entschluesselt = Regex.Unescape(body[position..ende]);
            }
            catch (ArgumentException)
            {
                // Kein blosses Escaping, sondern ein eigener regulaerer
                // Ausdruck (z.B. "\d+" oder "[A-Z]") -- kein Fall fuer die
                // Erfassungsmodi.
                return null;
            }

            sample.Append(entschluesselt);
            position = ende;
        }

        var kandidat = sample.ToString();
        if (kandidat.Length == 0)
            return null;

        var form = Shape(kandidat);
        if (form is not null && form.Pattern == pattern)
            return new RecognizedPattern(IsShape: true, kandidat, form.Description);

        if (!hasDigitRun)
        {
            var woertlich = Literal(kandidat);
            if (woertlich.Pattern == pattern)
                return new RecognizedPattern(IsShape: false, kandidat, woertlich.Description);
        }

        return null;
    }

    /// <summary>Ein Beispielwert aus <paramref name="length"/> Ziffern, zyklisch aus <see cref="DigitSampleChars"/>.</summary>
    private static string DigitSample(int length)
    {
        var text = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            text.Append(DigitSampleChars[i % DigitSampleChars.Length]);
        return text.ToString();
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
    /// Zeichenvorrat eines Bezeichners eingedampft. Ohne Buchstaben wird es
    /// <c>nummer</c>, wenn der Wert Ziffern hat (Kartennummer, Aktenzeichen),
    /// sonst <c>begriff</c>. Beides ist nur ein Vorschlag -- die Oberflaeche
    /// laesst ihn als "Bezeichnung" aendern; fuer die Eindeutigkeit innerhalb
    /// eines Profils sorgt der Aufrufer.
    /// </summary>
    public static string SuggestRuleName(string sample)
    {
        var runs = SplitIntoRuns(sample.Trim());
        var stem = runs.Where(run => run.IsLetters).Select(run => run.Text).FirstOrDefault() ?? "";
        var cleaned = DisallowedPrefixChars.Replace(stem, "").ToLowerInvariant();

        if (cleaned.Length > 0)
            return cleaned;

        return runs.Any(run => run.IsDigits) ? "nummer" : "begriff";
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

            // Leerzeichen in Anfuehrungszeichen ("„ “", "„AB “") liest
            // niemand als solche -- in der Beschreibung einer Kartennummer
            // stand sonst "4 Ziffern + „ “ + 4 Ziffern". Am Rand eines
            // woertlichen Stuecks werden sie deshalb als Wort abgetrennt;
            // Leerzeichen mitten im Stueck ("„Dr. med.“") bleiben stehen.
            var text = literal.ToString();
            var kern = text.Trim(' ');
            var vorn = text.Length - text.TrimStart(' ').Length;
            var hinten = text.Length - text.TrimEnd(' ').Length;

            if (kern.Length == 0)
            {
                parts.Add(Leerzeichen(text.Length));
            }
            else
            {
                if (vorn > 0)
                    parts.Add(Leerzeichen(vorn));
                parts.Add($"„{kern}“");
                if (hinten > 0)
                    parts.Add(Leerzeichen(hinten));
            }

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

        static string Leerzeichen(int anzahl) => anzahl == 1 ? "Leerzeichen" : $"{anzahl} Leerzeichen";
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
