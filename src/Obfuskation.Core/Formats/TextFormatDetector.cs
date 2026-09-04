using System.Text;

namespace Obfuskation.Core.Formats;

/// <summary>Was ueber eine Textdatei erkannt wurde.</summary>
/// <param name="Encoding">Der Zeichensatz.</param>
/// <param name="HasBom">Ob eine Byte-Reihenfolge-Markierung vorhanden war.</param>
/// <param name="Delimiter">Das erkannte CSV-Trennzeichen.</param>
/// <param name="NewLine">Das vorherrschende Zeilenende.</param>
public sealed record DetectedTextFormat(Encoding Encoding, bool HasBom, string Delimiter, string NewLine);

/// <summary>
/// Erkennt Zeichensatz, Trennzeichen und Zeilenende. Beides muss erhalten
/// bleiben: Ausfuhren aus Bankanwendungen kommen haeufig als Windows-1252 mit
/// Semikolon, und eine Ausgabe in UTF-8 mit Komma waere fuer die abnehmende
/// Anwendung eine andere Datei.
/// </summary>
public static class TextFormatDetector
{
    private static readonly char[] DelimiterCandidates = [';', ',', '\t', '|'];

    public static DetectedTextFormat Detect(byte[] content, string? forcedEncoding, string? forcedDelimiter)
    {
        RegisterCodePages();

        var (encoding, hasBom) = DetectEncoding(content, forcedEncoding);
        var text = Decode(content, encoding, hasBom);

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var delimiter = forcedDelimiter ?? DetectDelimiter(text);

        return new DetectedTextFormat(encoding, hasBom, delimiter, newLine);
    }

    /// <summary>Windows-1252 ist erst nach dieser Anmeldung verfuegbar.</summary>
    public static void RegisterCodePages()
    {
        if (!_codePagesRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePagesRegistered = true;
        }
    }

    private static bool _codePagesRegistered;

    public static string Decode(byte[] content, Encoding encoding, bool hasBom)
    {
        var preambleLength = hasBom ? encoding.GetPreamble().Length : 0;
        return encoding.GetString(content, preambleLength, content.Length - preambleLength);
    }

    private static (Encoding Encoding, bool HasBom) DetectEncoding(byte[] content, string? forced)
    {
        if (!string.IsNullOrWhiteSpace(forced))
        {
            RegisterCodePages();
            try
            {
                return (Encoding.GetEncoding(forced), StartsWithBom(content, Encoding.GetEncoding(forced)));
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Unbekannter Zeichensatz: '{forced}'", nameof(forced));
            }
        }

        if (StartsWith(content, [0xEF, 0xBB, 0xBF]))
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true);
        if (StartsWith(content, [0xFF, 0xFE]))
            return (Encoding.Unicode, true);
        if (StartsWith(content, [0xFE, 0xFF]))
            return (Encoding.BigEndianUnicode, true);

        // Ohne Markierung: strenges UTF-8 versuchen. Schlaegt das fehl, ist es
        // mit grosser Wahrscheinlichkeit Windows-1252 — der uebliche Fall bei
        // Ausfuhren aus aelteren Anwendungen.
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            strictUtf8.GetString(content);
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);
        }
        catch (DecoderFallbackException)
        {
            RegisterCodePages();
            return (Encoding.GetEncoding(1252), false);
        }
    }

    private static bool StartsWithBom(byte[] content, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        return preamble.Length > 0 && StartsWith(content, preamble);
    }

    private static bool StartsWith(byte[] content, ReadOnlySpan<byte> prefix)
        => content.Length >= prefix.Length && content.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    /// <summary>
    /// Waehlt das Trennzeichen, das in den ersten Zeilen am gleichmaessigsten
    /// auftritt. Ein Zeichen, das in jeder Zeile gleich oft vorkommt, ist mit
    /// hoher Wahrscheinlichkeit das Trennzeichen.
    /// </summary>
    private static string DetectDelimiter(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(20)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0)
            return ";";

        var best = ';';
        var bestScore = -1;

        foreach (var candidate in DelimiterCandidates)
        {
            var counts = lines.Select(line => CountOutsideQuotes(line, candidate)).ToList();
            var first = counts[0];
            if (first == 0)
                continue;

            var consistent = counts.Count(count => count == first);

            // Gleichmaessigkeit zaehlt mehr als Haeufigkeit: ein Komma in
            // Betraegen kommt oft vor, aber nicht in jeder Zeile gleich oft.
            var score = consistent * 100 + Math.Min(first, 50);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best.ToString();
    }

    private static int CountOutsideQuotes(string line, char candidate)
    {
        var count = 0;
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
                inQuotes = !inQuotes;
            else if (character == candidate && !inQuotes)
                count++;
        }

        return count;
    }
}
