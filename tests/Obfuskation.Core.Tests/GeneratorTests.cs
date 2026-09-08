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
    public void Ein_konfiguriertes_Praefix_steht_vor_dem_Token()
    {
        var generator = new TokenGenerator();
        generator.Configure(new GeneratorSettings { Prefix = "Artikel~" });

        var erzeugt = generator.Generate(Seed("Kategorie A"), "Kategorie A");

        Assert.StartsWith("Artikel~TOK_", erzeugt);
    }

    [Fact]
    public void Ohne_Praefix_bleibt_es_beim_bekannten_Format()
    {
        // Abwaertskompatibilitaet: ein Profil ohne "prefix" im Generator-Eintrag
        // darf sich nicht anders verhalten als vor dieser Funktion.
        var generator = new TokenGenerator();
        generator.Configure(new GeneratorSettings());

        var erzeugt = generator.Generate(Seed("Kategorie A"), "Kategorie A");

        Assert.StartsWith("TOK_", erzeugt);
    }

    [Fact]
    public void Derselbe_Klartext_liefert_zweimal_denselben_praefigierten_Wert()
    {
        var generator = new TokenGenerator();
        generator.Configure(new GeneratorSettings { Prefix = "Artikel~" });

        var seed = Seed("Kategorie A");
        Assert.Equal(generator.Generate(seed, "Kategorie A"), generator.Generate(seed, "Kategorie A"));
    }

    [Fact]
    public void Alle_eingebauten_Generatoren_arbeiten_bestaendig()
    {
        var profile = new Profile();

        // wordlist ist der einzige Generator mit einer echten Pflichtoption:
        // ohne "values" wirft er eine GenerationException (siehe
        // Wordlist_ohne_Werte_meldet_einen_Fehler unten). Damit dieser
        // Bestandstest trotzdem alle eingebauten Generatoren durchlaufen kann,
        // bekommt er hier eine Minimalkonfiguration -- kein Schein-Default im
        // Generator selbst, nur diese eine Ausnahme im Test.
        profile.Generators["wordlist"] = new GeneratorSettings { Values = ["Alpha", "Beta", "Gamma"] };

        var deriver = Deriver();
        var registry = GeneratorRegistry.Build(profile, deriver);

        foreach (var (name, generator) in registry.All)
        {
            var seed = deriver.Derive(name, "Testwert 12345", 0);

            // Datumswerte brauchen eine lesbare Vorlage.
            var original = generator is DateShiftGenerator or DateRangeGenerator or DateGeneralizeGenerator
                ? "15.03.1980"
                : "Testwert 12345";

            var ersterLauf = generator.Generate(seed, original);
            var zweiterLauf = generator.Generate(seed, original);

            Assert.Equal(ersterLauf, zweiterLauf);
            Assert.False(string.IsNullOrEmpty(ersterLauf), $"Generator '{name}' lieferte nichts.");
        }
    }

    [Fact]
    public void DateRange_bleibt_ohne_Konfiguration_im_Kalenderjahr_des_Originals()
    {
        var generator = new DateRangeGenerator();

        for (var i = 0; i < 30; i++)
        {
            var erzeugt = generator.Generate(Seed("Original", i), "15.03.1980");
            var datum = DateTime.ParseExact(erzeugt, "dd.MM.yyyy", CultureInfo.InvariantCulture);

            Assert.Equal(1980, datum.Year);
        }
    }

    [Fact]
    public void DateRange_haelt_sich_an_einen_konfigurierten_Zeitraum()
    {
        var generator = new DateRangeGenerator();
        generator.Configure(new GeneratorSettings { From = "2000-01-01", To = "2000-01-31" });

        for (var i = 0; i < 30; i++)
        {
            var erzeugt = generator.Generate(Seed("Original", i), "15.03.1980");
            var datum = DateTime.ParseExact(erzeugt, "dd.MM.yyyy", CultureInfo.InvariantCulture);

            Assert.InRange(datum, new DateTime(2000, 1, 1), new DateTime(2000, 1, 31));
        }
    }

    [Fact]
    public void DateRange_ist_deterministisch()
    {
        var generator = new DateRangeGenerator();
        var seed = Seed("15.03.1980");

        Assert.Equal(generator.Generate(seed, "15.03.1980"), generator.Generate(seed, "15.03.1980"));
    }

    [Theory]
    [InlineData("month", "01.03.2021")]
    [InlineData("quarter", "01.01.2021")]
    [InlineData("year", "01.01.2021")]
    public void DateGeneralize_rundet_je_Granularitaet_auf_den_Anfang(string granularity, string erwartet)
    {
        var generator = new DateGeneralizeGenerator();
        generator.Configure(new GeneratorSettings { Granularity = granularity });

        var erzeugt = generator.Generate(Seed("15.03.2021"), "15.03.2021");

        Assert.Equal(erwartet, erzeugt);
    }

    [Fact]
    public void DateGeneralize_ist_ausdruecklich_nicht_umkehrbar()
    {
        var generator = new DateGeneralizeGenerator();
        Assert.False(generator.IsReversible);
    }

    [Fact]
    public void Pattern_folgt_einer_gesetzten_Maske()
    {
        var generator = new PatternGenerator();
        generator.Configure(new GeneratorSettings { Pattern = "AA-9999" });

        var erzeugt = generator.Generate(Seed("Original"), "Original");

        Assert.Matches("^[A-Z]{2}-[0-9]{4}$", erzeugt);
    }

    [Fact]
    public void Pattern_leitet_ohne_gesetzte_Maske_das_Format_aus_dem_Original_ab()
    {
        var generator = new PatternGenerator();

        var erzeugt = generator.Generate(Seed("AB-1234"), "AB-1234");

        Assert.Equal(7, erzeugt.Length);
        Assert.True(char.IsUpper(erzeugt[0]));
        Assert.True(char.IsUpper(erzeugt[1]));
        Assert.Equal('-', erzeugt[2]);
        Assert.All(erzeugt[3..], character => Assert.True(char.IsDigit(character)));
    }

    [Fact]
    public void Pattern_behandelt_ein_escaptes_Zeichen_woertlich()
    {
        var generator = new PatternGenerator();
        generator.Configure(new GeneratorSettings { Pattern = @"99\9" });

        var erzeugt = generator.Generate(Seed("Original"), "Original");

        Assert.Equal('9', erzeugt[2]);
        Assert.True(char.IsDigit(erzeugt[0]));
        Assert.True(char.IsDigit(erzeugt[1]));
    }

    [Fact]
    public void Wordlist_waehlt_ausschliesslich_aus_der_eigenen_Liste()
    {
        var generator = new WordlistGenerator();
        var werte = new List<string> { "Rot", "Gruen", "Blau" };
        generator.Configure(new GeneratorSettings { Values = werte });

        for (var i = 0; i < 20; i++)
        {
            var erzeugt = generator.Generate(Seed("Original", i), "Original");
            Assert.Contains(erzeugt, werte);
        }
    }

    [Fact]
    public void Wordlist_ohne_Werte_meldet_einen_Fehler()
    {
        var generator = new WordlistGenerator();

        var ex = Assert.Throws<GenerationException>(() => generator.Generate(Seed("Original"), "Original"));

        Assert.Contains("values", ex.Message);
    }

    [Fact]
    public void PartialMask_behaelt_Anfang_und_Ende_und_maskiert_die_Mitte()
    {
        var generator = new PartialMaskGenerator();
        generator.Configure(new GeneratorSettings { KeepFirst = 2, KeepLast = 2, MaskChar = "#" });

        var erzeugt = generator.Generate(Seed("0123456789"), "0123456789");

        Assert.Equal("01######89", erzeugt);
    }

    [Fact]
    public void PartialMask_maskiert_einen_zu_kurzen_Wert_vollstaendig()
    {
        // Waere der Wert kuerzer als die Summe der behaltenen Zeichen, zeigte
        // ein ueberlappender Ausschnitt trotzdem einen Teil des Klartexts --
        // deshalb muss ein kurzer Wert komplett maskiert werden.
        var generator = new PartialMaskGenerator();
        generator.Configure(new GeneratorSettings { KeepFirst = 2, KeepLast = 4 });

        var erzeugt = generator.Generate(Seed("123"), "123");

        Assert.Equal("***", erzeugt);
    }

    [Fact]
    public void PartialMask_ist_ausdruecklich_nicht_umkehrbar()
    {
        var generator = new PartialMaskGenerator();
        Assert.False(generator.IsReversible);
    }

    [Fact]
    public void PartialMask_haelt_ohne_jede_Einstellung_die_letzten_vier_Zeichen()
    {
        var generator = new PartialMaskGenerator();
        generator.Configure(new GeneratorSettings());

        var erzeugt = generator.Generate(Seed("0123456789"), "0123456789");

        Assert.Equal("******6789", erzeugt);
    }

    [Fact]
    public void PartialMask_zieht_bei_gesetztem_keepFirst_nicht_die_Vorgabe_am_Ende_mit()
    {
        // Sobald eine Seite eingestellt ist, zaehlt allein das Eingestellte.
        // Zoege 'keepFirst' die vorgegebenen vier Zeichen am Ende mit, gaebe
        // es keinen Weg, ausschliesslich den Anfang stehen zu lassen.
        var generator = new PartialMaskGenerator();
        generator.Configure(new GeneratorSettings { KeepFirst = 3 });

        var erzeugt = generator.Generate(Seed("0123456789"), "0123456789");

        Assert.Equal("012*******", erzeugt);
    }
}
