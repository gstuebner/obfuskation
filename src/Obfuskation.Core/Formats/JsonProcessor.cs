using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Verarbeitet JSON-Dokumente rekursiv. Fuer Eigenschaftsnamen gilt dieselbe
/// Regelmenge wie fuer CSV-Spalten; zusaetzlich lassen sich Regeln ueber einen
/// JSON-Pfad an eine Position binden.
///
/// Struktur, Reihenfolge und Zahlentypen bleiben erhalten; ersetzt werden nur
/// Zeichenketten- und Zahlenblaetter.
/// </summary>
public sealed class JsonProcessor
{
    private readonly Profile _profile;
    private CancellationToken _cancellationToken;

    public JsonProcessor(Profile profile) => _profile = profile;

    public byte[] Process(
        byte[] content,
        IRecordTransformer transformer,
        RunReport report,
        IProgress<RunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        var fortschritt = new ProgressReporter(progress, "json");

        report.Format = "json";

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, ",");
        report.Encoding = format.Encoding.WebName;

        var text = TextFormatDetector.Decode(content, format.Encoding, format.HasBom);
        var indented = LooksIndented(text);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Die Eingabe ist kein gültiges JSON: {ex.Message}", ex);
        }

        if (root is null)
            return content;

        // Erst die Struktur einsammeln, damit der Abbruch bei unbehandelten
        // Feldern faellt, bevor irgendetwas veraendert wurde.
        var fieldNames = new List<string>();
        CollectFieldNames(root, fieldNames, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        transformer.OnFields(fieldNames);

        var rowCount = 0;
        var result = Transform(root, "$", null, transformer, report, ref rowCount);

        report.RowsProcessed = rowCount;
        fortschritt.Complete(rowCount);

        var serialized = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = indented }) ?? "null";
        return Encode(serialized, format);
    }

    private static void CollectFieldNames(JsonNode? node, List<string> names, HashSet<string> seen)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (seen.Add(name))
                        names.Add(name);
                    CollectFieldNames(child, names, seen);
                }
                break;

            case JsonArray array:
                foreach (var child in array)
                    CollectFieldNames(child, names, seen);
                break;
        }
    }

    private JsonNode? Transform(
        JsonNode? node,
        string path,
        string? fieldName,
        IRecordTransformer transformer,
        RunReport report,
        ref int rowCount)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var replacement = new JsonObject();
                foreach (var (name, child) in obj.ToList())
                {
                    var childPath = path + "." + name;
                    if (transformer.ShouldDrop(name, childPath))
                    {
                        report.CountHit("drop:" + name);
                        continue;
                    }

                    var count = rowCount;
                    replacement[name] = Transform(child, childPath, name, transformer, report, ref count);
                    rowCount = count;
                }
                return replacement;
            }

            case JsonArray array:
            {
                var replacement = new JsonArray();
                for (var index = 0; index < array.Count; index++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    var count = rowCount;
                    replacement.Add(Transform(array[index], $"{path}[{index}]", fieldName,
                        transformer, report, ref count));
                    rowCount = count;
                }

                // Ein Feld von Objekten entspricht am ehesten den Zeilen einer Tabelle.
                if (array.Count > 0 && array[0] is JsonObject)
                    rowCount += array.Count;

                return replacement;
            }

            case JsonValue value:
            {
                if (fieldName is null)
                    return value.DeepClone();

                var element = value.GetValue<JsonElement>();

                if (element.ValueKind == JsonValueKind.String)
                {
                    var original = element.GetString() ?? "";
                    var transformed = transformer.TransformField(fieldName, path, original, path);
                    return JsonValue.Create(transformed);
                }

                if (element.ValueKind == JsonValueKind.Number)
                {
                    var original = element.GetRawText();
                    var transformed = transformer.TransformField(fieldName, path, original, path);

                    // Bleibt das Ergebnis eine Zahl, wird es auch als Zahl
                    // geschrieben — sonst zerfaellt der Typ der Testdaten.
                    if (transformed == original)
                        return value.DeepClone();

                    return decimal.TryParse(transformed, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                        ? JsonValue.Create(number)
                        : JsonValue.Create(transformed);
                }

                return value.DeepClone();
            }

            default:
                return node?.DeepClone();
        }
    }

    private static bool LooksIndented(string text)
    {
        // Ein Zeilenumbruch gefolgt von Leerzeichen deutet auf eingerueckte Ausgabe.
        var newLine = text.IndexOf('\n');
        return newLine >= 0 && newLine + 1 < text.Length && (text[newLine + 1] == ' ' || text[newLine + 1] == '\t');
    }

    private static byte[] Encode(string text, DetectedTextFormat format)
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
