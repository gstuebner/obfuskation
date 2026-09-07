using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Die Profiluebersicht, geprueft ohne Fenster -- wie <see cref="MainViewModelTests"/>
/// arbeitet sie nur mit Pfaden im Wegwerfverzeichnis, nie mit dem zentralen
/// Profilordner des Anwenders (der bliebe sonst leer, waere aber trotzdem nicht
/// die Quelle: die Testumgebung leitet <c>XDG_CONFIG_HOME</c> um).
/// </summary>
public class ProfilesViewModelTests : IDisposable
{
    private readonly string _verzeichnis;
    private readonly GuiSettings _settings;

    public ProfilesViewModelTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-gui-tests-profiles",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);

        _settings = new GuiSettings();
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
            // Ein liegengebliebenes Wegwerfverzeichnis stoert den Testlauf nicht.
        }
    }

    /// <summary>
    /// Keiner der hier geprueften Wege (Sortieren, Filtern, Auswaehlen,
    /// Entfernen) fragt einen Dialog -- nur Umbenennen und "Aus Datei waehlen"
    /// taeten das.
    /// </summary>
    private ProfilesViewModel Erzeugen(string? aktuellerSitzungspfad = null, bool ungespeichert = false)
        => new(_settings,
            () => throw new InvalidOperationException("In diesem Test darf kein Dialog aufgehen."),
            aktuellerSitzungspfad, ungespeichert);

    private string SchreibeProfil(string dateiname, string profilName, string? description = null)
    {
        var profil = new Profile
        {
            ProfileName = profilName,
            Description = description,
            MappingStore = Path.Combine(_verzeichnis, dateiname + ".mapping.json"),
        };

        var pfad = Path.Combine(_verzeichnis, dateiname);
        ProfileStore.Save(profil, pfad);
        return pfad;
    }

    private static void SetzeLetzteNutzung(string profilPfad, DateTimeOffset zeitpunkt)
    {
        var index = ProfileIndex.Load();
        index.RecordProfileUse(profilPfad);

        var eintrag = index.Profiles.Single(p =>
            string.Equals(p.Path, Path.GetFullPath(profilPfad), StringComparison.Ordinal));
        eintrag.LastUsedUtc = zeitpunkt;

        index.Save();
    }

    [Fact]
    public void Sortierung_wechselt_zwischen_allen_drei_Schluesseln_und_Richtung()
    {
        var anna = SchreibeProfil("a.json", "anna");
        var bruno = SchreibeProfil("b.json", "bruno");
        var carla = SchreibeProfil("c.json", "carla");

        File.SetLastWriteTimeUtc(anna, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(bruno, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(carla, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        SetzeLetzteNutzung(anna, DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
        SetzeLetzteNutzung(bruno, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        SetzeLetzteNutzung(carla, DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        _settings.RecentProfiles.AddRange(new[] { anna, bruno, carla });

        var modell = Erzeugen();

        // Vorgabe: zuletzt benutzt, absteigend -- anna war zuletzt dran.
        Assert.Equal(new[] { "anna", "carla", "bruno" }, modell.Rows.Select(r => r.Name));

        // Ein neuer Schluessel beginnt wieder absteigend, wie die Vorgabe.
        modell.SortByNameCommand.Execute(null);
        Assert.Equal(new[] { "carla", "bruno", "anna" }, modell.Rows.Select(r => r.Name));

        modell.SortByNameCommand.Execute(null);   // erneuter Klick: Richtung wechselt
        Assert.Equal(new[] { "anna", "bruno", "carla" }, modell.Rows.Select(r => r.Name));

        modell.SortByModifiedCommand.Execute(null);   // neuer Schluessel: wieder absteigend
        Assert.Equal(new[] { "carla", "bruno", "anna" }, modell.Rows.Select(r => r.Name));

        modell.SortByLastUsedCommand.Execute(null);
        Assert.Equal(new[] { "anna", "carla", "bruno" }, modell.Rows.Select(r => r.Name));
    }

    [Fact]
    public void Filter_greift_auf_Name_Beschreibung_und_Dateipfad()
    {
        var kunden = SchreibeProfil("kunden.json", "kunden", "Stammdaten Q3");
        var lieferanten = SchreibeProfil("lieferanten.json", "lieferanten", "Einkauf");

        var index = ProfileIndex.Load();
        index.RecordDataFile(kunden, Path.Combine(_verzeichnis, "sonderablage.csv"));
        index.Save();

        _settings.RecentProfiles.AddRange(new[] { kunden, lieferanten });

        var modell = Erzeugen();
        Assert.Equal(2, modell.Rows.Count);

        modell.FilterText = "Stammdaten";              // Treffer ueber die Beschreibung
        Assert.Equal(new[] { "kunden" }, modell.Rows.Select(r => r.Name));

        modell.FilterText = "lieferanten";              // Treffer ueber den Namen
        Assert.Equal(new[] { "lieferanten" }, modell.Rows.Select(r => r.Name));

        modell.FilterText = "sonderablage";              // Treffer ueber einen Dateipfad
        Assert.Equal(new[] { "kunden" }, modell.Rows.Select(r => r.Name));

        modell.FilterText = "";
        Assert.Equal(2, modell.Rows.Count);
    }

    [Fact]
    public void Ein_unlesbares_Profil_laesst_sich_nicht_oeffnen()
    {
        var kaputt = Path.Combine(_verzeichnis, "kaputt.json");
        File.WriteAllText(kaputt, "{ das ist kein gueltiges JSON");

        _settings.RecentProfiles.Add(kaputt);

        var modell = Erzeugen();
        var zeile = Assert.Single(modell.Rows);

        Assert.True(zeile.HasError);
        Assert.False(zeile.CanOpen);

        modell.Selected = zeile;
        Assert.False(modell.OpenCommand.CanExecute(null));
        Assert.False(modell.RenameCommand.CanExecute(null));

        // "Aus Liste entfernen" bleibt erreichbar -- ein unlesbarer Eintrag
        // muss trotzdem aus der Uebersicht verschwinden koennen.
        Assert.True(modell.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Entfernen_loescht_nur_den_Eintrag_nicht_die_Datei()
    {
        var kunden = SchreibeProfil("kunden.json", "kunden");
        _settings.RecentProfiles.Add(kunden);

        var modell = Erzeugen();
        modell.Selected = Assert.Single(modell.Rows);

        modell.RemoveCommand.Execute(null);

        Assert.True(File.Exists(kunden));
        Assert.Empty(modell.Rows);
        Assert.DoesNotContain(_settings.RecentProfiles, p =>
            string.Equals(Path.GetFullPath(p), Path.GetFullPath(kunden), StringComparison.Ordinal));

        var index = ProfileIndex.Load();
        Assert.DoesNotContain(index.Profiles, p =>
            string.Equals(p.Path, Path.GetFullPath(kunden), StringComparison.Ordinal));
    }
}
