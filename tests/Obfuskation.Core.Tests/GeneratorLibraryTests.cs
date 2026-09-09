using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Generator-Bibliothek reist neben dem Profil und darf auf keinen Fall
/// den echten Bestand unter ~/.config/obfuskation lesen -- TestUmgebung
/// leitet XDG_CONFIG_HOME dafuer in ein Wegwerfverzeichnis um. Die meisten
/// Faelle hier bauen die Bibliothek deshalb direkt im Speicher und ruehren
/// GeneratorLibrary.DefaultPath gar nicht erst an.
/// </summary>
public class GeneratorLibraryTests
{
    [Fact]
    public void Fehlende_Datei_ergibt_eine_leere_Bibliothek()
    {
        var pfad = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N") + ".json");

        var bibliothek = GeneratorLibrary.Load(pfad);

        Assert.True(bibliothek.IsEmpty);
    }

    [Fact]
    public void Der_Vorgabepfad_liegt_unter_dem_umgeleiteten_Konfigurationsverzeichnis()
    {
        // Das ist die kritische Absicherung: ein Test, der DefaultPath oder
        // Load() ohne Pfad auswertet, darf niemals in ~/.config/obfuskation
        // nachsehen. TestUmgebung leitet XDG_CONFIG_HOME assemblyweit um,
        // bevor irgendein Test laeuft -- dieser Test schreibt nichts und
        // braucht deshalb kein Aufraeumen.
        Assert.StartsWith(TestUmgebung.KonfigVerzeichnis, GeneratorLibrary.DefaultPath);
        Assert.True(GeneratorLibrary.Load().IsEmpty);
    }

    [Fact]
    public void Kaputtes_JSON_wirft_mit_dem_Pfad_in_der_Meldung()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verzeichnis);
        var pfad = Path.Combine(verzeichnis, "generators.json");
        File.WriteAllText(pfad, "{ kein gueltiges json");

        var ex = Assert.Throws<ConfigurationException>(() => GeneratorLibrary.Load(pfad));

        Assert.Contains(pfad, ex.Message);
    }

    [Fact]
    public void Ein_Bibliotheksgenerator_ist_ueber_die_Registry_erreichbar()
    {
        var library = new GeneratorLibrary
        {
            Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        };
        var profile = new Profile();
        var deriver = new SeedDeriver(new byte[32]);

        var registry = GeneratorRegistry.Build(profile, deriver, library);

        Assert.True(registry.Contains("assetTag"));
        var wert = registry.Get("assetTag").Generate(deriver.Derive("assetTag", "INV123456", 0), "INV123456");
        Assert.Matches("^INV[0-9]{6}$", wert);
    }

    [Fact]
    public void Ein_gleichnamiger_Profileintrag_gewinnt_gegen_die_Bibliothek()
    {
        var library = new GeneratorLibrary
        {
            Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        };
        var profile = new Profile();
        profile.Generators["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "ZZ999999" };
        var deriver = new SeedDeriver(new byte[32]);

        var registry = GeneratorRegistry.Build(profile, deriver, library);

        var wert = registry.Get("assetTag").Generate(deriver.Derive("assetTag", "INV123456", 0), "INV123456");
        Assert.StartsWith("ZZ", wert);
    }

    [Fact]
    public void Eine_gleichnamige_Textregel_des_Profils_gewinnt_und_die_Bibliotheksregel_greift_nicht_mehr()
    {
        // Die Bibliotheksregel bekommt bewusst die hoehere Prioritaet: griffe
        // sie trotz Namensgleichheit noch mit, wuerde sie die Ueberlappung
        // gewinnen und der Test wuerde einen echten Fehler zuverlaessig zeigen
        // (statt sich auf eine zufaellige Sortierreihenfolge bei Gleichstand
        // zu verlassen).
        var library = new GeneratorLibrary
        {
            Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
            TextRules =
            [
                new TextRule { Name = "assetTag", Priority = 99, Pattern = @"\bINV\d{6}\b", Generator = "assetTag" },
            ],
        };

        using var setup = new TestProfile(p =>
        {
            p.TextRules.Clear();
            p.TextRules.Add(new TextRule
            {
                Name = "assetTag", Priority = 50, Pattern = @"\bINV\d{6}\b", Generator = "redact",
            });
            p.Fields.Add(new FieldRule
            {
                Match = "Notiz", MatchType = FieldMatchType.Exact, Action = FieldAction.ScanText,
            });
        });

        var engine = setup.CreateEngine(library);
        var eingabe = TestProfile.Utf8("Notiz\nNeustart von INV123456\n");

        var ergebnis = engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });
        var text = TestProfile.FromUtf8(ergebnis.Content);

        // Die Bibliotheksregel (Generator "assetTag", Format INV######) griff
        // nicht -- stattdessen die gleichnamige Profilregel mit "redact".
        Assert.Contains("***", text);
        Assert.DoesNotContain("INV123456", text);
    }
}
