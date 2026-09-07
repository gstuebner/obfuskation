using Obfuskation.Core;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Cli.Tests;

/// <summary>
/// Die Namensauflösung in <see cref="CommandContext.LoadProfile"/>: <c>--config</c>
/// nimmt jetzt auch einen blossen Profilnamen entgegen, der im zentralen
/// Profilordner aufgeloest wird (Stufe 3 der Profilverwaltung).
/// </summary>
public sealed class CommandContextTests : IDisposable
{
    private readonly string _arbeitsverzeichnis;

    public CommandContextTests()
    {
        _arbeitsverzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-cli-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_arbeitsverzeichnis);
    }

    private static Profile NeuesProfil(string name) => new() { ProfileName = name };

    [Fact]
    public void Ein_blosser_Profilname_wird_im_zentralen_Ordner_gefunden()
    {
        var profil = NeuesProfil("probe");
        ProfileStore.Save(profil, PathHelper.DefaultProfilePath("probe"));

        var geladen = CommandContext.LoadProfile("probe");

        Assert.Equal("probe", geladen.ProfileName);
    }

    [Fact]
    public void Ein_unbekannter_Profilname_wirft_mit_Hinweis_auf_profile_list()
    {
        var ausnahme = Assert.Throws<ConfigurationException>(
            () => CommandContext.LoadProfile("gibtsnicht"));

        Assert.Contains("obfuskation profile list", ausnahme.Message);
    }

    [Fact]
    public void Ein_woertlicher_Dateipfad_wird_direkt_geladen_auch_wenn_er_wie_ein_Name_aussieht()
    {
        // Eine Datei, deren Name zufaellig wie ein Profilname aussieht, aber im
        // Arbeitsverzeichnis liegt statt im zentralen Ordner -- der Pfad
        // enthaelt ein Trennzeichen und muss deshalb woertlich genommen werden,
        // ohne erst im zentralen Ordner nachzusehen.
        var pfad = Path.Combine(_arbeitsverzeichnis, "eigenesProfil.json");
        ProfileStore.Save(NeuesProfil("eigenesProfil"), pfad);

        var geladen = CommandContext.LoadProfile(pfad);

        Assert.Equal("eigenesProfil", geladen.ProfileName);
    }

    [Fact]
    public void Eine_json_Endung_zaehlt_als_Pfad_nicht_als_Name()
    {
        // "kaputt.json" existiert weder im Arbeitsverzeichnis noch im
        // zentralen Ordner -- die .json-Endung darf trotzdem nicht dazu
        // fuehren, dass im zentralen Ordner gesucht wird; die Meldung muss
        // sich auf den woertlichen Pfad beziehen.
        var ausnahme = Assert.Throws<ConfigurationException>(
            () => CommandContext.LoadProfile("kaputt.json"));

        Assert.DoesNotContain("zentralen Ordner", ausnahme.Message);
    }

    [Fact]
    public void Ein_woertlicher_Pfad_mit_Trennzeichen_sucht_nicht_im_zentralen_Ordner()
    {
        // "doppelt.json" existiert sowohl als woertlicher Pfad als auch (unter
        // anderem Inhalt) im zentralen Ordner -- der uebergebene Wert enthaelt
        // ein Trennzeichen und muss deshalb woertlich genommen werden.
        ProfileStore.Save(NeuesProfil("zentrale-fassung"), PathHelper.DefaultProfilePath("doppelt"));

        var lokalerPfad = Path.Combine(_arbeitsverzeichnis, "doppelt.json");
        ProfileStore.Save(NeuesProfil("lokale-fassung"), lokalerPfad);

        var geladen = CommandContext.LoadProfile(lokalerPfad);

        Assert.Equal("lokale-fassung", geladen.ProfileName);
    }

    [Fact]
    public void Ohne_config_und_ohne_gefundene_obfuskation_json_wirft_mit_Hinweis_auf_profile_list()
    {
        var vorher = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_arbeitsverzeichnis);
        try
        {
            var ausnahme = Assert.Throws<ConfigurationException>(() => CommandContext.LoadProfile(null));
            Assert.Contains("obfuskation profile list", ausnahme.Message);
        }
        finally
        {
            Directory.SetCurrentDirectory(vorher);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_arbeitsverzeichnis))
                Directory.Delete(_arbeitsverzeichnis, recursive: true);
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis darf den Testlauf nicht stoeren.
        }
    }
}
