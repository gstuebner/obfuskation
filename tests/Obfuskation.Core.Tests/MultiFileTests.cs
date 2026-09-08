using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Mehrere Dateien mit gemeinsamen Schluesselfeldern.
///
/// Der uebliche Fall: Stammdaten, Konten und Buchungen liegen in getrennten
/// Dateien und haengen ueber eine Personennummer zusammen. Wuerde diese Nummer
/// je Datei anders ersetzt, waeren die Testdaten wertlos — nichts liesse sich
/// mehr verknuepfen.
/// </summary>
public class MultiFileTests
{
    [Fact]
    public void Ein_Schluesselfeld_wird_ueber_alle_Dateien_gleich_ersetzt()
    {
        using var setup = new TestProfile()
            .WithField("Personennummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Kontostand", FieldAction.Passthrough)
            .WithField("Buchungstext", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var stammdaten = engine.Obfuscate(TestProfile.Utf8(
            "Personennummer;Name\n" +
            "4711;Max Mustermann\n" +
            "4712;Erika Musterfrau\n"), "stammdaten.csv", new RunOptions { Strict = true });

        var konten = engine.Obfuscate(TestProfile.Utf8(
            "Personennummer;Kontostand\n" +
            "4711;1234,56\n" +
            "4712;-89,90\n"), "konten.csv", new RunOptions { Strict = true });

        var nummernStamm = Spalte(stammdaten.Content, 0);
        var nummernKonten = Spalte(konten.Content, 0);

        // Dieselben Nummern in derselben Reihenfolge: die Verknuepfung bleibt.
        Assert.Equal(nummernStamm, nummernKonten);

        // Und die zweite Datei hat keine neuen Eintraege gebraucht.
        Assert.Equal(0, konten.Report.NewMappings);
    }

    [Fact]
    public void Auch_bei_verschiedenen_Spaltennamen_bleibt_die_Verknuepfung()
    {
        // In der einen Datei heisst die Spalte "Personennummer", in der
        // anderen "PersNr". Entscheidend ist nicht der Spaltenname, sondern
        // dass beide denselben Generator verwenden.
        using var setup = new TestProfile()
            .WithField("Personennummer", FieldAction.Pseudonymize, "numericId")
            .WithField("PersNr", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Betrag", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var eine = engine.Obfuscate(TestProfile.Utf8("Personennummer;Name\n4711;Max Mustermann\n"),
            "a.csv", new RunOptions { Strict = true });

        var andere = engine.Obfuscate(TestProfile.Utf8("PersNr;Betrag\n4711;10,00\n"),
            "b.csv", new RunOptions { Strict = true });

        Assert.Equal(Spalte(eine.Content, 0), Spalte(andere.Content, 0));
    }

    [Fact]
    public void Ein_praefigierter_Namensraum_verknuepft_ueber_verschiedene_Spaltennamen()
    {
        // Muster von Auch_bei_verschiedenen_Spaltennamen_bleibt_die_Verknuepfung,
        // diesmal mit einem konfigurierten Praefix: das Praefix haengt am
        // Generator-Eintrag, nicht am Feldnamen, also traegt es den Namensraum
        // unabhaengig davon, wie die Spalte in der jeweiligen Datei heisst.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["artikelKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Artikel~" };
        });

        setup.WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie")
             .WithField("Warengruppe", FieldAction.Pseudonymize, "artikelKategorie")
             .WithField("Menge", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var eine = engine.Obfuscate(TestProfile.Utf8("Artikelkategorie;Menge\nSchrauben;12\n"),
            "a.csv", new RunOptions { Strict = true });
        var andere = engine.Obfuscate(TestProfile.Utf8("Warengruppe;Menge\nSchrauben;5\n"),
            "b.csv", new RunOptions { Strict = true });

        var wertA = Spalte(eine.Content, 0)[0];
        var wertB = Spalte(andere.Content, 0)[0];

        Assert.Equal(wertA, wertB);
        Assert.StartsWith("Artikel~TOK_", wertA);

        // Der Klartext war aus der ersten Datei bereits bekannt — die zweite
        // Datei braucht keinen neuen Mapping-Eintrag.
        Assert.Equal(0, andere.Report.NewMappings);
    }

