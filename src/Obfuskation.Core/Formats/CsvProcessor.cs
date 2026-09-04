using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Verarbeitet CSV-Dateien feldweise.
///
/// Der Inhalt wird konsequent ueber einen CSV-Leser zerlegt und nicht mit
/// regulaeren Ausdruecken ueber den Rohtext bearbeitet: nur so bleiben
/// Anfuehrungszeichen, eingebettete Zeilenumbrueche und Trennzeichen im Feld
/// unversehrt. Zeichensatz, Trennzeichen und Zeilenende der Eingabe werden
/// uebernommen.
/// </summary>
public sealed class CsvProcessor
{
    private readonly Profile _profile;

    public CsvProcessor(Profile profile) => _profile = profile;

    public byte[] Process(
        byte[] content,
        IRecordTransformer transformer,
        RunReport report,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fortschritt = new ProgressReporter(progress, "csv");

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, _profile.Input.CsvDelimiter);
        report.Encoding = format.Encoding.WebName;
        report.Delimiter = format.Delimiter == "\t" ? "\\t" : format.Delimiter;
        report.Format = "csv";

        var text = TextFormatDetector.Decode(content, format.Encoding, format.HasBom);

        var readerConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = format.Delimiter,
            HasHeaderRecord = _profile.Input.HasHeaderRecord,
            // Fehlende oder ueberzaehlige Felder duerfen den Lauf nicht abbrechen;
            // reale Ausfuhren sind nicht immer sauber.
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.None,
        };

        var writerConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = format.Delimiter,
            HasHeaderRecord = _profile.Input.HasHeaderRecord,
            NewLine = format.NewLine,
            // Die Anfuehrungszeichen des Originals lassen sich nicht
            // zuverlaessig rekonstruieren; es wird gesetzt, was noetig ist.
            ShouldQuote = args => ConfigurationFunctions.ShouldQuote(args),
        };

        var output = new StringWriter { NewLine = format.NewLine };

        using (var stringReader = new StringReader(text))
        using (var csvReader = new CsvReader(stringReader, readerConfiguration))
        using (var csvWriter = new CsvWriter(output, writerConfiguration))
        {
            string[] headers;

            if (_profile.Input.HasHeaderRecord)
            {
                if (!csvReader.Read() || !csvReader.ReadHeader())
                    return content; // Leere Datei unveraendert durchreichen.

                headers = csvReader.HeaderRecord ?? Array.Empty<string>();
            }
            else
            {
                // Ohne Kopfzeile werden die Spalten ueber ihre Position benannt.
                if (!csvReader.Read())
                    return content;

                headers = Enumerable.Range(1, csvReader.Parser.Count)
                    .Select(index => index.ToString(CultureInfo.InvariantCulture))
                    .ToArray();
            }

            transformer.OnFields(headers);

            var keptIndexes = new List<int>(headers.Length);
            for (var index = 0; index < headers.Length; index++)
            {
                if (transformer.ShouldDrop(headers[index], null))
                    report.CountHit("drop:" + headers[index]);
                else
                    keptIndexes.Add(index);
            }

            if (_profile.Input.HasHeaderRecord)
            {
                foreach (var index in keptIndexes)
                    csvWriter.WriteField(headers[index]);
                csvWriter.NextRecord();
            }
            else
            {
                // Der erste Datensatz wurde bereits gelesen und muss mit verarbeitet werden.
                WriteRecord(csvReader, csvWriter, headers, keptIndexes, transformer, report, 1);
            }

            var rowNumber = _profile.Input.HasHeaderRecord ? 1 : 2;
            while (csvReader.Read())
            {
                // Je Datensatz pruefen: bei einer grossen Datei soll ein
                // Abbruch nicht erst am Ende wirken.
                cancellationToken.ThrowIfCancellationRequested();

                WriteRecord(csvReader, csvWriter, headers, keptIndexes, transformer, report, rowNumber);
                rowNumber++;

                fortschritt.Report(rowNumber);
            }

            fortschritt.Complete(rowNumber - 1);

            report.RowsProcessed = _profile.Input.HasHeaderRecord ? rowNumber - 1 : rowNumber - 1;
            csvWriter.Flush();
        }

        return EncodeResult(output.ToString(), format);
    }

    private static void WriteRecord(
        CsvReader reader,
        CsvWriter writer,
        string[] headers,
        List<int> keptIndexes,
        IRecordTransformer transformer,
        RunReport report,
        int rowNumber)
    {
        foreach (var index in keptIndexes)
        {
            var fieldName = index < headers.Length ? headers[index] : index.ToString(CultureInfo.InvariantCulture);
            var value = index < reader.Parser.Count ? reader.Parser[index] ?? "" : "";

            var transformed = transformer.TransformField(
                fieldName, null, value, $"Zeile {rowNumber}, Spalte {fieldName}");

            writer.WriteField(transformed);
        }

        writer.NextRecord();
    }

    private static byte[] EncodeResult(string text, DetectedTextFormat format)
    {
        var body = format.Encoding.GetBytes(text);
        if (!format.HasBom)
            return body;

        var preamble = format.Encoding.GetPreamble();
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }
}
