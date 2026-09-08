using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Rundet ein Datum auf den Anfang von Monat, Quartal oder Jahr (Vorgabe
/// Monat). Reine Funktion des Eingabewerts, der Seed wird nicht gebraucht.
///
/// Nicht umkehrbar, weil viele-zu-eins: aus dem 1. eines Monats laesst sich
/// das urspruengliche Tagesdatum nicht mehr rekonstruieren -- wie bei
/// <c>redact</c> bekommt dieser Generator deshalb keinen Eintrag in der
/// Ersetzungstabelle.
/// </summary>
public sealed class DateGeneralizeGenerator : IPseudonymGenerator
{
    private static readonly string[] AllowedGranularities = ["month", "quarter", "year"];

    private string[] _formats = DateValues.FallbackFormats;
    private string _granularity = "month";

    public string Name => "dateGeneralize";
    public bool IsReversible => false;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        _formats = DateValues.CombineFormats(settings.Formats);

        // Der ProfileValidator hat die Granularitaet bereits geprueft; eine
        // unbekannte Angabe hier waere ein Fehler im Aufrufer, nicht im Wert,
        // und faellt deshalb defensiv auf die Vorgabe zurueck statt zu werfen.
        if (!string.IsNullOrWhiteSpace(settings.Granularity) &&
            AllowedGranularities.Contains(settings.Granularity, StringComparer.OrdinalIgnoreCase))
        {
            _granularity = settings.Granularity.ToLowerInvariant();
        }
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        if (!DateValues.TryParse(original, _formats, out var parsed, out var format))
            throw new GenerationException(Name,
                $"Wert ist mit keinem der konfigurierten Datumsformate lesbar: '{original}'. " +
                "Format in generators.dateGeneralize.formats ergänzen oder einen anderen Generator wählen.");

        // Der Zeitanteil entfaellt: die Rundung auf Monats-, Quartals- oder
        // Jahresanfang macht ihn ohnehin bedeutungslos.
        var rounded = _granularity switch
        {
            "year" => new DateTime(parsed.Year, 1, 1),
            "quarter" => new DateTime(parsed.Year, ((parsed.Month - 1) / 3 * 3) + 1, 1),
            _ => new DateTime(parsed.Year, parsed.Month, 1), // "month", auch die Vorgabe
        };

        return DateValues.Format(rounded, format);
    }
}
