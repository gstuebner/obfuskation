using System.Globalization;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>Ein Wert liess sich nicht typgerecht ersetzen.</summary>
public sealed class GenerationException : Exception
{
    public GenerationException(string generatorName, string message)
        : base(message)
        => GeneratorName = generatorName;

    public string GeneratorName { get; }
}

/// <summary>
/// Verschiebt alle Datumswerte um denselben, profilweit konstanten Betrag.
/// Reihenfolge und Abstaende zwischen Datumsangaben bleiben damit erhalten,
/// was Auswertungen ueber Zeitraeume auf den Testdaten weiterhin sinnvoll macht.
///
/// Die Umkehrung erfolgt durch Zurueckrechnen und braucht deshalb keinen Eintrag
/// in der Mapping-Tabelle. Sie darf auch keinen bekommen: ein verschobenes Datum
/// kann zufaellig mit einem echten Datum aus demselben Bestand zusammenfallen,
/// und ein Tabelleneintrag waere dann mehrdeutig.
/// </summary>
public sealed class DateShiftGenerator : IPseudonymGenerator
{
    private static readonly string[] FallbackFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm",
        "dd.MM.yy", "yyyyMMdd",
    ];

    private string[] _formats = FallbackFormats;
    private int _maxDays = 400;
    private int _offsetDays;

    public string Name => "dateShift";
    public bool IsReversible => true;
    public bool IsWordLike => false;
    public bool HasIntrinsicInverse => true;

    /// <summary>Die tatsaechlich angewandte Verschiebung in Tagen.</summary>
    public int OffsetDays => _offsetDays;

    public void Configure(GeneratorSettings settings)
    {
        if (settings.MaxDays > 0)
            _maxDays = settings.MaxDays;

        if (settings.Formats is { Count: > 0 })
        {
            // Die eigenen Formate zuerst pruefen, die Standardformate als Auffangnetz.
            _formats = settings.Formats.Concat(FallbackFormats).Distinct().ToArray();
        }
    }

    /// <summary>
    /// Setzt die profilweite Verschiebung. Wird vom Registry-Aufbau aus dem
    /// Salt abgeleitet, damit sie ueber alle Laeufe hinweg gleich bleibt.
    /// </summary>
    public void SetOffsetFrom(SeedDeriver deriver)
    {
        var constant = deriver.DeriveProfileConstant("dateShift");
        var reader = new SeedReader(constant);

        // Gleichverteilt in [-maxDays, +maxDays], aber niemals null: eine
        // Verschiebung um null Tage waere gar keine Verschiebung.
        var magnitude = reader.NextInt(_maxDays) + 1;
        _offsetDays = reader.NextInt(2) == 0 ? -magnitude : magnitude;
    }

    /// <summary>Nur fuer Tests: setzt die Verschiebung unmittelbar.</summary>
    public void SetOffsetDays(int days) => _offsetDays = days;

    public string Generate(ReadOnlySpan<byte> seed, string original) => Shift(original, _offsetDays);

    public bool TryInvert(string pseudonym, out string original)
    {
        if (TryParse(pseudonym, out var value, out var format))
        {
            original = value.AddDays(-_offsetDays).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }

        original = pseudonym;
        return false;
    }

    private string Shift(string value, int days)
    {
        if (!TryParse(value, out var parsed, out var format))
            throw new GenerationException(Name,
                $"Wert ist mit keinem der konfigurierten Datumsformate lesbar: '{value}'. " +
                "Format in generators.dateShift.formats ergänzen oder einen anderen Generator wählen.");

        return parsed.AddDays(days).ToString(format, CultureInfo.InvariantCulture);
    }

    private bool TryParse(string value, out DateTime result, out string format)
    {
        var trimmed = value.Trim();
        foreach (var candidate in _formats)
        {
            if (DateTime.TryParseExact(trimmed, candidate, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out result))
            {
                format = candidate;
                return true;
            }
        }

        result = default;
        format = _formats[0];
        return false;
    }
}
