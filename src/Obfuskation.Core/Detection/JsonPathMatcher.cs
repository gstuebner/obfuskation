namespace Obfuskation.Core.Detection;

/// <summary>
/// Vergleicht vereinfachte JSON-Pfade. Unterstuetzt werden benannte Abschnitte,
/// <c>[*]</c> fuer jedes Element eines Feldes und <c>*</c> als Platzhalter fuer
/// einen Abschnitt — mehr braucht die Feldauswahl nicht.
///
/// Beispiel: <c>$.customers[*].iban</c> passt auf <c>$.customers[3].iban</c>.
/// </summary>
public static class JsonPathMatcher
{
    public static bool Matches(string pattern, string path)
    {
        var patternSegments = Split(pattern);
        var pathSegments = Split(path);

        if (patternSegments.Count != pathSegments.Count)
            return false;

        for (var i = 0; i < patternSegments.Count; i++)
        {
            var expected = patternSegments[i];
            var actual = pathSegments[i];

            if (expected == "*")
                continue;

            // Ein Feldindex im Muster steht fuer jeden Index.
            if (expected == "[*]")
            {
                if (actual.Length >= 2 && actual[0] == '[' && actual[^1] == ']')
                    continue;
                return false;
            }

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>Zerlegt einen Pfad in Abschnitte; Feldindizes bleiben eigenstaendig.</summary>
    private static List<string> Split(string path)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < path.Length; i++)
        {
            var character = path[i];

            if (character == '.')
            {
                Flush(segments, current);
                continue;
            }

            if (character == '[')
            {
                Flush(segments, current);
                var close = path.IndexOf(']', i);
                if (close < 0)
                {
                    current.Append(path[i..]);
                    break;
                }
                segments.Add(path[i..(close + 1)]);
                i = close;
                continue;
            }

            current.Append(character);
        }

        Flush(segments, current);
        return segments;
    }

    private static void Flush(List<string> segments, System.Text.StringBuilder current)
    {
        if (current.Length > 0)
        {
            segments.Add(current.ToString());
            current.Clear();
        }
    }
}
