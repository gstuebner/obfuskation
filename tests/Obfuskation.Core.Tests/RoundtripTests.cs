using System.Text;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Der Roundtrip ist die Kernzusage des Werkzeugs: was ersetzt wurde, muss sich
/// vollstaendig zurueckholen lassen. Schlaegt einer dieser Tests fehl, ist das
/// Werkzeug nicht benutzbar, gleich was sonst funktioniert.
/// </summary>
public class RoundtripTests
{
    private static TestProfile CsvProfile() =>
        new TestProfile()
            .WithField("Kundennummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("IBAN", FieldAction.Pseudonymize, "iban")
            .WithField("Geburtsdatum", FieldAction.Pseudonymize, "dateShift")
            .WithField("Betrag", FieldAction.Passthrough)
            .WithField("Zweck", FieldAction.ScanText);

    [Fact]
    public void Csv_mit_Semikolon_und_Anfuehrungszeichen_kommt_unveraendert_zurueck()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        // Das Feld enthaelt das Trennzeichen selbst — der klassische Fall, an dem
        // eine Verarbeitung mit regulaeren Ausdruecken die Datei zerstoert.
        var original =
            "Kundennummer;Name;IBAN;Geburtsdatum;Betrag;Zweck\r\n" +
            "4711;Max Mustermann;DE02120300000000202051;15.03.1980;1234,56;\"Miete; Maerz\"\r\n" +
            "4712;Erika Musterfrau;DE02500105170137075030;22.11.1975;-89,90;Rueckzahlung\r\n";

        var content = TestProfile.Utf8(original);
        var obfuscated = engine.Obfuscate(content, "kunden.csv", new RunOptions { Strict = true });
        var restored = engine.Deobfuscate(obfuscated.Content, "kunden.csv", new RunOptions());

        Assert.Equal(original, TestProfile.FromUtf8(restored.Content));
    }

    [Fact]
    public void Csv_mit_Zeilenumbruch_im_Feld_bleibt_unversehrt()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        var original =
            "Kundennummer;Name;IBAN;Geburtsdatum;Betrag;Zweck\r\n" +
            "4711;Max Mustermann;DE02120300000000202051;15.03.1980;1,00;\"Zeile eins\r\nZeile zwei\"\r\n";

        var content = TestProfile.Utf8(original);
        var obfuscated = engine.Obfuscate(content, "kunden.csv", new RunOptions { Strict = true });
        var restored = engine.Deobfuscate(obfuscated.Content, "kunden.csv", new RunOptions());

        Assert.Equal(original, TestProfile.FromUtf8(restored.Content));
    }

    [Fact]
    public void Windows1252_bleibt_Windows1252()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var windows1252 = Encoding.GetEncoding(1252);

        var original =
            "Kundennummer;Name;IBAN;Geburtsdatum;Betrag;Zweck\n" +
            "4711;Jürgen Groß;DE02120300000000202051;15.03.1980;1,00;Rückzahlung für März\n";

        var content = windows1252.GetBytes(original);
        var obfuscated = engine.Obfuscate(content, "kunden.csv", new RunOptions { Strict = true });

        Assert.Equal("windows-1252", obfuscated.Report.Encoding);

        var restored = engine.Deobfuscate(obfuscated.Content, "kunden.csv", new RunOptions());
        Assert.Equal(content, restored.Content);
    }

    [Fact]
    public void Utf8_mit_Byte_Reihenfolge_Markierung_behaelt_sie()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        var original = "Kundennummer;Name;IBAN;Geburtsdatum;Betrag;Zweck\n" +
                       "4711;Max Mustermann;DE02120300000000202051;15.03.1980;1,00;Test\n";

        // GetBytes liefert die Markierung nicht mit; sie muss ausdruecklich davor.
        var content = Encoding.UTF8.GetPreamble()
            .Concat(new UTF8Encoding(false).GetBytes(original))
            .ToArray();
        Assert.Equal(0xEF, content[0]);

        var obfuscated = engine.Obfuscate(content, "kunden.csv", new RunOptions { Strict = true });

        Assert.Equal(0xEF, obfuscated.Content[0]);
        Assert.Equal(0xBB, obfuscated.Content[1]);
        Assert.Equal(0xBF, obfuscated.Content[2]);

        var restored = engine.Deobfuscate(obfuscated.Content, "kunden.csv", new RunOptions());
        Assert.Equal(content, restored.Content);
    }

    [Fact]
    public void Json_kommt_mit_erhaltener_Struktur_zurueck()
    {
        using var setup = new TestProfile()
            .WithField("kunden", FieldAction.Passthrough)
            .WithField("name", FieldAction.Pseudonymize, "personName")
            .WithField("iban", FieldAction.Pseudonymize, "iban")
            .WithField("betrag", FieldAction.Passthrough)
            .WithField("aktiv", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var original = """
            {
              "kunden": [
                { "name": "Max Mustermann", "iban": "DE02120300000000202051", "betrag": 1234.56, "aktiv": true },
                { "name": "Erika Musterfrau", "iban": "DE02500105170137075030", "betrag": -89.9, "aktiv": false }
              ]
            }
            """;

        var content = TestProfile.Utf8(original);
        var obfuscated = engine.Obfuscate(content, "kunden.json", new RunOptions { Strict = true });
        var restored = engine.Deobfuscate(obfuscated.Content, "kunden.json", new RunOptions());

        // Verglichen wird der Inhalt, nicht die Formatierung: die Einrueckung
        // wird bewusst nur naeherungsweise uebernommen.
        Assert.Equal(Normalize(original), Normalize(TestProfile.FromUtf8(restored.Content)));
    }

    [Fact]
    public void Fliesstext_mit_eingebettetem_Quelltext_kommt_zurueck()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        // Erst einen Bestand aufbauen, denn die Rueckabbildung im Fliesstext
        // arbeitet ueber die Ersetzungstabelle.
        var csv = "Kundennummer;Name;IBAN;Geburtsdatum;Betrag;Zweck\n" +
                  "4711;Max Mustermann;DE02120300000000202051;15.03.1980;1,00;Test\n";
        engine.Obfuscate(TestProfile.Utf8(csv), "kunden.csv", new RunOptions { Strict = true });

        var antwort = """
            Der Kunde Max Mustermann (Nummer 4711) hat gebucht.

                var kunde = repo.Find("4711");
                Assert.Equal("Max Mustermann", kunde.Name);
                Assert.Equal("DE02120300000000202051", kunde.Iban);
            """;

        var obfuscated = engine.Obfuscate(TestProfile.Utf8(antwort), "antwort.txt", new RunOptions());
        var pseudonymisiert = TestProfile.FromUtf8(obfuscated.Content);

        // Die IBAN wird von der Textregel erfasst; Name und Nummer nicht, weil
        // fuer sie kein Muster existiert — genau die dokumentierte Grenze.
        Assert.DoesNotContain("DE02120300000000202051", pseudonymisiert);

        var restored = engine.Deobfuscate(obfuscated.Content, "antwort.txt", new RunOptions());
        Assert.Equal(antwort, TestProfile.FromUtf8(restored.Content));
    }

    /// <summary>Serialisiert JSON ohne Leerraum neu, damit nur der Inhalt zaehlt.</summary>
    private static string Normalize(string json)
        => System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json));
}
