using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Obfuskation.Core.Formats;

namespace Obfuskation.Core.Configuration;

/// <summary>Was sich einer Datei ansehen laesst, ohne sie zu verarbeiten.</summary>
/// <param name="Format">Erkanntes Dateiformat.</param>
/// <param name="Encoding">Erkannter Zeichensatz, etwa <c>windows-1252</c>.</param>
/// <param name="Delimiter">Erkanntes CSV-Trennzeichen, sonst <c>null</c>.</param>
/// <param name="FieldNames">Spaltennamen beziehungsweise JSON-Eigenschaften.</param>
public sealed record InspectedFile(
    DataFormat Format,
    string Encoding,
    string? Delimiter,
    IReadOnlyList<string> FieldNames);

/// <summary>
/// Liest die Struktur einer Datei: Format, Zeichensatz, Trennzeichen und
/// Feldnamen.
///
/// Bewusst getrennt von der Verarbeitung: die Oberflaeche soll eine Datei
/// oeffnen und sofort die Feldliste zeigen koennen, auch wenn die Datei gross
/// ist. Gelesen wird deshalb nur die Kopfzeile beziehungsweise die
/// Eigenschaftsnamen — keine Werte, und nichts davon bleibt liegen.
/// </summary>
public static class FieldInspector
{
    public static InspectedFile Inspect(byte[] content, string? fileName, InputSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        settings ??= new InputSettings();

        var format = ObfuscationEngine.ResolveFormat(DataFormat.Auto, fileName);

        return format switch
        {
            DataFormat.Csv => InspectCsv(content, settings),
            DataFormat.Json => InspectJson(content, settings),
            _ => InspectText(content, settings),
        };
    }

    /// <summary>Liest eine Datei von der Platte und sieht sie sich an.</summary>
    public static InspectedFile InspectFile(string path, InputSettings? settings = null)
    {
        var expanded = PathHelper.ExpandHome(path);
        if (!File.Exists(expanded))
            throw new FileNotFoundException($"Datei nicht gefunden: {expanded}", expanded);

        return Inspect(File.ReadAllBytes(expanded), expanded, settings);
    }

    private static InspectedFile InspectCsv(byte[] content, InputSettings settings)
    {
        var detected = TextFormatDetector.Detect(content, settings.Encoding, settings.CsvDelimiter);
        var text = TextFormatDetector.Decode(content, detected.Encoding, detected.HasBom);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = detected.Delimiter,
            HasHeaderRecord = settings.HasHeaderRecord,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, configuration);

        var names = new List<string>();

        if (settings.HasHeaderRecord)
        {
            if (csv.Read() && csv.ReadHeader() && csv.HeaderRecord is { } header)
                names.AddRange(header);
        }
        else if (csv.Read())
        {
            // Ohne Kopfzeile werden die Spalten ueber ihre Position benannt —
            // genauso wie spaeter beim Verarbeiten.
            for (var index = 1; index <= csv.Parser.Count; index++)
                names.Add(index.ToString(CultureInfo.InvariantCulture));
        }

        return new InspectedFile(
            DataFormat.Csv,
            detected.Encoding.WebName,
            detected.Delimiter == "\t" ? "\\t" : detected.Delimiter,
            names);
    }

    private static InspectedFile InspectJson(byte[] content, InputSettings settings)
    {
        var detected = TextFormatDetector.Detect(content, settings.Encoding, ",");

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            Collect(document.RootElement, names, seen);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Die Datei ist kein gültiges JSON: {ex.Message}", ex);
        }

        return new InspectedFile(DataFormat.Json, detected.Encoding.WebName, null, names);
    }

    private static InspectedFile InspectText(byte[] content, InputSettings settings)
    {
        var detected = TextFormatDetector.Detect(content, settings.Encoding, ",");

        // Unstrukturierter Text hat keine Felder; dort greifen allein die
        // Textregeln.
        return new InspectedFile(DataFormat.Text, detected.Encoding.WebName, null, Array.Empty<string>());
    }

    private static void Collect(JsonElement element, List<string> names, HashSet<string> seen)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (seen.Add(property.Name))
                        names.Add(property.Name);
                    Collect(property.Value, names, seen);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, names, seen);
                break;
        }
    }
}
