namespace Obfuskation.Core.Detection;

/// <summary>
/// Sucht viele Zeichenfolgen auf einmal in einem Text (Aho-Corasick): der
/// Automat wird einmal je Lauf gebaut, danach kostet die Suche einen Durchgang
/// ueber den Text, unabhaengig von der Zahl der Muster. Ersetzt den Vergleich
/// jedes Werts mit jedem Tabelleneintrag, der quadratisch wuchs (Befund C1).
/// </summary>
public sealed class AhoCorasick
{
    private sealed class Node
    {
        public readonly Dictionary<char, int> Next = new();
        public int Fail;

        /// <summary>Muster, die an diesem Knoten enden (Index in die Musterliste).</summary>
        public int Output = -1;

        /// <summary>Naechster Knoten ueber die Fehlerkette, an dem ein Muster endet.</summary>
        public int DictionaryLink = -1;

        public int Depth;
    }

    private readonly List<Node> _nodes = [new Node()];

    /// <param name="patterns">Die Suchmuster; leere werden uebergangen, doppelte zaehlen einmal.</param>
    public AhoCorasick(IReadOnlyList<string> patterns)
    {
        for (var index = 0; index < patterns.Count; index++)
        {
            var pattern = patterns[index];
            if (pattern.Length == 0)
                continue;

            var state = 0;
            foreach (var character in pattern)
            {
                if (!_nodes[state].Next.TryGetValue(character, out var next))
                {
                    next = _nodes.Count;
                    _nodes.Add(new Node { Depth = _nodes[state].Depth + 1 });
                    _nodes[state].Next[character] = next;
                }
                state = next;
            }

            if (_nodes[state].Output < 0)
                _nodes[state].Output = index;
        }

        BuildFailureLinks();
    }

    private void BuildFailureLinks()
    {
        var queue = new Queue<int>();
        foreach (var child in _nodes[0].Next.Values)
        {
            _nodes[child].Fail = 0;
            queue.Enqueue(child);
        }

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (var (character, child) in _nodes[state].Next)
            {
                var fail = _nodes[state].Fail;
                while (fail != 0 && !_nodes[fail].Next.ContainsKey(character))
                    fail = _nodes[fail].Fail;

                _nodes[child].Fail = _nodes[fail].Next.TryGetValue(character, out var target) && target != child
                    ? target
                    : 0;

                var failNode = _nodes[_nodes[child].Fail];
                _nodes[child].DictionaryLink = failNode.Output >= 0 ? _nodes[child].Fail : failNode.DictionaryLink;

                queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// Alle Vorkommen aller Muster, auch ueberlappende, als (Start, Laenge,
    /// Musterindex), nach Endposition geordnet.
    /// </summary>
    public IEnumerable<(int Start, int Length, int Pattern)> FindAll(string text)
    {
        var state = 0;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            while (state != 0 && !_nodes[state].Next.ContainsKey(character))
                state = _nodes[state].Fail;

            state = _nodes[state].Next.TryGetValue(character, out var next) ? next : 0;

            for (var output = _nodes[state].Output >= 0 ? state : _nodes[state].DictionaryLink;
                 output >= 0;
                 output = _nodes[output].DictionaryLink)
            {
                var node = _nodes[output];
                yield return (position - node.Depth + 1, node.Depth, node.Output);
            }
        }
    }

    /// <summary>
    /// Wortgrenzen nur dort, wo sie ueberhaupt greifen koennen -- wie
    /// <c>\b</c> in einem regulaeren Ausdruck: an einem Rand, der mit einem
    /// Sonderzeichen beginnt, ist keine Grenze verlangt. Gemeinsam fuer die
    /// Rueckuebersetzung und den Prueflauf, damit beide dasselbe als Wort
    /// ansehen.
    /// </summary>
    public static bool HasWordBoundaries(string text, int start, int length)
    {
        var end = start + length;
        if (IsWordChar(text[start]) && start > 0 && IsWordChar(text[start - 1]))
            return false;
        if (IsWordChar(text[end - 1]) && end < text.Length && IsWordChar(text[end]))
            return false;
        return true;
    }

    private static bool IsWordChar(char character) => char.IsLetterOrDigit(character) || character == '_';
}
