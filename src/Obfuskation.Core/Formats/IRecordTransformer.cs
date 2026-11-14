using Obfuskation.Core.Detection;

namespace Obfuskation.Core.Formats;

/// <summary>
/// Ein Feld, wie es ein Prozessor in der Datei vorfindet.
/// </summary>
/// <param name="Name">
/// Spaltenname oder JSON-Eigenschaft. Bei einem namenlosen JSON-Wert (Element
/// eines Wurzelfeldes, der Wurzelwert selbst) steht hier der Pfad.
/// </param>
/// <param name="JsonPath">
/// JSON-Pfad mit <c>[*]</c> statt konkreter Indizes (siehe
/// <see cref="JsonPathMatcher.Normalize"/>), bei CSV <c>null</c>.
/// </param>
/// <param name="IsNameless">Ob der Wert keinen umgebenden Eigenschaftsnamen hat.</param>
public sealed record FieldOccurrence(string Name, string? JsonPath, bool IsNameless = false)
{
    /// <summary>Schluessel fuer Zuordnungen je Feld: der Pfad, sonst der Name.</summary>
    public string Key => JsonPath ?? Name;
}

/// <summary>
/// Die Verarbeitung eines einzelnen Wertes, unabhaengig vom Dateiformat.
///
/// Damit teilen sich CSV, JSON und Freitext dieselbe Regellogik: die Prozessoren
/// kuemmern sich um Zerlegen und Zusammensetzen der Datei, der Transformer
/// allein um die Frage, was mit einem Wert geschieht.
/// </summary>
public interface IRecordTransformer
{
    /// <summary>
    /// Meldet die erkannte Feldstruktur, bevor der erste Wert verarbeitet wird.
    /// Hier faellt der Abbruch bei unbehandelten Feldern — also bevor
    /// irgendeine Ausgabe entstanden ist. Bei JSON kommt jedes Feld je Pfad,
    /// damit auch Regeln mit <c>matchType: jsonPath</c> greifen (Befund B1).
    /// </summary>
    void OnFields(IReadOnlyList<FieldOccurrence> fields);

    /// <summary>
    /// Ob der Transformer vorab alle Werte dieses Feldes sehen muss, etwa um
    /// das Datumsformat einer Spalte festzulegen (Befund A2).
    /// </summary>
    bool WantsValues(FieldOccurrence field) => false;

    /// <summary>Alle nicht leeren Werte eines Feldes, fuer das <see cref="WantsValues"/> galt.</summary>
    void OnFieldValues(FieldOccurrence field, IReadOnlyCollection<string> values) { }

    /// <summary>Ob das Feld ganz aus der Ausgabe entfernt wird.</summary>
    bool ShouldDrop(string fieldName, string? jsonPath);

    /// <summary>Verarbeitet einen Feldwert.</summary>
    /// <param name="location">Fundstelle fuer Meldungen, etwa <c>Zeile 42</c>.</param>
    string TransformField(string fieldName, string? jsonPath, string value, string location);

    /// <summary>
    /// Wie <see cref="TransformField"/>, fuer eine JSON-Zahl. Wer neue Werte
    /// vergibt, bevorzugt dann eine gueltige Zahl, damit der Typ erhalten
    /// bleibt (Befund A3).
    /// </summary>
    string TransformNumber(string fieldName, string? jsonPath, string value, string location)
        => TransformField(fieldName, jsonPath, value, location);

    /// <summary>
    /// Verarbeitet einen JSON-Wert ohne umgebenden Eigenschaftsnamen (Befund
    /// A7). Greift eine Regel ueber den Pfad, gilt sie; sonst laufen die
    /// Textregeln -- ausser die Vorgabe verlangt den Abbruch.
    /// </summary>
    string TransformNameless(string jsonPath, string value, string location);

    /// <summary>Verarbeitet unstrukturierten Text ohne Feldbezug.</summary>
    string TransformFreeText(string text, string location);
}
