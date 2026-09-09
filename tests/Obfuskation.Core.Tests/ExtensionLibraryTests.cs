using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Erweiterungsdatei reist neben dem Profil und darf auf keinen Fall den
/// echten Bestand unter ~/.config/obfuskation lesen -- TestUmgebung leitet
/// XDG_CONFIG_HOME dafuer in ein Wegwerfverzeichnis um. Die meisten Faelle
/// hier bauen die Erweiterung deshalb direkt im Speicher und ruehren
/// ExtensionLibrary.ResolvePath() ohne ausdruecklich uebergebenes Verzeichnis
/// gar nicht erst an -- der Fundort "neben der Programmdatei" wird immer mit
/// einem eigens angelegten Testverzeichnis geprueft, nie ueber
/// Environment.ProcessPath des Testlaeufers (siehe ResolvePath_Tests).
/// </summary>
public class ExtensionLibraryTests
{
    [Fact]
    public void Fehlende_Datei_ergibt_eine_leere_Erweiterung()
    {
        var pfad = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N") + ".json");

        var erweiterung = ExtensionLibrary.Load(pfad);

        Assert.True(erweiterung.IsEmpty);
    }

    [Fact]
    public void Ohne_Fundort_ergibt_Load_eine_leere_Erweiterung()
    {
        // Kritische Absicherung: ein Load() ohne Pfad darf niemals in
        // ~/.config/obfuskation nachsehen -- TestUmgebung leitet
        // XDG_CONFIG_HOME assemblyweit um, bevor irgendein Test laeuft. Der
        // Fundort "neben der Programmdatei" zeigt beim Testlaeufer auf ein
        // Verzeichnis ohne obfuskation.json, bleibt also ebenfalls leer.
        Assert.True(ExtensionLibrary.Load().IsEmpty);
    }

    [Fact]
    public void Kaputtes_JSON_wirft_mit_dem_Pfad_in_der_Meldung()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verzeichnis);
        var pfad = Path.Combine(verzeichnis, "obfuskation.json");
        File.WriteAllText(pfad, "{ kein gueltiges json");

        var ex = Assert.Throws<ConfigurationException>(() => ExtensionLibrary.Load(pfad));

        Assert.Contains(pfad, ex.Message);
    }

    [Fact]
    public void Ein_Erweiterungsgenerator_ist_ueber_die_Registry_erreichbar()
    {
        var extensions = new ExtensionLibrary
        {
            Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        };
        var profile = new Profile();
        var deriver = new SeedDeriver(new byte[32]);

        var registry = GeneratorRegistry.Build(profile, deriver, extensions);

        Assert.True(registry.Contains("assetTag"));
        var wert = registry.Get("assetTag").Generate(deriver.Derive("assetTag", "INV123456", 0), "INV123456");
        Assert.Matches("^INV[0-9]{6}$", wert);
    }

    [Fact]
    public void Ein_gleichnamiger_Profileintrag_gewinnt_gegen_die_Erweiterung()
    {
        var extensions = new ExtensionLibrary
        {
            Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        };
        var profile = new Profile();
        profile.Generators["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "ZZ999999" };
        var deriver = new SeedDeriver(new byte[32]);

        var registry = GeneratorRegistry.Build(profile, deriver, extensions);

        var wert = registry.Get("assetTag").Generate(deriver.Derive("assetTag", "INV123456", 0), "INV123456");
        Assert.StartsWith("ZZ", wert);
    }

    [Fact]
    public void Eine_gleichnamige_Textregel_des_Profils_gewinnt_und_die_Erweiterungsregel_greift_nicht_mehr()
    {
        // Die Erweiterungsregel bekommt bewusst die hoehere Prioritaet: griffe
        // sie trotz Namensgleichheit noch mit, wuerde sie die Ueberlappung
        // gewinnen und der Test wuerde einen echten Fehler zuverlaessig zeigen
        // (statt sich auf eine zufaellige Sortierreihenfolge bei Gleichstand
        // zu verlassen).
        var extensions = new ExtensionLibrary
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

        var engine = setup.CreateEngine(extensions);
        var eingabe = TestProfile.Utf8("Notiz\nNeustart von INV123456\n");

        var ergebnis = engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });
        var text = TestProfile.FromUtf8(ergebnis.Content);

        // Die Erweiterungsregel (Generator "assetTag", Format INV######) griff
        // nicht -- stattdessen die gleichnamige Profilregel mit "redact".
        Assert.Contains("***", text);
        Assert.DoesNotContain("INV123456", text);
    }
}

