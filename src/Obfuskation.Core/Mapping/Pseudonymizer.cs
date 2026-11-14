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

    /// <summary>
    /// Weitere Versuche mit erweitertem Vorrat (<see cref="IExpandableGenerator"/>),
    /// erst nach den gewoehnlichen. Ihre Zaehler liegen hinter
    /// <see cref="MaxCollisionRetries"/>, die bisherigen Seeds bleiben damit
    /// unberuehrt.
    /// </summary>
    private const int MaxExpandedRetries = 400;

    /// <summary>
    /// Liefert ein Generator so oft hintereinander den Klartext selbst, kann er
    /// mit diesem Wert nichts anfangen (Befund B5).
    /// </summary>
    private const int UnchangedLimit = 10;

    private readonly SeedDeriver _deriver;
    private readonly GeneratorRegistry _generators;
    private readonly MappingStore _store;
    private readonly Func<string, bool> _isEffectivelyEmpty;

    /// <summary>
    /// Zuordnungen, die ein Probelauf (<c>persist: false</c>) vergeben hat.
    /// Ohne diese Merkliste koennten zwei Klartexte in der Vorschau dasselbe
    /// Pseudonym zeigen, das sie im echten Lauf nie bekaemen (Befund B11).
    /// </summary>
    private readonly Dictionary<string, (Dictionary<string, string> Forward, HashSet<string> Taken)> _transient =
        new(StringComparer.Ordinal);

    /// <param name="isEffectivelyEmpty">
    /// Welche Werte als leer gelten (<c>defaults.emptyValues</c>). Ein solcher
    /// Wert wird nie als Pseudonym vergeben: die Rueckuebersetzung liesse ihn
    /// stehen, statt ihn zurueckzufuehren.
    /// </param>
    public Pseudonymizer(SeedDeriver deriver, GeneratorRegistry generators, MappingStore store,
        Func<string, bool>? isEffectivelyEmpty = null)
    {
        _deriver = deriver;
        _generators = generators;
        _store = store;
        _isEffectivelyEmpty = isEffectivelyEmpty ?? string.IsNullOrWhiteSpace;
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
    /// <param name="dateFormat">
    /// Das fuer die ganze Spalte festgelegte Datumsformat, falls bekannt
    /// (<see cref="DateValues.DetermineColumnFormat"/>).
    /// </param>
    /// <param name="preferred">
    /// Eine bevorzugte Form fuer ein neues Pseudonym, etwa "gueltige
    /// JSON-Zahl" (Befund A3). Nur eine Bevorzugung: erfuellt in den ersten
    /// <see cref="PreferredWindow"/> Versuchen kein freies Pseudonym sie, gilt
    /// das erste freie. Ein schon vergebenes Pseudonym bleibt, wie es ist.
    /// </param>
    public string Pseudonymize(string generatorName, string plaintext, bool persist = true, string? dateFormat = null,
        Func<string, bool>? preferred = null)
    {
        // Leerraum am Rand gehoert nicht zum Wert: " Müller" und "Müller"
        // bekommen dasselbe Pseudonym, der Rand bleibt in der Ausgabe stehen
        // (Befund C6). Ein Eintrag aus der Zeit davor, der den Rand noch im
        // Schluessel traegt, geht vor -- sonst aenderten sich bereits
        // vergebene Pseudonyme.
        var (leading, core, trailing) = SplitEdges(plaintext);
        if (core.Length != plaintext.Length && core.Length > 0)
        {
            var canonical = _generators.CanonicalName(generatorName);
            if (_store.TryGetPseudonym(canonical, plaintext, out var legacy))
                return legacy;

            return leading + PseudonymizeCore(generatorName, core, persist, dateFormat, preferred) + trailing;
        }

        return PseudonymizeCore(generatorName, plaintext, persist, dateFormat, preferred);
    }

    /// <summary>Teilt einen Wert in Leerraum vorn, Kern und Leerraum hinten.</summary>
    private static (string Leading, string Core, string Trailing) SplitEdges(string value)
    {
        var start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
            start++;

        var end = value.Length;
        while (end > start && char.IsWhiteSpace(value[end - 1]))
            end--;

        return (value[..start], value[start..end], value[end..]);
    }

    private string PseudonymizeCore(string generatorName, string plaintext, bool persist, string? dateFormat,
        Func<string, bool>? preferred)
    {
        var generator = _generators.Get(generatorName);

        // Namensraum und Seed haengen an der Schreibweise des Registers, nicht
        // an der aus der Regel (Befund A9).
        var requestedName = generatorName;
        generatorName = _generators.CanonicalName(generatorName);

        // Nicht umkehrbare Generatoren erzeugen keinen Tabelleneintrag: ein
        // fester Platzhalter liesse sich ohnehin nicht zurueckfuehren.
        // Generatoren mit eigenem Umkehrweg (die Datumsverschiebung) bekommen
        // bewusst ebenfalls keinen Eintrag: ein verschobenes Datum kann mit
        // einem echten Datum desselben Bestands zusammenfallen, und der Eintrag
        // waere dann mehrdeutig. Zurueckgerechnet wird ueber den Generator.
        if (!generator.IsReversible || generator.HasIntrinsicInverse)
            return Generate(generator, _deriver.Derive(generatorName, plaintext, 0), plaintext, dateFormat);

        if (_store.TryGetPseudonym(generatorName, plaintext, out var existing))
            return existing;

        // Eine Tabelle aus der Zeit vor 1.14.0 kann den Namensraum in der
        // Schreibweise der Regel fuehren; ihre Eintraege bleiben gueltig.
        if (!string.Equals(requestedName, generatorName, StringComparison.Ordinal)
            && _store.TryGetPseudonym(requestedName, plaintext, out existing))
            return existing;

        if (!persist && _transient.TryGetValue(generatorName, out var vorschau)
            && vorschau.Forward.TryGetValue(plaintext, out existing))
            return existing;

        var unchanged = 0;
        string? fallback = null;
        for (var counter = 0; counter < MaxCollisionRetries + MaxExpandedRetries; counter++)
        {
            if (fallback is not null && counter >= PreferredWindow)
                return Accept(generatorName, plaintext, fallback, persist);

            var seed = _deriver.Derive(generatorName, plaintext, counter);

            string? candidate;
            if (counter < MaxCollisionRetries)
            {
                candidate = Generate(generator, seed, plaintext, dateFormat);
            }
            else
            {
                // Erst wenn der einfache Vorrat nichts Freies mehr hergibt:
                // so bleibt jedes bisher vergebene Pseudonym, was es war.
                candidate = (generator as IExpandableGenerator)
                    ?.GenerateExpanded(seed, plaintext, counter - MaxCollisionRetries);
                if (candidate is null)
                    break;
            }

            if (string.Equals(candidate, plaintext, StringComparison.Ordinal))
            {
                if (++unchanged == UnchangedLimit && unchanged == counter + 1)
                {
                    throw new GenerationException(generatorName,
                        $"Der Wert passt nicht zum Generator '{generatorName}': er enthält nichts, was " +
                        "sich ersetzen ließe (etwa eine Telefonnummer ohne austauschbare Ziffern), " +
                        "das Pseudonym wäre der Klartext selbst. Den Wert unter defaults.emptyValues " +
                        "eintragen oder für dieses Feld einen anderen Generator wählen.");
                }
                continue;
            }

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

            // Ein Pseudonym, das als leer gilt, liesse die Rueckuebersetzung
            // stehen (Befund B11).
            if (_isEffectivelyEmpty(candidate))
                continue;

            if (!persist && _transient.TryGetValue(generatorName, out vorschau) && vorschau.Taken.Contains(candidate))
                continue;

            if (preferred is not null && !preferred(candidate))
            {
                fallback ??= candidate;
                continue;
            }

            return Accept(generatorName, plaintext, candidate, persist);
        }

        if (fallback is not null)
            return Accept(generatorName, plaintext, fallback, persist);

        var vergeben = _store.Entries(generatorName).Count
                       + (_transient.TryGetValue(generatorName, out vorschau) ? vorschau.Forward.Count : 0);

        throw new MappingConflictException(
            $"Der Generator '{generatorName}' fand kein freies Pseudonym mehr; in diesem Namensraum " +
            $"sind bereits {vergeben} Werte vergeben. Der Wertevorrat ist für diesen Datenbestand zu klein — " +
            RemedyFor(generator.Name));
    }

    /// <summary>Versuche, in denen eine bevorzugte Form gesucht wird.</summary>
    private const int PreferredWindow = 20;

    private string Accept(string generatorName, string plaintext, string candidate, bool persist)
    {
        if (persist)
        {
            _store.Add(generatorName, plaintext, candidate);
            return candidate;
        }

        if (!_transient.TryGetValue(generatorName, out var vorschau))
        {
            vorschau = (new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
            _transient[generatorName] = vorschau;
        }
        vorschau.Forward[plaintext] = candidate;
        vorschau.Taken.Add(candidate);
        return candidate;
    }

    private static string Generate(IPseudonymGenerator generator, byte[] seed, string plaintext, string? dateFormat)
        => dateFormat is not null && generator is IDateFormatGenerator dated
            ? dated.Generate(seed, plaintext, dateFormat)
            : generator.Generate(seed, plaintext);

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
                       "das Kalenderjahr des Originals samt Nachbarjahren zur Verfügung.",
        "expression" => "den Ausdruck erweitern (mehr Stellen, größere Zeichenklassen oder längere Tabellen).",
        _ => "einen Generator mit größerem Vorrat wählen (etwa 'token').",
    };

    /// <summary>
    /// Ob der Generator seine Werte ueberhaupt zurueckfuehren kann. Bei
    /// <c>false</c> ist ein nicht gefundener Wert kein Hinweis auf einen
    /// fremden Bestand, sondern die Bauart des Generators.
    /// </summary>
    public bool IsReversible(string generatorName) => _generators.Get(generatorName).IsReversible;

    /// <summary>Die Datumsformate des Generators, falls er Daten erzeugt; sonst <c>null</c>.</summary>
    public string[]? DateFormatsOf(string generatorName)
        => _generators.Contains(generatorName) && _generators.Get(generatorName) is IDateFormatGenerator dated
            ? dated.Formats
            : null;

    /// <summary>Ob der Generator ohne Tabelle zurueckrechnet (<see cref="IPseudonymGenerator.HasIntrinsicInverse"/>).</summary>
    public bool HasIntrinsicInverse(string generatorName)
        => _generators.Contains(generatorName) && _generators.Get(generatorName).HasIntrinsicInverse;

    /// <summary>
    /// Fuehrt ein Pseudonym auf den Klartext zurueck. Liefert <c>false</c>, wenn
    /// der Wert unbekannt ist.
    /// </summary>
    public bool TryReverse(string generatorName, string pseudonym, out string plaintext, string? dateFormat = null)
    {
        // Gegenstueck zu Pseudonymize: der Kern traegt den Eintrag, der Rand
        // bleibt (Befund C6). Ohne Tabelle (dateShift) gibt es keinen
        // Alteintrag mit Rand, der vorgehen muesste.
        var (leading, core, trailing) = SplitEdges(pseudonym);
        var intrinsic = _generators.Get(generatorName).HasIntrinsicInverse;
        if ((!intrinsic || core.Length == pseudonym.Length)
            && TryReverseCore(generatorName, pseudonym, out plaintext, dateFormat))
            return true;

        if (core.Length == pseudonym.Length || core.Length == 0
            || !TryReverseCore(generatorName, core, out var corePlaintext, dateFormat))
        {
            plaintext = pseudonym;
            return false;
        }

        plaintext = leading + corePlaintext + trailing;
        return true;
    }

    private bool TryReverseCore(string generatorName, string pseudonym, out string plaintext, string? dateFormat)
    {
        var generator = _generators.Get(generatorName);

        if (generator.HasIntrinsicInverse)
        {
            return dateFormat is not null && generator is IDateFormatGenerator dated
                ? dated.TryInvert(pseudonym, dateFormat, out plaintext)
                : generator.TryInvert(pseudonym, out plaintext);
        }

        var canonical = _generators.CanonicalName(generatorName);
        return _store.TryGetPlaintext(canonical, pseudonym, out plaintext)
               || (!string.Equals(canonical, generatorName, StringComparison.Ordinal)
                   && _store.TryGetPlaintext(generatorName, pseudonym, out plaintext));
    }
}
