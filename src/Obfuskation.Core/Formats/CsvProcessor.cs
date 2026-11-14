using System.Globalization;
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
/// uebernommen, ebenso Leerzeilen und ein fehlendes Zeilenende am Dateiende.
///
/// Eine Zeile mit mehr Feldern als die Kopfzeile verliert keines davon: die
/// ueberzaehligen Felder heissen nach ihrer Position ("3", "4", ...) und werden
/// wie unbekannte Felder behandelt, bei <c>unknownField: error</c> also mit
/// Abbruch (Befund A8).
/// </summary>
public sealed class CsvProcessor
{
    private readonly Profile _profile;

    public CsvProcessor(Profile profile) => _profile = profile;

    private sealed record Record(string[] Fields, bool IsBlank);

    public byte[] Process(
        byte[] content,
        IRecordTransformer transformer,
        RunReport report,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fortschritt = new ProgressReporter(progress, "csv");

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, _profile.Input.CsvDelimiter, out var text);
        report.Encoding = format.Encoding.WebName;
        report.Delimiter = format.Delimiter == "\t" ? "\\t" : format.Delimiter;
        report.Format = "csv";

        var records = ReadAll(text, format.Delimiter, cancellationToken);
        if (records.Count == 0)
            return content; // Leere Datei unveraendert durchreichen.

        var hasHeader = _profile.Input.HasHeaderRecord;
        string[] headers = hasHeader
            ? records[0].Fields
            // Ohne Kopfzeile werden die Spalten ueber ihre Position benannt.
            : Enumerable.Range(1, records[0].Fields.Length).Select(PositionName).ToArray();
        var data = hasHeader ? records.Skip(1).ToList() : records;

        transformer.OnFields(headers.Select(name => new FieldOccurrence(name, null)).ToList());

        // Vorablauf fuer Felder, deren Werte der Transformer als Ganzes sehen
        // muss (Datumsformat einer Spalte, Befund A2).
        for (var index = 0; index < headers.Length; index++)
        {
            var field = new FieldOccurrence(headers[index], null);
            if (!transformer.WantsValues(field))
                continue;

            var column = index;
            transformer.OnFieldValues(field, data
                .Where(record => !record.IsBlank && column < record.Fields.Length)
                .Select(record => record.Fields[column])
                .ToList());
        }

        var keptIndexes = new List<int>(headers.Length);
        for (var index = 0; index < headers.Length; index++)
        {
            if (transformer.ShouldDrop(headers[index], null))
                report.CountHit("drop:" + headers[index]);
            else
                keptIndexes.Add(index);
        }

        var writerConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = format.Delimiter,
            HasHeaderRecord = hasHeader,
            NewLine = format.NewLine,
            // Die Anfuehrungszeichen des Originals lassen sich nicht
            // zuverlaessig rekonstruieren; es wird gesetzt, was noetig ist.
            ShouldQuote = args => ConfigurationFunctions.ShouldQuote(args),
        };

        var output = new StringWriter { NewLine = format.NewLine };
        var abweichend = 0;
        var ersteAbweichung = 0;
        var rowsProcessed = 0;

        using (var csvWriter = new CsvWriter(output, writerConfiguration))
        {
            if (hasHeader)
            {
                foreach (var index in keptIndexes)
                    csvWriter.WriteField(headers[index]);
                csvWriter.NextRecord();
            }

            var droppedExtra = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var row = 0; row < data.Count; row++)
            {
                // Je Datensatz pruefen: bei einer grossen Datei soll ein
                // Abbruch nicht erst am Ende wirken.
                cancellationToken.ThrowIfCancellationRequested();

                var rowNumber = row + 1;
                var record = data[row];

                if (record.IsBlank)
                {
                    // Leerzeilen bleiben stehen, damit Zeilennummern und Aufbau
                    // der Datei erhalten bleiben.
                    csvWriter.Flush();
                    output.Write(format.NewLine);
                    continue;
                }

                if (record.Fields.Length != headers.Length)
                {
                    abweichend++;
                    if (ersteAbweichung == 0)
                        ersteAbweichung = rowNumber;
                }

                foreach (var index in keptIndexes)
                {
                    // Eine kuerzere Zeile wird mit leeren Feldern aufgefuellt.
                    var value = index < record.Fields.Length ? record.Fields[index] : "";
                    csvWriter.WriteField(transformer.TransformField(
                        headers[index], null, value, $"Zeile {rowNumber}, Spalte {headers[index]}"));
                }

                for (var index = headers.Length; index < record.Fields.Length; index++)
                {
                    var name = PositionName(index + 1);
                    if (!droppedExtra.TryGetValue(name, out var drop))
                    {
                        drop = transformer.ShouldDrop(name, null);
                        droppedExtra[name] = drop;
                    }
                    if (drop)
                        continue;

                    csvWriter.WriteField(transformer.TransformField(
                        name, null, record.Fields[index], $"Zeile {rowNumber}, Spalte {name}"));
                }

                csvWriter.NextRecord();
                rowsProcessed++;
                fortschritt.Report(rowsProcessed);
            }

            csvWriter.Flush();
        }

        fortschritt.Complete(rowsProcessed);
        report.RowsProcessed = rowsProcessed;

        if (abweichend > 0)
        {
            report.Warn("abweichendeFeldanzahl",
                $"{abweichend} Zeile(n) haben eine andere Feldzahl als die Kopfzeile (erste: Zeile " +
                $"{ersteAbweichung}). Überzählige Felder wurden nach ihrer Position benannt und nach der " +
                "Vorgabe für unbekannte Felder behandelt, fehlende leer ergänzt.");
        }

        var result = output.ToString();

        // Ein fehlendes Zeilenende am Dateiende bleibt fehlend.
        if (!text.EndsWith('\n') && result.EndsWith(format.NewLine, StringComparison.Ordinal))
            result = result[..^format.NewLine.Length];

        return TextFormatDetector.Encode(result, format);
    }

    private static string PositionName(int position) => position.ToString(CultureInfo.InvariantCulture);

    private List<Record> ReadAll(string text, string delimiter, CancellationToken cancellationToken)
    {
        var readerConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            // Fehlende oder ueberzaehlige Felder duerfen den Lauf nicht abbrechen;
            // reale Ausfuhren sind nicht immer sauber.
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.None,
            // Leerzeilen werden gelesen, damit sie erhalten bleiben und die
            // Zeilennummern in Meldungen stimmen.
            IgnoreBlankLines = false,
        };

        var records = new List<Record>();
        using var stringReader = new StringReader(text);
        using var csvReader = new CsvReader(stringReader, readerConfiguration);

        while (csvReader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fields = csvReader.Parser.Record ?? [];
            var raw = csvReader.Parser.RawRecord.AsSpan().TrimEnd("\r\n");
            var blank = raw.Length == 0 && fields.Length <= 1;
            records.Add(new Record(fields, blank));
        }

        // Eine Leerzeile ganz am Anfang kann keine Kopfzeile sein; vor der
        // Kopfzeile stehende Leerzeilen werden deshalb nicht mitgezaehlt.
        while (records.Count > 0 && records[0].IsBlank && _profile.Input.HasHeaderRecord)
            records.RemoveAt(0);

        return records;
    }
}
