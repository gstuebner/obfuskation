using System.Runtime.InteropServices;
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
    public void Eine_unlesbare_Datei_ergibt_eine_ConfigurationException()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
            return; // Dateirechte gibt es unter Windows so nicht, und root liest ohnehin alles.

        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verzeichnis);
        var pfad = Path.Combine(verzeichnis, "obfuskation.json");
        File.WriteAllText(pfad, "{}");
        File.SetUnixFileMode(pfad, UnixFileMode.None);

        try
        {
            var ex = Assert.Throws<ConfigurationException>(() => ExtensionLibrary.Load(pfad));
            Assert.Contains(pfad, ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
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

    // ------------------------------------------ AdoptProjectGenerators (P4)

    [Fact]
    public void Ein_nur_im_Profil_vorhandener_Generator_wird_mitkopiert()
    {
        var profile = new Profile();
        profile.Generators["fw"] = new GeneratorSettings { Type = "token", Prefix = "FW~" };

        var extensions = new ExtensionLibrary
        {
            TextRules = [new TextRule { Name = "fw", Pattern = @"\bFW\d{6}\b", Generator = "fw" }],
        };

        var kopiert = extensions.AdoptProjectGenerators(profile);

        Assert.Equal(new[] { "fw" }, kopiert);
        Assert.True(extensions.Generators.ContainsKey("fw"));
        Assert.Equal("FW~", extensions.Generators["fw"].Prefix);

        // Kopie, keine geteilte Referenz -- eine spaetere Aenderung an der
        // einen Fassung darf die andere nicht beruehren.
        Assert.NotSame(profile.Generators["fw"], extensions.Generators["fw"]);
    }

    [Fact]
    public void Ein_bereits_vorhandener_globaler_Generator_wird_nicht_ueberschrieben()
    {
        var profile = new Profile();
        profile.Generators["fw"] = new GeneratorSettings { Type = "token", Prefix = "FW~" };

        var extensions = new ExtensionLibrary
        {
            Generators = { ["fw"] = new GeneratorSettings { Type = "token", Prefix = "ANDERS~" } },
            TextRules = [new TextRule { Name = "fw", Pattern = @"\bFW\d{6}\b", Generator = "fw" }],
        };

        var kopiert = extensions.AdoptProjectGenerators(profile);

        Assert.Empty(kopiert);
        Assert.Equal("ANDERS~", extensions.Generators["fw"].Prefix);
    }

    [Fact]
    public void Eingebaute_Generatoren_und_scanText_werden_ignoriert()
    {
        var profile = new Profile();

        var extensions = new ExtensionLibrary
        {
            TextRules = [new TextRule { Name = "mail", Pattern = @"\S+@\S+", Generator = "email" }],
            FieldRules = [new FieldNameRule { Pattern = "Notiz", Generator = "scanText" }],
        };

        var kopiert = extensions.AdoptProjectGenerators(profile);

        Assert.Empty(kopiert);
        Assert.Empty(extensions.Generators);
    }

    [Fact]
    public void Ein_Feldregel_Generator_aus_dem_Profil_wird_ebenfalls_mitkopiert()
    {
        var profile = new Profile();
        profile.Generators["kundennummer"] = new GeneratorSettings { Type = "numericId" };

        var extensions = new ExtensionLibrary
        {
            FieldRules = [new FieldNameRule { Pattern = "Kdnr", Generator = "kundennummer" }],
        };

        var kopiert = extensions.AdoptProjectGenerators(profile);

        Assert.Equal(new[] { "kundennummer" }, kopiert);
        Assert.True(extensions.Generators.ContainsKey("kundennummer"));
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
    public void Eine_kommentierte_Erweiterung_wird_gewaehlt_und_geladen()
    {
        // Fehler 2 des Plans: JsonDocument.Parse ohne CommentHandling warf hier
        // eine JsonException, LooksLikeProfile liess sie durch (galt als
        // Profil), und ResolvePath uebersprang die Datei -- eine von Hand
        // kommentierte Erweiterungsdatei wirkte nie.
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        File.WriteAllText(pfad, """
            {
              // Kommentar am Zeilenanfang
              "textRules": [
                { "name": "fw", "pattern": "\\bFW\\d{6}\\b", "generator": "token" }
              ],
              /* Blockkommentar */
              "fieldRules": [
                { "pattern": ".*iban.*", "generator": "iban" },
                { "pattern": ".*plz.*", "generator": "postalCode" }
              ]
            }
            """);

        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);
        Assert.Equal(pfad, ergebnis.Path);

        var erweiterung = ExtensionLibrary.Load(ergebnis.Path);
        Assert.Single(erweiterung.TextRules);
        Assert.Equal(2, erweiterung.FieldRules.Count);
    }

    [Fact]
    public void Kaputtes_JSON_wird_gewaehlt_statt_uebersprungen_und_Load_wirft()
    {
        // Anders als eine Datei mit Profilinhalt (siehe naechster Test) gilt
        // eine kaputte Datei nicht als "dort liegt ein Profil" -- sie wird
        // gewaehlt, und erst Load meldet den eigentlichen Fehler.
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        File.WriteAllText(pfad, "{ kein gueltiges json");

        var ergebnis = ExtensionLibrary.ResolvePath(_programVerzeichnis);

        Assert.Equal(pfad, ergebnis.Path);
        var kandidat = ergebnis.Candidates.Single(c => c.Origin == ExtensionOrigin.ProgramDirectory);
        Assert.False(kandidat.SkippedAsProfile);

        var ex = Assert.Throws<ConfigurationException>(() => ExtensionLibrary.Load(ergebnis.Path));
        Assert.Contains(pfad, ex.Message);
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

/// <summary>
/// Die Erweiterungsdatei schreiben. Seit die Oberflaeche ein hauseigenes
/// Muster anlegen kann, entsteht diese Datei nicht mehr nur im Texteditor --
/// gepruefte wird deshalb vor allem, dass dabei nichts verlorengeht, was ein
/// Mensch von Hand hineingeschrieben hat.
/// </summary>
public sealed class ExtensionLibrarySaveTests : IDisposable
{
    private readonly string _programVerzeichnis;

    public ExtensionLibrarySaveTests()
    {
        _programVerzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-extensions-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_programVerzeichnis);
        Directory.CreateDirectory(PathHelper.ConfigDirectory);
    }

    [Fact]
    public void Geschrieben_und_wieder_gelesen_ergibt_dieselbe_Regel()
    {
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        var erweiterung = new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "fw", Priority = 60, Generator = "token", Pattern = @"\bFW\d{6}\b" } },
        };

        erweiterung.Save(pfad);
        var gelesen = ExtensionLibrary.Load(pfad);

        var regel = Assert.Single(gelesen.TextRules);
        Assert.Equal("fw", regel.Name);
        Assert.Equal(@"\bFW\d{6}\b", regel.Pattern);
        Assert.Equal(60, regel.Priority);
    }

    [Fact]
    public void Die_geschriebene_Datei_traegt_keine_berechneten_Felder()
    {
        // "isEmpty" ist eine Auskunft der Klasse, kein Bestandteil des
        // Dateiformats -- wer die Datei danach von Hand oeffnet, soll nur
        // finden, was er selbst hineinschreiben wuerde.
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(pfad);

        Assert.DoesNotContain("isEmpty", File.ReadAllText(pfad), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vor_dem_Ueberschreiben_einer_kommentierten_Datei_entsteht_eine_Sicherungskopie()
    {
        // Die mitgelieferte Beispieldatei ist ausfuehrlich kommentiert, und
        // System.Text.Json schreibt Kommentare nicht zurueck. Wer seine Datei
        // von Hand erklaert hat, soll die Erklaerungen wiederfinden.
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        File.WriteAllText(pfad, "// Hauseigene Muster, gepflegt von der IT\n{ \"version\": 1 }");

        new ExtensionLibrary().Save(pfad);

        Assert.True(File.Exists(pfad + ".bak"));
        Assert.Contains("gepflegt von der IT", File.ReadAllText(pfad + ".bak"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_Kommentare_entsteht_keine_Sicherungskopie()
    {
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(pfad);

        new ExtensionLibrary().Save(pfad);

        Assert.False(File.Exists(pfad + ".bak"));
    }

    [Fact]
    public void Ein_Schraegstrich_in_einem_Muster_gilt_nicht_als_Kommentar()
    {
        // Sonst legte jedes Speichern einer Datei mit einer URL oder einem
        // Datumsmuster eine Sicherungskopie an.
        var pfad = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "quelle", Pattern = @"https://beispiel\.de/\d+" } },
        }.Save(pfad);

        Assert.False(ExtensionLibrary.HasComments(pfad));
    }

    [Fact]
    public void Ohne_vorhandene_Datei_wird_in_den_Konfigurationsordner_geschrieben()
    {
        var zustand = ExtensionLibrary.GetWriteState(_programVerzeichnis);

        Assert.Equal(Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName), zustand.Path);
        Assert.True(zustand.CanWrite);
        Assert.Null(zustand.Reason);
    }

    [Fact]
    public void Eine_vorhandene_Datei_im_Konfigurationsordner_ist_das_Ziel()
    {
        var vorhanden = Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(vorhanden);

        var zustand = ExtensionLibrary.GetWriteState(_programVerzeichnis);

        Assert.Equal(vorhanden, zustand.Path);
        Assert.True(zustand.CanWrite);
    }

    [Fact]
    public void Eine_beschreibbare_Datei_neben_der_Programmdatei_bleibt_das_Ziel()
    {
        // Wer seine Erweiterung bewusst neben das Programm gelegt hat und sie
        // beschreiben darf, soll sie dort weiterpflegen -- sonst entstuenden
        // zwei Dateien, von denen nur die erste gilt.
        var neben = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(neben);

        var zustand = ExtensionLibrary.GetWriteState(_programVerzeichnis);

        Assert.Equal(neben, zustand.Path);
        Assert.True(zustand.CanWrite);
    }

    [Fact]
    public void Eine_schreibgeschuetzte_Datei_neben_der_Programmdatei_ist_nicht_beschreibbar()
    {
        // Genau der Fehlerfall aus Fehler 2 des Plans: die geltende Datei
        // liegt neben der Programmdatei, ist dort aber schreibgeschuetzt --
        // GetWriteState darf NICHT stillschweigend auf den
        // Konfigurationsordner ausweichen (das war der Fehler von
        // ResolveWritePath: ResolvePath liest das Programmverzeichnis
        // weiterhin zuerst, eine Aenderung im Konfigurationsordner waere also
        // nach dem naechsten Neustart wirkungslos gewesen).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;  // Dateirechte gibt es dort in dieser Form nicht.

        var neben = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(neben);
        File.SetUnixFileMode(neben, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        try
        {
            var zustand = ExtensionLibrary.GetWriteState(_programVerzeichnis);

            Assert.Equal(neben, zustand.Path);
            Assert.False(zustand.CanWrite);
            Assert.NotNull(zustand.Reason);
            Assert.Contains("Programmverzeichnis", zustand.Reason);
        }
        finally
        {
            // Sonst kann Dispose() die Testdatei nicht mehr loeschen.
            File.SetUnixFileMode(neben, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Ein_schreibgeschuetzter_Ordner_um_eine_beschreibbare_Datei_ist_nicht_beschreibbar()
    {
        // A2 des Plans: die Datei selbst ist beschreibbar, aber Save braucht
        // fuer die Nebendatei (und ggf. .bak) auch Schreibrecht auf den
        // Ordner -- das faellt sonst erst beim tatsaechlichen Speichern auf.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
            return; // Ordnerrechte gibt es unter Windows so nicht, und root darf ohnehin ueberall schreiben.

        var neben = Path.Combine(_programVerzeichnis, ExtensionLibrary.FileName);
        new ExtensionLibrary().Save(neben);

        var vorherigerModus = File.GetUnixFileMode(_programVerzeichnis);
        File.SetUnixFileMode(_programVerzeichnis,
            UnixFileMode.UserRead | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        try
        {
            var zustand = ExtensionLibrary.GetWriteState(_programVerzeichnis);

            Assert.Equal(neben, zustand.Path);
            Assert.False(zustand.CanWrite);
            Assert.NotNull(zustand.Reason);
            Assert.Contains("Ordner", zustand.Reason);
        }
        finally
        {
            // Sonst kann Dispose() das Testverzeichnis nicht mehr aufraeumen.
            File.SetUnixFileMode(_programVerzeichnis, vorherigerModus);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_programVerzeichnis))
                Directory.Delete(_programVerzeichnis, recursive: true);

            foreach (var datei in new[]
                     {
                         Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName),
                         Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName + ".bak"),
                     })
            {
                if (File.Exists(datei))
                    File.Delete(datei);
            }
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis darf den Testlauf nicht stoeren.
        }
    }
}
