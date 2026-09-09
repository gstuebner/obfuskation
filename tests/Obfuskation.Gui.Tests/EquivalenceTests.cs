using System.Text;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Oberflaeche und Kommandozeile muessen dasselbe Ergebnis liefern.
///
/// Sie gehen verschiedene Wege — die eine ueber Ansichtsmodelle, die andere
/// ueber Befehlszeilenargumente —, aber beide enden in derselben
/// <see cref="ObfuscationEngine"/> mit derselben Ersetzungstabelle. Liefe das
/// auseinander, waere die Rueckabbildung je nach benutztem Programm
/// verschieden, und das Werkzeug damit unbrauchbar.
/// </summary>
public class EquivalenceTests : IDisposable
{
    private readonly string _verzeichnis;

    public EquivalenceTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-gleichlauf",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_verzeichnis))
                Directory.Delete(_verzeichnis, recursive: true);
        }
        catch (IOException)
        {
            // Wegwerfverzeichnis; ein Rest stoert den Testlauf nicht.
        }
    }

    private const string Inhalt =
        "Kundennummer;Kundenname;IBAN;Betrag\n"
        + "4711;Max Mustermann;DE02120300000000202051;1234,56\n"
        + "4712;Erika Musterfrau;DE02500105170137075030;-89,90\n";

    private Profile ErzeugeProfil()
    {
        var profil = new Profile
        {
            ProfileName = "gleichlauf",
            MappingStore = Path.Combine(_verzeichnis, "mapping.json"),
            Fields =
            [
                new FieldRule
                {
                    Match = "Kundennummer", Action = FieldAction.Pseudonymize, Generator = "numericId",
                },
                new FieldRule
                {
                    Match = "Kundenname", Action = FieldAction.Pseudonymize, Generator = "personName",
                },
                new FieldRule { Match = "IBAN", Action = FieldAction.Pseudonymize, Generator = "iban" },
                new FieldRule { Match = "Betrag", Action = FieldAction.Passthrough },
            ],
        };

        ProfileStore.Save(profil, Path.Combine(_verzeichnis, ProfileStore.DefaultFileName));
        return profil;
    }

    [Fact]
    public async Task Die_Oberflaeche_liefert_dasselbe_wie_ein_unmittelbarer_Lauf()
    {
        var profil = ErzeugeProfil();
        var eingabe = new UTF8Encoding(false).GetBytes(Inhalt);

        // Der Weg, den auch das Kommandozeilenprogramm nimmt.
        var ueberEngine = new ObfuscationEngine(profil)
            .Obfuscate(eingabe, "kunden.csv", new RunOptions { Strict = true });

        // Der Weg der Oberflaeche: Profil laden, Datei einlesen, verarbeiten.
        // Dieselbe Tabelle, also muessen dieselben Pseudonyme herauskommen.
        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        await File.WriteAllBytesAsync(csvPfad, eingabe);

        var sitzung = ProfileSession.Load(Path.Combine(_verzeichnis, ProfileStore.DefaultFileName));
        var ueberOberflaeche = sitzung.Engine.Obfuscate(
            await File.ReadAllBytesAsync(csvPfad), csvPfad, new RunOptions { Strict = true });

        Assert.Equal(
            Encoding.UTF8.GetString(ueberEngine.Content),
            Encoding.UTF8.GetString(ueberOberflaeche.Content));
    }

    [Fact]
    public async Task Was_die_Oberflaeche_ersetzt_holt_sie_auch_zurueck()
    {
        ErzeugeProfil();

        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        var eingabe = new UTF8Encoding(false).GetBytes(Inhalt);
        await File.WriteAllBytesAsync(csvPfad, eingabe);

        var sitzung = ProfileSession.Load(Path.Combine(_verzeichnis, ProfileStore.DefaultFileName));

        var ersetzt = sitzung.Engine.Obfuscate(eingabe, csvPfad, new RunOptions { Strict = true });
        Assert.DoesNotContain("Max Mustermann", Encoding.UTF8.GetString(ersetzt.Content));

        var zurueck = sitzung.Engine.Deobfuscate(ersetzt.Content, csvPfad, new RunOptions());

        Assert.Equal(eingabe, zurueck.Content);
    }

    [Fact]
    public async Task Die_Vorschau_zeigt_genau_das_was_der_Lauf_erzeugt()
    {
        ErzeugeProfil();

        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        await File.WriteAllBytesAsync(csvPfad, new UTF8Encoding(false).GetBytes(Inhalt));

        var settings = new GuiSettings();
        var modell = new MainViewModel(settings,
            () => throw new InvalidOperationException("kein Dialog in diesem Test"));

        await modell.InitializeAsync(
            Path.Combine(_verzeichnis, ProfileStore.DefaultFileName), csvPfad);

        // Erst einen Bestand anlegen, damit das Salt feststeht.
        var lauf = modell.Session!.Engine.Obfuscate(
            await File.ReadAllBytesAsync(csvPfad), csvPfad, new RunOptions { Strict = true });

        var ersteZeile = Encoding.UTF8.GetString(lauf.Content).Split('\n')[1].Split(';');

        // Und jetzt muss die Vorschau in der Oberflaeche dasselbe zeigen.
        var feld = modell.Fields.Single(f => f.FieldName == "Kundenname");
        modell.SelectedField = feld;

        Assert.False(feld.PreviewIsExample);   // Die Tabelle besteht jetzt.
        Assert.Equal(ersteZeile[1], feld.Preview);
    }

    /// <summary>
    /// Ein zweites Profil, das "Betrag" ueber einen eigenen Namensraum auf
    /// je einen der neuen Generatoren stellt -- dieselbe Konstruktion, die
    /// der Optionsdialog anlegt (siehe FieldRuleViewModel.EnsureNamespace),
    /// hier von Hand nachgebaut, um Oberflaeche und Kommandozeile auch fuer
    /// diese Generatoren gegeneinander zu pruefen.
    /// </summary>
    private Profile ErzeugeProfilMitNeuenGeneratoren()
    {
        var profil = new Profile
        {
            ProfileName = "gleichlauf-neu",
            MappingStore = Path.Combine(_verzeichnis, "mapping-neu.json"),
            Generators =
            {
                ["betragMaske"] = new GeneratorSettings { Type = "pattern", Pattern = "999,99" },
            },
            Fields =
            [
                new FieldRule
                {
                    Match = "Kundennummer", Action = FieldAction.Pseudonymize, Generator = "numericId",
                },
                new FieldRule
                {
                    Match = "Kundenname", Action = FieldAction.Pseudonymize, Generator = "personName",
                },
                new FieldRule { Match = "IBAN", Action = FieldAction.Pseudonymize, Generator = "iban" },
                new FieldRule
                {
                    Match = "Betrag", Action = FieldAction.Pseudonymize, Generator = "betragMaske",
                },
            ],
        };

        ProfileStore.Save(profil, Path.Combine(_verzeichnis, "profil-neu.json"));
        return profil;
    }

    [Fact]
    public async Task Die_Oberflaeche_liefert_dasselbe_wie_ein_unmittelbarer_Lauf_mit_den_neuen_Generatoren()
    {
        var profil = ErzeugeProfilMitNeuenGeneratoren();
        var eingabe = new UTF8Encoding(false).GetBytes(Inhalt);

        var ueberEngine = new ObfuscationEngine(profil)
            .Obfuscate(eingabe, "kunden.csv", new RunOptions { Strict = true });

        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        await File.WriteAllBytesAsync(csvPfad, eingabe);

        var sitzung = ProfileSession.Load(Path.Combine(_verzeichnis, "profil-neu.json"));
        var ueberOberflaeche = sitzung.Engine.Obfuscate(
            await File.ReadAllBytesAsync(csvPfad), csvPfad, new RunOptions { Strict = true });

        Assert.Equal(
            Encoding.UTF8.GetString(ueberEngine.Content),
            Encoding.UTF8.GetString(ueberOberflaeche.Content));

        // Die Maske muss auch tatsaechlich gegriffen haben, sonst prueft der
        // Vergleich oben nur zwei gleich falsche Ergebnisse gegeneinander.
        var ersteZeile = Encoding.UTF8.GetString(ueberOberflaeche.Content).Split('\n')[1].Split(';');
        Assert.Matches(@"^\d{3},\d{2}$", ersteZeile[3]);
    }

    /// <summary>
    /// Ein Profil, das ein Feld ueber einen Erweiterungseintrag ersetzt statt
    /// ueber einen eigenen Namensraum -- der Inventarnummer-Fall aus dem Plan: die
    /// Erweiterung traegt "assetTag" (Basistyp <c>pattern</c>), das Profil
    /// kennt den Schluessel selbst nicht.
    /// </summary>
    private Profile ErzeugeProfilMitErweiterungsverweis()
    {
        var profil = new Profile
        {
            ProfileName = "gleichlauf-erweiterung",
            MappingStore = Path.Combine(_verzeichnis, "mapping-erweiterung.json"),
            Fields =
            [
                new FieldRule { Match = "Kundennummer", Action = FieldAction.Pseudonymize, Generator = "assetTag" },
                new FieldRule
                {
                    Match = "Kundenname", Action = FieldAction.Pseudonymize, Generator = "personName",
                },
                new FieldRule { Match = "IBAN", Action = FieldAction.Pseudonymize, Generator = "iban" },
                new FieldRule { Match = "Betrag", Action = FieldAction.Passthrough },
            ],
        };

        ProfileStore.Save(profil, Path.Combine(_verzeichnis, "profil-erweiterung.json"));
        return profil;
    }

    /// <summary>
    /// Wird bewusst als Objekt statt als Datei unter einem der beiden
    /// Fundorte von <see cref="ExtensionLibrary.ResolvePath"/> erzeugt und
    /// explizit durchgereicht -- so bleibt der Test unabhaengig vom (fuer den
    /// ganzen Testlauf gemeinsamen) <c>XDG_CONFIG_HOME</c> und kann keinen
    /// anderen Test beeinflussen.
    /// </summary>
    private static ExtensionLibrary ErzeugeErweiterung() => new()
    {
        Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        TextRules =
        {
            new TextRule { Name = "assetTag", Priority = 95, Pattern = @"\bINV\d{6}\b", Generator = "assetTag" },
        },
    };

    [Fact]
    public async Task Die_Oberflaeche_liefert_dasselbe_wie_ein_unmittelbarer_Lauf_mit_gesetzter_Erweiterung()
    {
        var profil = ErzeugeProfilMitErweiterungsverweis();
        var erweiterung = ErzeugeErweiterung();

        var eingabe = new UTF8Encoding(false).GetBytes(
            "Kundennummer;Kundenname;IBAN;Betrag\n"
            + "INV123456;Max Mustermann;DE02120300000000202051;1234,56\n"
            + "INV654321;Erika Musterfrau;DE02500105170137075030;-89,90\n");

        // Der Weg, den auch das Kommandozeilenprogramm nimmt -- diesmal mit
        // gesetzter Erweiterung.
        var ueberEngine = new ObfuscationEngine(profil, erweiterung)
            .Obfuscate(eingabe, "kunden.csv", new RunOptions { Strict = true });

        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        await File.WriteAllBytesAsync(csvPfad, eingabe);

        // Der Weg der Oberflaeche: dieselbe Erweiterung wird -- wie beim echten
        // Programmstart -- an die Sitzung durchgereicht statt neu geladen.
        var sitzung = ProfileSession.Load(Path.Combine(_verzeichnis, "profil-erweiterung.json"), erweiterung);
        var ueberOberflaeche = sitzung.Engine.Obfuscate(
            await File.ReadAllBytesAsync(csvPfad), csvPfad, new RunOptions { Strict = true });

        Assert.Equal(
            Encoding.UTF8.GetString(ueberEngine.Content),
            Encoding.UTF8.GetString(ueberOberflaeche.Content));

        // Der Erweiterungsgenerator muss auch tatsaechlich gegriffen haben --
        // sonst pruefte der Vergleich oben nur zwei gleich falsche Ergebnisse
        // gegeneinander.
        var ersteZeile = Encoding.UTF8.GetString(ueberOberflaeche.Content).Split('\n')[1].Split(';');
        Assert.Matches(@"^INV\d{6}$", ersteZeile[0]);
    }

    [Fact]
    public async Task Die_Pruefung_der_Oberflaeche_findet_die_Echtwerte_wieder()
    {
        ErzeugeProfil();

        var csvPfad = Path.Combine(_verzeichnis, "kunden.csv");
        var eingabe = new UTF8Encoding(false).GetBytes(Inhalt);
        await File.WriteAllBytesAsync(csvPfad, eingabe);

        var sitzung = ProfileSession.Load(Path.Combine(_verzeichnis, ProfileStore.DefaultFileName));

        var ersetzt = sitzung.Engine.Obfuscate(eingabe, csvPfad, new RunOptions { Strict = true });

        // Die eigene Ausgabe muss sauber durchgehen ...
        var sauber = sitzung.Engine.Scan(ersetzt.Content, csvPfad, new RunOptions());
        Assert.Empty(sauber.Report.Findings);

        // ... das Original dagegen anschlagen.
        var befunde = sitzung.Engine.Scan(eingabe, csvPfad, new RunOptions());
        Assert.NotEmpty(befunde.Report.Findings);
        Assert.Contains(befunde.Report.Findings, f => f.Kind == "echtwertAusTabelle");
    }
}
