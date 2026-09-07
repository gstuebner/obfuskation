using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

public class ProfileCatalogTests
{
    private static string NeuesVerzeichnis()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verzeichnis);
        return verzeichnis;
    }

    [Fact]
    public void Ordner_und_Zusatzpfade_werden_entdoppelt()
    {
        var verzeichnis = NeuesVerzeichnis();
        try
        {
            var pfad = Path.Combine(verzeichnis, "kunden.json");
            ProfileStore.Save(new Profile { ProfileName = "kunden" }, pfad);

            // Zusatzpfad zeigt auf dieselbe Datei, nur mit "./"-Umweg.
            var zusatzpfad = Path.Combine(verzeichnis, ".", "kunden.json");

            var ergebnis = ProfileCatalogAusOrdner(verzeichnis, new ProfileIndex(), new[] { zusatzpfad });

            Assert.Single(ergebnis);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Kaputtes_JSON_ergibt_einen_Fehlereintrag_und_wirft_nicht()
    {
        var verzeichnis = NeuesVerzeichnis();
        try
        {
            var pfad = Path.Combine(verzeichnis, "kaputt.json");
            File.WriteAllText(pfad, "{ \"profileName\": \"x\", kein gueltiges json");

            var ergebnis = ProfileCatalogAusOrdner(verzeichnis, new ProfileIndex(), Array.Empty<string>());

            var eintrag = Assert.Single(ergebnis);
            Assert.NotNull(eintrag.Error);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Fremdes_JSON_ohne_profileName_oder_fields_wird_uebergangen()
    {
        var verzeichnis = NeuesVerzeichnis();
        try
        {
            var pfad = Path.Combine(verzeichnis, "irgendwas.json");
            File.WriteAllText(pfad, """{ "irgendeinFeld": 42 }""");

            var ergebnis = ProfileCatalogAusOrdner(verzeichnis, new ProfileIndex(), Array.Empty<string>());

            Assert.Empty(ergebnis);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Verschwundene_Datei_aus_der_Zuletzt_Liste_ergibt_einen_Fehlereintrag_und_wirft_nicht()
    {
        var verzeichnis = NeuesVerzeichnis();
        try
        {
            var verschwunden = Path.Combine(verzeichnis, "verschwunden.json");

            var ergebnis = ProfileCatalog.Collect(new ProfileIndex(), new[] { verschwunden });

            var eintrag = Assert.Single(ergebnis);
            Assert.NotNull(eintrag.Error);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Fehlender_Profilordner_ergibt_leer_und_legt_nichts_an()
    {
        // TestUmgebung.KonfigVerzeichnis existiert, aber .../obfuskation/profile
        // wurde nie angelegt -- genau der Zustand nach einer frischen Installation.
        Assert.False(Directory.Exists(PathHelper.ProfileDirectory));

        var ergebnis = ProfileCatalog.Collect(new ProfileIndex(), Array.Empty<string>());

        Assert.Empty(ergebnis);
        Assert.False(Directory.Exists(PathHelper.ProfileDirectory));
    }

    /// <summary>
    /// Ruft <see cref="ProfileCatalog.Collect"/> so auf, dass zusaetzlich der
    /// Testordner als "additionalPaths" durchsucht wird -- der eigentliche
    /// zentrale Ordner (<see cref="PathHelper.ProfileDirectory"/>) bleibt in
    /// diesen Tests bewusst leer, um ihn von den Testverzeichnissen fernzuhalten.
    /// </summary>
    private static IReadOnlyList<ProfileSummary> ProfileCatalogAusOrdner(
        string verzeichnis, ProfileIndex index, IEnumerable<string> zusatzpfade)
    {
        var dateien = Directory.EnumerateFiles(verzeichnis, "*.json");
        return ProfileCatalog.Collect(index, dateien.Concat(zusatzpfade));
    }
}
