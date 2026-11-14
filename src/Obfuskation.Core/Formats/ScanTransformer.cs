using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Durchsucht eine bereits pseudonymisierte Datei nach Restbestaenden und
/// veraendert dabei nichts.
///
/// Das ist das Sicherheitsnetz gegen die Schwaeche des Musteransatzes: was keine
/// Regel getroffen hat, steht noch drin. Geprueft wird gegen zwei Quellen:
/// <list type="number">
/// <item>Die Klartexte aus der Ersetzungstabelle. Das ist der harte Befund —
/// von diesen Werten ist erwiesen, dass sie echt sind.</item>
/// <item>Die Textregeln des Profils. Hier gilt ein Treffer nur dann als
/// Verdacht, wenn der Wert nicht selbst ein bekanntes Pseudonym ist: eine
/// erzeugte Test-IBAN passt zwangslaeufig auf das IBAN-Muster, und ohne diese
/// Einschraenkung meldete die Pruefung ausschliesslich sich selbst.</item>
/// </list>
///
/// Klartexte werden ohne Ruecksicht auf Gross- und Kleinschreibung gesucht,
/// solche aus wortartigen Namensraeumen (<see cref="Generation.IPseudonymGenerator.IsWordLike"/>)
/// nur als ganzes Wort: der Klartext "Mark" ist in "MARK" ein Befund, in
/// "Marketing" nicht (Befund C1).
/// </summary>
public sealed class ScanTransformer : IRecordTransformer
{
    /// <summary>
    /// Mindestlaenge fuer den Abgleich mit Klartexten. Kuerzere Werte erzeugen
    /// zu viele Zufallstreffer und machen den Bericht unbrauchbar.
    /// </summary>
    private const int MinimumPlaintextLength = 4;

    private readonly Profile _profile;
    private readonly FieldRuleResolver _resolver;
    private readonly TextRuleEngine _textEngine;
    private readonly RunReport _report;
    /// <summary>Ein Automat ueber alle Klartexte (Befund C1) und ihre Namensraeume je Muster.</summary>
    private readonly Detection.AhoCorasick? _plaintextFinder;
    private readonly List<List<(string Namespace, bool WordLike)>> _namespacesByPlaintext = new();
    private readonly HashSet<string> _knownPseudonyms;

    /// <param name="generators">
    /// Bestimmt, welche Namensraeume wortartig sind. Ohne Register (oder fuer
    /// einen Namensraum, den das Profil nicht mehr kennt) wird jede
    /// Teilzeichenfolge gemeldet -- die vorsichtigere Richtung.
    /// </param>
    public ScanTransformer(
        Profile profile,
        FieldRuleResolver resolver,
        TextRuleEngine textEngine,
        MappingStore store,
        RunReport report,
        Generation.GeneratorRegistry? generators = null)
    {
        _profile = profile;
        _resolver = resolver;
        _textEngine = textEngine;
        _report = report;

        var plaintexts = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (namespaceName, plaintext) in store.AllPlaintexts())
        {
            if (plaintext.Length < MinimumPlaintextLength)
                continue;

            // ToLowerInvariant bildet Zeichen fuer Zeichen ab, die Laenge
            // bleibt gleich; Fundstellen im kleingeschriebenen Wert gelten
            // damit auch im Original.
            var folded = plaintext.ToLowerInvariant();
            if (!index.TryGetValue(folded, out var position))
            {
                position = plaintexts.Count;
                index[folded] = position;
                plaintexts.Add(folded);
                _namespacesByPlaintext.Add(new List<(string, bool)>());
            }

            var wordLike = generators is not null && generators.Contains(namespaceName)
                && generators.Get(namespaceName).IsWordLike;
            if (!_namespacesByPlaintext[position].Contains((namespaceName, wordLike)))
                _namespacesByPlaintext[position].Add((namespaceName, wordLike));
        }

        if (plaintexts.Count > 0)
            _plaintextFinder = new Detection.AhoCorasick(plaintexts);

        _knownPseudonyms = store.AllReverseEntries()
            .Select(entry => entry.Pseudonym)
            .ToHashSet(StringComparer.Ordinal);
    }

    public void OnFields(IReadOnlyList<FieldOccurrence> fields)
    {
        foreach (var field in fields)
        {
            var rule = _resolver.Resolve(field.Name, field.JsonPath);
            _report.FieldActions[field.Name] = rule.Action.ToString().ToLowerInvariant();

            if (rule.IsFromDefault && !_report.UnhandledFields.Contains(field.Name))
                _report.UnhandledFields.Add(field.Name);
        }
    }

    public bool ShouldDrop(string fieldName, string? jsonPath) => false;

    public string TransformField(string fieldName, string? jsonPath, string value, string location)
    {
        Inspect(value, location);
        return value;
    }

    public string TransformNameless(string jsonPath, string value, string location)
    {
        Inspect(value, location);
        return value;
    }

    public string TransformFreeText(string text, string location)
    {
        // Im Fliesstext wird zeilenweise gemeldet, damit die Fundstelle brauchbar ist.
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            Inspect(lines[index], $"Zeile {index + 1}");

        return text;
    }

    private void Inspect(string value, string location)
    {
        // Derselbe Helfer wie in ObfuscateTransformer und DeobfuscateTransformer,
        // sonst laufen Obfuskation, Pruefung und Rueckuebersetzung auseinander.
        if (_profile.Defaults.IsEffectivelyEmpty(value))
            return;

        // Ein Wert, der selbst ein vergebenes Pseudonym ist, ist geprueft in
        // Ordnung — auch wenn er zufaellig mit einem Klartext zusammenfaellt
        // oder auf ein Muster passt.
        if (_knownPseudonyms.Contains(value))
            return;

        // Der harte Befund zuerst: ein Wert, der als Klartext in der Tabelle steht.
        var reportedAsPlaintext = false;
        if (_plaintextFinder is not null)
        {
            // Je Muster: ob es irgendwo vorkommt, und ob einmal als ganzes Wort.
            var found = new SortedDictionary<int, bool>();
            foreach (var (start, length, pattern) in _plaintextFinder.FindAll(value.ToLowerInvariant()))
            {
                var whole = Detection.AhoCorasick.HasWordBoundaries(value, start, length);
                found[pattern] = whole || found.GetValueOrDefault(pattern);
            }

            foreach (var (pattern, wholeWord) in found)
            {
                foreach (var (namespaceName, wordLike) in _namespacesByPlaintext[pattern])
                {
                    if (wordLike && !wholeWord)
                        continue;

                    _report.Findings.Add(new ReportFinding(namespaceName, location, "echtwertAusTabelle"));
                    _report.CountHit("fund:echtwert");
                    reportedAsPlaintext = true;
                }
            }
        }

        // Musterverdacht ist die schwaechere Aussage und waere neben einem
        // belegten Echtwert nur Doppelmeldung.
        if (reportedAsPlaintext)
            return;

        foreach (var match in _textEngine.FindMatches(value, _profile.TextRules))
        {
            if (_knownPseudonyms.Contains(match.Value))
                continue;

            _report.Findings.Add(new ReportFinding(match.Rule.Name, location, "musterTreffer"));
            _report.CountHit("fund:" + match.Rule.Name);
        }
    }
}
