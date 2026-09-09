using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// <see cref="ProfileStore.Discover"/> und <see cref="ProfileStore.LooksLikeProfile"/>:
/// der neue Name <see cref="ProfileStore.DefaultFileName"/> gewinnt immer
/// gegen den alten <see cref="ProfileStore.LegacyFileName"/>, und der alte
/// Name zaehlt nur als Profil, wenn er auch wie eines aussieht -- an
/// derselben Stelle kann sonst die gleichnamige Erweiterungsdatei liegen
/// (siehe <see cref="ExtensionLibraryTests"/>).
/// </summary>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _verzeichnis;

    public ProfileStoreTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-store-tests", Guid.NewGuid().ToString("N"));
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
            // Ein liegengebliebenes Wegwerfverzeichnis darf den Testlauf nicht stoeren.
        }
    }

    private static Profile NeuesProfil(string name) => new() { ProfileName = name };

    [Fact]
    public void Discover_findet_den_neuen_Namen()
    {
        var pfad = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        ProfileStore.Save(NeuesProfil("test"), pfad);

        Assert.Equal(pfad, ProfileStore.Discover(_verzeichnis));
    }

    [Fact]
    public void Discover_findet_ein_Altprofil()
    {
        var pfad = Path.Combine(_verzeichnis, ProfileStore.LegacyFileName);
        ProfileStore.Save(NeuesProfil("test"), pfad);

        Assert.Equal(pfad, ProfileStore.Discover(_verzeichnis));
    }

    [Fact]
    public void Liegen_beide_im_selben_Verzeichnis_gewinnt_der_neue()
    {
        var neu = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        var alt = Path.Combine(_verzeichnis, ProfileStore.LegacyFileName);
        ProfileStore.Save(NeuesProfil("neu"), neu);
        ProfileStore.Save(NeuesProfil("alt"), alt);

        Assert.Equal(neu, ProfileStore.Discover(_verzeichnis));
    }

    [Fact]
    public void Ein_neuer_Fund_weiter_unten_gewinnt_gegen_einen_alten_weiter_oben()
    {
        var unten = Path.Combine(_verzeichnis, "projekt");
        Directory.CreateDirectory(unten);

        var altOben = Path.Combine(_verzeichnis, ProfileStore.LegacyFileName);
        var neuUnten = Path.Combine(unten, ProfileStore.DefaultFileName);
        ProfileStore.Save(NeuesProfil("oben"), altOben);
        ProfileStore.Save(NeuesProfil("unten"), neuUnten);

        Assert.Equal(neuUnten, ProfileStore.Discover(unten));
    }

    [Fact]
    public void Eine_obfuskation_json_mit_Erweiterungsinhalt_wird_nicht_als_Profil_genommen()
    {
        // Am selben Ort koennte "obfuskation.json" auch die Erweiterungsdatei
        // sein (siehe ExtensionLibrary.ResolvePath) -- ohne profileName oder
        // fields zaehlt sie nicht als Profil, und die Suche geht eine Ebene
        // hoeher weiter, statt an ihr haengenzubleiben.
        var erweiterungAlsAltname = Path.Combine(_verzeichnis, ProfileStore.LegacyFileName);
        File.WriteAllText(erweiterungAlsAltname, """{ "version": 1, "generators": {} }""");

        Assert.Null(ProfileStore.Discover(_verzeichnis));
    }

    [Fact]
    public void Eine_uebergangene_Erweiterungsdatei_laesst_die_Suche_weiter_aufwaerts_gehen()
    {
        var unten = Path.Combine(_verzeichnis, "projekt");
        Directory.CreateDirectory(unten);

        var erweiterungUnten = Path.Combine(unten, ProfileStore.LegacyFileName);
        File.WriteAllText(erweiterungUnten, """{ "version": 1, "generators": {} }""");

        var profilOben = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        ProfileStore.Save(NeuesProfil("oben"), profilOben);

        Assert.Equal(profilOben, ProfileStore.Discover(unten));
    }

    [Fact]
    public void Ohne_jede_Datei_ergibt_Discover_null()
    {
        Assert.Null(ProfileStore.Discover(_verzeichnis));
    }

    [Fact]
    public void LooksLikeProfile_erkennt_profileName()
    {
        var pfad = Path.Combine(_verzeichnis, "x.json");
        File.WriteAllText(pfad, """{ "profileName": "x" }""");

        Assert.True(ProfileStore.LooksLikeProfile(pfad));
    }

    [Fact]
    public void LooksLikeProfile_erkennt_fields()
    {
        var pfad = Path.Combine(_verzeichnis, "x.json");
        File.WriteAllText(pfad, """{ "fields": [] }""");

        Assert.True(ProfileStore.LooksLikeProfile(pfad));
    }

    [Fact]
    public void LooksLikeProfile_verneint_weder_profileName_noch_fields()
    {
        var pfad = Path.Combine(_verzeichnis, "x.json");
        File.WriteAllText(pfad, """{ "generators": {}, "textRules": [], "fieldRules": [] }""");

        Assert.False(ProfileStore.LooksLikeProfile(pfad));
    }

    [Fact]
    public void LooksLikeProfile_laesst_kaputtes_JSON_durch()
    {
        // Kaputtes JSON laesst sich hier nicht beurteilen -- ProfileStore.Load
        // wirft gleich noch einmal und liefert dann die brauchbare Meldung.
        var pfad = Path.Combine(_verzeichnis, "x.json");
        File.WriteAllText(pfad, "{ kein gueltiges json");

        Assert.True(ProfileStore.LooksLikeProfile(pfad));
    }
}
