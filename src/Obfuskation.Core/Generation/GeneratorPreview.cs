using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Baut aus einem einzelnen Generator-Eintrag ein Beispielergebnis -- fuer den
/// Generator-Dialog (Plan P3), der einen Entwurf zeigen soll, bevor er
/// tatsaechlich irgendwo gespeichert wird.
///
/// Dazu entsteht ein Wegwerf-Profil, das nur diesen einen Generator kennt,
/// und darauf <see cref="GeneratorRegistry.Build"/> genau wie bei einem echten
/// Lauf -- derselbe Weg wie <see cref="ObfuscationEngine"/>, damit die Vorschau
/// nie etwas anderes zeigt als der Lauf selbst zeigen wuerde.
/// </summary>
public static class GeneratorPreview
{
    /// <summary>
    /// Liefert <c>true</c> und <paramref name="example"/>, wenn sich aus
    /// <paramref name="settings"/> ein Beispielwert erzeugen liess -- sonst
    /// <c>false</c> und <paramref name="error"/> mit der Meldung von
    /// <see cref="ConfigurationException"/> (etwa ein unbekannter Typ) oder
    /// <see cref="GenerationException"/> (etwa eine leere Werteliste bei
    /// <c>wordlist</c>).
    /// </summary>
    /// <param name="key">Der Name, unter dem der Entwurf im Wegwerf-Profil steht.</param>
    /// <param name="settings">Der zu erprobende Entwurf.</param>
    /// <param name="sample">Der Beispielwert, gegen den erzeugt wird.</param>
    /// <param name="deriver">
    /// Vom Aufrufer gehalten (ein <see cref="SeedDeriver"/> je Dialog), damit
    /// das Beispiel bei jedem Tastenschlag denselben Seed nutzt und nicht bei
    /// jeder Aenderung einen anderen Wert zeigt.
    /// </param>
    public static bool TryExample(
        string key, GeneratorSettings settings, string sample, SeedDeriver deriver,
        out string example, out string? error)
    {
        if (TryExamples(key, settings, sample, deriver, count: 1, out var examples, out error))
        {
            example = examples.Count > 0 ? examples[0] : "";
            return true;
        }

        example = "";
        return false;
    }

    /// <summary>
    /// Wie <see cref="TryExample"/>, aber mit bis zu <paramref name="count"/>
    /// Beispielen -- fuer eine Vorschau, die mehrere verschiedene Werte auf
    /// einmal zeigt (Plan P1d). Jeder Versuch <c>0..count-1</c> nutzt
    /// <see cref="SeedDeriver.Derive"/> mit einem eigenen Zaehler, wie es
    /// <see cref="Mapping.Pseudonymizer"/> bei einer Kollision auch tut.
    /// Liefert nur verschiedene Werte zurueck -- ein Generator mit kleinem
    /// Wertevorrat (etwa <c>redact</c>) zeigt dann folgerichtig weniger als
    /// <paramref name="count"/> Beispiele.
    /// </summary>
    public static bool TryExamples(
        string key, GeneratorSettings settings, string sample, SeedDeriver deriver, int count,
        out IReadOnlyList<string> examples, out string? error)
    {
        var profile = new Profile();
        profile.Generators[key] = settings;

        try
        {
            var registry = GeneratorRegistry.Build(profile, deriver, ExtensionLibrary.Empty);
            var generator = registry.Get(key);

            var distinct = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var attempt = 0; attempt < count; attempt++)
            {
                var value = generator.Generate(deriver.Derive(key, sample, attempt), sample);
                if (seen.Add(value))
                    distinct.Add(value);
            }

            examples = distinct;
            error = null;
            return true;
        }
        catch (ConfigurationException ex)
        {
            examples = Array.Empty<string>();
            error = ex.Message;
            return false;
        }
        catch (GenerationException ex)
        {
            examples = Array.Empty<string>();
            error = ex.Message;
            return false;
        }
    }
}
