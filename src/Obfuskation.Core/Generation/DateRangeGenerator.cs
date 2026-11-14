using System.Globalization;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Zieht ein zufaelliges Datum aus einem Zeitraum. Anders als <c>dateShift</c>
/// erhaelt dieser Generator weder Reihenfolge noch Abstaende zwischen
/// Datumsangaben -- genau das ist der Zweck: wer ein einzelnes Datum des
/// Bestands kennt, kann kein anderes daraus zurueckrechnen.
///
/// Der Generator geht ueber die Ersetzungstabelle (kein <c>HasIntrinsicInverse</c>):
/// die Kollisionspruefung im <see cref="Mapping.Pseudonymizer"/> faengt den
/// Fall ab, dass ein zufaelliges Datum zufaellig mit einem echten Datum des
/// Bestands zusammenfaellt.
/// </summary>
public sealed class DateRangeGenerator : IPseudonymGenerator, IDateFormatGenerator, IExpandableGenerator
{
    private string[] _formats = DateValues.FallbackFormats;
    private DateTime? _from;
    private DateTime? _to;

    public string Name => "dateRange";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public string[] Formats => _formats;

    public void Configure(GeneratorSettings settings)
    {
        _formats = DateValues.CombineFormats(settings.Formats);

        // from/to sind zu diesem Zeitpunkt bereits durch ProfileValidator
        // geprueft (parsebar, from <= to) -- hier wird nur noch uebernommen.
        if (!string.IsNullOrWhiteSpace(settings.From) &&
            DateTime.TryParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var from))
        {
            _from = from;
        }

        if (!string.IsNullOrWhiteSpace(settings.To) &&
            DateTime.TryParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var to))
        {
            _to = to;
        }
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        if (!DateValues.TryParse(original, _formats, out var parsed, out var format))
            throw Unreadable();

        return Draw(seed, parsed, format, widenYears: 0);
    }

    public string Generate(ReadOnlySpan<byte> seed, string original, string format)
    {
        if (!DateValues.TryParseExact(original, format, out var parsed))
            throw Unreadable();

        return Draw(seed, parsed, format, widenYears: 0);
    }

    /// <summary>
    /// Ohne eigenen Zeitraum reicht das Kalenderjahr des Originals nur fuer
    /// 365 Tage; ein Bestand mit Buchungen ueber ein ganzes Jahr scheiterte
    /// daran (Befund B6). Ist das Jahr erschoepft, weicht der Generator
    /// schrittweise auf die Nachbarjahre aus. Mit eigenem Zeitraum gilt der
    /// ausdruecklich gesetzte Rahmen, und es bleibt beim Abbruch.
    /// </summary>
    public string? GenerateExpanded(ReadOnlySpan<byte> seed, string original, int attempt)
    {
        if (_from.HasValue && _to.HasValue)
            return null;

        if (!DateValues.TryParse(original, _formats, out var parsed, out var format))
            throw Unreadable();

        return Draw(seed, parsed, format, widenYears: 1 + attempt / 25);
    }

    private string Draw(ReadOnlySpan<byte> seed, DateTime parsed, string format, int widenYears)
    {
        DateTime from;
        DateTime to;
        if (_from.HasValue && _to.HasValue)
        {
            from = _from.Value;
            to = _to.Value;
        }
        else
        {
            // Ohne eigenen Zeitraum bleibt das Kalenderjahr des Originals
            // erhalten: die Altersstruktur des Bestands bleibt auswertbar,
            // das exakte Datum ist weg -- ohne einen willkuerlichen
            // Vorgabezeitraum zu brauchen.
            from = new DateTime(Math.Max(1, parsed.Year - widenYears), 1, 1);
            to = new DateTime(Math.Min(9999, parsed.Year + widenYears), 12, 31);
        }

        var reader = new SeedReader(seed);
        var totalDays = (to.Date - from.Date).Days + 1;
        var drawn = from.Date.AddDays(reader.NextInt(totalDays));

        // Ein Zeitanteil im Original bleibt unveraendert stehen.
        drawn = drawn.Add(parsed.TimeOfDay);

        return DateValues.Format(drawn, format);
    }

    private GenerationException Unreadable() => new(Name,
        "Wert ist mit keinem der konfigurierten Datumsformate lesbar. " +
        $"Format in generators.{Name}.formats ergänzen (amerikanische Daten etwa mit \"MM/dd/yyyy\") " +
        "oder einen anderen Generator wählen.");
}
