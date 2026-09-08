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

    /// <summary>Freitext des Anwenders: wofuer dieses Profil da ist. Rein erklaerend.</summary>
    public string? Description { get; set; }

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

    /// <summary>
    /// Werte, die zusaetzlich zu "" und reinem Leerraum als faktisch leer
    /// gelten (nach Trimmen, ohne Ruecksicht auf Gross-/Kleinschreibung), etwa
    /// "-" oder "N/A". Solche Werte laufen unveraendert durch, statt ein
    /// Pseudonym zu bekommen, das einen Wert vortaeuschen wuerde, der im
    /// Original gar nicht stand.
    /// </summary>
    public List<string> EmptyValues { get; set; } = new();

    /// <summary>
    /// Ob ein Wert faktisch leer ist: "" ist es, reiner Leerraum ist es, und
    /// nach Trimmen ohne Ruecksicht auf Gross-/Kleinschreibung jeder Eintrag
    /// aus <see cref="EmptyValues"/>. Gemeinsamer Helfer fuer alle drei
    /// Transformer (Obfuskation, Pruefung, Rueckuebersetzung), damit die
    /// Definition an genau einer Stelle steht und die drei nicht auseinanderlaufen.
    /// </summary>
    public bool IsEffectivelyEmpty(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return true;

        foreach (var candidate in EmptyValues)
        {
            if (string.Equals(trimmed, candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
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

    /// <summary>Erkannte Datumsformate (<c>dateShift</c>, <c>dateRange</c>, <c>dateGeneralize</c>).</summary>
    public List<string>? Formats { get; set; }

    /// <summary>Laendercode fuer IBAN und BIC.</summary>
    public string? Country { get; set; }

    /// <summary>Domain fuer erzeugte E-Mail-Adressen.</summary>
    public string? Domain { get; set; }

    /// <summary>Untere Grenze des Zeitraums (nur <c>dateRange</c>), ISO-Datum ("yyyy-MM-dd").</summary>
    public string? From { get; set; }

    /// <summary>Obere Grenze des Zeitraums (nur <c>dateRange</c>), ISO-Datum ("yyyy-MM-dd").</summary>
    public string? To { get; set; }

    /// <summary>
    /// Rundungsstufe fuer <c>dateGeneralize</c>: "month", "quarter" oder
    /// "year". Ohne Angabe gilt "month".
    /// </summary>
    public string? Granularity { get; set; }

    /// <summary>
    /// Zeichenmaske fuer <c>pattern</c>: 'A' Grossbuchstabe, 'a' Kleinbuchstabe,
    /// '9' Ziffer, 'X' alphanumerisch, '\' escaped das Folgezeichen, alles
    /// Uebrige bleibt woertlich. Ohne Angabe wird die Maske aus dem Original
    /// abgeleitet.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>Eigene Werteliste, aus der <c>wordlist</c> deterministisch waehlt.</summary>
    public List<string>? Values { get; set; }

    /// <summary>Anzahl der am Anfang sichtbar bleibenden Zeichen (nur <c>partialMask</c>).</summary>
    public int KeepFirst { get; set; }

    /// <summary>
    /// Anzahl der am Ende sichtbar bleibenden Zeichen (nur <c>partialMask</c>).
    /// Ohne Angabe (0) gelten effektiv 4 -- wer wirklich keine Endstellen
    /// sichtbar lassen will, muss deshalb 'keepFirst' und die Maskierung ueber
    /// die Gesamtlaenge des Wertes steuern.
    /// </summary>
    public int KeepLast { get; set; }

    /// <summary>Maskierungszeichen, genau ein Zeichen (nur <c>partialMask</c>). Ohne Angabe '*'.</summary>
    public string? MaskChar { get; set; }

    /// <summary>
    /// Eigener Platzhalter (nur <c>redact</c>). Ohne Angabe gilt
    /// <c>defaults.redactionPlaceholder</c> -- dieses Feld erlaubt einem
    /// eigenen <c>redact</c>-Namensraum einen abweichenden Platzhalter, der
    /// nicht von der Profilvorgabe ueberschrieben wird.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Vorangestellte Kennzeichnung des Pseudonyms (nur <c>token</c>). Gehoert
    /// an den Generator und nicht an das Feld: derselbe <c>generators</c>-Eintrag
    /// gilt fuer jedes Feld, das ihn referenziert, unabhaengig vom Spaltennamen.
    /// Wuerde das Praefix stattdessen aus dem Feldnamen abgeleitet, bekaeme
    /// derselbe Klartext in zwei Dateien mit abweichenden Spaltennamen zwei
    /// verschiedene Pseudonyme, und die dateiuebergreifende Verknuepfung
    /// braeche genau dort, wo sie heute garantiert ist (siehe
    /// <c>MultiFileTests.Auch_bei_verschiedenen_Spaltennamen_bleibt_die_Verknuepfung</c>).
    /// </summary>
    public string? Prefix { get; set; }
}
