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

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, ",");
        report.Encoding = format.Encoding.WebName;

        var text = TextFormatDetector.Decode(content, format.Encoding, format.HasBom);

        transformer.OnFields(Array.Empty<string>());
        var transformed = transformer.TransformFreeText(text, "Text");

        report.RowsProcessed = CountLines(text);
        fortschritt.Complete(report.RowsProcessed);

        var body = format.Encoding.GetBytes(transformed);
        if (!format.HasBom)
            return body;

        var preamble = format.Encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
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
