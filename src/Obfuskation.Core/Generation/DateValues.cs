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
    /// <summary>Die zehn Standardformate, gegen die ohne eigene Vorgabe geprueft wird.</summary>
    public static readonly string[] FallbackFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm",
        "dd.MM.yy", "yyyyMMdd",
    ];

    /// <summary>
    /// Eigene Formate zuerst, die Standardformate als Auffangnetz danach. Ohne
    /// eigene Formate gelten allein die Standardformate.
    /// </summary>
    public static string[] CombineFormats(List<string>? own)
        => own is { Count: > 0 } ? own.Concat(FallbackFormats).Distinct().ToArray() : FallbackFormats;

    /// <summary>
    /// Versucht, einen Wert gegen die gegebenen Formate zu parsen. Das erste
    /// passende Format gewinnt und wird ueber <paramref name="usedFormat"/>
    /// zurueckgegeben, damit dieselbe Schreibweise beim Formatieren wieder
    /// entsteht.
    /// </summary>
    public static bool TryParse(string original, string[] formats, out DateTime value, out string usedFormat)
    {
        var trimmed = original.Trim();
        foreach (var candidate in formats)
        {
            if (DateTime.TryParseExact(trimmed, candidate, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out value))
            {
                usedFormat = candidate;
                return true;
            }
        }

        value = default;
        usedFormat = formats[0];
        return false;
    }

    /// <summary>Formatiert einen Wert im angegebenen Format.</summary>
    public static string Format(DateTime value, string usedFormat)
        => value.ToString(usedFormat, CultureInfo.InvariantCulture);
}
