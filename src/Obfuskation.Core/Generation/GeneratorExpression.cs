using System.Text;
using System.Text.RegularExpressions;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Ein Befund beim Parsen eines <see cref="GeneratorExpression"/>. Die Position
/// ist 1-basiert, wie ein Mensch eine Stelle im Text zaehlen wuerde.
/// </summary>
public sealed record ExpressionError(int Position, string Message);

/// <summary>
/// Ein RegEx-aehnlicher Ausdruck, der zufaellige Werte nach einem Muster
/// erzeugt -- kein Suchausdruck, siehe docs/plan-ausdruck-generator.md. Damit
/// lassen sich Muster wie ein KFZ-Kennzeichen ohne Programmierung bauen:
/// 1-3 Zeichen aus einer Tabelle, zwei zufaellige Buchstaben, 2 oder 3
/// Ziffern, optional ein "E".
///
/// <see cref="TryParse"/> statt eines Konstruktors, der wirft: ein
/// fehlerhafter Ausdruck ist eine erwartbare Anwendereingabe (Tippfehler in
/// einem Profil), keine Ausnahmesituation im Programmablauf. Intern ist das
/// ein kleiner Syntaxbaum (Folge, Alternative, Wiederholung, Zeichenklasse,
/// Tabellenverweis, woertliches Zeichen), rekursiv absteigend geparst.
/// </summary>
public sealed class GeneratorExpression
{
    /// <summary>Laenge des Ausdruckstexts selbst, nicht der erzeugten Werte.</summary>
    private const int MaxExpressionTextLength = 500;

    /// <summary>Tiefe verschachtelter Gruppen -- eine Grenze gegen versehentlich endlose Verschachtelung.</summary>
    private const int MaxNestingDepth = 10;

    /// <summary>Obergrenze einer Anzahl wie <c>{2,64}</c>; groesser ergibt keinen sinnvollen Wertevorrat mehr.</summary>
    private const int MaxRepeatUpperBound = 64;

    /// <summary>
    /// Hoechstlaenge eines erzeugten Werts. Gilt fuer die Pruefung
    /// (<see cref="Configuration.ProfileValidator"/>) und fuer
    /// <see cref="ExpressionGenerator"/> selbst: schon zwei geschachtelte
    /// Anzahlen wie <c>((a{64}){64}){64}</c> ergaeben sonst Werte mit
    /// hunderttausenden Zeichen -- und die Vorschau im Generator-Dialog laeuft
    /// bei jedem Tastenschlag, auch ohne vorherige Pruefung.
    /// </summary>
    public const int MaxValueLength = 256;