/// <summary>
/// <see cref="ExtensionLibrary.ResolvePath"/>: der Fundort "neben der
/// Programmdatei" wird ausschliesslich ueber den ausdruecklich uebergebenen
/// Parameter geprueft, niemals ueber <see cref="Environment.ProcessPath"/>
/// des Testlaeufers -- der zeigt auf den <c>dotnet test</c>-Host, nicht auf
/// irgendetwas, das dieser Test kontrolliert.
/// </summary>
public sealed class ExtensionLibraryResolvePathTests : IDisposable
{
    private readonly string _programVerzeichnis;

    public ExtensionLibraryResolvePathTests()
    {
        _programVerzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-extensions-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_programVerzeichnis);

        // PathHelper.ConfigDirectory haengt "obfuskation" an XDG_CONFIG_HOME an --
        // TestUmgebung legt nur das XDG-Verzeichnis selbst an, nicht diesen
        // Unterordner.
        Directory.CreateDirectory(PathHelper.ConfigDirectory);
    }

    private static ExtensionLibrary NeueErweiterung(string assetTagPattern) => new()
    {
        Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = assetTagPattern } },
    };

    private static void SchreibeErweiterung(string pfad, ExtensionLibrary erweiterung)
        => File.WriteAllText(pfad, System.Text.Json.JsonSerializer.Serialize(erweiterung, ProfileStore.JsonOptions));

    [Fact]
    public void Ohne_beide_Fundorte_ergibt_sich_eine_leere_Erweiterung()
    {
        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);

        Assert.Null(ergebnis.Path);
        Assert.True(ExtensionLibrary.Load(ergebnis.Path).IsEmpty);

        // Beide Fundorte muessen benannt sein, keiner davon vorhanden.
        Assert.Equal(2, ergebnis.Candidates.Count);
        Assert.All(ergebnis.Candidates, c => Assert.False(c.Exists));
    }

    [Fact]
    public void Eine_Datei_neben_der_Programmdatei_gewinnt_gegen_die_im_Konfigurationsordner()
    {
        SchreibeErweiterung(
            Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), NeueErweiterung("ZZ999999"));
        SchreibeErweiterung(
            Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName), NeueErweiterung("INV999999"));

        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);

        Assert.Equal(Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName), ergebnis.Path);
        Assert.Equal(ExtensionOrigin.ProgramDirectory, ergebnis.Origin);

        var geladen = ExtensionLibrary.Load(ergebnis.Path);
        Assert.Equal("INV999999", geladen.Generators["assetTag"].Pattern);
    }

    [Fact]
    public void Ohne_Datei_neben_der_Programmdatei_greift_der_Konfigurationsordner()
    {
        SchreibeErweiterung(
            Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), NeueErweiterung("ZZ999999"));

        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);

        Assert.Equal(Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), ergebnis.Path);
        Assert.Equal(ExtensionOrigin.ConfigDirectory, ergebnis.Origin);
    }

    [Fact]
    public void Eine_obfuskation_json_mit_Profilinhalt_neben_der_Programmdatei_wird_uebersprungen()
    {
        // Dieselbe Datei koennte an dieser Stelle auch ein Altprofil sein
        // (siehe ProfileStore.Discover) -- LooksLikeProfile entscheidet, und
        // ResolvePath sucht am naechsten Ort weiter, statt sie als
        // Erweiterung zu nehmen.
        ProfileStore.Save(
            new Profile { ProfileName = "faelschlich-hier" },
            Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName));
        SchreibeErweiterung(
            Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), NeueErweiterung("INV999999"));

        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);

        Assert.Equal(Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), ergebnis.Path);

        var programKandidat = ergebnis.Candidates.Single(c => c.Origin == ExtensionOrigin.ProgramDirectory);
        Assert.True(programKandidat.Exists);
        Assert.True(programKandidat.SkippedAsProfile);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_programVerzeichnis))
                Directory.Delete(_programVerzeichnis, recursive: true);

            var erweiterungImKonfigOrdner = Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName);
            if (File.Exists(erweiterungImKonfigOrdner))
                File.Delete(erweiterungImKonfigOrdner);
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis darf den Testlauf nicht stoeren.
        }
    }
}
