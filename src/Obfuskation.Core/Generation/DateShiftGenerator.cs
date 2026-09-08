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
    private string[] _formats = DateValues.FallbackFormats;
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

        _formats = DateValues.CombineFormats(settings.Formats);
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
        if (DateValues.TryParse(pseudonym, _formats, out var value, out var format))
        {
            original = DateValues.Format(value.AddDays(-_offsetDays), format);
            return true;
        }

        original = pseudonym;
        return false;
    }

    private string Shift(string value, int days)
    {
        if (!DateValues.TryParse(value, _formats, out var parsed, out var format))
            throw new GenerationException(Name,
                $"Wert ist mit keinem der konfigurierten Datumsformate lesbar: '{value}'. " +
                "Format in generators.dateShift.formats ergänzen oder einen anderen Generator wählen.");

        return DateValues.Format(parsed.AddDays(days), format);
    }
}
