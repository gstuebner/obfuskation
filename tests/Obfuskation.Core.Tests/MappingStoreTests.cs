using System.Runtime.InteropServices;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Ersetzungstabelle vorab anlegen, damit eine Vorschau verbindlich ist.
///
/// Der Kernfall steht in
/// <see cref="Nach_dem_Anlegen_liefert_der_Probelauf_dasselbe_wie_der_echte_Lauf"/>:
/// die Textansicht der Oberflaeche zeigt fortlaufend das Ergebnis eines
/// Probelaufs, und der Anwender kopiert genau das. Weichen Probelauf und
/// echter Lauf voneinander ab, gibt das Programm etwas anderes heraus, als es
/// gezeigt hat -- der schlimmste denkbare Fehler in einem Werkzeug, dem man
/// Echtdaten anvertraut.
/// </summary>
public class MappingStoreTests
{
    [Fact]
    public void EnsureCreated_legt_die_Tabelle_an()
    {
        using var profil = new TestProfile();
        var pfad = profil.Profile.MappingStore!;

        Assert.False(File.Exists(pfad));

        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
            store.EnsureCreated();

        Assert.True(File.Exists(pfad));
    }

    [Fact]
    public void EnsureCreated_laesst_ein_bestehendes_Salt_unangetastet()
    {
        using var profil = new TestProfile();
        var pfad = profil.Profile.MappingStore!;

        byte[] erstesSalt;
        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.EnsureCreated();
            erstesSalt = store.Salt;
        }

        // Ein zweiter Aufruf darf das Salt nicht erneuern: alle bereits
        // vergebenen Pseudonyme haengen daran.
        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.EnsureCreated();
            Assert.Equal(erstesSalt, store.Salt);
        }
    }

    [Fact]
    public void Nach_dem_Anlegen_liefert_der_Probelauf_dasselbe_wie_der_echte_Lauf()
    {
        using var profil = new TestProfile();
        var engine = profil.CreateEngine();
        var text = TestProfile.Utf8("Herr Max Mustermann, max.mustermann@beispiel.de, DE02120300000000202051.");

        engine.EnsureMappingStore();

        var probe = engine.Obfuscate(text, "eingabe.txt", new RunOptions { DryRun = true });
        var echt = engine.Obfuscate(text, "eingabe.txt", new RunOptions());

        Assert.Equal(TestProfile.FromUtf8(probe.Content), TestProfile.FromUtf8(echt.Content));
    }

    [Fact]
    public void Ohne_Anlegen_ist_ein_Probelauf_nur_beispielhaft()
    {
        // Die Gegenprobe zum Fall darueber: ohne bestehende Tabelle entsteht in
        // jedem Probelauf ein fluechtiges Salt, und die gezeigten Werte sind
        // nicht die, die der echte Lauf spaeter vergibt. Genau deshalb ruft die
        // Textansicht EnsureMappingStore, bevor sie eine Vorschau zeigt.
        using var profil = new TestProfile();
        var engine = profil.CreateEngine();
        var text = TestProfile.Utf8("max.mustermann@beispiel.de");

        var ersteProbe = engine.Obfuscate(text, "eingabe.txt", new RunOptions { DryRun = true });
        var zweiteProbe = engine.Obfuscate(text, "eingabe.txt", new RunOptions { DryRun = true });

        Assert.NotEqual(
            TestProfile.FromUtf8(ersteProbe.Content),
            TestProfile.FromUtf8(zweiteProbe.Content));
    }

    [Fact]
    public void EnsureMappingStore_haelt_sich_an_die_Git_Sicherung()
    {
        // Dieselbe Sperre wie bei jedem anderen Zugriff: die Tabelle enthaelt
        // saemtliche Echtwerte und darf nicht in einem Arbeitsverzeichnis
        // landen, das eingecheckt wird.
        using var profil = new TestProfile();
        Directory.CreateDirectory(Path.Combine(profil.Directory, ".git"));

        var engine = profil.CreateEngine();

        Assert.Throws<MappingConflictException>(() => engine.EnsureMappingStore());
    }

    // --------------------------------------------------------- Loeschen

    [Fact]
    public void Remove_entfernt_den_Eintrag_und_der_Bestand_bleibt_nach_dem_Neuoeffnen_geloescht()
    {
        using var profil = new TestProfile();
        var pfad = profil.Profile.MappingStore!;

        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.Add("email", "max@beispiel.de", "TOK_1");
            Assert.True(store.Remove("email", "max@beispiel.de"));
            store.Save();
        }

        using var wiederGeoeffnet = MappingStore.Open(pfad, profil.Profile.ProfileName);
        Assert.False(wiederGeoeffnet.IsPlaintextKnown("email", "max@beispiel.de"));
        Assert.False(wiederGeoeffnet.TryGetPlaintext("email", "TOK_1", out _));
        Assert.Equal(0, wiederGeoeffnet.TotalEntries);
    }

    [Fact]
    public void Remove_liefert_falsch_fuer_einen_unbekannten_Eintrag()
    {
        using var profil = new TestProfile();
        using var store = MappingStore.Open(profil.Profile.MappingStore!, profil.Profile.ProfileName);
        store.Add("email", "max@beispiel.de", "TOK_1");

        Assert.False(store.Remove("email", "unbekannt@beispiel.de"));
        Assert.False(store.Remove("unbekannterNamensraum", "max@beispiel.de"));
    }

    [Fact]
    public void RemoveNamespace_entfernt_alle_Eintraege_eines_Namensraums_und_gibt_die_Anzahl_zurueck()
    {
        using var profil = new TestProfile();
        var pfad = profil.Profile.MappingStore!;

        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.Add("email", "max@beispiel.de", "TOK_1");
            store.Add("email", "erika@beispiel.de", "TOK_2");
            store.Add("iban", "DE02120300000000202051", "TOK_3");

            Assert.Equal(2, store.RemoveNamespace("email"));
            store.Save();
        }

        using var wiederGeoeffnet = MappingStore.Open(pfad, profil.Profile.ProfileName);
        Assert.DoesNotContain("email", wiederGeoeffnet.NamespaceNames);
        Assert.Contains("iban", wiederGeoeffnet.NamespaceNames);
        Assert.Equal(1, wiederGeoeffnet.TotalEntries);
    }

    [Fact]
    public void RemoveNamespace_ohne_Treffer_liefert_null_und_bleibt_ohne_Wirkung()
    {
        using var profil = new TestProfile();
        using var store = MappingStore.Open(profil.Profile.MappingStore!, profil.Profile.ProfileName);

        Assert.Equal(0, store.RemoveNamespace("unbekannterNamensraum"));
        Assert.False(store.HasUnsavedChanges);
    }

    [Fact]
    public void Nach_dem_Loeschen_bleiben_die_Dateirechte_0600()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;  // Dateirechte gibt es dort in dieser Form nicht.

        using var profil = new TestProfile();
        var pfad = profil.Profile.MappingStore!;

        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.Add("email", "max@beispiel.de", "TOK_1");
            store.Save();
        }

        using (var store = MappingStore.Open(pfad, profil.Profile.ProfileName))
        {
            store.Remove("email", "max@beispiel.de");
            store.Save();
        }

        var mode = File.GetUnixFileMode(pfad);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }
}
