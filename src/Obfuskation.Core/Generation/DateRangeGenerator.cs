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
public sealed class DateRangeGenerator : IPseudonymGenerator
{
    private string[] _formats = DateValues.FallbackFormats;
    private DateTime? _from;
    private DateTime? _to;

    public string Name => "dateRange";
    public bool IsReversible => true;
    public bool IsWordLike => false;

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
            throw new GenerationException(Name,
                $"Wert ist mit keinem der konfigurierten Datumsformate lesbar: '{original}'. " +
                "Format in generators.dateRange.formats ergänzen oder einen anderen Generator wählen.");

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
            from = new DateTime(parsed.Year, 1, 1);
            to = new DateTime(parsed.Year, 12, 31);
        }

        var reader = new SeedReader(seed);
        var totalDays = (to.Date - from.Date).Days + 1;
        var drawn = from.Date.AddDays(reader.NextInt(totalDays));

        // Ein Zeitanteil im Original bleibt unveraendert stehen.
        drawn = drawn.Add(parsed.TimeOfDay);

        return DateValues.Format(drawn, format);
    }
}
