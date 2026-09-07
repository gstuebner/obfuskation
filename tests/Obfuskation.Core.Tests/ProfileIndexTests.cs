using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

public class ProfileIndexTests
{
    [Fact]
    public void Eingetragene_Profilnutzung_ist_wiederzufinden()
    {
        var index = new ProfileIndex();
        var pfad = Path.Combine(Path.GetTempPath(), "profil-a.json");

        index.RecordProfileUse(pfad);

        var eintrag = Assert.Single(index.Profiles);
        Assert.Equal(Path.GetFullPath(pfad), eintrag.Path);
        Assert.NotNull(eintrag.LastUsedUtc);
    }

    [Fact]
    public void Eingetragene_Datendatei_ist_wiederzufinden()
    {
        var index = new ProfileIndex();
        var profilPfad = Path.Combine(Path.GetTempPath(), "profil-a.json");
        var datenPfad = Path.Combine(Path.GetTempPath(), "kunden.csv");

        index.RecordDataFile(profilPfad, datenPfad);

        var eintrag = Assert.Single(index.Profiles);
        var datei = Assert.Single(eintrag.Files);
        Assert.Equal(Path.GetFullPath(datenPfad), datei.Path);
    }

    [Fact]
    public void Mehr_als_zwanzig_Dateien_je_Profil_lassen_nur_die_juengsten_stehen()
    {
        var index = new ProfileIndex();
        var profilPfad = Path.Combine(Path.GetTempPath(), "profil-a.json");

        for (var i = 0; i < 25; i++)
        {
            index.RecordDataFile(profilPfad, Path.Combine(Path.GetTempPath(), $"datei-{i}.csv"));
            // Aufloesung von DateTimeOffset.UtcNow ist fein genug, aber ganz
            // sicher ist sicher: minimal auseinanderziehen.
            Thread.Sleep(1);
        }

        var eintrag = Assert.Single(index.Profiles);
        Assert.Equal(20, eintrag.Files.Count);
        Assert.Contains(eintrag.Files, f => f.Path.EndsWith("datei-24.csv"));
        Assert.DoesNotContain(eintrag.Files, f => f.Path.EndsWith("datei-0.csv"));
    }

    [Fact]
    public void Mehr_als_fuenfzig_Profile_lassen_nur_die_juengsten_stehen()
    {
        var index = new ProfileIndex();

        for (var i = 0; i < 55; i++)
        {
            index.RecordProfileUse(Path.Combine(Path.GetTempPath(), $"profil-{i}.json"));
            Thread.Sleep(1);
        }

        Assert.Equal(50, index.Profiles.Count);
        Assert.Contains(index.Profiles, p => p.Path.EndsWith("profil-54.json"));
        Assert.DoesNotContain(index.Profiles, p => p.Path.EndsWith("profil-0.json"));
    }

    [Fact]
    public void Prune_entfernt_Eintraege_zu_verschwundenen_Profilen()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-index-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verzeichnis);
        try
        {
            var vorhanden = Path.Combine(verzeichnis, "vorhanden.json");
            File.WriteAllText(vorhanden, "{}");
            var verschwunden = Path.Combine(verzeichnis, "verschwunden.json");

            var index = new ProfileIndex();
            index.RecordProfileUse(vorhanden);
            index.RecordProfileUse(verschwunden);

            var entfernt = index.Prune();

            Assert.Equal(1, entfernt);
            Assert.Single(index.Profiles);
            Assert.Equal(Path.GetFullPath(vorhanden), index.Profiles[0].Path);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Eine_kaputte_Indexdatei_ergibt_einen_leeren_Index()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileIndex.FilePath)!);
        File.WriteAllText(ProfileIndex.FilePath, "{ kein gueltiges json");

        try
        {
            var index = ProfileIndex.Load();

            Assert.Empty(index.Profiles);
        }
        finally
        {
            // Aufraeumen, damit ein nachfolgender Test in derselben Umgebung
            // (gleiche TestUmgebung, gleicher Prozess) nicht auf diese Datei stoesst.
            File.Delete(ProfileIndex.FilePath);
        }
    }

    [Fact]
    public void MoveProfile_zieht_den_Pfad_nach()
    {
        var index = new ProfileIndex();
        var alterPfad = Path.Combine(Path.GetTempPath(), "alt.json");
        var neuerPfad = Path.Combine(Path.GetTempPath(), "neu.json");

        index.RecordProfileUse(alterPfad);
        index.MoveProfile(alterPfad, neuerPfad);

        var eintrag = Assert.Single(index.Profiles);
        Assert.Equal(Path.GetFullPath(neuerPfad), eintrag.Path);
    }

    [Fact]
    public void Speichern_und_Laden_ist_verlustfrei()
    {
        var index = new ProfileIndex();
        var profilPfad = Path.Combine(Path.GetTempPath(), "profil-a.json");
        index.RecordDataFile(profilPfad, Path.Combine(Path.GetTempPath(), "kunden.csv"));

        try
        {
            index.Save();

            var geladen = ProfileIndex.Load();

            var eintrag = Assert.Single(geladen.Profiles);
            Assert.Equal(Path.GetFullPath(profilPfad), eintrag.Path);
            Assert.Single(eintrag.Files);
        }
        finally
        {
            if (File.Exists(ProfileIndex.FilePath))
                File.Delete(ProfileIndex.FilePath);
        }
    }
}
