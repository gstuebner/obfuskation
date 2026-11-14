using System.Globalization;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Ein Wert liess sich nicht typgerecht ersetzen.
///
/// Die Meldung nennt den Wert selbst nicht (Befund B11): sie landet in
/// Statuszeilen und im Absturzprotokoll. Die Fundstelle traegt der
/// Transformer ueber <see cref="WithLocation"/> nach.
/// </summary>
public sealed class GenerationException : Exception
{
    public GenerationException(string generatorName, string message)
        : base(message)
        => GeneratorName = generatorName;

    private GenerationException(string generatorName, string message, string location)
        : base($"{location}: {message}")
    {
        GeneratorName = generatorName;
        Location = location;
    }

    public string GeneratorName { get; }

    /// <summary>Fundstelle, etwa <c>Zeile 3, Spalte Geburtstag</c>, sobald bekannt.</summary>
    public string? Location { get; }

    /// <summary>Dieselbe Ausnahme mit Fundstelle; eine schon gesetzte bleibt stehen.</summary>
    public GenerationException WithLocation(string location)
        => Location is null ? new GenerationException(GeneratorName, Message, location) : this;
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
///
/// Platzhalterdaten wie 31.12.9999 ("gueltig bis auf Weiteres") bleiben
/// unveraendert, in beide Richtungen (Befund B3): verschoben stuerzten sie an
/// der Kalendergrenze ab, und ein verschobener Platzhalter verriete den
/// profilweiten Versatz.
/// </summary>
public sealed class DateShiftGenerator : IPseudonymGenerator, IDateFormatGenerator
{
    /// <summary>Vorgabe fuer <see cref="GeneratorSettings.KeepDates"/>.</summary>
    public static readonly string[] DefaultKeepDates = ["0001-01-01", "1900-01-01", "9999-12-31"];

    private string[] _formats = DateValues.FallbackFormats;
    private int _maxDays = GeneratorSettings.DefaultMaxDays;
    private int _offsetDays;
    private HashSet<DateTime> _keepDates = ParseKeepDates(DefaultKeepDates);

    public string Name => "dateShift";
    public bool IsReversible => true;
    public bool IsWordLike => false;
    public bool HasIntrinsicInverse => true;

    public string[] Formats => _formats;

    /// <summary>Die tatsaechlich angewandte Verschiebung in Tagen.</summary>
    public int OffsetDays => _offsetDays;

    public void Configure(GeneratorSettings settings)
    {
        if (settings.MaxDays > 0)
            _maxDays = settings.MaxDays;

        _formats = DateValues.CombineFormats(settings.Formats);

        if (settings.KeepDates is not null)
            _keepDates = ParseKeepDates(settings.KeepDates);
    }

    private static HashSet<DateTime> ParseKeepDates(IEnumerable<string> dates)
    {
        var result = new HashSet<DateTime>();
        foreach (var date in dates)
        {
            // Der ProfileValidator hat die Eintraege bereits geprueft.
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                result.Add(parsed.Date);
        }
        return result;
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

    /// <summary>
    /// Setzt die Verschiebung unmittelbar: der in der Ersetzungstabelle
    /// festgehaltene Versatz geht dem abgeleiteten vor (Befund C3).
    /// </summary>
    public void SetOffsetDays(int days) => _offsetDays = days;

    /// <summary>
    /// Ob in diesem Lauf ein Datum verschoben wurde. Nur dann haelt der Lauf
    /// den Versatz in der Tabelle fest -- ein bloss angelegter Namensraum soll
    /// seinen Versatz nicht vor der ersten Benutzung einfrieren.
    /// </summary>
    public bool WasUsed { get; private set; }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        if (!DateValues.TryParse(original, _formats, out var parsed, out var format))
            throw Unreadable();

        WasUsed = true;
        return Shift(parsed, format, _offsetDays);
    }

    public string Generate(ReadOnlySpan<byte> seed, string original, string format)
    {
        if (!DateValues.TryParseExact(original, format, out var parsed))
            throw Unreadable();

        WasUsed = true;
        return Shift(parsed, format, _offsetDays);
    }

    public bool TryInvert(string pseudonym, out string original)
    {
        if (DateValues.TryParse(pseudonym, _formats, out var value, out var format))
        {
            original = Shift(value, format, -_offsetDays);
            return true;
        }

        original = pseudonym;
        return false;
    }

    public bool TryInvert(string pseudonym, string format, out string original)
    {
        if (DateValues.TryParseExact(pseudonym, format, out var value))
        {
            original = Shift(value, format, -_offsetDays);
            return true;
        }

        original = pseudonym;
        return false;
    }

    private string Shift(DateTime value, string format, int days)
    {
        if (_keepDates.Contains(value.Date))
            return DateValues.Format(value, format);

        var ticks = value.Ticks + TimeSpan.TicksPerDay * (long)days;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            throw new GenerationException(Name,
                "Das verschobene Datum läge außerhalb des Kalenders (vor dem Jahr 1 oder nach 9999). " +
                $"Handelt es sich um einen Platzhalter, ihn unter generators.{Name}.keepDates " +
                "(Format yyyy-MM-dd) eintragen.");
        }

        var shifted = new DateTime(ticks, value.Kind);

        // Ein verschobener Wert, der auf einen Platzhalter faellt, liesse sich
        // nicht zurueckrechnen: die Umkehrung liesse ihn unveraendert.
        if (_keepDates.Contains(shifted.Date))
        {
            throw new GenerationException(Name,
                "Das verschobene Datum fiele auf einen unter keepDates geführten Platzhalter und " +
                "ließe sich nicht zurückrechnen. Den Platzhalter aus keepDates entfernen oder " +
                "einen anderen Generator wählen.");
        }

        return DateValues.Format(shifted, format);
    }

    private GenerationException Unreadable() => new(Name,
        "Wert ist mit keinem der konfigurierten Datumsformate lesbar. " +
        $"Format in generators.{Name}.formats ergänzen (amerikanische Daten etwa mit \"MM/dd/yyyy\") " +
        "oder einen anderen Generator wählen.");
}
