using Obfuskation.Core.Configuration;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Verarbeitet unstrukturierten Text. Hier greifen ausschliesslich die
/// Textregeln — es gibt keine Felder, an denen eine Whitelist ansetzen koennte.
/// Das ist der Weg fuer beliebige Dateien und fuer Text von der Standardeingabe.
/// </summary>
public sealed class PlainTextProcessor
{
    private readonly Profile _profile;

    public PlainTextProcessor(Profile profile) => _profile = profile;

    public byte[] Process(
        byte[] content,
        IRecordTransformer transformer,
        RunReport report,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fortschritt = new ProgressReporter(progress, "text");

        report.Format = "text";

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, ",", out var text);
        report.Encoding = format.Encoding.WebName;

        // Ein NUL-Zeichen kommt in Text nicht vor, wohl aber in jeder
        // Binaerdatei (Bild, Archiv, Office-Dokument). Die lief bis 1.13.0
        // unveraendert und mit Erfolgsmeldung durch (Befund A6).
        if (text.Contains('\0'))
        {
            throw new ConfigurationException(
                "Die Eingabe enthält Binärdaten (NUL-Zeichen) und lässt sich nicht als Text verarbeiten. " +
                "Unterstützt sind CSV, JSON und Text in UTF-8, UTF-16 mit Markierung oder Windows-1252.");
        }

        transformer.OnFields(Array.Empty<FieldOccurrence>());
        var transformed = transformer.TransformFreeText(text, "Text");

        report.RowsProcessed = CountLines(text);
        fortschritt.Complete(report.RowsProcessed);

        return TextFormatDetector.Encode(transformed, format);
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        var lines = 1;
        foreach (var character in text)
            if (character == '\n')
                lines++;
        return lines;
    }
}
