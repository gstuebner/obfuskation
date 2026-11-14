using System.Globalization;
using System.Text;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Regressionstests zu den Befunden der Codepruefung von 1.13.0
/// (_dev/features+bugs/befunde-codepruefung-1.13.0.md). Jeder Test traegt die
/// Befundnummer im Namen, damit der Bezug erhalten bleibt.
/// </summary>
public sealed class Codepruefung1130Tests
{
    // ------------------------------------------------------------------ A1

    [Fact]
    public void A1_Ein_Lauf_ohne_Tabelleneintrag_speichert_trotzdem_das_Salt()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");
        var input = TestProfile.Utf8("d\n15.06.2020\n");

        var a = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(input, "a.csv", new RunOptions()).Content);
        var b = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(input, "a.csv", new RunOptions()).Content);
        var back = TestProfile.FromUtf8(p.CreateEngine()
            .Deobfuscate(TestProfile.Utf8(a), "a.csv", new RunOptions()).Content);

        Assert.Equal(a, b);
        Assert.Equal("d\n15.06.2020\n", back);
        Assert.True(File.Exists(p.Profile.MappingStore));
    }

    [Fact]
    public void A1_Ein_Probelauf_legt_weiterhin_keine_Tabelle_an()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");

        p.CreateEngine().Obfuscate(TestProfile.Utf8("d\n15.06.2020\n"), "a.csv", new RunOptions { DryRun = true });

        Assert.False(File.Exists(p.Profile.MappingStore));
    }

    // ------------------------------------------------------------------ A2

    private static string Jahresspalte(string format)
    {
        var sb = new StringBuilder("d;k\n");
        for (var i = 0; i < 366; i++)
            sb.Append(new DateTime(2024, 1, 1).AddDays(i).ToString(format, CultureInfo.InvariantCulture))
              .Append(";k").Append(i).Append('\n');
        return sb.ToString();
    }

    private static string Rundreise(TestProfile p, string input, string name = "a.csv")
    {
        var obf = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), name, new RunOptions());
        return TestProfile.FromUtf8(p.CreateEngine().Deobfuscate(obf.Content, name, new RunOptions()).Content);
    }

    [Theory]
    [InlineData("dd.MM.yyyy")]
    [InlineData("d.M.yyyy")]
    [InlineData("yyyy-MM-dd")]
    [InlineData("dd/MM/yyyy")]
    public void A2_dateShift_Rundreise_ohne_Abweichung_mit_den_Vorgabeformaten(string format)
    {
        using var p = new TestProfile()
            .WithField("d", FieldAction.Pseudonymize, "dateShift")
            .WithField("k", FieldAction.Pseudonymize, "token");
        var input = Jahresspalte(format);

        Assert.Equal(input, Rundreise(p, input));
    }

    [Fact]
    public void A2_Amerikanische_Daten_brechen_ohne_eigenes_Format_ab_statt_still_falsch_zu_lesen()
    {
        using var p = new TestProfile()
            .WithField("d", FieldAction.Pseudonymize, "dateShift")
            .WithField("k", FieldAction.Pseudonymize, "token");

        var ex = Assert.Throws<GenerationException>(() => p.CreateEngine()
            .Obfuscate(TestProfile.Utf8(Jahresspalte("MM/dd/yyyy")), "a.csv", new RunOptions()));

        Assert.Contains("formats", ex.Message);
        Assert.DoesNotContain("/2024", ex.Message); // kein Echtwert in der Meldung (B11)
    }

    [Fact]
    public void A2_Mit_eigenem_amerikanischem_Format_gelingt_die_Rundreise()
    {
        using var p = new TestProfile()
            .WithField("d", FieldAction.Pseudonymize, "us")
            .WithField("k", FieldAction.Pseudonymize, "token");
        p.Profile.Generators["us"] = new GeneratorSettings { Type = "dateShift", Formats = ["MM/dd/yyyy"] };
        var input = Jahresspalte("MM/dd/yyyy");

        Assert.Equal(input, Rundreise(p, input));
    }

    [Fact]
    public void A2_Eine_Spalte_die_auf_zwei_Formate_mit_verschiedener_Bedeutung_passt_bricht_ab()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "beides");
        p.Profile.Generators["beides"] = new GeneratorSettings
        {
            Type = "dateShift",
            Formats = ["dd/MM/yyyy", "MM/dd/yyyy"],
        };

        var ex = Assert.Throws<GenerationException>(() => p.CreateEngine()
            .Obfuscate(TestProfile.Utf8("d\n03/04/2024\n05/06/2024\n"), "a.csv", new RunOptions()));

        Assert.Contains("mehrdeutig", ex.Message);
    }

    [Fact]
    public void A2_Eigene_Formate_ersetzen_die_Vorgabeliste_statt_sie_zu_ergaenzen()
    {
        Assert.Equal(["yyyy-MM-dd"], DateValues.CombineFormats(["yyyy-MM-dd"]));
        Assert.DoesNotContain("MM/dd/yyyy", DateValues.FallbackFormats);
    }

    // ------------------------------------------------------------------ A3

    [Fact]
    public void A3_JSON_Zahlen_mit_numericId_ueberstehen_die_Rundreise()
    {
        using var p = new TestProfile().WithField("nr", FieldAction.Pseudonymize, "numericId");
        var input = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"{{\"nr\":{4000 + i * 7}}}")) + "]";

        var obf = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), "a.json", new RunOptions());
        var back = p.CreateEngine().Deobfuscate(obf.Content, "a.json", new RunOptions());

        Assert.Equal(input, TestProfile.FromUtf8(back.Content));
        Assert.Empty(back.Report.Findings);
    }

    [Fact]
    public void A3_Ein_Pseudonym_das_keine_gueltige_JSON_Zahl_ist_wird_Zeichenkette_mit_einem_Hinweis_je_Feld()
    {
        using var p = new TestProfile().WithField("nr", FieldAction.Pseudonymize, "token");

        var r = p.CreateEngine().Obfuscate(TestProfile.Utf8("[{\"nr\":1},{\"nr\":2},{\"nr\":3}]"), "a.json",
            new RunOptions());

        Assert.Contains("\"nr\":\"TOK_", TestProfile.FromUtf8(r.Content));
        Assert.Single(r.Report.Warnings, w => w.Code == "zahlAlsText");
    }

    // ------------------------------------------------------------------ A4

    [Fact]
    public void A4_Keine_Kettenersetzung_im_Freitext_ab_1000_Eintraegen()
    {
        using var p = new TestProfile();
        p.Profile.Generators["g1"] = new GeneratorSettings { Type = "wordlist", Values = ["LANGESPSEUDONYM_XYZ_0123456789"] };
        p.Profile.Generators["g2"] = new GeneratorSettings { Type = "wordlist", Values = ["Hans"] };
        p.WithField("a", FieldAction.Pseudonymize, "g1")
         .WithField("b", FieldAction.Pseudonymize, "g2")
         .WithField("c", FieldAction.Pseudonymize, "token");
        var sb = new StringBuilder("a;b;c\nHans Müller;Peter;x0\n");
        for (var i = 1; i < 1200; i++) sb.Append(";;x").Append(i).Append('\n');
        p.CreateEngine().Obfuscate(TestProfile.Utf8(sb.ToString()), "a.csv", new RunOptions());

        var back = TestProfile.FromUtf8(p.CreateEngine().Deobfuscate(
            TestProfile.Utf8("Antwort: LANGESPSEUDONYM_XYZ_0123456789 ist Kunde."), null, new RunOptions()).Content);

        Assert.Equal("Antwort: Hans Müller ist Kunde.", back);
    }

    [Fact]
    public void A4_Gleiches_Pseudonym_in_zwei_Namensraeumen_mit_verschiedenem_Klartext_wird_gemeldet()
    {
        using var p = new TestProfile();
        p.Profile.Generators["g1"] = new GeneratorSettings { Type = "wordlist", Values = ["Gleich"] };
        p.Profile.Generators["g2"] = new GeneratorSettings { Type = "wordlist", Values = ["Gleich"] };
        p.WithField("a", FieldAction.Pseudonymize, "g1").WithField("b", FieldAction.Pseudonymize, "g2");
        p.CreateEngine().Obfuscate(TestProfile.Utf8("a;b\nHans;Peter\n"), "a.csv", new RunOptions());

        var r = p.CreateEngine().Deobfuscate(TestProfile.Utf8("Gleich"), null, new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "mehrdeutigesPseudonym");
    }

    // ------------------------------------------------------------------ A5

    [Fact]
    public void A5_Ueberlappende_Textregeln_lassen_keinen_Rest_des_Echtwerts_stehen()
    {
        using var p = new TestProfile();
        p.Profile.TextRules.Add(new TextRule { Name = "kundenwort", Priority = 10, Generator = "token", Pattern = @"Kunde \w{6}" });

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("Kunde DE89370400440532013000 zahlt."), null, new RunOptions()).Content);

        Assert.DoesNotContain("0400440532013000", s);
        Assert.DoesNotContain("DE89", s);
    }

    [Fact]
    public void A5_Die_hoehere_Prioritaet_gewinnt_auch_wenn_sie_spaeter_beginnt()
    {
        var engine = new Detection.TextRuleEngine();
        var hoch = new TextRule { Name = "hoch", Priority = 100, Pattern = "BCD" };
        var niedrig = new TextRule { Name = "niedrig", Priority = 10, Pattern = "ABC" };

        var funde = engine.FindMatches("ABCDE", [niedrig, hoch]);

        var fund = Assert.Single(funde);
        Assert.Equal("hoch", fund.Rule.Name);
    }

    // ------------------------------------------------------------------ A6

    [Fact]
    public void A6_Freitextlauf_ohne_Ersetzung_und_ohne_Textregeln_warnt_deutlich()
    {
        using var p = new TestProfile(pr => pr.TextRules.Clear());
        var input = "name;strasse\nMax Mustermann;Hauptstr. 1\n";

        var r = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), "kunden.txt", new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "freitextOhneErsetzung");
        Assert.Contains(r.Report.Warnings, w => w.Code == "freitextOhneTextregeln");
    }

    [Fact]
    public void A6_Unbekannte_Endung_bricht_im_strengen_Modus_ab()
    {
        using var p = new TestProfile();

        var ex = Assert.Throws<ConfigurationException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("a;b\n1;2\n"), "kunden.dat", new RunOptions { Strict = true }));

        Assert.Contains("--format", ex.Message);
    }

    [Fact]
    public void A6_Unbekannte_Endung_ohne_strengen_Modus_warnt()
    {
        using var p = new TestProfile();

        var r = p.CreateEngine().Obfuscate(TestProfile.Utf8("x DE89370400440532013000"), "kunden.dat", new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "formatUnbekannt");
    }

    [Fact]
    public void A6_Ausdrueckliches_Textformat_und_Standardeingabe_bleiben_ohne_Formatwarnung()
    {
        using var p = new TestProfile();

        var r1 = p.CreateEngine().Obfuscate(TestProfile.Utf8("x"), "kunden.dat",
            new RunOptions { Strict = true, Format = DataFormat.Text });
        var r2 = p.CreateEngine().Obfuscate(TestProfile.Utf8("x"), null, new RunOptions { Strict = true });

        Assert.DoesNotContain(r1.Report.Warnings, w => w.Code == "formatUnbekannt");
        Assert.DoesNotContain(r2.Report.Warnings, w => w.Code == "formatUnbekannt");
    }

    [Fact]
    public void A6_Binaerdateien_werden_abgelehnt()
    {
        using var p = new TestProfile();

        Assert.Throws<ConfigurationException>(() => p.CreateEngine().Obfuscate(
            [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x41], "bild.txt", new RunOptions()));
    }

    // ------------------------------------------------------------------ A7

    [Fact]
    public void A7_Namenlose_Zeichenketten_laufen_durch_die_Textregeln()
    {
        using var p = new TestProfile(pr => pr.Defaults.UnknownField = FieldAction.Passthrough);

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("[\"DE89370400440532013000\", \"max@firma.de\"]"), "a.json", new RunOptions()).Content);

        Assert.DoesNotContain("DE89370400440532013000", s);
        Assert.DoesNotContain("max@firma.de", s);
    }

    [Fact]
    public void A7_Im_strengen_Modus_ist_ein_namenloser_Wert_ein_unbehandeltes_Feld()
    {
        using var p = new TestProfile();

        var ex = Assert.Throws<UnhandledFieldException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("[\"DE89370400440532013000\", \"max@firma.de\"]"), "a.json", new RunOptions { Strict = true }));

        Assert.Contains("$[*]", ex.FieldNames);
    }

    [Fact]
    public void A7_Eine_Pfadregel_greift_fuer_namenlose_Werte()
    {
        using var p = new TestProfile();
        p.Profile.Fields.Add(new FieldRule { Match = "$[*]", MatchType = FieldMatchType.JsonPath, Action = FieldAction.ScanText });

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("[\"DE89370400440532013000\"]"), "a.json", new RunOptions { Strict = true }).Content);

        Assert.DoesNotContain("DE89370400440532013000", s);
    }

    // ------------------------------------------------------------------ A8

    [Fact]
    public void A8_Ueberzaehlige_Felder_brechen_bei_unknownField_error_mit_Zeilenangabe_ab()
    {
        using var p = new TestProfile()
            .WithField("a", FieldAction.Passthrough)
            .WithField("b", FieldAction.Passthrough);

        var ex = Assert.Throws<UnhandledFieldException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("a;b\n1;2;DE89370400440532013000\n\n3;4"), "a.csv", new RunOptions()));

        // Zeilen zaehlen wie ueberall ab dem ersten Datensatz nach der Kopfzeile.
        Assert.Contains("Zeile 1", ex.Message);
    }

    [Fact]
    public void A8_Ueberzaehlige_Felder_Leerzeilen_und_fehlendes_Zeilenende_bleiben_erhalten()
    {
        using var p = new TestProfile(pr => pr.Defaults.UnknownField = FieldAction.ScanText)
            .WithField("a", FieldAction.Passthrough)
            .WithField("b", FieldAction.Passthrough);

        var r = p.CreateEngine().Obfuscate(
            TestProfile.Utf8("a;b\n1;2;DE89370400440532013000\n\n3;4"), "a.csv", new RunOptions());
        var s = TestProfile.FromUtf8(r.Content);

        Assert.StartsWith("a;b\n1;2;", s);
        Assert.DoesNotContain("DE89370400440532013000", s);
        Assert.EndsWith("\n\n3;4", s);
        Assert.Contains(r.Report.Warnings, w => w.Code == "abweichendeFeldanzahl");
    }

    // ------------------------------------------------------------------ A9

    [Fact]
    public void A9_Schreibweise_des_Generatornamens_trennt_keine_Namensraeume()
    {
        using var p = new TestProfile()
            .WithField("a", FieldAction.Pseudonymize, "IBAN")
            .WithField("b", FieldAction.ScanText);

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("a;b\nDE89370400440532013000;DE89370400440532013000\n"), "a.csv", new RunOptions()).Content);

        var felder = s.Split('\n')[1].Split(';');
        Assert.Equal(felder[0], felder[1]);
    }

    [Fact]
    public void A9_Der_Pruefer_warnt_bei_abweichender_Schreibweise()
    {
        using var p = new TestProfile().WithField("a", FieldAction.Pseudonymize, "IBAN");

        var befunde = ProfileValidator.Validate(p.Profile, ExtensionLibrary.Empty);

        Assert.Contains(befunde, b => b.Severity == ValidationSeverity.Warning && b.Message.Contains("'iban'"));
    }

    [Fact]
    public void A9_Ein_bestehender_Namensraum_in_abweichender_Schreibweise_bleibt_gueltig()
    {
        using var p = new TestProfile().WithField("a", FieldAction.Pseudonymize, "IBAN");
        using (var store = MappingStore.Open(p.Profile.MappingStore!, "test"))
        {
            store.Add("IBAN", "DE89370400440532013000", "DE02120300000000202051");
            store.Save();
        }

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("a\nDE89370400440532013000\n"), "a.csv", new RunOptions()).Content);
        var back = TestProfile.FromUtf8(p.CreateEngine().Deobfuscate(
            TestProfile.Utf8(s), "a.csv", new RunOptions()).Content);

        Assert.Equal("a\nDE02120300000000202051\n", s);
        Assert.Equal("a\nDE89370400440532013000\n", back);
    }

    // ------------------------------------------------------------------ B1

    [Fact]
    public void B1_Feldregeln_ueber_einen_JSON_Pfad_fuehren_nicht_zum_Abbruch()
    {
        using var p = new TestProfile();
        p.Profile.Fields.Add(new FieldRule { Match = "$.kunden[*].name", MatchType = FieldMatchType.JsonPath,
            Action = FieldAction.Pseudonymize, Generator = "personName" });
        p.Profile.Fields.Add(new FieldRule { Match = "kunden", Action = FieldAction.Passthrough });

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("{\"kunden\":[{\"name\":\"Max Mustermann\"}]}"), "a.json", new RunOptions()).Content);

        Assert.DoesNotContain("Max Mustermann", s);
    }

    [Fact]
    public void B1_Analyze_zeigt_dasselbe_wie_der_Lauf()
    {
        using var p = new TestProfile();
        p.Profile.Fields.Add(new FieldRule { Match = "$.kunden[*].name", MatchType = FieldMatchType.JsonPath,
            Action = FieldAction.Pseudonymize, Generator = "personName" });
        p.Profile.Fields.Add(new FieldRule { Match = "kunden", Action = FieldAction.Passthrough });

        var analyse = p.CreateEngine().Analyze(
            TestProfile.Utf8("{\"kunden\":[{\"name\":\"Max Mustermann\"}]}"), "a.json");

        Assert.Empty(analyse.Undecided);
        Assert.Contains(analyse.Fields, f => f.FieldName == "name" && f.Action == FieldAction.Pseudonymize);
    }

    // ------------------------------------------------------------------ B3

    [Theory]
    [InlineData(-30)]
    [InlineData(30)]
    public void B3_Platzhalterdaten_bleiben_unveraendert_und_stuerzen_nicht_ab(int versatz)
    {
        var g = new DateShiftGenerator();
        g.Configure(new GeneratorSettings());
        g.SetOffsetDays(versatz);

        Assert.Equal("01.01.0001", g.Generate(new byte[32], "01.01.0001"));
        Assert.Equal("31.12.9999", g.Generate(new byte[32], "31.12.9999"));
        Assert.True(g.TryInvert("31.12.9999", out var zurueck));
        Assert.Equal("31.12.9999", zurueck);
    }

    [Fact]
    public void B3_Ein_Ueberlauf_ausserhalb_der_Platzhalter_wirft_eine_GenerationException()
    {
        var g = new DateShiftGenerator();
        g.Configure(new GeneratorSettings());
        g.SetOffsetDays(-30);

        Assert.Throws<GenerationException>(() => g.Generate(new byte[32], "02.01.0001"));
    }

    // ------------------------------------------------------------------ B4

    [Fact]
    public void B4_Doppelte_JSON_Schluessel_werden_als_ConfigurationException_gemeldet()
    {
        using var p = new TestProfile(pr => pr.Defaults.UnknownField = FieldAction.Passthrough);

        var ex = Assert.Throws<ConfigurationException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("{\"a\":1,\"a\":2}"), "a.json", new RunOptions()));

        Assert.Contains("doppelt", ex.Message);
    }

    // ------------------------------------------------------------------ B5

    [Fact]
    public void B5_Telefonfeld_ohne_Ziffern_bekommt_eine_vollstaendige_Nummer()
    {
        using var p = new TestProfile().WithField("t", FieldAction.Pseudonymize, "phone");

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("t\nunbekannt\n"), "a.csv", new RunOptions()).Content);

        Assert.Matches(@"^t\n\+49 30 \d{8}\n$", s);
    }

    [Fact]
    public void B5_Ein_Wert_den_der_Generator_nicht_veraendern_kann_meldet_die_Ursache()
    {
        using var p = new TestProfile().WithField("t", FieldAction.Pseudonymize, "pattern");

        var ex = Assert.Throws<GenerationException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("t\n---\n"), "a.csv", new RunOptions()));

        Assert.Contains("passt nicht zum Generator", ex.Message);
        Assert.Contains("Zeile 1", ex.Message);
    }

    // ------------------------------------------------------------------ B6

    [Theory]
    [InlineData("firstName", 400)]
    [InlineData("lastName", 2000)]
    [InlineData("city", 1000)]
    [InlineData("street", 600)]
    public void B6_Kleine_Wortlisten_erweitern_sich_statt_abzubrechen(string generator, int anzahl)
    {
        using var p = new TestProfile().WithField("v", FieldAction.Pseudonymize, generator);
        var sb = new StringBuilder("v\n");
        for (var i = 0; i < anzahl; i++) sb.Append("Wert").Append(i).Append('\n');
        var input = sb.ToString();

        var obf = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), "a.csv", new RunOptions());
        var back = p.CreateEngine().Deobfuscate(obf.Content, "a.csv", new RunOptions());

        Assert.Equal(anzahl, obf.Report.NewMappings);
        Assert.Equal(input, TestProfile.FromUtf8(back.Content));
    }

    [Fact]
    public void B6_dateRange_ohne_Zeitraum_weicht_auf_Nachbarjahre_aus()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateRange");
        var sb = new StringBuilder("d\n");
        for (var i = 0; i < 366; i++)
            sb.Append(new DateTime(2024, 1, 1).AddDays(i).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append('\n');
        var input = sb.ToString();

        var obf = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), "a.csv", new RunOptions());
        var back = p.CreateEngine().Deobfuscate(obf.Content, "a.csv", new RunOptions());

        Assert.Equal(input, TestProfile.FromUtf8(back.Content));
    }

    [Fact]
    public void B6_Die_Erweiterung_aendert_nichts_an_den_bisherigen_Pseudonymen()
    {
        // Was bis 1.13.0 ein freies Pseudonym bekam, muss es unveraendert behalten.
        var deriver = new SeedDeriver(new byte[32]);
        var g = new FirstNameGenerator();
        var erwartet = g.Generate(deriver.Derive("firstName", "Anna", 0), "Anna");

        using var p = new TestProfile().WithField("v", FieldAction.Pseudonymize, "firstName");
        File.WriteAllText(p.Profile.MappingStore!,
            "{\"version\":1,\"profileName\":\"test\",\"salt\":\"" + Convert.ToBase64String(new byte[32]) + "\",\"namespaces\":{}}");

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("v\nAnna\n"), "a.csv", new RunOptions()).Content);

        Assert.Equal($"v\n{erwartet}\n", s);
    }

    [Fact]
    public void B6_Die_Fehlermeldung_nennt_die_Zahl_der_vergebenen_Werte()
    {
        using var p = new TestProfile().WithField("v", FieldAction.Pseudonymize, "klein");
        p.Profile.Generators["klein"] = new GeneratorSettings { Type = "wordlist", Values = ["A", "B", "C"] };

        var ex = Assert.Throws<MappingConflictException>(() => p.CreateEngine().Obfuscate(
            TestProfile.Utf8("v\nw\nx\ny\nz\n"), "a.csv", new RunOptions()));

        Assert.Contains("bereits 3 Werte vergeben", ex.Message);
    }

    // ------------------------------------------------------------------ B7

    [Fact]
    public void B7_Ein_relativer_Pfad_der_Ersetzungstabelle_wird_abgelehnt()
    {
        var profil = new Profile { ProfileName = "rel", MappingStore = "mapping.json" };

        var befunde = ProfileValidator.Validate(profil, ExtensionLibrary.Empty);

        Assert.Contains(befunde, b => b.Path == "mappingStore" && b.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void B7_Pfade_mit_Tilde_und_absolute_Pfade_bleiben_erlaubt()
    {
        foreach (var pfad in new[] { "~/obf/mapping.json", Path.Combine(Path.GetTempPath(), "m.json") })
        {
            var befunde = ProfileValidator.Validate(
                new Profile { ProfileName = "x", MappingStore = pfad }, ExtensionLibrary.Empty);
            Assert.DoesNotContain(befunde, b => b.Path == "mappingStore");
        }
    }

    // ------------------------------------------------------------------ B8

    [Fact]
    public void B8_Nur_lesendes_Oeffnen_legt_kein_Verzeichnis_an()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N"));

        using (MappingStore.Open(Path.Combine(verzeichnis, "mapping.json"), "x", readOnly: true))
        {
        }

        Assert.False(Directory.Exists(verzeichnis));
    }

    // ------------------------------------------------------------------ B9

    [Fact]
    public void B9_Nur_lesendes_Oeffnen_einer_kaputten_Tabelle_laesst_eine_fremde_Sperre_stehen()
    {
        using var p = new TestProfile();
        File.WriteAllText(p.Profile.MappingStore!, "{ kaputt");
        var sperre = p.Profile.MappingStore + ".lock";
        File.WriteAllText(sperre, "");

        Assert.Throws<MappingConflictException>(() => MappingStore.Open(p.Profile.MappingStore!, "test", readOnly: true));

        Assert.True(File.Exists(sperre));
    }

    // ----------------------------------------------------------------- B11

    [Fact]
    public void B11_Meldungen_ueber_widerspruechliche_Tabellen_nennen_keinen_Klartext()
    {
        using var p = new TestProfile();
        File.WriteAllText(p.Profile.MappingStore!,
            "{\"version\":1,\"profileName\":\"test\",\"salt\":\"" + Convert.ToBase64String(new byte[32]) + "\"," +
            "\"namespaces\":{\"token\":{\"Geheim1\":\"TOK_1\",\"Geheim2\":\"TOK_1\"}}}");

        var ex = Assert.Throws<MappingConflictException>(() => MappingStore.Open(p.Profile.MappingStore!, "test", readOnly: true));

        Assert.DoesNotContain("Geheim", ex.Message);
    }

    [Fact]
    public void B11_Der_Probelauf_vergibt_kein_Pseudonym_doppelt()
    {
        using var p = new TestProfile().WithField("v", FieldAction.Pseudonymize, "eins");
        p.Profile.Generators["eins"] = new GeneratorSettings { Type = "wordlist", Values = ["A", "B", "C"] };

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("v\nx\ny\nz\nx\n"), "a.csv", new RunOptions { DryRun = true }).Content);

        var werte = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.Equal(3, werte.Take(3).Distinct().Count());
        Assert.Equal(werte[0], werte[3]);
    }

    [Fact]
    public void B11_Ein_Pseudonym_das_als_leer_gilt_wird_nicht_vergeben()
    {
        using var p = new TestProfile(pr => pr.Defaults.EmptyValues.Add("-"))
            .WithField("v", FieldAction.Pseudonymize, "eins");
        p.Profile.Generators["eins"] = new GeneratorSettings { Type = "wordlist", Values = ["-", "B"] };

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("v\nx\n"), "a.csv", new RunOptions()).Content);

        Assert.Equal("v\nB\n", s);
    }

    [Fact]
    public void B11_Unbekannte_Pseudonyme_bekommen_eine_deutliche_Abschlusswarnung()
    {
        using var p = new TestProfile().WithField("v", FieldAction.Pseudonymize, "token");

        var r = p.CreateEngine().Deobfuscate(TestProfile.Utf8("v\nTOK_UNBEKANNT\n"), "a.csv", new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "unbekanntePseudonyme");
    }

    [Fact]
    public void B11_Nicht_darstellbare_Zeichen_im_Pseudonym_brechen_mit_Meldung_ab()
    {
        using var p = new TestProfile(pr => pr.Input.Encoding = "windows-1252")
            .WithField("v", FieldAction.Pseudonymize, "eins");
        p.Profile.Generators["eins"] = new GeneratorSettings { Type = "wordlist", Values = ["Ωmega"] };

        var ex = Assert.Throws<ConfigurationException>(() => p.CreateEngine().Obfuscate(
            [(byte)'v', (byte)'\n', (byte)'x', (byte)'\n'], "a.csv", new RunOptions()));

        Assert.Contains("windows-1252", ex.Message);
    }

    // ------------------------------------------------------------------ C2

    [Fact]
    public void C2_JSON_Ausgabe_schreibt_Umlaute_und_Sonderzeichen_unmaskiert()
    {
        using var p = new TestProfile().WithField("o", FieldAction.Passthrough);

        var s = TestProfile.FromUtf8(p.CreateEngine().Obfuscate(
            TestProfile.Utf8("{\"o\":\"Köln <a&b> + x\"}"), "a.json", new RunOptions()).Content);

        Assert.Equal("{\"o\":\"Köln <a&b> + x\"}", s);
    }

    // ------------------------------------------------------------------ C1

    private static List<string> Pruefbefunde(TestProfile p, string text)
        => p.CreateEngine().Scan(TestProfile.Utf8(text), "a.txt", new RunOptions()).Report.Findings
            .Where(f => f.Kind == "echtwertAusTabelle")
            .Select(f => f.Rule)
            .ToList();

    [Fact]
    public void C1_Der_Prueflauf_meldet_wortartige_Klartexte_nur_als_ganzes_Wort()
    {
        using var p = new TestProfile().WithField("n", FieldAction.Pseudonymize, "lastName");
        p.CreateEngine().Obfuscate(TestProfile.Utf8("n\nMark\n"), "a.csv", new RunOptions());

        Assert.Empty(Pruefbefunde(p, "Die Marketing-Abteilung meldet sich."));
        Assert.Equal(["lastName"], Pruefbefunde(p, "Gespräch mit Herrn Mark am Montag."));
    }

    [Fact]
    public void C1_Der_Prueflauf_unterscheidet_nicht_nach_Gross_und_Kleinschreibung()
    {
        using var p = new TestProfile()
            .WithField("n", FieldAction.Pseudonymize, "lastName")
            .WithField("i", FieldAction.Pseudonymize, "iban");
        p.CreateEngine().Obfuscate(
            TestProfile.Utf8("n;i\nMark;DE89370400440532013000\n"), "a.csv", new RunOptions());

        Assert.Equal(["lastName"], Pruefbefunde(p, "HERR MARK"));
        Assert.Equal(["iban"], Pruefbefunde(p, "Konto de89370400440532013000"));
    }

    [Fact]
    public void C1_Nicht_wortartige_Klartexte_werden_auch_mitten_im_Wort_gemeldet()
    {
        using var p = new TestProfile().WithField("i", FieldAction.Pseudonymize, "iban");
        p.CreateEngine().Obfuscate(TestProfile.Utf8("i\nDE89370400440532013000\n"), "a.csv", new RunOptions());

        Assert.Equal(["iban"], Pruefbefunde(p, "IBAN:DE89370400440532013000X"));
    }

    // ------------------------------------------------------------------ C3

    private static int? GespeicherterVersatz(TestProfile p, string name)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p.Profile.MappingStore!));
        return doc.RootElement.TryGetProperty("dateOffsets", out var offsets)
               && offsets.TryGetProperty(name, out var value)
            ? value.GetInt32()
            : null;
    }

    [Fact]
    public void C3_Der_Datumsversatz_wird_beim_ersten_Lauf_festgehalten()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");

        p.CreateEngine().Obfuscate(TestProfile.Utf8("d\n15.06.2020\n"), "a.csv", new RunOptions());

        Assert.NotNull(GespeicherterVersatz(p, "dateShift"));
    }

    [Fact]
    public void C3_Ein_unbenutzter_Datumsgenerator_haelt_keinen_Versatz_fest()
    {
        using var p = new TestProfile().WithField("k", FieldAction.Pseudonymize, "token");

        p.CreateEngine().Obfuscate(TestProfile.Utf8("k\nabc\n"), "a.csv", new RunOptions());

        Assert.Null(GespeicherterVersatz(p, "dateShift"));
    }

    [Fact]
    public void C3_Nach_Aenderung_von_maxDays_rechnet_die_Rueckuebersetzung_mit_dem_alten_Versatz()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");
        p.Profile.Generators["dateShift"] = new GeneratorSettings { MaxDays = 30 };
        var input = "d\n15.06.2020\n";
        var obf = p.CreateEngine().Obfuscate(TestProfile.Utf8(input), "a.csv", new RunOptions());

        p.Profile.Generators["dateShift"] = new GeneratorSettings { MaxDays = 3000 };
        var back = p.CreateEngine().Deobfuscate(obf.Content, "a.csv", new RunOptions());

        Assert.Equal(input, TestProfile.FromUtf8(back.Content));
    }

    [Fact]
    public void C3_Ein_abweichender_festgehaltener_Versatz_gilt_und_wird_gemeldet()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");
        p.CreateEngine().Obfuscate(TestProfile.Utf8("d\n15.06.2020\n"), "a.csv", new RunOptions());

        // Den festgehaltenen Wert so verstellen, als waere er unter anderem
        // maxDays entstanden.
        var gespeichert = GespeicherterVersatz(p, "dateShift")!.Value;
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p.Profile.MappingStore!))!;
        json["dateOffsets"]!["dateShift"] = gespeichert + 1;
        File.WriteAllText(p.Profile.MappingStore!, json.ToJsonString());

        var r = p.CreateEngine().Deobfuscate(TestProfile.Utf8("d\n15.06.2020\n"), "a.csv", new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "datumsversatzFestgehalten");
        Assert.DoesNotContain(gespeichert.ToString(CultureInfo.InvariantCulture),
            r.Report.Warnings.Single(w => w.Code == "datumsversatzFestgehalten").Message);
        var erwartet = new DateTime(2020, 6, 15).AddDays(-(gespeichert + 1)).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        Assert.Equal($"d\n{erwartet}\n", TestProfile.FromUtf8(r.Content));
    }

    // ------------------------------------------------------------------ C5

    [Fact]
    public void C5_Unbekannte_Profileigenschaften_werden_mit_Vorschlag_gewarnt()
    {
        using var p = new TestProfile();
        var pfad = Path.Combine(p.Directory, "profil.json");
        File.WriteAllText(pfad, """
            {
              "profileName": "test",
              "textRuels": [],
              "defaults": { "emtpyValues": ["-"] },
              "fields": [ { "match": "a", "action": "passthrough", "commnet": "x" } ],
              "generators": { "iban": { "contry": "AT" } }
            }
            """);

        var profil = ProfileStore.Load(pfad);
        var warnungen = ProfileValidator.Validate(profil, ExtensionLibrary.Empty)
            .Where(i => i.Severity == ValidationSeverity.Warning)
            .ToList();

        Assert.Contains(warnungen, w => w.Path == "textRuels" && w.Message.Contains("'textRules'"));
        Assert.Contains(warnungen, w => w.Path == "defaults.emtpyValues" && w.Message.Contains("'emptyValues'"));
        Assert.Contains(warnungen, w => w.Path == "fields[0].commnet" && w.Message.Contains("'comment'"));
        Assert.Contains(warnungen, w => w.Path == "generators.iban.contry" && w.Message.Contains("'country'"));
    }

    [Fact]
    public void C5_Der_Laufbericht_wiederholt_die_Warnung_fuer_die_Kommandozeile()
    {
        using var p = new TestProfile().WithField("a", FieldAction.Passthrough);
        p.Profile.UnknownProperties = [new UnknownProperty("textRuels", "textRules")];

        var r = p.CreateEngine().Obfuscate(TestProfile.Utf8("a\nx\n"), "a.csv", new RunOptions());

        Assert.Contains(r.Report.Warnings, w => w.Code == "unbekannteEigenschaft" && w.Message.Contains("textRuels"));
    }

    [Fact]
    public void C5_Ein_gespeichertes_Profil_hat_keine_unbekannten_Eigenschaften()
    {
        using var p = new TestProfile().WithField("a", FieldAction.Passthrough);
        p.Profile.Generators["eins"] = new GeneratorSettings { Type = "wordlist", Values = ["x", "y"] };
        var pfad = Path.Combine(p.Directory, "profil.json");

        ProfileStore.Save(p.Profile, pfad);

        Assert.Empty(ProfileStore.Load(pfad).UnknownProperties);
    }

    // ------------------------------------------------------------------ C6

    [Fact]
    public void C6_Leerraum_am_Rand_aendert_das_Pseudonym_nicht_und_bleibt_erhalten()
    {
        using var p = new TestProfile().WithField("n", FieldAction.Pseudonymize, "lastName");
        var input = "[{\"n\":\" Müller\"},{\"n\":\"Müller  \"},{\"n\":\"Müller\"}]";

        var obf = System.Text.Json.Nodes.JsonNode.Parse(p.CreateEngine()
            .Obfuscate(TestProfile.Utf8(input), "a.json", new RunOptions()).Content)!.AsArray();
        var kern = (string)obf[2]!["n"]!;

        Assert.Equal(" " + kern, (string)obf[0]!["n"]!);
        Assert.Equal(kern + "  ", (string)obf[1]!["n"]!);
        Assert.Equal(input, Rundreise(p, input, "a.json"));
    }

    [Fact]
    public void C6_In_CSV_steht_ein_Wert_mit_Randleerraum_in_Anfuehrungszeichen()
    {
        using var p = new TestProfile().WithField("n", FieldAction.Pseudonymize, "lastName");

        var zeilen = TestProfile.FromUtf8(p.CreateEngine()
            .Obfuscate(TestProfile.Utf8("n\n Müller\nMüller\n"), "a.csv", new RunOptions()).Content).Split('\n');

        Assert.Equal($"\" {zeilen[2]}\"", zeilen[1]);
    }

    [Fact]
    public void C6_Ein_alter_Eintrag_mit_Leerraum_im_Schluessel_geht_vor()
    {
        using var p = new TestProfile().WithField("n", FieldAction.Pseudonymize, "lastName");
        using (var store = MappingStore.Open(p.Profile.MappingStore!, "test"))
        {
            store.Add("lastName", " Müller", "Altname");
            store.Save();
        }

        var obf = TestProfile.FromUtf8(p.CreateEngine()
            .Obfuscate(TestProfile.Utf8("n\n Müller\n"), "a.csv", new RunOptions()).Content);

        Assert.Equal("n\nAltname\n", obf);
    }

    [Fact]
    public void C6_dateShift_behaelt_den_Leerraum_am_Rand()
    {
        using var p = new TestProfile().WithField("d", FieldAction.Pseudonymize, "dateShift");
        var input = "{\"d\":\" 15.06.2020\"}";

        Assert.Equal(input, Rundreise(p, input, "a.json"));
    }
}
