namespace Obfuskation.Core.Generation;

/// <summary>
/// Kurze deutsche Erklärungen zu den Generatoren.
///
/// Der Name eines Generators steht so in der Konfigurationsdatei und bleibt
/// deshalb englisch; für die Auswahl in der Oberfläche braucht es aber einen
/// Begriff, den man auf Anhieb versteht. Die Zuordnung liegt in der
/// Bibliothek, damit Oberfläche und Hilfetexte dasselbe sagen.
/// </summary>
public static class GeneratorDescriptions
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["personName"] = "Vor- und Nachname",
        ["firstName"] = "nur Vorname",
        ["lastName"] = "nur Nachname",
        ["companyName"] = "Firmenname mit Rechtsform",
        ["street"] = "Straßenname mit Hausnummer",
        ["city"] = "Ortsname",
        ["postalCode"] = "Postleitzahl",
        ["email"] = "E-Mail-Adresse",
        ["phone"] = "Telefonnummer",
        ["iban"] = "IBAN mit gültiger Prüfziffer",
        ["bic"] = "BIC",
        ["numericId"] = "Zahlenkennung, Stellenzahl bleibt",
        ["dateShift"] = "Datum, um festen Betrag verschoben",
        ["dateRange"] = "Datum, zufällig aus einem Zeitraum — ohne Angabe im Kalenderjahr des Originals",
        ["dateGeneralize"] = "Datum, auf Monats-, Quartals- oder Jahresanfang gerundet — nicht umkehrbar",
        ["pattern"] = "Wert nach Zeichenmaske, ohne Angabe aus dem Original abgeleitet",
        ["wordlist"] = "Wert aus einer eigenen Werteliste",
        ["partialMask"] = "teilweise maskiert, Anfang und Ende bleiben sichtbar — nicht umkehrbar",
        ["token"] = "allgemeine Kennung (TOK_…), mit Kennzeichnung davor",
        ["redact"] = "durch *** ersetzen — nicht umkehrbar",
    };

    /// <summary>Die Erklärung zu einem Generator, oder eine leere Zeichenkette.</summary>
    public static string For(string generatorName)
        => Descriptions.TryGetValue(generatorName, out var text) ? text : "";

    /// <summary>
    /// Name und Erklärung in einer Zeile, etwa <c>street — Straßenname mit
    /// Hausnummer</c>. Für Auswahllisten und Hilfetexte.
    /// </summary>
    public static string Label(string generatorName)
    {
        var text = For(generatorName);
        return text.Length == 0 ? generatorName : $"{generatorName} — {text}";
    }
}