    [Fact]
    public void Zwei_Praefix_Namensraeume_trennen_gleichlautende_Werte()
    {
        // Gegenprobe zu obigem Test, im Muster von
        // Ein_eigener_Namensraum_trennt_gleichlautende_Nummern: derselbe
        // Klartext unter zwei verschiedenen Praefix-Namensraeumen darf nicht
        // dasselbe Pseudonym ergeben.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["artikelKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Artikel~" };
            profile.Generators["lieferantenKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Lieferant~" };
        });

        setup.WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie")
             .WithField("Lieferantenkategorie", FieldAction.Pseudonymize, "lieferantenKategorie");

        var ergebnis = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Artikelkategorie;Lieferantenkategorie\nSchrauben;Schrauben\n"),
            "a.csv", new RunOptions { Strict = true });

        var werte = TestProfile.FromUtf8(ergebnis.Content).Split('\n')[1].TrimEnd('\r').Split(';');

        Assert.NotEqual(werte[0], werte[1]);
        Assert.StartsWith("Artikel~", werte[0]);
        Assert.StartsWith("Lieferant~", werte[1]);
    }

    [Fact]
    public void Ein_eigener_Namensraum_trennt_gleichlautende_Nummern()
    {
        // Personennummer 4711 und Belegnummer 4711 sind verschiedene Dinge.
        // Mit demselben Generator bekaemen sie dasselbe Pseudonym und die
        // Testdaten zeigten eine Verbindung, die es nie gab. Ein eigener
        // Eintrag unter "generators" schafft einen zweiten Namensraum.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" };
        });

        setup.WithField("Personennummer", FieldAction.Pseudonymize, "numericId")
             .WithField("Belegnummer", FieldAction.Pseudonymize, "belegNummer");

        var ergebnis = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Personennummer;Belegnummer\n4711;4711\n"),
            "a.csv", new RunOptions { Strict = true });

        var werte = TestProfile.FromUtf8(ergebnis.Content).Split('\n')[1].TrimEnd('\r').Split(';');

        Assert.NotEqual(werte[0], werte[1]);
    }

    [Fact]
    public void Getrennt_geoeffnete_Profile_liefern_dasselbe_Ergebnis()
    {
        // So arbeitet die Oberflaeche: Datei oeffnen, verarbeiten, naechste
        // Datei oeffnen. Zwischendurch entsteht eine neue Engine — verbunden
        // sind die Laeufe allein ueber die Ersetzungstabelle auf der Platte.
        using var setup = new TestProfile()
            .WithField("Personennummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Betrag", FieldAction.Passthrough);

        var erste = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Personennummer;Name\n4711;Max Mustermann\n"),
            "a.csv", new RunOptions { Strict = true });

        var zweite = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Personennummer;Betrag\n4711;10,00\n"),
            "b.csv", new RunOptions { Strict = true });

        Assert.Equal(Spalte(erste.Content, 0), Spalte(zweite.Content, 0));
    }

    [Fact]
    public void Die_Rueckabbildung_gilt_ebenso_ueber_alle_Dateien()
    {
        using var setup = new TestProfile()
            .WithField("Personennummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Betrag", FieldAction.Passthrough);

        var engine = setup.CreateEngine();

        var stamm = "Personennummer;Name\n4711;Max Mustermann\n";
        var konto = "Personennummer;Betrag\n4711;10,00\n";

        var stammErsetzt = engine.Obfuscate(TestProfile.Utf8(stamm), "a.csv",
            new RunOptions { Strict = true });
        var kontoErsetzt = engine.Obfuscate(TestProfile.Utf8(konto), "b.csv",
            new RunOptions { Strict = true });

        Assert.Equal(stamm, TestProfile.FromUtf8(
            engine.Deobfuscate(stammErsetzt.Content, "a.csv", new RunOptions()).Content));
        Assert.Equal(konto, TestProfile.FromUtf8(
            engine.Deobfuscate(kontoErsetzt.Content, "b.csv", new RunOptions()).Content));
    }

    [Fact]
    public void Wordlist_verknuepft_gleiche_Klartexte_ueber_mehrere_Dateien()
    {
        using var setup = new TestProfile(profile =>
            profile.Generators["kategorie"] = new GeneratorSettings
            {
                Type = "wordlist",
                Values = ["Buero", "Lager", "Produktion", "Vertrieb", "Verwaltung"],
            });

        setup.WithField("Kategorie", FieldAction.Pseudonymize, "kategorie")
             .WithField("Warengruppe", FieldAction.Pseudonymize, "kategorie");

        var engine = setup.CreateEngine();

        var eine = engine.Obfuscate(TestProfile.Utf8("Kategorie\nSchrauben\n"),
            "a.csv", new RunOptions { Strict = true });
        var andere = engine.Obfuscate(TestProfile.Utf8("Warengruppe\nSchrauben\n"),
            "b.csv", new RunOptions { Strict = true });

        Assert.Equal(Spalte(eine.Content, 0), Spalte(andere.Content, 0));
        Assert.Equal(0, andere.Report.NewMappings);
    }

    [Fact]
    public void Pattern_verknuepft_gleiche_Klartexte_ueber_mehrere_Dateien()
    {
        using var setup = new TestProfile(profile =>
            profile.Generators["belegNummer"] = new GeneratorSettings { Type = "pattern", Pattern = "AA-9999" });

        setup.WithField("Belegnummer", FieldAction.Pseudonymize, "belegNummer")
             .WithField("Vorgangsnummer", FieldAction.Pseudonymize, "belegNummer");

        var engine = setup.CreateEngine();

        var eine = engine.Obfuscate(TestProfile.Utf8("Belegnummer\nRE-2024\n"),
            "a.csv", new RunOptions { Strict = true });
        var andere = engine.Obfuscate(TestProfile.Utf8("Vorgangsnummer\nRE-2024\n"),
            "b.csv", new RunOptions { Strict = true });

        Assert.Equal(Spalte(eine.Content, 0), Spalte(andere.Content, 0));
        Assert.Equal(0, andere.Report.NewMappings);
    }

    /// <summary>Die Werte einer Spalte, ohne die Kopfzeile.</summary>
    private static List<string> Spalte(byte[] content, int index)
        => TestProfile.FromUtf8(content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(zeile => zeile.TrimEnd('\r').Split(';')[index])
            .ToList();
}
