namespace Obfuskation.Core.Formats;

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
    /// irgendeine Ausgabe entstanden ist.
    /// </summary>
    void OnFields(IReadOnlyList<string> fieldNames);

    /// <summary>Ob das Feld ganz aus der Ausgabe entfernt wird.</summary>
    bool ShouldDrop(string fieldName, string? jsonPath);

    /// <summary>Verarbeitet einen Feldwert.</summary>
    /// <param name="location">Fundstelle fuer Meldungen, etwa <c>Zeile 42</c>.</param>
    string TransformField(string fieldName, string? jsonPath, string value, string location);

    /// <summary>Verarbeitet unstrukturierten Text ohne Feldbezug.</summary>
    string TransformFreeText(string text, string location);
}
