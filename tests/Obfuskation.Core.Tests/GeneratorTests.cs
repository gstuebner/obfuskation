using System.Globalization;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Eigenschaften der einzelnen Generatoren. Entscheidend ist hier nicht, dass
/// ein bestimmter Wert herauskommt, sondern dass die Form des Originals erhalten
/// bleibt — sonst sind die Testdaten fuer die abnehmende Anwendung wertlos.
/// </summary>
public class GeneratorTests
{
    private static SeedDeriver Deriver() => new(new byte[32]);

    private static byte[] Seed(string value, int counter = 0)
        => Deriver().Derive("test", value, counter);

    [Theory]
    [InlineData("DE02120300000000202051")]
    [InlineData("DE02500105170137075030")]
    [InlineData("AT611904300234573201")]
    [InlineData("CH9300762011623852957")]
    public void Erzeugte_Ibans_bestehen_die_Pruefziffernrechnung(string original)
    {
        var generator = new IbanGenerator();
        var erzeugt = generator.Generate(Seed(original), original);

        Assert.True(IbanGenerator.IsValid(erzeugt), $"Ungueltige IBAN erzeugt: {erzeugt}");
        Assert.Equal(original.Length, erzeugt.Length);
        Assert.Equal(original[..2], erzeugt[..2]);
        Assert.NotEqual(original, erzeugt);
    }

    [Fact]
    public void Die_Pruefziffernrechnung_erkennt_eine_verfaelschte_Iban()
    {
        Assert.True(IbanGenerator.IsValid("DE02120300000000202051"));

        // Eine einzelne veraenderte Ziffer muss auffallen.
        Assert.False(IbanGenerator.IsValid("DE02120300000000202052"));
    }

    [Fact]
    public void Gruppierte_Ibans_behalten_ihre_Gruppierung()
    {
        var generator = new IbanGenerator();
        var original = "DE02 1203 0000 0000 2020 51";
        var erzeugt = generator.Generate(Seed(original), original);

        Assert.Contains(' ', erzeugt);
        Assert.True(IbanGenerator.IsValid(erzeugt));
    }

    [Theory]
    [InlineData("4711", 4)]
    [InlineData("000123", 6)]
    [InlineData("1234567890", 10)]
    public void Zahlenkennungen_behalten_ihre_Stellenzahl(string original, int erwarteteLaenge)
    {
        var generator = new NumericIdGenerator();
        var erzeugt = generator.Generate(Seed(original), original);

        Assert.Equal(erwarteteLaenge, erzeugt.Length);
        Assert.All(erzeugt, character => Assert.True(char.IsDigit(character)));
    }

    [Theory]
    [InlineData("+49 30 12345678")]
    [InlineData("030/1234-5678")]
    [InlineData("(030) 12345678")]
    public void Telefonnummern_behalten_ihre_Gliederung(string original)
    {
        var generator = new PhoneGenerator();
        var erzeugt = generator.Generate(Seed(original), original);

        Assert.Equal(original.Length, erzeugt.Length);

        // An jeder Stelle steht wieder derselbe Zeichentyp.
        for (var i = 0; i < original.Length; i++)
            Assert.Equal(char.IsDigit(original[i]), char.IsDigit(erzeugt[i]));
    }

    [Fact]
    public void Erzeugte_Adressen_liegen_unter_einer_reservierten_Domain()
    {
        var generator = new EmailGenerator();
        var erzeugt = generator.Generate(Seed("max@beispiel.de"), "max@beispiel.de");

        // ".invalid" ist per RFC 2606 reserviert und kann keine echte Adresse sein.
        Assert.EndsWith("@example.invalid", erzeugt);
        Assert.Contains('@', erzeugt);
    }

    [Fact]
    public void Die_Datumsverschiebung_erhaelt_Reihenfolge_und_Abstaende()
    {
        var generator = new DateShiftGenerator();
        generator.SetOffsetFrom(Deriver());

        var daten = new[] { "01.01.2020", "15.03.2020", "31.12.2020" };
        var verschoben = daten.Select(d => generator.Generate(Seed(d), d)).ToArray();

        var originalWerte = daten.Select(Parse).ToArray();
        var verschobeneWerte = verschoben.Select(Parse).ToArray();

        // Gleiche Abstaende zwischen allen Paaren.
        Assert.Equal(originalWerte[1] - originalWerte[0], verschobeneWerte[1] - verschobeneWerte[0]);
        Assert.Equal(originalWerte[2] - originalWerte[1], verschobeneWerte[2] - verschobeneWerte[1]);

        // Und die Reihenfolge bleibt.
        Assert.True(verschobeneWerte[0] < verschobeneWerte[1]);
        Assert.True(verschobeneWerte[1] < verschobeneWerte[2]);

        static DateTime Parse(string value)
            => DateTime.ParseExact(value, "dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Die_Datumsverschiebung_laesst_sich_ohne_Tabelle_zurueckrechnen()
    {
        var generator = new DateShiftGenerator();
        generator.SetOffsetFrom(Deriver());

        var verschoben = generator.Generate(Seed("15.03.1980"), "15.03.1980");
        Assert.True(generator.TryInvert(verschoben, out var zurueck));
        Assert.Equal("15.03.1980", zurueck);
    }

    [Fact]
    public void Die_Datumsverschiebung_meldet_einen_unlesbaren_Wert()
    {
        var generator = new DateShiftGenerator();
        generator.SetOffsetFrom(Deriver());

        // Stillschweigend durchreichen waere hier gefaehrlich: der Echtwert
        // bliebe stehen, ohne dass es jemand bemerkt.
        var ex = Assert.Throws<GenerationException>(
            () => generator.Generate(Seed("kein Datum"), "kein Datum"));

        Assert.Contains("Datumsformate", ex.Message);
    }

    [Fact]
    public void Die_Datumsverschiebung_ist_niemals_null_Tage()
    {
        // Eine Verschiebung um null Tage waere gar keine Verschiebung; das darf
        // auch bei ungluecklichem Salt nicht herauskommen.
        for (var i = 0; i < 50; i++)
        {
            var salt = new byte[32];
            salt[0] = (byte)i;
            salt[1] = (byte)(i * 7);

            var generator = new DateShiftGenerator();
            generator.SetOffsetFrom(new SeedDeriver(salt));

            Assert.NotEqual(0, generator.OffsetDays);
        }
    }

    [Fact]
    public void Der_Platzhalter_gilt_ausdruecklich_als_nicht_umkehrbar()
    {
        var generator = new RedactGenerator();
        Assert.False(generator.IsReversible);
        Assert.Equal("***", generator.Generate(Seed("geheim"), "geheim"));
    }

    [Fact]
    public void Alle_eingebauten_Generatoren_arbeiten_bestaendig()
    {
        var profile = new Profile();
        var deriver = Deriver();
        var registry = GeneratorRegistry.Build(profile, deriver);

        foreach (var (name, generator) in registry.All)
        {
            var seed = deriver.Derive(name, "Testwert 12345", 0);

            // Datumswerte brauchen eine lesbare Vorlage.
            var original = generator is DateShiftGenerator ? "15.03.1980" : "Testwert 12345";

            var ersterLauf = generator.Generate(seed, original);
            var zweiterLauf = generator.Generate(seed, original);

            Assert.Equal(ersterLauf, zweiterLauf);
            Assert.False(string.IsNullOrEmpty(ersterLauf), $"Generator '{name}' lieferte nichts.");
        }
    }
}
