using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Obfuskation.Core.Formats;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Liest Beispielwerte je Feld aus den ersten Datensaetzen einer Datei.
///
/// Steht im Kern statt in der Oberflaeche, aus drei Gruenden: CsvHelper und
/// <see cref="JsonDocument"/> gehoeren in den Kern, nicht in eine eigene
/// Formatzerlegung der Oberflaeche; die Lesung laesst sich so mit den
/// Core-Tests pruefen, ganz ohne Ansichtsmodell; und sie steht direkt neben
/// <see cref="FieldInspector"/>, der dieselbe Datei bereits fuer die
/// Feldnamen liest.
/// </summary>
public static class FieldSampler
{
    /// <summary>
    /// Wie viele Datenzeilen hoechstens gelesen werden, selbst wenn dann noch
    /// nicht jedes Feld genug Werte hat. Ohne diese Grenze wuerde eine Datei
    /// mit einer durchgaengig leeren Spalte den ganzen Bestand einlesen, nur
    /// um am Ende trotzdem nichts fuer sie zu finden.
    /// </summary>
    private const int MaxRowsToScan = 50;

    /// <summary>Beispielwerte je Feld, aus den ersten Datensätzen der Datei.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Sample(
        byte[] content, string? fileName, InputSettings? settings = null, int maxPerField = 3)
    {
        ArgumentNullException.ThrowIfNull(content);
        settings ??= new InputSettings();

        var format = ObfuscationEngine.ResolveFormat(DataFormat.Auto, fileName);

        return format switch
        {
            DataFormat.Csv => SampleCsv(content, settings, maxPerField),
            DataFormat.Json => SampleJson(content, maxPerField),
            // Unstrukturierter Text hat keine Felder, wie in FieldInspector.InspectText.
            _ => Empty(),
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SampleCsv(
        byte[] content, InputSettings settings, int maxPerField)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Dieselbe Erkennung und dieselbe CsvConfiguration wie CsvProcessor:
            // die Vorschau soll denselben Wert zeigen, den der echte Lauf
            // spaeter ersetzt. Eine abweichende Zerlegung waere eine Luege.
            var detected = TextFormatDetector.Detect(content, settings.Encoding, settings.CsvDelimiter);
            var text = TextFormatDetector.Decode(content, detected.Encoding, detected.HasBom);

            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = detected.Delimiter,
                HasHeaderRecord = settings.HasHeaderRecord,
                MissingFieldFound = null,
                BadDataFound = null,
                DetectColumnCountChanges = false,
                TrimOptions = TrimOptions.None,
            };

            using var reader = new StringReader(text);
            using var csv = new CsvReader(reader, configuration);

            string[] names;
            var rowsScanned = 0;

            if (settings.HasHeaderRecord)
            {
                if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { } header)
                    return Empty();

                names = header;
            }
            else
            {
                // Ohne Kopfzeile werden die Spalten ueber ihre Position benannt,
                // genau wie in FieldInspector.InspectCsv — und die eben gelesene
                // Zeile ist bereits die erste Datenzeile.
                if (!csv.Read())
                    return Empty();

                names = Enumerable.Range(1, csv.Parser.Count)
                    .Select(index => index.ToString(CultureInfo.InvariantCulture))
                    .ToArray();

                CollectRow(csv, names, result, maxPerField);
                rowsScanned++;
            }

            while (rowsScanned < MaxRowsToScan
                   && !AllFieldsSatisfied(names, result, maxPerField)
                   && csv.Read())
            {
                CollectRow(csv, names, result, maxPerField);
                rowsScanned++;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or CsvHelperException)
        {
            // Es ist nur eine Vorschau: eine unlesbare Datei liefert eben, was
            // bis dahin gesammelt wurde, statt die Oberflaeche zu stoeren.
        }

        return ToReadOnly(result);
    }

    private static void CollectRow(
        CsvReader csv, string[] names, Dictionary<string, List<string>> result, int maxPerField)
    {
        for (var index = 0; index < names.Length && index < csv.Parser.Count; index++)
        {
            var value = csv.Parser[index];
            if (string.IsNullOrEmpty(value))
                continue;

            if (!result.TryGetValue(names[index], out var values))
            {
                values = new List<string>();
                result[names[index]] = values;
            }

            if (values.Count < maxPerField)
                values.Add(value);
        }
    }

    private static bool AllFieldsSatisfied(
        string[] names, Dictionary<string, List<string>> result, int maxPerField)
        => names.All(name => result.TryGetValue(name, out var values) && values.Count >= maxPerField);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> SampleJson(byte[] content, int maxPerField)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            Collect(document.RootElement, result, maxPerField);
        }
        catch (JsonException)
        {
            // Es ist nur eine Vorschau: ungueltiges JSON liefert eben ein
            // leeres Ergebnis, statt die Oberflaeche zu stoeren. Ein echter
            // Lauf meldet den Fehler ohnehin deutlich ueber FieldInspector.
        }

        return ToReadOnly(result);
    }

    /// <summary>
    /// Sammelt rekursiv wie FieldInspector.Collect, aber Werte statt Namen —
    /// und nur Blattwerte je Eigenschaftsname, nie das Objekt oder Array
    /// selbst.
    /// </summary>
    private static void Collect(JsonElement element, Dictionary<string, List<string>> result, int maxPerField)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.Object:
                        case JsonValueKind.Array:
                            Collect(property.Value, result, maxPerField);
                            break;
                        case JsonValueKind.String:
                        case JsonValueKind.Number:
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            AddJsonValue(result, property.Name, property.Value, maxPerField);
                            break;
                            // Null liefert keinen Beispielwert.
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, result, maxPerField);
                break;
        }
    }

    private static void AddJsonValue(
        Dictionary<string, List<string>> result, string name, JsonElement value, int maxPerField)
    {
        if (!result.TryGetValue(name, out var values))
        {
            values = new List<string>();
            result[name] = values;
        }

        if (values.Count >= maxPerField)
            return;

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        if (text.Length > 0)
            values.Add(text);
    }

    private static Dictionary<string, IReadOnlyList<string>> Empty()
        => new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToReadOnly(
        Dictionary<string, List<string>> result)
    {
        var final = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in result)
            final[key] = value;
        return final;
    }
}
