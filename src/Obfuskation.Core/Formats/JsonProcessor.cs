using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Verarbeitet JSON-Dokumente rekursiv. Fuer Eigenschaftsnamen gilt dieselbe
/// Regelmenge wie fuer CSV-Spalten; zusaetzlich lassen sich Regeln ueber einen
/// JSON-Pfad an eine Position binden.
///
/// Struktur, Reihenfolge und Zahlentypen bleiben erhalten; ersetzt werden nur
/// Zeichenketten- und Zahlenblaetter. Eine Zahl, deren Pseudonym keine gueltige
/// JSON-Zahl ist, wird als Zeichenkette geschrieben (Befund A3).
/// </summary>
public sealed class JsonProcessor
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    // Umlaute, '<', '&' und '+' unmaskiert, wie in ProfileStore.JsonOptions:
    // inhaltlich gleich, aber die Datei sieht sonst anders aus als die Eingabe
    // und wird groesser (Befund C2).
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Die Grammatik eines JSON-Zahlliterals nach RFC 8259.</summary>
    private static readonly Regex NumberLiteral = new(
        @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant);

    private readonly Profile _profile;
    private CancellationToken _cancellationToken;
    private readonly HashSet<string> _numbersAsText = new(StringComparer.Ordinal);

    public JsonProcessor(Profile profile) => _profile = profile;

    /// <summary>Ob <paramref name="text"/> unveraendert als JSON-Zahl geschrieben werden kann.</summary>
    public static bool IsJsonNumberLiteral(string text) => NumberLiteral.IsMatch(text);

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

        var format = TextFormatDetector.Detect(content, _profile.Input.Encoding, ",", out var text);
        report.Encoding = format.Encoding.WebName;

        var indented = LooksIndented(text);
        var root = Parse(text);

        if (root is null)
            return content;

        // Erst die Struktur einsammeln, damit der Abbruch bei unbehandelten
        // Feldern faellt, bevor irgendetwas veraendert wurde. Gesammelt wird
        // je Pfad (mit [*] statt konkreter Indizes), damit Regeln mit
        // matchType jsonPath schon hier greifen (Befund B1).
        var fields = new List<FieldOccurrence>();
        CollectFields(root, "$", null, fields, new HashSet<(string, string?)>());
        transformer.OnFields(fields);

        // Vorablauf: Felder, deren Werte der Transformer als Ganzes sehen
        // muss (Datumsformat einer Spalte, Befund A2).
        var wanted = fields.Where(transformer.WantsValues).ToDictionary(f => f.Key, StringComparer.Ordinal);
        if (wanted.Count > 0)
        {
            var values = wanted.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);
            CollectValues(root, "$", null, values);
            foreach (var (key, field) in wanted)
                transformer.OnFieldValues(field, values[key]);
        }

        var rowCount = 0;
        var result = Transform(root, "$", null, transformer, report, ref rowCount);

        report.RowsProcessed = rowCount;
        fortschritt.Complete(rowCount);

        var serialized = result?.ToJsonString(indented ? Indented : Compact) ?? "null";
        return TextFormatDetector.Encode(serialized, format);
    }

    private static JsonNode? Parse(string text)
    {
        try
        {
            // Doppelte Schluessel zuerst selbst pruefen: JsonNode wirft sonst
            // je nach Laufzeit erst beim ersten Zugriff eine rohe
            // ArgumentException, oder nimmt stillschweigend einen der Werte
            // (Befund B4).
            using (var document = JsonDocument.Parse(text, DocumentOptions))
                CheckDuplicateKeys(document.RootElement, "$");

            return JsonNode.Parse(text, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Die Eingabe ist kein gültiges JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Die Felder einer JSON-Datei je Pfad, wie <see cref="Process"/> sie dem
    /// Transformer meldet -- fuer <see cref="ObfuscationEngine.Analyze"/>, damit
    /// Feldliste und Lauf dasselbe zeigen (Befund B1).
    /// </summary>
    public static IReadOnlyList<FieldOccurrence> ListFields(byte[] content, string? encoding)
    {
        TextFormatDetector.Detect(content, encoding, ",", out var text);
        var root = Parse(text);
        var fields = new List<FieldOccurrence>();
        if (root is not null)
            CollectFields(root, "$", null, fields, new HashSet<(string, string?)>());
        return fields;
    }

    private static void CheckDuplicateKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                        throw new ConfigurationException(
                            $"Die Eingabe enthält unter {path} den Schlüssel '{property.Name}' doppelt. " +
                            "Welcher der Werte gilt, ist nicht festgelegt; die Datei vorher bereinigen.");
                    CheckDuplicateKeys(property.Value, path + "." + property.Name);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    CheckDuplicateKeys(item, $"{path}[{index++}]");
                break;
        }
    }

    /// <summary>
    /// Sammelt jedes Feld je Pfad: Eigenschaften (auch solche mit Objekt oder
    /// Feld als Wert) und Blaetter, dazu namenlose Blaetter (Befund A7).
    /// </summary>
    private static void CollectFields(
        JsonNode? node, string path, string? fieldName, List<FieldOccurrence> fields, HashSet<(string, string?)> seen)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    var childPath = path + "." + name;
                    Add(new FieldOccurrence(name, JsonPathMatcher.Normalize(childPath)));
                    CollectFields(child, childPath, name, fields, seen);
                }
                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    CollectFields(array[index], $"{path}[{index}]", fieldName, fields, seen);
                break;

            case JsonValue value:
                // true, false und null tragen keine Daten, die zu ersetzen waeren.
                if (value.GetValue<JsonElement>().ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                    break;

                var normalized = JsonPathMatcher.Normalize(path);
                Add(fieldName is null
                    ? new FieldOccurrence(normalized, normalized, IsNameless: true)
                    : new FieldOccurrence(fieldName, normalized));
                break;
        }

        void Add(FieldOccurrence field)
        {
            if (seen.Add((field.Name, field.JsonPath)))
                fields.Add(field);
        }
    }

    private static void CollectValues(JsonNode? node, string path, string? fieldName, Dictionary<string, List<string>> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                    CollectValues(child, path + "." + name, name, values);
                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    CollectValues(array[index], $"{path}[{index}]", fieldName, values);
                break;

            case JsonValue value:
                if (!values.TryGetValue(JsonPathMatcher.Normalize(path), out var list))
                    break;
                var element = value.GetValue<JsonElement>();
                if (element.ValueKind == JsonValueKind.String)
                    list.Add(element.GetString() ?? "");
                else if (element.ValueKind == JsonValueKind.Number)
                    list.Add(element.GetRawText());
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
                var element = value.GetValue<JsonElement>();

                if (element.ValueKind == JsonValueKind.String)
                {
                    var original = element.GetString() ?? "";

                    // Werte ohne umgebenden Namen (Elemente eines Wurzelfeldes,
                    // der Wurzelwert selbst) wurden bis 1.13.0 ungeprueft
                    // kopiert (Befund A7).
                    var transformed = fieldName is null
                        ? transformer.TransformNameless(path, original, path)
                        : transformer.TransformField(fieldName, path, original, path);
                    return JsonValue.Create(transformed);
                }

                if (element.ValueKind == JsonValueKind.Number)
                {
                    var original = element.GetRawText();
                    var transformed = fieldName is null
                        ? transformer.TransformNameless(path, original, path)
                        : transformer.TransformNumber(fieldName, path, original, path);

                    // Bleibt das Ergebnis eine Zahl, wird es auch als Zahl
                    // geschrieben — sonst zerfaellt der Typ der Testdaten.
                    if (transformed == original)
                        return value.DeepClone();

                    // Als Rohtext uebernehmen statt ueber decimal: "0657" waere
                    // sonst zu 657 geworden, waehrend in der Tabelle "0657"
                    // steht -- die Rueckuebersetzung fand den Wert nicht mehr.
                    if (IsJsonNumberLiteral(transformed))
                        return JsonNode.Parse(transformed);

                    var label = fieldName ?? JsonPathMatcher.Normalize(path);
                    if (_numbersAsText.Add(label))
                    {
                        report.Warn("zahlAlsText",
                            $"Im Feld '{label}' war mindestens ein Ersatzwert keine gültige JSON-Zahl " +
                            "(etwa wegen einer führenden Null); er steht als Zeichenkette in der Ausgabe.");
                    }

                    return JsonValue.Create(transformed);
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
}
