using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Mapping;

/// <summary>
/// Bildet Klartexte auf Pseudonyme ab und wieder zurueck. Hier laufen
/// Seed-Ableitung, Generator und Ersetzungstabelle zusammen.
/// </summary>
public sealed class Pseudonymizer
{
    /// <summary>
    /// Obergrenze fuer Ausweichversuche bei Kollisionen. Wird sie erreicht, ist
    /// der Wertevorrat des Generators fuer diesen Bestand zu klein.
    /// </summary>
    private const int MaxCollisionRetries = 100;

    private readonly SeedDeriver _deriver;
    private readonly GeneratorRegistry _generators;
    private readonly MappingStore _store;

    public Pseudonymizer(SeedDeriver deriver, GeneratorRegistry generators, MappingStore store)
    {
        _deriver = deriver;
        _generators = generators;
        _store = store;
    }

    /// <summary>
    /// Liefert das Pseudonym zu einem Klartext und legt es bei Bedarf an.
    /// Derselbe Klartext ergibt im selben Profil stets dasselbe Pseudonym.
    /// </summary>
    /// <param name="generatorName">Name des Generators, zugleich der Namensraum.</param>
    /// <param name="plaintext">Der zu ersetzende Wert.</param>
    /// <param name="persist">
    /// Ob die Zuordnung gespeichert werden soll. Bei einem Probelauf steht hier
    /// <c>false</c>, damit der Bestand unveraendert bleibt.
    /// </param>
    public string Pseudonymize(string generatorName, string plaintext, bool persist = true)
    {
        var generator = _generators.Get(generatorName);

        // Nicht umkehrbare Generatoren erzeugen keinen Tabelleneintrag: ein
        // fester Platzhalter liesse sich ohnehin nicht zurueckfuehren.
        if (!generator.IsReversible)
            return generator.Generate(_deriver.Derive(generatorName, plaintext, 0), plaintext);

        // Generatoren mit eigenem Umkehrweg (die Datumsverschiebung) bekommen
        // bewusst keinen Eintrag: ein verschobenes Datum kann mit einem echten
        // Datum desselben Bestands zusammenfallen, und der Eintrag waere dann
        // mehrdeutig. Zurueckgerechnet wird stattdessen ueber den Generator.
        if (generator.HasIntrinsicInverse)
            return generator.Generate(_deriver.Derive(generatorName, plaintext, 0), plaintext);

        if (_store.TryGetPseudonym(generatorName, plaintext, out var existing))
            return existing;

        for (var counter = 0; counter < MaxCollisionRetries; counter++)
        {
            var seed = _deriver.Derive(generatorName, plaintext, counter);
            var candidate = generator.Generate(seed, plaintext);

            // Zwei Bedingungen muessen gelten, damit die Rueckabbildung eindeutig
            // bleibt: das Pseudonym darf noch nicht vergeben sein, und es darf
            // nicht selbst als Klartext im Bestand vorkommen.
            //
            // Die zweite Bedingung greift naturgemaess nur fuer das, was zu
            // diesem Zeitpunkt bekannt ist. Ein spaeter hinzukommender Klartext
            // kann mit einem laengst vergebenen Pseudonym zusammenfallen —
            // unvermeidlich, sobald der Generator sein Format erhaelt und damit
            // aus demselben Wertevorrat schoepft wie die Echtdaten. Die
            // Rueckabbildung bleibt trotzdem eindeutig: in der Ausgabe steht an
            // dieser Stelle das Pseudonym, waehrend der gleichlautende Klartext
            // seinerseits durch sein eigenes Pseudonym ersetzt wurde.
            if (_store.IsPseudonymTaken(generatorName, candidate))
                continue;
            if (_store.IsPlaintextKnown(generatorName, candidate))
                continue;
            if (string.Equals(candidate, plaintext, StringComparison.Ordinal))
                continue;

            if (persist)
                _store.Add(generatorName, plaintext, candidate);

            return candidate;
        }

        throw new MappingConflictException(
            $"Der Generator '{generatorName}' fand nach {MaxCollisionRetries} Versuchen kein freies " +
            "Pseudonym. Der Wertevorrat ist für diesen Datenbestand zu klein — " +
            RemedyFor(generator.Name));
    }

    /// <summary>
    /// Der Abhilfesatz zur <see cref="MappingConflictException"/>. Generatoren
    /// mit einstellbarem Wertevorrat brauchen einen anderen Rat als ein
    /// pauschales "nimm token": bei ihnen liegt die Ursache in der eigenen
    /// Einstellung, und die laesst sich gezielt aufweiten.
    /// </summary>
    private static string RemedyFor(string baseName) => baseName switch
    {
        "wordlist" => "mehr Werte unter 'values' eintragen.",
        "pattern" => "eine längere Maske unter 'pattern' wählen.",
        "dateRange" => "einen weiteren Zeitraum über 'from' und 'to' setzen; ohne Angabe steht " +
                       "nur das Kalenderjahr des Originals zur Verfügung.",
        _ => "einen Generator mit größerem Vorrat wählen (etwa 'token').",
    };

    /// <summary>
    /// Ob der Generator seine Werte ueberhaupt zurueckfuehren kann. Bei
    /// <c>false</c> ist ein nicht gefundener Wert kein Hinweis auf einen
    /// fremden Bestand, sondern die Bauart des Generators.
    /// </summary>
    public bool IsReversible(string generatorName) => _generators.Get(generatorName).IsReversible;

    /// <summary>
    /// Fuehrt ein Pseudonym auf den Klartext zurueck. Liefert <c>false</c>, wenn
    /// der Wert unbekannt ist.
    /// </summary>
    public bool TryReverse(string generatorName, string pseudonym, out string plaintext)
    {
        var generator = _generators.Get(generatorName);

        if (generator.HasIntrinsicInverse)
            return generator.TryInvert(pseudonym, out plaintext);

        return _store.TryGetPlaintext(generatorName, pseudonym, out plaintext);
    }
}
