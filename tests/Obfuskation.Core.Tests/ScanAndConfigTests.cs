using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Nachpruefung und die Profilpruefung — beides Schutzwaelle davor, dass
/// Echtdaten unbemerkt hinausgehen.
/// </summary>
public class ScanAndConfigTests
{
    private static TestProfile ScanProfile() =>
        new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("IBAN", FieldAction.Pseudonymize, "iban")
            .WithField("Notiz", FieldAction.Passthrough);

    [Fact]
    public void Die_Pruefung_meldet_auf_der_eigenen_Ausgabe_nichts()
    {
        using var setup = ScanProfile();
        var engine = setup.CreateEngine();

        var eingabe = TestProfile.Utf8(
            "Name;IBAN;Notiz\nMax Mustermann;DE02120300000000202051;unverfaenglich\n");

        var pseudonymisiert = engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });
        var pruefung = engine.Scan(pseudonymisiert.Content, "a.csv", new RunOptions());

        // Eine erzeugte Test-IBAN passt zwangslaeufig auf das IBAN-Muster.
        // Wuerde die Pruefung das melden, meldete sie nur sich selbst.
        Assert.Empty(pruefung.Report.Findings);
    }

    [Fact]
    public void Die_Pruefung_findet_einen_stehengebliebenen_Echtwert()
    {
        using var setup = ScanProfile();
        var engine = setup.CreateEngine();

        var eingabe = TestProfile.Utf8(
            "Name;IBAN;Notiz\nMax Mustermann;DE02120300000000202051;unverfaenglich\n");

        engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });

        // Dieselbe Datei noch einmal pruefen — jetzt sind die Echtwerte bekannt.
        var pruefung = engine.Scan(eingabe, "a.csv", new RunOptions());

        Assert.NotEmpty(pruefung.Report.Findings);
        Assert.Contains(pruefung.Report.Findings, f => f.Kind == "echtwertAusTabelle");
    }

    [Fact]
    public void Die_Pruefung_meldet_ein_Muster_das_nie_ersetzt_wurde()
    {
        using var setup = ScanProfile();
        var engine = setup.CreateEngine();

        // Eine IBAN im Freitextfeld, das auf passthrough steht — sie wurde nie
        // ersetzt und steht noch im Klartext da.
        var eingabe = TestProfile.Utf8(
            "Name;IBAN;Notiz\nMax Mustermann;DE02120300000000202051;Konto DE02500105170137075030\n");

        var pruefung = engine.Scan(eingabe, "a.csv", new RunOptions());

        Assert.Contains(pruefung.Report.Findings, f => f.Kind == "musterTreffer" && f.Rule == "iban");
    }

    [Fact]
    public void Die_Pruefung_veraendert_die_Datei_nicht()
    {
        using var setup = ScanProfile();
        var engine = setup.CreateEngine();

        var eingabe = TestProfile.Utf8(
            "Name;IBAN;Notiz\nMax Mustermann;DE02120300000000202051;Text\n");

        var pruefung = engine.Scan(eingabe, "a.csv", new RunOptions());

        Assert.Equal(eingabe, pruefung.Content);
    }

    [Fact]
    public void Die_Pruefung_meldet_bei_praefigierten_Tokens_keinen_Fehlalarm()
    {
        // ScanTransformer prueft per Substring, ob Klartexte noch irgendwo
        // stehen — das Praefix ist Teil des gespeicherten Pseudonyms und darf
        // daran nichts aendern.
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["artikelKategorie"] =
                new GeneratorSettings { Type = "token", Prefix = "Artikel~" };
        }).WithField("Artikelkategorie", FieldAction.Pseudonymize, "artikelKategorie");

        var engine = setup.CreateEngine();

        var eingabe = TestProfile.Utf8("Artikelkategorie\nSchrauben\n");
        var pseudonymisiert = engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });
        var pruefung = engine.Scan(pseudonymisiert.Content, "a.csv", new RunOptions());

        Assert.Empty(pruefung.Report.Findings);
    }

    [Fact]
    public void Ein_Praefix_an_einem_anderen_Basistyp_als_token_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["kundenId"] = new GeneratorSettings { Type = "numericId", Prefix = "Kunde~" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.kundenId.prefix");
    }

    [Theory]
    [InlineData("Artikel;")]
    [InlineData("Artikel\"")]
    [InlineData("Artikel")]
    public void Ein_Praefix_mit_unzulaessigen_Zeichen_oder_ohne_Abschluss_wird_bemaengelt(string prefix)
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["artikelKategorie"] = new GeneratorSettings { Type = "token", Prefix = prefix };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.artikelKategorie.prefix");
    }

    [Fact]
    public void Ein_zu_langes_Praefix_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["artikelKategorie"] = new GeneratorSettings
        {
            Type = "token",
            Prefix = new string('A', 39) + "~", // 40 Zeichen, Zeichenvorrat gueltig
        };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.artikelKategorie.prefix");
    }

    [Fact]
    public void Ein_gueltiges_Praefix_erzeugt_keinen_Befund()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["artikelKategorie"] = new GeneratorSettings { Type = "token", Prefix = "Artikel~" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.DoesNotContain(befunde, b => b.Path == "generators.artikelKategorie.prefix");
    }

    [Fact]
    public void Ein_unbekannter_Generator_wird_beim_Laden_bemaengelt()
    {
        var profile = new Profile
        {
            ProfileName = "test",
            Fields =
            [
                new FieldRule { Match = "Name", Action = FieldAction.Pseudonymize, Generator = "gibtsNicht" },
            ],
        };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b => b.Severity == ValidationSeverity.Error && b.Path.Contains("generator"));
    }

    [Fact]
    public void Pseudonymisieren_ohne_Generator_wird_bemaengelt()
    {
        var profile = new Profile
        {
            ProfileName = "test",
            Fields = [new FieldRule { Match = "Name", Action = FieldAction.Pseudonymize }],
        };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b => b.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Ein_fehlerhaftes_Muster_wird_bemaengelt()
    {
        var profile = new Profile
        {
            ProfileName = "test",
            TextRules = [new TextRule { Name = "kaputt", Pattern = "[unvollstaendig", Generator = "token" }],
        };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b => b.Severity == ValidationSeverity.Error && b.Path.Contains("pattern"));
    }

    [Fact]
    public void Eine_nachlaessige_Vorgabe_wird_angemahnt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Defaults.UnknownField = FieldAction.Passthrough;
        profile.Fields.Add(new FieldRule { Match = "Name", Action = FieldAction.Passthrough });

        var befunde = ProfileValidator.Validate(profile);

        // Kein Fehler — die Entscheidung darf getroffen werden — aber ein Hinweis.
        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Warning && b.Path == "defaults.unknownField");
    }

    [Fact]
    public void Ein_fehlerhaftes_Profil_kommt_gar_nicht_erst_bis_zum_Lauf()
    {
        var profile = new Profile
        {
            ProfileName = "test",
            Fields =
            [
                new FieldRule { Match = "Name", Action = FieldAction.Pseudonymize, Generator = "gibtsNicht" },
            ],
        };

        Assert.Throws<ConfigurationException>(() => new ObfuscationEngine(profile));
    }

    [Fact]
    public void Ein_unlesbares_dateRange_von_bis_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["dateRange"] = new GeneratorSettings { From = "nicht-parsebar", To = "2000-12-31" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.dateRange.from");
    }

    [Fact]
    public void Ein_dateRange_bei_dem_von_nach_bis_liegt_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["dateRange"] = new GeneratorSettings { From = "2000-12-31", To = "2000-01-01" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.dateRange.from");
    }

    [Fact]
    public void Eine_unbekannte_Granularitaet_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["dateGeneralize"] = new GeneratorSettings { Granularity = "woche" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.dateGeneralize.granularity");
    }

    [Fact]
    public void Eine_leere_pattern_Maske_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["pattern"] = new GeneratorSettings { Pattern = "" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.pattern.pattern");
    }

    [Fact]
    public void Eine_kurze_pattern_Maske_wird_angemahnt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["pattern"] = new GeneratorSettings { Pattern = "99" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Warning && b.Path == "generators.pattern.pattern");
    }

    [Fact]
    public void Ein_wordlist_ohne_Werte_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["wordlist"] = new GeneratorSettings();

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.wordlist.values");
    }

    [Fact]
    public void Ein_wordlist_mit_wenigen_Werten_wird_angemahnt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["wordlist"] = new GeneratorSettings { Values = ["A", "B"] };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Warning && b.Path == "generators.wordlist.values");
    }

    [Fact]
    public void Negative_partialMask_Werte_werden_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["partialMask"] = new GeneratorSettings { KeepFirst = -1 };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.partialMask.keepFirst");
    }

    [Fact]
    public void Ein_mehrstelliges_partialMask_Maskierungszeichen_wird_bemaengelt()
    {
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["partialMask"] = new GeneratorSettings { MaskChar = "**" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.partialMask.maskChar");
    }

    [Fact]
    public void Eine_Option_an_einem_dafuer_nicht_vorgesehenen_Basistyp_wird_bemaengelt()
    {
        // Die Tabelle aus ValidateOptionOwnership deckt nicht nur "prefix" ab:
        // "granularity" gehoert zu dateGeneralize, nicht zu numericId.
        var profile = new Profile { ProfileName = "test" };
        profile.Generators["kundenId"] = new GeneratorSettings { Type = "numericId", Granularity = "year" };

        var befunde = ProfileValidator.Validate(profile);

        Assert.Contains(befunde, b =>
            b.Severity == ValidationSeverity.Error && b.Path == "generators.kundenId.granularity");
    }

    [Fact]
    public void Profile_ueberstehen_das_Schreiben_und_Lesen_unveraendert()
    {
        using var setup = ScanProfile();
        var pfad = Path.Combine(setup.Directory, "obfuskation.json");

        ProfileStore.Save(setup.Profile, pfad);
        var geladen = ProfileStore.Load(pfad);

        Assert.Equal(setup.Profile.ProfileName, geladen.ProfileName);
        Assert.Equal(setup.Profile.Fields.Count, geladen.Fields.Count);
        Assert.Equal(setup.Profile.Fields[0].Action, geladen.Fields[0].Action);
        Assert.Equal(setup.Profile.Fields[0].Generator, geladen.Fields[0].Generator);
        Assert.Equal(setup.Profile.TextRules.Count, geladen.TextRules.Count);

        // Die Aufzaehlungen sollen in der Datei klein geschrieben stehen.
        var inhalt = File.ReadAllText(pfad);
        Assert.Contains("\"pseudonymize\"", inhalt);
        Assert.DoesNotContain("\"Pseudonymize\"", inhalt);
    }
}
