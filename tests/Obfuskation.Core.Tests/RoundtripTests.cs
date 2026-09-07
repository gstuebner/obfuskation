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
    public void Ein_praefigierter_Token_Namensraum_kommt_unveraendert_zurueck()
    {
        // Das Praefix ist reine Kosmetik am Generatorausgang: der Vollstring
        // (samt Praefix) steht im Mapping und wird beim Zurueckuebersetzen
        // exakt so nachgeschlagen.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["artikelKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Artikelkategorie~" };
        }).WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie")
          .WithField("Menge", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var original = "Artikelkategorie;Menge\nSchrauben;12\nMuttern;7\n";

        var obfuscated = engine.Obfuscate(TestProfile.Utf8(original), "artikel.csv",
            new RunOptions { Strict = true });
        var pseudonymisiert = TestProfile.FromUtf8(obfuscated.Content);

        Assert.Contains("Artikelkategorie~TOK_", pseudonymisiert);

        var restored = engine.Deobfuscate(obfuscated.Content, "artikel.csv", new RunOptions());
        Assert.Equal(original, TestProfile.FromUtf8(restored.Content));
    }

    [Fact]
    public void Ein_nachtraeglich_gesetztes_Praefix_laesst_alte_Eintraege_unveraendert()
    {
        // Anwenderdokumentation, Abschnitt "Lesbare Tokens: Praefix",
        // erster Fallstrick: wird das Praefix erst gesetzt, nachdem schon
        // Eintraege ohne Praefix im Mapping stehen, bleiben die alten
        // unpraefigiert und nur neue Werte bekommen eines -- der Bestand
        // liest sich gemischt, aber beide Arten von Eintraegen muessen sich
        // weiterhin zurueckuebersetzen lassen, weil DeobfuscateTransformer
        // den kompletten gespeicherten String nachschlaegt, nicht ein
        // erwartetes Format. Genau das behauptet die Dokumentation, also
        // muss es hier belegt sein. A4 hat das nur am Code geprueft
        // (Kommentar in GeneratorSettings.Prefix), nicht mit einem Test.
        using var setup = new TestProfile(profile =>
                profile.Generators["artikelKategorie"] = new GeneratorSettings { Type = "token" })
            .WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie")
            .WithField("Menge", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var ersterLauf = "Artikelkategorie;Menge\nSchrauben;12\nMuttern;7\n";
        var ersteAusgabe = engine.Obfuscate(TestProfile.Utf8(ersterLauf), "artikel1.csv",
            new RunOptions { Strict = true });
        var ersteZeilen = TestProfile.FromUtf8(ersteAusgabe.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain(ersteZeilen, zeile => zeile.Contains('~'));

        // Praefix erst jetzt setzen -- auf demselben Mapping-Bestand, den der
        // erste Lauf gerade angelegt hat.
        setup.Profile.Generators["artikelKategorie"].Prefix = "Artikelkategorie~";

        var zweiterLauf = "Artikelkategorie;Menge\nSchrauben;12\nNaegel;500\n";
        var zweiteAusgabe = engine.Obfuscate(TestProfile.Utf8(zweiterLauf), "artikel2.csv",
            new RunOptions { Strict = true });
        var zweiteZeilen = TestProfile.FromUtf8(zweiteAusgabe.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // "Schrauben" stand schon vor dem Setzen des Praefixes im Bestand --
        // sein Pseudonym bleibt exakt dasselbe wie im ersten Lauf, also ohne
        // Praefix.
        Assert.Equal(ersteZeilen[1], zweiteZeilen[1]);
        Assert.DoesNotContain('~', zweiteZeilen[1]);

        // "Naegel" ist neu -- sein Pseudonym entsteht erst nach dem Setzen
        // des Praefixes und traegt es deshalb.
        Assert.StartsWith("Artikelkategorie~TOK_", zweiteZeilen[2]);

        // Beide Ausgaben lassen sich trotz des gemischten Bestands vollstaendig
        // zurueckuebersetzen.
        var ersteRueckuebersetzung = engine.Deobfuscate(ersteAusgabe.Content, "artikel1.csv", new RunOptions());
        Assert.Equal(ersterLauf, TestProfile.FromUtf8(ersteRueckuebersetzung.Content));

        var zweiteRueckuebersetzung = engine.Deobfuscate(zweiteAusgabe.Content, "artikel2.csv", new RunOptions());
        Assert.Equal(zweiterLauf, TestProfile.FromUtf8(zweiteRueckuebersetzung.Content));
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

    [Fact]
    public void Praefigierte_Tokens_werden_im_Fliesstext_zurueckuebersetzt()
    {
        // Der Alltagsfall aus der Aufgabenstellung: eine KI-Antwort zitiert das
        // lesbare Pseudonym samt Praefix, und die Rueckuebersetzung muss den
        // Klartext trotzdem finden. ReverseTextMapper baut sein Suchmuster aus
        // den tatsaechlich gespeicherten Pseudonymen und schuetzt jeden Eintrag
        // mit Regex.Escape — auch das '~' im Praefix ist damit sicher.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["artikelKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Artikel~" };
        }).WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie");

        var engine = setup.CreateEngine();

        var csv = "Artikelkategorie\nSchrauben\n";
        var obfuscated = engine.Obfuscate(TestProfile.Utf8(csv), "artikel.csv",
            new RunOptions { Strict = true });

        var pseudonym = TestProfile.FromUtf8(obfuscated.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].TrimEnd('\r');
        Assert.StartsWith("Artikel~TOK_", pseudonym);

        var antwort = $"Die Kategorie {pseudonym} enthaelt sieben Positionen.";

        var restored = engine.Deobfuscate(TestProfile.Utf8(antwort), "antwort.txt", new RunOptions());

        Assert.Equal("Die Kategorie Schrauben enthaelt sieben Positionen.",
            TestProfile.FromUtf8(restored.Content));
    }

    /// <summary>Serialisiert JSON ohne Leerraum neu, damit nur der Inhalt zaehlt.</summary>
    private static string Normalize(string json)
        => System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json));
}
