using Obfuskation.Core;

namespace Obfuskation.Core.Configuration;

/// <summary>
/// Erzeugt ein Regelgeruest aus einer vorhandenen Datei.
///
/// Bewusst bleibt jede Spalte auf <c>error</c> stehen, auch wenn der Name einen
/// Generator nahelegt: die Entscheidung, was mit einem Feld geschieht, soll ein
/// Mensch treffen. Der Vorschlag steht daneben und muss nur bestaetigt werden.
/// </summary>
public static class ProfileScaffolder
{
    /// <summary>Namensbestandteile, die einen Generator nahelegen.</summary>
    private static readonly (string Fragment, string Generator)[] Hints =
    [
        ("iban", "iban"),
        ("bic", "bic"),
        ("swift", "bic"),
        ("mail", "email"),
        ("email", "email"),
        ("telefon", "phone"),
        ("phone", "phone"),
        ("mobil", "phone"),
        ("fax", "phone"),
        ("nachname", "lastName"),
        ("familienname", "lastName"),
        ("vorname", "firstName"),
        ("kundenname", "personName"),
        ("inhaber", "personName"),
        ("name", "personName"),
        ("firma", "companyName"),
        ("unternehmen", "companyName"),
        ("strasse", "street"),
        ("straße", "street"),
        ("adresse", "street"),
        ("anschrift", "street"),
        ("plz", "postalCode"),
        ("postleitzahl", "postalCode"),
        ("ort", "city"),
        ("stadt", "city"),
        ("wohnort", "city"),
        ("geburtsdatum", "dateShift"),
        ("geburtstag", "dateShift"),
        ("datum", "dateShift"),
        ("date", "dateShift"),
        ("nummer", "numericId"),
        ("nr", "numericId"),
        ("id", "numericId"),
        ("konto", "numericId"),
        ("verwendungszweck", "scanText"),
        ("bemerkung", "scanText"),
        ("notiz", "scanText"),
        ("kommentar", "scanText"),
    ];

    public static Profile Create(string profileName, string? sampleFilePath, string? description = null)
    {
        var profile = new Profile
        {
            ProfileName = profileName,
            Description = description,
            MappingStore = PathHelper.DefaultMappingStorePath(profileName),
            TextRules = DefaultTextRules(),
        };

        if (string.IsNullOrWhiteSpace(sampleFilePath))
            return profile;

        var fieldNames = ReadFieldNames(sampleFilePath);
        foreach (var fieldName in fieldNames)
            profile.Fields.Add(CreateRule(fieldName));

        return profile;
    }

    private static FieldRule CreateRule(string fieldName)
    {
        var suggestion = Suggest(fieldName);

        if (suggestion == "scanText")
        {
            return new FieldRule
            {
                Match = fieldName,
                MatchType = FieldMatchType.Exact,
                Action = FieldAction.Error,
                Comment = "Vorschlag: scanText (Freitext). Sicherer waere redact oder drop — " +
                          "Muster finden in Freitext nicht alles. action anpassen.",
            };
        }

        return new FieldRule
        {
            Match = fieldName,
            MatchType = FieldMatchType.Exact,
            Action = FieldAction.Error,
            Generator = suggestion,
            Comment = suggestion is null
                ? "Kein Vorschlag ableitbar. action auf pseudonymize, passthrough, redact oder drop setzen."
                : $"Vorschlag: pseudonymize mit '{suggestion}'. Zum Uebernehmen action auf pseudonymize setzen.",
        };
    }

    /// <summary>
    /// Schlaegt anhand des Feldnamens einen Generator vor, oder <c>null</c>,
    /// wenn sich nichts ableiten laesst. Der Sonderwert <c>"scanText"</c> steht
    /// fuer ein Freitextfeld und ist kein Generatorname.
    ///
    /// Oeffentlich, damit die Oberflaeche dieselbe Zuordnung verwendet wie
    /// <c>init</c> — zwei getrennte Listen wuerden auseinanderlaufen.
    /// </summary>
    public static string? Suggest(string fieldName)
    {
        var normalized = fieldName.ToLowerInvariant();

        foreach (var (fragment, generator) in Hints)
            if (normalized.Contains(fragment, StringComparison.Ordinal))
                return generator;

        return null;
    }

    /// <summary>
    /// Liest die Feldnamen einer Datei. Duenne Huelle um
    /// <see cref="FieldInspector"/> — dieselbe Erkennung, die auch die
    /// Oberflaeche verwendet.
    /// </summary>
    public static IReadOnlyList<string> ReadFieldNames(string path)
        => FieldInspector.InspectFile(path).FieldNames;

    /// <summary>
    /// Ein brauchbarer Grundstock an Mustern. Bewusst zurueckhaltend gehalten:
    /// zu weit gefasste Muster erzeugen Fehltreffer und untergraben das
    /// Vertrauen in die Ausgabe.
    ///
    /// Die Muster sind ein Ausgangspunkt, keine Gewaehr. Sie gehoeren an den
    /// eigenen Datenbestand angepasst und mit "scan" nachgeprueft.
    /// </summary>
    public static List<TextRule> DefaultTextRules() =>
    [
        new TextRule
        {
            Name = "iban",
            Priority = 100,
            Generator = "iban",
            Pattern = @"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){2,7}(?:[ ]?[A-Z0-9]{1,4})?\b",
        },
        new TextRule
        {
            Name = "email",
            Priority = 90,
            Generator = "email",
            Pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        },
        new TextRule
        {
            Name = "bic",
            Priority = 85,
            Generator = "bic",
            Pattern = @"\b[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}(?:[A-Z0-9]{3})?\b",
        },
        new TextRule
        {
            Name = "phone",
            Priority = 80,
            Generator = "phone",

            // Der Blick nach hinten ist hier wesentlich: ohne ihn faengt das
            // Muster mitten in einer Rechnungsnummer wie "2024-0815" an und
            // erklaert deren Rest zur Telefonnummer.
            Pattern = @"(?<![\w\-/])\(?(?:\+49|0)[ \-/)]?\d[\d \-/()]{5,20}\d",
        },
    ];
}
