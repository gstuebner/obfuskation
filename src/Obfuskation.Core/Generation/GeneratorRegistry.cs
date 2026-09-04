using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Haelt die Generatoren eines Laufs. Sie sind bereits konfiguriert, tragen aber
/// Zustand (etwa den Datums-Offset) und duerfen deshalb nicht zwischen Profilen
/// geteilt werden.
/// </summary>
public sealed class GeneratorRegistry
{
    private readonly Dictionary<string, IPseudonymGenerator> _generators;

    private GeneratorRegistry(Dictionary<string, IPseudonymGenerator> generators)
        => _generators = generators;

    /// <summary>Namen aller eingebauten Generatoren, fuer Pruefung und Hilfetexte.</summary>
    public static IReadOnlyList<string> KnownNames { get; } =
        CreateDefaults().Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static List<IPseudonymGenerator> CreateDefaults() =>
    [
        new TokenGenerator(),
        new RedactGenerator(),
        new PersonNameGenerator(),
        new FirstNameGenerator(),
        new LastNameGenerator(),
        new CompanyNameGenerator(),
        new StreetGenerator(),
        new CityGenerator(),
        new PostalCodeGenerator(),
        new NumericIdGenerator(),
        new PhoneGenerator(),
        new EmailGenerator(),
        new IbanGenerator(),
        new BicGenerator(),
        new DateShiftGenerator(),
    ];

    /// <summary>
    /// Baut die Generatoren fuer ein Profil auf und wendet die Einstellungen an.
    /// </summary>
    public static GeneratorRegistry Build(Profile profile, SeedDeriver deriver)
    {
        var byName = new Dictionary<string, IPseudonymGenerator>(StringComparer.OrdinalIgnoreCase);
        foreach (var generator in CreateDefaults())
            byName[generator.Name] = generator;

        // Ein Eintrag unter neuem Schluessel mit "type" erzeugt eine zweite,
        // eigenstaendig konfigurierte Auspraegung eines eingebauten Generators.
        foreach (var (key, settings) in profile.Generators)
        {
            var baseName = string.IsNullOrWhiteSpace(settings.Type) ? key : settings.Type;

            if (!byName.TryGetValue(baseName, out _))
                throw new ConfigurationException(
                    $"Unbekannter Generatortyp '{baseName}' in generators.{key}. " +
                    $"Verfügbar: {string.Join(", ", KnownNames)}");

            var instance = CreateDefaults().First(
                g => string.Equals(g.Name, baseName, StringComparison.OrdinalIgnoreCase));
            instance.Configure(settings);
            byName[key] = instance;
        }

        // Der Platzhalter von "redact" kommt aus den Profilvorgaben.
        foreach (var redactor in byName.Values.OfType<RedactGenerator>())
            redactor.SetPlaceholder(profile.Defaults.RedactionPlaceholder);

        // Die Datumsverschiebung wird aus dem Salt abgeleitet und bleibt damit
        // ueber alle Laeufe desselben Profils konstant.
        foreach (var dateShift in byName.Values.OfType<DateShiftGenerator>())
            dateShift.SetOffsetFrom(deriver);

        return new GeneratorRegistry(byName);
    }

    public bool Contains(string name) => _generators.ContainsKey(name);

    public IPseudonymGenerator Get(string name)
        => _generators.TryGetValue(name, out var generator)
            ? generator
            : throw new ConfigurationException(
                $"Unbekannter Generator '{name}'. Verfügbar: {string.Join(", ", _generators.Keys.Order())}");

    public IEnumerable<KeyValuePair<string, IPseudonymGenerator>> All => _generators;
}
