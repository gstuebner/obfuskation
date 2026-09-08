using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Eigenschaften der Pseudonym-Erzeugung: Bestaendigkeit, Trennung der
/// Namensraeume und das Verhalten bei Kollisionen.
/// </summary>
public class PseudonymTests
{
    private static TestProfile CsvProfile() =>
        new TestProfile()
            .WithField("Nummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Beleg", FieldAction.Pseudonymize, "token");

    [Fact]
    public void Derselbe_Wert_ergibt_ueber_getrennte_Laeufe_dasselbe_Pseudonym()
    {
        using var setup = CsvProfile();

        // Zwei voneinander unabhaengige Laeufe mit eigener Instanz — nur der
        // gemeinsame Bestand auf Platte verbindet sie.
        var ersteAusgabe = setup.CreateEngine()
            .Obfuscate(TestProfile.Utf8("Nummer;Name\n4711;Max Mustermann\n"), "a.csv",
                new RunOptions { Strict = true });

        var zweiteAusgabe = setup.CreateEngine()
            .Obfuscate(TestProfile.Utf8("Nummer;Name\n4711;Max Mustermann\n"), "b.csv",
                new RunOptions { Strict = true });

        Assert.Equal(TestProfile.FromUtf8(ersteAusgabe.Content), TestProfile.FromUtf8(zweiteAusgabe.Content));

        // Der zweite Lauf darf keine neuen Eintraege erzeugt haben.
        Assert.Equal(0, zweiteAusgabe.Report.NewMappings);
    }

    [Fact]
    public void Derselbe_Wert_in_zwei_Dateien_bekommt_dasselbe_Pseudonym()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        var ersteDatei = engine.Obfuscate(
            TestProfile.Utf8("Nummer;Name\n4711;Max Mustermann\n"), "a.csv", new RunOptions { Strict = true });

        // Zweite Datei, andere Spaltenreihenfolge, derselbe Kunde.
        var zweiteDatei = engine.Obfuscate(
            TestProfile.Utf8("Name;Nummer\nMax Mustermann;4711\n"), "b.csv", new RunOptions { Strict = true });

        var ersteZeile = TestProfile.FromUtf8(ersteDatei.Content).Split('\n')[1].Split(';');
        var zweiteZeile = TestProfile.FromUtf8(zweiteDatei.Content).Split('\n')[1].Split(';');

        Assert.Equal(ersteZeile[0], zweiteZeile[1]);  // Nummer
        Assert.Equal(ersteZeile[1].TrimEnd('\r'), zweiteZeile[0]);  // Name
    }

    [Fact]
    public void Gleicher_Klartext_in_zwei_Namensraeumen_ergibt_verschiedene_Pseudonyme()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        // "4711" einmal als Kundennummer, einmal als Belegnummer. Getrennte
        // Namensraeume verhindern, dass die beiden verwechselt werden.
        var ausgabe = engine.Obfuscate(
            TestProfile.Utf8("Nummer;Beleg\n4711;4711\n"), "a.csv", new RunOptions { Strict = true });

        var werte = TestProfile.FromUtf8(ausgabe.Content).Split('\n')[1].TrimEnd('\r').Split(';');
        Assert.NotEqual(werte[0], werte[1]);
    }

    [Fact]
    public void Ein_bereits_vergebenes_Pseudonym_wird_nicht_erneut_verwendet()
    {
        using var setup = CsvProfile();

        // Der Bestand wird so vorbelegt, dass der erste Ableitungsversuch fuer
        // "4711" auf einen bereits vergebenen Wert trifft.
        var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName);
        var deriver = new SeedDeriver(store.Salt);
        var generators = GeneratorRegistry.Build(setup.Profile, deriver);

        var ersterVersuch = generators.Get("numericId").Generate(deriver.Derive("numericId", "4711", 0), "4711");
        store.Add("numericId", "9999", ersterVersuch);
        store.Save();
        store.Dispose();

        var ausgabe = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Nummer\n4711\n"), "a.csv", new RunOptions { Strict = true });

        var pseudonym = TestProfile.FromUtf8(ausgabe.Content).Split('\n')[1].TrimEnd('\r');

        // Der Zaehler muss gegriffen und einen anderen Wert geliefert haben.
        Assert.NotEqual(ersterVersuch, pseudonym);

        // Und die Rueckabbildung muss trotzdem stimmen.
        var zurueck = setup.CreateEngine().Deobfuscate(ausgabe.Content, "a.csv", new RunOptions());
        Assert.Equal("Nummer\n4711\n", TestProfile.FromUtf8(zurueck.Content));
    }

    [Fact]
    public void Ein_erschoepfter_Wertevorrat_nennt_die_zum_Generator_passende_Abhilfe()
    {
        // Drei Werte, vier Klartexte: der vierte kann kein freies Pseudonym
        // mehr finden. Der Abbruch ist richtig -- ein stilles Duplikat waere
        // der schlimmere Ausgang. Die Meldung muss aber zur Ursache fuehren,
        // und die liegt hier in der eigenen Werteliste, nicht in der Wahl des
        // Generators.
        using var setup = new TestProfile()
            .WithField("Abteilung", FieldAction.Pseudonymize, "abteilung");

        setup.Profile.Generators["abteilung"] = new GeneratorSettings
        {
            Type = "wordlist",
            Values = ["Nord", "Sued", "West"],
        };

        var ex = Assert.Throws<MappingConflictException>(() => setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Abteilung\nEins\nZwei\nDrei\nVier\n"), "a.csv",
            new RunOptions { Strict = true }));

        Assert.Contains("values", ex.Message);
        Assert.DoesNotContain("token", ex.Message);
    }

    [Fact]
    public void Auch_bei_engem_Wertevorrat_bleibt_jedes_Pseudonym_eindeutig()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        // 300 fuenfstellige Werte bei 100.000 moeglichen: Zusammenstoesse sind
        // hier nicht die Ausnahme, sondern der Normalfall. Genau das soll der
        // Ausweichzaehler abfangen.
        var zeilen = new List<string> { "Nummer" };
        for (var i = 0; i < 300; i++)
            zeilen.Add((10000 + i).ToString());

        var ausgabe = engine.Obfuscate(
            TestProfile.Utf8(string.Join("\n", zeilen) + "\n"), "a.csv", new RunOptions { Strict = true });

        using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName,
            readOnly: true);

        var pseudonyme = store.Entries("numericId").Values.ToHashSet(StringComparer.Ordinal);

        // Das ist die Zusage, auf der die Rueckabbildung beruht: kein Pseudonym
        // wird zweimal vergeben.
        Assert.Equal(300, pseudonyme.Count);

        // Und der Roundtrip stimmt fuer jede einzelne Zeile.
        var zurueck = setup.CreateEngine().Deobfuscate(ausgabe.Content, "a.csv", new RunOptions());
        Assert.Equal(string.Join("\n", zeilen) + "\n", TestProfile.FromUtf8(zurueck.Content));
    }

    [Fact]
    public void Leere_Werte_bleiben_leer()
    {
        using var setup = CsvProfile();
        var engine = setup.CreateEngine();

        var ausgabe = engine.Obfuscate(
            TestProfile.Utf8("Nummer;Name\n;\n"), "a.csv", new RunOptions { Strict = true });

        Assert.Equal("Nummer;Name\n;\n", TestProfile.FromUtf8(ausgabe.Content));
    }
}
