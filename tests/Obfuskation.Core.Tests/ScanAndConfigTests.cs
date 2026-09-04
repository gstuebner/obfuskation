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
