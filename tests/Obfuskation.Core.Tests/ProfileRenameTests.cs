using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die wichtigsten Tests des Vorhabens: ein Umbenennen darf niemals dazu
/// fuehren, dass bereits erzeugte Pseudonyme ihren Bezug zur Ersetzungstabelle
/// verlieren.
/// </summary>
public class ProfileRenameTests
{
    private static Profile NeuesProfil(string name) => new()
    {
        ProfileName = name,
        Fields =
        {
            new FieldRule
            {
                Match = "Name",
                MatchType = FieldMatchType.Exact,
                Action = FieldAction.Pseudonymize,
                Generator = "personName",
            },
        },
    };

    [Fact]
    public void Nach_dem_Umbenennen_liefert_derselbe_Klartext_dasselbe_Pseudonym()
    {
        var profil = NeuesProfil("alt");
        var eingabe = TestProfile.Utf8("Name\nMax Mustermann\n");

        var engine = new ObfuscationEngine(profil);
        var erstesErgebnis = engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });

        ProfileRenamer.Rename(profil, "neu");

        // Mit dem umbenannten Profil (derselbe Store!) erneut denselben
        // Klartext verarbeiten -- muss dasselbe Pseudonym ergeben.
        var engineNeu = new ObfuscationEngine(profil);
        var zweitesErgebnis = engineNeu.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true });

        Assert.Equal(
            TestProfile.FromUtf8(erstesErgebnis.Content),
            TestProfile.FromUtf8(zweitesErgebnis.Content));

        var zurueckgeholt = engineNeu.Deobfuscate(erstesErgebnis.Content, "a.csv", new RunOptions());
        Assert.Equal(TestProfile.FromUtf8(eingabe), TestProfile.FromUtf8(zurueckgeholt.Content));
    }

    [Fact]
    public void Der_leere_MappingStore_wird_auf_den_alten_Pfad_festgeschrieben()
    {
        var profil = NeuesProfil("alt");
        Assert.Null(profil.MappingStore);

        var alterVorgabepfad = PathHelper.DefaultMappingStorePath("alt");
        var ergebnis = ProfileRenamer.Rename(profil, "neu");

        Assert.True(ergebnis.MappingStorePinned);
        Assert.Equal(alterVorgabepfad, PathHelper.ExpandHome(profil.MappingStore!));
        Assert.Equal("neu", profil.ProfileName);
    }

    [Fact]
    public void Der_Profilname_im_Mapping_Dokument_wird_nachgezogen()
    {
        var profil = NeuesProfil("alt");
        var engine = new ObfuscationEngine(profil);
        engine.Obfuscate(TestProfile.Utf8("Name\nMax Mustermann\n"), "a.csv", new RunOptions { Strict = true });

        var ergebnis = ProfileRenamer.Rename(profil, "neu");
        Assert.True(ergebnis.MappingDocumentUpdated);

        using var store = MappingStore.Open(profil.MappingStore!, "neu", readOnly: true);
        Assert.Empty(store.OpenWarnings);
        Assert.Equal("neu", store.ProfileName);
    }

    [Fact]
    public void Eine_gesperrte_Tabelle_laesst_das_Profil_unveraendert()
    {
        var profil = NeuesProfil("alt");
        var engine = new ObfuscationEngine(profil);
        engine.Obfuscate(TestProfile.Utf8("Name\nMax Mustermann\n"), "a.csv", new RunOptions { Strict = true });

        var tabelle = PathHelper.ResolveMappingStore(profil);
        var vorherigerStore = profil.MappingStore;

        // Die Sperre eines parallel laufenden Vorgangs nachstellen.
        using (new FileStream(tabelle + ".lock", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.Throws<MappingLockedException>(() => ProfileRenamer.Rename(profil, "neu"));
        }

        // Ein halb umbenanntes Profil waere die eigentliche Gefahr: gespeichert
        // wuerde es auf eine Tabelle zeigen, die noch den alten Namen traegt.
        Assert.Equal("alt", profil.ProfileName);
        Assert.Equal(vorherigerStore, profil.MappingStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("...")]
    public void Ein_leerer_oder_bedeutungsloser_Name_wird_abgewiesen(string ungueltigerName)
    {
        var profil = NeuesProfil("alt");

        Assert.Throws<ConfigurationException>(() => ProfileRenamer.Rename(profil, ungueltigerName));

        // Nichts wurde angefasst.
        Assert.Equal("alt", profil.ProfileName);
    }
}
