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
        new DateRangeGenerator(),
        new DateGeneralizeGenerator(),
        new PatternGenerator(),
        new WordlistGenerator(),
        new PartialMaskGenerator(),
    ];

    /// <summary>
    /// Baut die Generatoren fuer ein Profil auf und wendet die Einstellungen an.
    /// Reihenfolge bei gleichem Schluessel: eingebaute Generatoren, dann die
    /// Erweiterungsdatei, dann das Profil — das Profil gewinnt immer.
    /// </summary>
    public static GeneratorRegistry Build(Profile profile, SeedDeriver deriver, ExtensionLibrary? extensions = null)
    {
        extensions ??= ExtensionLibrary.Load();

        var byName = new Dictionary<string, IPseudonymGenerator>(StringComparer.OrdinalIgnoreCase);
        foreach (var generator in CreateDefaults())
            byName[generator.Name] = generator;

        // Erweiterungseintraege zuerst, das Profil ueberschreibt bei gleichem
        // Schluessel — siehe ExtensionLibrary. "Origin" fliesst nur in die
        // Fehlermeldung ein, die Zusammenfuehrung selbst braucht sie nicht.
        var effective = new Dictionary<string, (GeneratorSettings Settings, string Origin)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (key, settings) in extensions.Generators)
            effective[key] = (settings, $"Erweiterungsdatei: {key}");

        foreach (var (key, settings) in profile.Generators)
            effective[key] = (settings, $"generators.{key}");

        // Ein Eintrag unter neuem Schluessel mit "type" erzeugt eine zweite,
        // eigenstaendig konfigurierte Auspraegung eines eingebauten Generators.
        foreach (var (key, entry) in effective)
        {
            var baseName = string.IsNullOrWhiteSpace(entry.Settings.Type) ? key : entry.Settings.Type;

            if (!byName.ContainsKey(baseName))
                throw new ConfigurationException(
                    $"Unbekannter Generatortyp '{baseName}' in {entry.Origin}. " +
                    $"Verfügbar: {string.Join(", ", KnownNames)}");

            var instance = CreateDefaults().First(
                g => string.Equals(g.Name, baseName, StringComparison.OrdinalIgnoreCase));
            instance.Configure(entry.Settings);
            byName[key] = instance;
        }

        // Der Platzhalter von "redact" kommt aus den Profilvorgaben — ausser
        // der Namensraum hat unter generators.<key>.placeholder (oder dem
        // gleichwertigen Erweiterungseintrag) einen eigenen gesetzt; der bleibt
        // dann unangetastet stehen, statt gleich wieder ueberschrieben zu werden.
        foreach (var (key, generator) in byName)
        {
            if (generator is not RedactGenerator redactor)
                continue;

            var hasOwnPlaceholder = effective.TryGetValue(key, out var entry)
                && !string.IsNullOrEmpty(entry.Settings.Placeholder);

            if (!hasOwnPlaceholder)
                redactor.SetPlaceholder(profile.Defaults.RedactionPlaceholder);
        }

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
