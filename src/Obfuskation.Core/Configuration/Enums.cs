namespace Obfuskation.Core.Configuration;

/// <summary>Was mit einem erkannten Feld geschehen soll.</summary>
public enum FieldAction
{
    /// <summary>Wert durch ein Pseudonym ersetzen. Umkehrbar.</summary>
    Pseudonymize,

    /// <summary>Wert unveraendert uebernehmen.</summary>
    Passthrough,

    /// <summary>Wert durch einen festen Platzhalter ersetzen. NICHT umkehrbar.</summary>
    Redact,

    /// <summary>Feld komplett aus der Ausgabe entfernen. NICHT umkehrbar.</summary>
    Drop,

    /// <summary>Feldinhalt mit den Textregeln durchsuchen und Treffer ersetzen.</summary>
    ScanText,

    /// <summary>Kein bewusster Umgang festgelegt: Lauf abbrechen.</summary>
    Error,
}

/// <summary>Wie der Feldname mit dem Muster verglichen wird.</summary>
public enum FieldMatchType
{
    /// <summary>Exakter Vergleich, Gross-/Kleinschreibung wird ignoriert.</summary>
    Exact,

    /// <summary>Regulaerer Ausdruck gegen den Feldnamen.</summary>
    Regex,

    /// <summary>Vereinfachter JSON-Pfad, z.B. <c>$.customers[*].iban</c>. Nur fuer JSON.</summary>
    JsonPath,
}

/// <summary>Schweregrad eines Befunds der Profilpruefung.</summary>
public enum ValidationSeverity
{
    Warning,
    Error,
}
