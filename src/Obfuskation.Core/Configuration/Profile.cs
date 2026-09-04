namespace Obfuskation.Core.Configuration;

/// <summary>
/// Das Regelwerk eines Projekts. Reines Datenobjekt ohne Verhalten, damit die
/// spaetere GUI es direkt an Formulare binden kann.
/// </summary>
public sealed class Profile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Name des Profils. Bestimmt auch den Standardpfad des Mapping-Stores.</summary>
    public string ProfileName { get; set; } = "default";

    /// <summary>
    /// Pfad zur Mapping-Datei. <c>~</c> wird aufgeloest. Bleibt das Feld leer,
    /// gilt <c>~/.local/share/obfuskation/&lt;ProfileName&gt;/mapping.json</c>.
    /// </summary>
    public string? MappingStore { get; set; }

    public InputSettings Input { get; set; } = new();

    public ProfileDefaults Defaults { get; set; } = new();

    /// <summary>
    /// Feldregeln fuer CSV-Spalten und JSON-Eigenschaften. Die erste passende
    /// Regel gewinnt, die Reihenfolge in der Datei ist also die Prioritaet.
    /// </summary>
    public List<FieldRule> Fields { get; set; } = new();

    /// <summary>Muster fuer Freitext und unstrukturierte Dateien.</summary>
    public List<TextRule> TextRules { get; set; } = new();

    /// <summary>Einstellungen einzelner Generatoren, adressiert ueber ihren Namen.</summary>
    public Dictionary<string, GeneratorSettings> Generators { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InputSettings
{
    /// <summary>CSV-Trennzeichen. <c>null</c> bedeutet: aus der Kopfzeile erkennen.</summary>
    public string? CsvDelimiter { get; set; }

    /// <summary>Zeichensatzname. <c>null</c> bedeutet: erkennen (BOM, sonst UTF-8-Pruefung, sonst Windows-1252).</summary>
    public string? Encoding { get; set; }

    public bool HasHeaderRecord { get; set; } = true;
}

public sealed class ProfileDefaults
{
    /// <summary>
    /// Behandlung von Feldern, auf die keine Regel passt. Vorgabe ist
    /// <see cref="FieldAction.Error"/> — bewusst die sichere Richtung.
    /// </summary>
    public FieldAction UnknownField { get; set; } = FieldAction.Error;

    /// <summary>Platzhalter fuer <see cref="FieldAction.Redact"/>.</summary>
    public string RedactionPlaceholder { get; set; } = "***";
}

public sealed class FieldRule
{
    /// <summary>Muster fuer den Feldnamen, gemaess <see cref="MatchType"/>.</summary>
    public string Match { get; set; } = "";

    public FieldMatchType MatchType { get; set; } = FieldMatchType.Exact;

    public FieldAction Action { get; set; } = FieldAction.Error;

    /// <summary>Name des Generators. Nur bei <see cref="FieldAction.Pseudonymize"/> noetig.</summary>
    public string? Generator { get; set; }

    /// <summary>
    /// Namen der Textregeln, die bei <see cref="FieldAction.ScanText"/> angewandt
    /// werden. Leer bedeutet: alle Textregeln des Profils.
    /// </summary>
    public List<string>? TextRules { get; set; }

    /// <summary>Freitext fuer den Menschen, wird von <c>init</c> gesetzt.</summary>
    public string? Comment { get; set; }
}

public sealed class TextRule
{
    public string Name { get; set; } = "";

    /// <summary>Hoehere Werte gewinnen bei ueberlappenden Treffern.</summary>
    public int Priority { get; set; } = 50;

    public string Pattern { get; set; } = "";

    public string Generator { get; set; } = "token";

    /// <summary>
    /// Nummer der Gruppe, deren Inhalt ersetzt wird. 0 bedeutet den gesamten
    /// Treffer. Nuetzlich, um Praefixe wie <c>IBAN:</c> stehen zu lassen.
    /// </summary>
    public int CaptureGroup { get; set; }

    public bool IgnoreCase { get; set; }
}

public sealed class GeneratorSettings
{
    /// <summary>Zugrundeliegender Generatortyp. Leer bedeutet: wie der Schluessel.</summary>
    public string? Type { get; set; }

    /// <summary>Maximaler Betrag der Datumsverschiebung in Tagen (nur <c>dateShift</c>).</summary>
    public int MaxDays { get; set; } = 400;

    /// <summary>Erkannte Datumsformate (nur <c>dateShift</c>).</summary>
    public List<string>? Formats { get; set; }

    /// <summary>Laendercode fuer IBAN und BIC.</summary>
    public string? Country { get; set; }

    /// <summary>Domain fuer erzeugte E-Mail-Adressen.</summary>
    public string? Domain { get; set; }
}
