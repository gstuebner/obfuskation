using Obfuskation.Core.Generation;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Eine der einstellbaren Grundlagen im Generator-Dialog (Plan
/// docs/plan-ausdruck-generator.md, P2a). Anders als <see cref="GeneratorOption"/>
/// (alle eingebauten Generatoren, fuer die Generatorauswahl an einer Regel)
/// zeigt diese Liste nur die Arten, die hier tatsaechlich eigene
/// Einstellungen anbieten -- die uebrigen (etwa <c>numericId</c>) trennen nur
/// eine eigene Ersetzungstabelle und bleiben als solcher Namensraum weiterhin
/// per JSON erreichbar. Eine Auswahl ohne sichtbare Wirkung im Dialog wirkte
/// sonst wie ein blosses Kopieren eines bestehenden Generators.
/// </summary>
/// <param name="Name">Der Name, wie er in der Konfigurationsdatei steht.</param>
/// <param name="Title">Verstaendlicher Titel fuer die Auswahlliste.</param>
/// <param name="Description">Kurze deutsche Erklaerung, aus <see cref="GeneratorDescriptions"/>.</param>
/// <param name="Example">Ein Beispielwert zur Orientierung. Leer bei einer nicht einstellbaren Art.</param>
public sealed record GeneratorKindOption(string Name, string Title, string Description, string Example)
{
    /// <summary>Die einstellbaren Grundlagen, in fester Reihenfolge.</summary>
    public static IReadOnlyList<GeneratorKindOption> Configurable { get; } =
    [
        new("token", "Kennung mit Kennzeichnung", GeneratorDescriptions.For("token"), "FW~TOK_A1B2C3D4"),
        new("expression", "Ausdruck mit Tabellen", GeneratorDescriptions.For("expression"), "M-AB 123E"),
        new("pattern", "Zeichenmaske", GeneratorDescriptions.For("pattern"), "AB-1234"),
        new("wordlist", "Werteliste", GeneratorDescriptions.For("wordlist"), "Rot · Grün · Blau"),
        new("partialMask", "Teilmaskierung", GeneratorDescriptions.For("partialMask"), "****1234"),
        new("redact", "Platzhalter", GeneratorDescriptions.For("redact"), "***"),
        new("dateRange", "Datum aus Zeitraum", GeneratorDescriptions.For("dateRange"), "2021-03-14"),
        new("dateGeneralize", "Datum gerundet", GeneratorDescriptions.For("dateGeneralize"), "2024-01-01"),
    ];

    /// <summary>
    /// Fuer eine per JSON angelegte, hier nicht einstellbare Art (etwa
    /// <c>numericId</c>): ein Eintrag ohne eigene Einstellungen, Titel gleich
    /// dem Namen, ohne Beispiel -- damit das Bearbeiten eines solchen
    /// Generators nicht still auf <c>token</c> zurueckfaellt.
    /// </summary>
    public static GeneratorKindOption ForUnconfigurable(string name)
        => new(name, name, GeneratorDescriptions.For(name), "");

    public override string ToString() => Name;
}
