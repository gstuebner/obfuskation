using System.Text.Json.Serialization;

namespace Obfuskation.Core.Mapping;

/// <summary>
/// Der auf Platte liegende Inhalt der Mapping-Datei.
///
/// ACHTUNG: Dieses Dokument enthaelt saemtliche Echtdaten in kompakter,
/// maschinenlesbarer Form. Es ist das schuetzenswerteste Artefakt des gesamten
/// Vorgangs und darf weder in ein Repository noch in ein Verzeichnis geraten,
/// aus dem Dateien nach aussen gegeben werden.
/// </summary>
public sealed class MappingDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string ProfileName { get; set; } = "default";

    /// <summary>Platzhalter fuer eine spaetere Verschluesselung. Derzeit stets <c>none</c>.</summary>
    public string Encryption { get; set; } = "none";

    /// <summary>Base64 des profilweiten Salts. Bestimmt saemtliche Pseudonyme.</summary>
    public string Salt { get; set; } = "";

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Je Generator ein eigener Namensraum: Klartext auf Pseudonym. Dadurch
    /// bekommt derselbe Wert als Kundennummer und als Belegnummer voneinander
    /// unabhaengige Pseudonyme.
    /// </summary>
    [JsonPropertyName("namespaces")]
    public Dictionary<string, Dictionary<string, string>> Namespaces { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Datumsversatz in Tagen je <c>dateShift</c>-Namensraum, festgehalten beim
    /// ersten echten Lauf, der ihn benutzt hat (Befund C3). Ohne diesen Eintrag
    /// ergaebe sich der Versatz bei jeder Rueckuebersetzung neu aus Salt und
    /// <c>maxDays</c>; nach einer Aenderung von <c>maxDays</c> kaemen dann
    /// plausible, aber falsche Daten heraus. Optional: Tabellen aus der Zeit
    /// davor haben das Feld nicht und bleiben gueltig.
    /// </summary>
    [JsonPropertyName("dateOffsets")]
    public Dictionary<string, int>? DateOffsets { get; set; }
}

/// <summary>Der Mapping-Bestand ist in sich widerspruechlich.</summary>
public sealed class MappingConflictException : Exception
{
    public MappingConflictException(string message) : base(message) { }
}

/// <summary>Der Mapping-Store wird bereits von einem anderen Lauf benutzt.</summary>
public sealed class MappingLockedException : Exception
{
    public MappingLockedException(string path)
        : base($"Der Mapping-Store wird bereits verwendet: {path}. " +
               "Laeuft ein anderer Vorgang, oder ist eine verwaiste Sperrdatei uebrig?")
        => LockPath = path;

    public string LockPath { get; }
}
