using System.Globalization;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Gemeinsames Datumshandwerk fuer alle Datumsgeneratoren: Parsen gegen eine
/// Formatliste und Formatieren im erkannten Format. <c>dateShift</c>,
/// <c>dateRange</c> und <c>dateGeneralize</c> teilen sich dieses Verhalten,
/// damit ein Datum in jeder Spalte gleich gelesen und im selben Format
/// zurueckgegeben wird.
/// </summary>
public static class DateValues
{
    /// <summary>
    /// Die Standardformate, gegen die ohne eigene Vorgabe geprueft wird.
    ///
    /// Bis 1.13.0 stand hier auch "MM/dd/yyyy" hinter "dd/MM/yyyy". Ein Wert
    /// wie 03/04/2024 passt auf beide, und je Wert gewann das erste passende
    /// Format: eine US-Spalte wurde uneinheitlich gelesen, und ein
    /// verschobenes Datum konnte beim Zurueckrechnen auf das andere Format
    /// fallen -- still falsche Daten (Befund A2). Amerikanische Daten gibt es
    /// deshalb nur noch ausdruecklich ueber <c>generators.&lt;name&gt;.formats</c>.
    /// </summary>
    public static readonly string[] FallbackFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm",
        "dd.MM.yy", "yyyyMMdd",
    ];

    /// <summary>
    /// Die wirksame Formatliste: eigene Formate, wenn welche gesetzt sind,
    /// sonst die Standardformate. Eigene Formate ersetzen die Vorgabe, statt
    /// sie als Auffangnetz anzuhaengen -- wer "MM/dd/yyyy" setzt, will nicht,
    /// dass ein Wert hilfsweise doch europaeisch gelesen wird.
    /// </summary>
    public static string[] CombineFormats(List<string>? own)
        => own is { Count: > 0 } ? own.Distinct().ToArray() : FallbackFormats;

    /// <summary>
    /// Versucht, einen Wert gegen die gegebenen Formate zu parsen. Ein Format,
    /// das den Wert unveraendert wiederherstellt, hat Vorrang; sonst gewinnt
    /// das erste, das ihn lesen kann. Das gewaehlte Format kommt ueber
    /// <paramref name="usedFormat"/> zurueck, damit dieselbe Schreibweise
    /// beim Formatieren wieder entsteht.
    /// </summary>
    public static bool TryParse(string original, string[] formats, out DateTime value, out string usedFormat)
    {
        var trimmed = original.Trim();
        string? lenientFormat = null;
        DateTime lenientValue = default;

        foreach (var candidate in formats)
        {
            if (!TryParseExact(trimmed, candidate, out var parsed))
                continue;

            if (Format(parsed, candidate) == trimmed)
            {
                value = parsed;
                usedFormat = candidate;
                return true;
            }

            if (lenientFormat is null)
            {
                lenientFormat = candidate;
                lenientValue = parsed;
            }
        }

        if (lenientFormat is not null)
        {
            value = lenientValue;
            usedFormat = lenientFormat;
            return true;
        }

        value = default;
        usedFormat = formats[0];
        return false;
    }

    /// <summary>Parst einen Wert in genau einem Format.</summary>
    public static bool TryParseExact(string value, string format, out DateTime parsed)
        => DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    /// <summary>Formatiert einen Wert im angegebenen Format.</summary>
    public static string Format(DateTime value, string usedFormat)
        => value.ToString(usedFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Legt das Format einer ganzen Spalte fest, statt es je Wert zu raten
    /// (Befund A2). Ein Format kommt in Frage, wenn es jeden Wert der Spalte
    /// liest und unveraendert wiederherstellt; von mehreren gewinnt das erste
    /// der Liste.
    ///
    /// Lesen zwei solche Formate denselben Wert als verschiedene Tage (etwa
    /// 03/04/2024 unter "dd/MM/yyyy" und "MM/dd/yyyy"), ist die Spalte
    /// mehrdeutig, und es wird abgebrochen -- ein still falsches Datum ist
    /// schlimmer als kein Lauf. Passt kein Format auf alle Werte, liefert die
    /// Methode <c>null</c>; dann gilt wie bisher die Wahl je Wert.
    /// </summary>
    /// <param name="values">Die Werte der Spalte, leere Werte bereits entfernt.</param>
    /// <param name="formats">Die wirksame Formatliste des Generators.</param>
    /// <param name="generatorName">Fuer die Meldung.</param>
    /// <param name="fieldName">Fuer die Meldung.</param>
    /// <param name="candidates">Alle Formate, die auf die ganze Spalte passen.</param>
    public static string? DetermineColumnFormat(
        IReadOnlyCollection<string> values, string[] formats, string generatorName, string fieldName,
        out IReadOnlyList<string> candidates)
    {
        var fitting = new List<string>();
        if (values.Count == 0)
        {
            candidates = fitting;
            return null;
        }

        foreach (var format in formats)
        {
            var all = true;
            foreach (var value in values)
            {
                var trimmed = value.Trim();
                if (!TryParseExact(trimmed, format, out var parsed) || Format(parsed, format) != trimmed)
                {
                    all = false;
                    break;
                }
            }

            if (all)
                fitting.Add(format);
        }

        candidates = fitting;
        if (fitting.Count == 0)
            return null;

        var chosen = fitting[0];
        foreach (var other in fitting.Skip(1))
        {
            foreach (var value in values)
            {
                TryParseExact(value, chosen, out var a);
                TryParseExact(value, other, out var b);
                if (a != b)
                {
                    throw new GenerationException(generatorName,
                        $"Die Datumswerte im Feld '{fieldName}' sind mehrdeutig: sie passen auf " +
                        $"'{chosen}' und auf '{other}' und ergeben dabei verschiedene Tage. Unter " +
                        $"generators.{generatorName}.formats genau das zutreffende Format angeben.");
                }
            }
        }

        return chosen;
    }
}

/// <summary>
/// Ein Datumsgenerator, dem sich das Format einer Spalte vorgeben laesst --
/// siehe <see cref="DateValues.DetermineColumnFormat"/>.
/// </summary>
public interface IDateFormatGenerator
{
    /// <summary>Die wirksame Formatliste.</summary>
    string[] Formats { get; }

    /// <summary>Wie <see cref="IPseudonymGenerator.Generate"/>, aber in genau diesem Format.</summary>
    string Generate(ReadOnlySpan<byte> seed, string original, string format);

    /// <summary>
    /// Wie <see cref="IPseudonymGenerator.TryInvert"/>, aber in genau diesem
    /// Format. Nur fuer Generatoren mit eigener Umkehrung von Bedeutung.
    /// </summary>
    bool TryInvert(string pseudonym, string format, out string original)
    {
        original = pseudonym;
        return false;
    }
}