    /// <summary>
    /// Erlaubter Name eines Tabellenverweises <c>{name}</c>: Buchstabe am
    /// Anfang, danach Buchstaben, Ziffern, "_" oder "-". Auch von
    /// <see cref="Configuration.ProfileValidator"/> genutzt, damit ein
    /// Tabellenname in <c>tables</c> demselben Massstab folgt wie im Ausdruck selbst.
    /// </summary>
    internal static readonly Regex TableNamePattern =
        new(@"^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly char[] Digits = "0123456789".ToCharArray();

    private readonly Node _root;
    private readonly List<string> _tableReferences;

    private GeneratorExpression(Node root, List<string> tableReferences)
    {
        _root = root;
        _tableReferences = tableReferences;
    }

    /// <summary>Tabellenverweise im Ausdruck, in Reihenfolge des ersten Auftretens, ohne Doppel.</summary>
    public IReadOnlyList<string> TableReferences => _tableReferences;

    /// <summary>
    /// Parst <paramref name="text"/>. Liefert bei Erfolg <c>true</c> und
    /// <paramref name="expression"/>, sonst <c>false</c> und
    /// <paramref name="error"/> mit einer deutschen Meldung samt Stelle.
    /// </summary>
    public static bool TryParse(string? text, out GeneratorExpression? expression, out ExpressionError? error)
    {
        expression = null;

        if (string.IsNullOrEmpty(text))
        {
            error = new ExpressionError(1, "Der Ausdruck darf nicht leer sein.");
            return false;
        }

        if (text.Length > MaxExpressionTextLength)
        {
            error = new ExpressionError(MaxExpressionTextLength + 1,
                $"Der Ausdruck ist zu lang ({text.Length} Zeichen), erlaubt sind höchstens " +
                $"{MaxExpressionTextLength}.");
            return false;
        }

        var parser = new Parser(text);
        if (!parser.TryParseRoot(out var root, out error) || root is null)
            return false;

        var tableReferences = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectTableReferences(root, tableReferences, seen);

        expression = new GeneratorExpression(root, tableReferences);
        error = null;
        return true;
    }

    private static void CollectTableReferences(Node node, List<string> into, HashSet<string> seen)
    {
        switch (node)
        {
            case TableRefNode tableRef:
                if (seen.Add(tableRef.Name))
                    into.Add(tableRef.Name);
                break;
            case SequenceNode sequence:
                foreach (var child in sequence.Children)
                    CollectTableReferences(child, into, seen);
                break;
            case AlternativeNode alternative:
                foreach (var option in alternative.Options)
                    CollectTableReferences(option, into, seen);
                break;
            case RepeatNode repeat:
                CollectTableReferences(repeat.Inner, into, seen);
                break;
        }
    }

    /// <summary>
    /// Erzeugt einen Wert. Alternative: gleichverteilte Wahl. Anzahl:
    /// gleichverteilt in <c>[n, m]</c>, jede Wiederholung neu gezogen. Klasse:
    /// <see cref="SeedReader.NextInt"/>. Tabelle: <see cref="SeedReader.Pick"/>.
    /// </summary>
    /// <exception cref="GenerationException">
    /// Ein Tabellenverweis im Ausdruck hat keine (oder eine leere) Entsprechung
    /// in <paramref name="tables"/>. Kommt im normalen Betrieb nicht vor, weil
    /// <see cref="Configuration.ProfileValidator"/> das vorher meldet -- ein
    /// Schutz fuer den Fall, dass jemand die Bibliothek ohne Pruefung nutzt.
    /// </exception>
    public string Generate(ref SeedReader reader, IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
    {
        var builder = new StringBuilder();
        GenerateNode(_root, ref reader, tables, builder);
        return builder.ToString();
    }

    private static void GenerateNode(
        Node node, ref SeedReader reader, IReadOnlyDictionary<string, IReadOnlyList<string>> tables,
        StringBuilder builder)
    {
        switch (node)
        {
            case LiteralNode literal:
                builder.Append(literal.Value);
                break;

            case CharClassNode charClass:
                builder.Append(charClass.Characters[reader.NextInt(charClass.Characters.Count)]);
                break;

            case TableRefNode tableRef:
                if (!tables.TryGetValue(tableRef.Name, out var values) || values.Count == 0)
                {
                    throw new GenerationException("expression",
                        $"Die Tabelle '{tableRef.Name}' fehlt oder ist leer -- unter 'tables' im " +
                        "zugehoerigen generators-Eintrag ergaenzen.");
                }
                builder.Append(reader.Pick(values));
                break;

            case SequenceNode sequence:
                foreach (var child in sequence.Children)
                    GenerateNode(child, ref reader, tables, builder);
                break;

            case AlternativeNode alternative:
                GenerateNode(alternative.Options[reader.NextInt(alternative.Options.Count)], ref reader, tables, builder);
                break;

            case RepeatNode repeat:
                var count = repeat.Min == repeat.Max
                    ? repeat.Min
                    : repeat.Min + reader.NextInt(repeat.Max - repeat.Min + 1);
                for (var i = 0; i < count; i++)
                    GenerateNode(repeat.Inner, ref reader, tables, builder);
                break;
        }
    }

    /// <summary>
    /// Groesse des Wertevorrats: Folge = Produkt, Alternative = Summe,
    /// <c>{n,m}</c> = Summe ueber p^k fuer k von n bis m, Klasse = Groesse,
    /// Tabelle = Anzahl. Saettigt bei <paramref name="cap"/> -- die genaue
    /// Groesse jenseits davon interessiert fuer eine Warnung nicht mehr, und
    /// ohne Saettigung koennte die Rechnung ueberlaufen.
    /// </summary>
    public long ValuePool(IReadOnlyDictionary<string, IReadOnlyList<string>> tables, long cap)
        => Pool(_root, tables, cap);

    private static long Pool(Node node, IReadOnlyDictionary<string, IReadOnlyList<string>> tables, long cap)
    {
        switch (node)
        {
            case LiteralNode:
                return 1;

            case CharClassNode charClass:
                return Math.Min(charClass.Characters.Count, cap);

            case TableRefNode tableRef:
                return tables.TryGetValue(tableRef.Name, out var values)
                    ? Math.Min(Math.Max(values.Count, 0), cap)
                    : 0;

            case SequenceNode sequence:
                {
                    long product = 1;
                    foreach (var child in sequence.Children)
                    {
                        product = SaturatingMultiply(product, Pool(child, tables, cap), cap);
                        if (product >= cap)
                            return cap;
                    }
                    return product;
                }

            case AlternativeNode alternative:
                {
                    long sum = 0;
                    foreach (var option in alternative.Options)
                    {
                        sum = SaturatingAdd(sum, Pool(option, tables, cap), cap);
                        if (sum >= cap)
                            return cap;
                    }
                    return sum;
                }

            case RepeatNode repeat:
                return RepeatPool(repeat, tables, cap);

            default:
                return 1;
        }
    }

    private static long RepeatPool(RepeatNode repeat, IReadOnlyDictionary<string, IReadOnlyList<string>> tables, long cap)
    {
        var perElement = Pool(repeat.Inner, tables, cap);

        long sum = 0;
        long power = 1; // p^0 fuer k=0: genau eine Moeglichkeit, naemlich keine Wiederholung.
        for (var k = 0; k <= repeat.Max; k++)
        {
            if (k > 0)
                power = SaturatingMultiply(power, perElement, cap);

            if (k >= repeat.Min)
            {
                sum = SaturatingAdd(sum, power, cap);
                if (sum >= cap)
                    return cap;
            }
        }
        return sum;
    }

    private static long SaturatingMultiply(long a, long b, long cap)
    {
        if (a == 0 || b == 0)
            return 0;
        if (a > cap / b)
            return cap;

        var result = a * b;
        return result >= cap ? cap : result;
    }

    private static long SaturatingAdd(long a, long b, long cap)
    {
        var result = a + b;
        return result >= cap ? cap : result;
    }

    /// <summary>Kuerzeste Laenge eines erzeugten Werts.</summary>
    public int MinLength(IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
        => (int)LengthBound(_root, tables, min: true);

    /// <summary>
    /// Laengste Laenge eines erzeugten Werts. Saettigt bei
    /// <see cref="int.MaxValue"/>: geschachtelte Anzahlen multiplizieren sich
    /// (zehn Ebenen mit <c>{64}</c> ergaeben 64^10), und ein Ueberlauf ins
    /// Negative liesse die Laengenpruefung ausgerechnet dort durchgehen.
    /// </summary>
    public int MaxLength(IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
        => (int)LengthBound(_root, tables, min: false);

    private static long LengthBound(Node node, IReadOnlyDictionary<string, IReadOnlyList<string>> tables, bool min)
    {
        const long cap = int.MaxValue;

        switch (node)
        {
            case LiteralNode:
            case CharClassNode:
                return 1;

            case TableRefNode tableRef:
                if (!tables.TryGetValue(tableRef.Name, out var values) || values.Count == 0)
                    return 0; // Fehlende/leere Tabelle meldet der Validator gesondert, hier neutral.
                return min ? values.Min(v => v.Length) : values.Max(v => v.Length);

            case SequenceNode sequence:
                {
                    long total = 0;
                    foreach (var child in sequence.Children)
                        total = SaturatingAdd(total, LengthBound(child, tables, min), cap);
                    return total;
                }

            case AlternativeNode alternative:
                return min
                    ? alternative.Options.Min(o => LengthBound(o, tables, min))
                    : alternative.Options.Max(o => LengthBound(o, tables, min));

            case RepeatNode repeat:
                {
                    var count = min ? repeat.Min : repeat.Max;
                    var inner = LengthBound(repeat.Inner, tables, min);
                    return count == 0 || inner == 0 ? 0 : SaturatingMultiply(count, inner, cap);
                }

            default:
                return 0;
        }
    }

    // ------------------------------------------------------------- Syntaxbaum

    private abstract class Node;

    private sealed class SequenceNode : Node
    {
        public List<Node> Children { get; } = new();
    }

    private sealed class AlternativeNode : Node
    {
        public List<Node> Options { get; } = new();
    }

    private sealed class RepeatNode(Node inner, int min, int max) : Node
    {
        public Node Inner { get; } = inner;
        public int Min { get; } = min;
        public int Max { get; } = max;
    }

    private sealed class CharClassNode(IReadOnlyList<char> characters) : Node
    {
        public IReadOnlyList<char> Characters { get; } = characters;
    }

    private sealed class TableRefNode(string name) : Node
    {
        public string Name { get; } = name;
    }

    private sealed class LiteralNode(char value) : Node
    {
        public char Value { get; } = value;
    }

    // ----------------------------------------------------------------- Parser

    /// <summary>
    /// Rekursiv absteigender Parser. Arbeitet auf einer 0-basierten Position
    /// im Text, Fehlermeldungen zaehlen die Stelle dagegen 1-basiert (siehe
    /// <see cref="Err"/>) -- so, wie ein Mensch eine Stelle im Text nennen wuerde.
    /// </summary>
    private sealed class Parser(string text)
    {
        private readonly string _text = text;
        private int _pos;

        public bool TryParseRoot(out Node? root, out ExpressionError? error)
        {
            root = null;

            if (!TryParseAlternative(out var node, out error, depth: 0))
            {
                root = null;
                return false;
            }

            if (_pos < _text.Length)
            {
                error = _text[_pos] == ')'
                    ? Err(_pos, "„)“ ist zu viel — keine passende geöffnete Klammer.")
                    : Err(_pos, $"unerwartetes Zeichen „{_text[_pos]}“.");
                return false;
            }

            root = node;
            error = null;
            return true;
        }

        private bool TryParseAlternative(out Node node, out ExpressionError? error, int depth)
        {
            if (!TryParseSequence(out node, out error, depth))
                return false;

            if (_pos >= _text.Length || _text[_pos] != '|')
                return true;

            var alternative = new AlternativeNode();
            alternative.Options.Add(node);

            while (_pos < _text.Length && _text[_pos] == '|')
            {
                _pos++; // '|' konsumieren
                if (!TryParseSequence(out var next, out error, depth))
                {
                    node = alternative;
                    return false;
                }
                alternative.Options.Add(next);
            }

            node = alternative;
            error = null;
            return true;
        }

        private bool TryParseSequence(out Node node, out ExpressionError? error, int depth)
        {
            var sequence = new SequenceNode();
            error = null;

            while (_pos < _text.Length && _text[_pos] != '|' && _text[_pos] != ')')
            {
                var c = _text[_pos];

                if (c == '^')
                {
                    if (_pos != 0)
                    {
                        error = Err(_pos, "„^“ ist nur am Anfang des Ausdrucks erlaubt.");
                        node = sequence;
                        return false;
                    }
                    _pos++;
                    continue;
                }

                if (c == '$')
                {
                    if (_pos != _text.Length - 1)
                    {
                        error = Err(_pos, "„$“ ist nur am Ende des Ausdrucks erlaubt.");
                        node = sequence;
                        return false;
                    }
                    _pos++;
                    continue;
                }

                // '{' vor einer Ziffer ist als Anzahl gemeint -- gehoert dann zu
                // einem Element, das hier (am Beginn eines neuen Sequenzglieds)
                // fehlt. '{' vor einem Buchstaben ist dagegen ein eigenstaendiger
                // Tabellenverweis, den TryParseAtom selbst parst.
                if (c == '{' && _pos + 1 < _text.Length && char.IsAsciiDigit(_text[_pos + 1]))
                {
                    error = Err(_pos, "hier fehlt ein Element, auf das sich die Anzahl bezieht.");
                    node = sequence;
                    return false;
                }

                if (!TryParseAtom(out var atom, out error, depth))
                {
                    node = sequence;
                    return false;
                }

                if (!TryParseQuantifierSuffix(atom!, out var wrapped, out error))
                {
                    node = sequence;
                    return false;
                }

                sequence.Children.Add(wrapped);
            }

            node = sequence;
            return true;
        }

        private bool TryParseAtom(out Node? node, out ExpressionError? error, int depth)
        {
            node = null;
            var c = _text[_pos];
            var startPos = _pos;

            switch (c)
            {
                case '\\':
                    return TryParseEscape(out node, out error);

                case '[':
                    return TryParseCharClass(out node, out error);

                case '(':
                    return TryParseGroup(out node, out error, depth);

                case '{':
                    return TryParseTableRef(out node, out error);

                case '*':
                case '+':
                    error = Err(startPos, "unbegrenzt ist nicht erlaubt; „{n,m}“ mit Obergrenze verwenden.");
                    return false;

                case '.':
                    error = Err(startPos,
                        "für einen Punkt „\\.“ schreiben, für beliebige Zeichen eine Klasse wie „[A-Z0-9]“.");
                    return false;

                case '?':
                    error = Err(startPos, "hier fehlt ein Element, auf das sich „?“ bezieht.");
                    return false;

                case ')':
                    error = Err(startPos, "„)“ ist zu viel — keine passende geöffnete Klammer.");
                    return false;

                default:
                    _pos++;
                    node = new LiteralNode(c);
                    error = null;
                    return true;
            }
        }

        private bool TryParseQuantifierSuffix(Node atom, out Node result, out ExpressionError? error)
        {
            error = null;

            if (_pos >= _text.Length)
            {
                result = atom;
                return true;
            }

            var c = _text[_pos];

            if (c == '?')
            {
                _pos++;
                result = new RepeatNode(atom, 0, 1);
                return true;
            }

            if (c != '{' || _pos + 1 >= _text.Length || !char.IsAsciiDigit(_text[_pos + 1]))
            {
                // Kein Suffix hier -- ein '{' vor einem Buchstaben ist ein
                // eigener Tabellenverweis und wird erst im naechsten
                // Schleifendurchlauf von TryParseSequence als neues Element geparst.
                result = atom;
                return true;
            }

            var startPos = _pos;
            _pos++; // '{' konsumieren

            if (!TryParseInt(out var min, out error))
            {
                result = atom;
                return false;
            }

            int max;
            if (_pos < _text.Length && _text[_pos] == ',')
            {
                _pos++; // ',' konsumieren

                if (_pos < _text.Length && _text[_pos] == '}')
                {
                    error = Err(startPos,
                        "eine obere Grenze fehlt; „{n,}“ (unbegrenzt) wird nicht unterstützt, „{n,m}“ mit " +
                        "Obergrenze verwenden.");
                    result = atom;
                    return false;
                }

                if (!TryParseInt(out max, out error))
                {
                    result = atom;
                    return false;
                }
            }
            else
            {
                max = min;
            }

            if (_pos >= _text.Length || _text[_pos] != '}')
            {
                error = Err(startPos, "„}“ fehlt.");
                result = atom;
                return false;
            }
            _pos++; // '}' konsumieren

            if (max < min)
            {
                error = Err(startPos,
                    $"„{{{min},{max}}}“: die untere Grenze darf nicht größer als die obere sein.");
                result = atom;
                return false;
            }

            if (min == 0 && max == 0)
            {
                error = Err(startPos, "„{0}“ erzeugt nie ein Element; das ergibt keinen Sinn.");
                result = atom;
                return false;
            }

            if (max > MaxRepeatUpperBound)
            {
                error = Err(startPos, $"die Obergrenze darf höchstens {MaxRepeatUpperBound} sein.");
                result = atom;
                return false;
            }

            result = new RepeatNode(atom, min, max);
            return true;
        }

        private bool TryParseInt(out int value, out ExpressionError? error)
        {
            var start = _pos;
            while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
                _pos++;

            if (_pos == start)
            {
                error = Err(start, "hier wird eine Zahl erwartet.");
                value = 0;
                return false;
            }

            // Auf eine vernuenftige Stellenzahl begrenzt, damit int.Parse nicht
            // an einer absichtlich uebertrieben langen Ziffernfolge ueberlaeuft.
            var digits = _text[start.._pos];
            if (digits.Length > 6 || !int.TryParse(digits, out value))
            {
                error = Err(start, $"die Zahl „{digits}“ ist zu groß.");
                value = 0;
                return false;
            }

            error = null;
            return true;
        }

        private bool TryParseEscape(out Node? node, out ExpressionError? error)
        {
            node = null;
            var startPos = _pos;
            _pos++; // '\' konsumieren

            if (_pos >= _text.Length)
            {
                error = Err(startPos, "„\\“ am Ende: danach fehlt das zu schützende Zeichen.");
                return false;
            }

            var c = _text[_pos];

            if (c == 'd')
            {
                _pos++;
                node = new CharClassNode(Digits);
                error = null;
                return true;
            }

            if (char.IsLetter(c))
            {
                error = Err(startPos, $"„\\{c}“ ist keine bekannte Kurzform (bekannt ist nur „\\d“).");
                return false;
            }

            _pos++;
            node = new LiteralNode(c);
            error = null;
            return true;
        }

        private bool TryParseCharClass(out Node? node, out ExpressionError? error)
        {
            node = null;
            var startPos = _pos;
            _pos++; // '[' konsumieren

            if (_pos < _text.Length && _text[_pos] == '^')
            {
                error = Err(_pos, "eine verneinte Zeichenklasse „[^…]“ wird nicht unterstützt.");
                return false;
            }

            var characters = new List<char>();
            var seen = new HashSet<char>();

            while (true)
            {
                if (_pos >= _text.Length)
                {
                    error = Err(startPos, "„]“ fehlt – die Zeichenklasse ist nicht geschlossen.");
                    return false;
                }

                var c = _text[_pos];

                // Zeichen ausserhalb der Grundebene (etwa Emoji) bestehen in
                // .NET aus zwei Haelften; einzeln gezogen ergaeben sie
                // ungueltigen Text. Ausserhalb einer Klasse bleiben sie als
                // Folge zweier woertlicher Zeichen dagegen heil.
                if (char.IsSurrogate(c))
                {
                    error = Err(_pos, "Sonderzeichen wie Emoji sind in einer Zeichenklasse nicht erlaubt.");
                    return false;
                }

                if (c == ']')
                {
                    _pos++;
                    break;
                }

                if (c == '\\')
                {
                    _pos++;
                    if (_pos >= _text.Length)
                    {
                        error = Err(startPos, "„]“ fehlt – die Zeichenklasse ist nicht geschlossen.");
                        return false;
                    }
                    if (char.IsSurrogate(_text[_pos]))
                    {
                        error = Err(_pos, "Sonderzeichen wie Emoji sind in einer Zeichenklasse nicht erlaubt.");
                        return false;
                    }
                    AddChar(_text[_pos], characters, seen);
                    _pos++;
                    continue;
                }

                // Bereich wie a-z: c, '-', Endzeichen -- ausser das Ende waere
                // gleich das schliessende ']', dann bleibt der Strich woertlich
                // (Rand-Regel: "-" am Rand der Klasse ist woertlich).
                if (_pos + 2 < _text.Length && _text[_pos + 1] == '-' && _text[_pos + 2] != ']')
                {
                    var from = c;
                    var to = _text[_pos + 2];

                    if (char.IsSurrogate(to))
                    {
                        error = Err(_pos + 2, "Sonderzeichen wie Emoji sind in einer Zeichenklasse nicht erlaubt.");
                        return false;
                    }

                    if (to < from)
                    {
                        error = Err(_pos, $"der Bereich „{from}-{to}“ ist rückwärts.");
                        return false;
                    }

                    // Mit int statt char gezaehlt: endet der Bereich bei
                    // U+FFFF, liefe ein char nach dem letzten Zeichen auf 0
                    // ueber und die Schleife endete nie.
                    for (int value = from; value <= to; value++)
                        AddChar((char)value, characters, seen);

                    _pos += 3;
                    continue;
                }

                AddChar(c, characters, seen);
                _pos++;
            }

            if (characters.Count == 0)
            {
                error = Err(startPos, "die Zeichenklasse darf nicht leer sein.");
                return false;
            }

            error = null;
            node = new CharClassNode(characters);
            return true;
        }

        private static void AddChar(char value, List<char> characters, HashSet<char> seen)
        {
            if (seen.Add(value))
                characters.Add(value);
        }

        private bool TryParseGroup(out Node? node, out ExpressionError? error, int depth)
        {
            node = null;
            var startPos = _pos;
            _pos++; // '(' konsumieren

            if (depth + 1 > MaxNestingDepth)
            {
                error = Err(startPos, $"zu tief verschachtelt (mehr als {MaxNestingDepth} Ebenen).");
                return false;
            }

            // "(?:" wird wie eine gewoehnliche Gruppe behandelt: dieser
            // Generator faengt nichts ein, der Unterschied zu "(" hat hier
            // keine Bedeutung. Akzeptiert wird die Schreibweise trotzdem, weil
            // sie aus RegEx gewohnt ist.
            if (_pos + 1 < _text.Length && _text[_pos] == '?' && _text[_pos + 1] == ':')
                _pos += 2;

            if (!TryParseAlternative(out var inner, out error, depth + 1))
            {
                node = inner;
                return false;
            }

            if (_pos >= _text.Length || _text[_pos] != ')')
            {
                error = Err(startPos, "„)“ fehlt – die Klammer ist nicht geschlossen.");
                return false;
            }
            _pos++; // ')' konsumieren

            node = inner;
            error = null;
            return true;
        }

        private bool TryParseTableRef(out Node? node, out ExpressionError? error)
        {
            node = null;
            var startPos = _pos;
            _pos++; // '{' konsumieren

            var nameStart = _pos;
            while (_pos < _text.Length && _text[_pos] != '}')
                _pos++;

            if (_pos >= _text.Length)
            {
                error = Err(startPos, "die Klammer „{“ ist nicht geschlossen.");
                return false;
            }

            var name = _text[nameStart.._pos];
            _pos++; // '}' konsumieren

            if (!TableNamePattern.IsMatch(name))
            {
                error = Err(startPos,
                    $"„{name}“ ist kein gültiger Tabellenname (erlaubt: ein Buchstabe am Anfang, danach " +
                    "Buchstaben, Ziffern, „_“ oder „-“).");
                return false;
            }

            error = null;
            node = new TableRefNode(name);
            return true;
        }

        /// <summary>Wandelt eine 0-basierte Position in eine 1-basierte Stelle fuer den Menschen um.</summary>
        private static ExpressionError Err(int zeroBasedPosition, string message)
            => new(zeroBasedPosition + 1, message);
    }
}
