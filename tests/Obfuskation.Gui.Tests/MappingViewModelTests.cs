using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Die Ersetzungstabelle, geprueft ohne Fenster: Werte bleiben verborgen, bis
/// sie ausdruecklich eingeblendet werden, die Suche filtert ueber Klartext
/// und Pseudonym, und Loeschen laeuft ueber die Rueckfrage von
/// <see cref="IDialogService.AskRemoveMappingEntriesAsync"/>.
/// </summary>
public sealed class MappingViewModelTests : IDisposable
{
    private readonly string _verzeichnis;

    public MappingViewModelTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-mapping-viewmodel-tests",
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
            // Ein liegengebliebenes Wegwerfverzeichnis stoert den Testlauf nicht.
        }
    }

    /// <summary>Ein Profil mit einer Ersetzungstabelle, die bereits drei Eintraege in zwei Namensraeumen traegt.</summary>
    private (Profile Profile, ObfuscationEngine Engine) SitzungMitEintraegen()
    {
        var profil = new Profile
        {
            ProfileName = "test",
            MappingStore = Path.Combine(_verzeichnis, "mapping.json"),
        };

        using (var store = MappingStore.Open(profil.MappingStore, profil.ProfileName))
        {
            store.Add("email", "max@beispiel.de", "TOK_1");
            store.Add("email", "erika@beispiel.de", "TOK_2");
            store.Add("iban", "DE02120300000000202051", "TOK_3");
            store.Save();
        }

        return (profil, new ObfuscationEngine(profil));
    }

    /// <summary>
    /// Fuehrt einen <see cref="AsyncRelayCommand"/> aus und wartet dessen Ende
    /// ab -- wie <c>MainViewModelTests.AusfuehrenUndWartenAsync</c>, hier
    /// eigens, weil <see cref="AsyncRelayCommand"/> kein <c>ExecuteAsync</c> kennt.
    /// </summary>
    private static async Task AusfuehrenUndWartenAsync(AsyncRelayCommand befehl)
    {
        var fertig = new TaskCompletionSource();

        void Beobachten(object? sender, EventArgs args)
        {
            if (!befehl.IsRunning)
                fertig.TrySetResult();
        }

        befehl.CanExecuteChanged += Beobachten;
        try
        {
            befehl.Execute(null);
            await fertig.Task;
        }
        finally
        {
            befehl.CanExecuteChanged -= Beobachten;
        }
    }

    private MappingViewModel Erzeugen(
        ObfuscationEngine engine, string profileName, FakeDialogService? dialoge = null, Action? onChanged = null)
        => new(engine, profileName, () => dialoge ?? new FakeDialogService(), onChanged ?? (() => { }));

    // ----------------------------------------------------------- Verbergen

    [Fact]
    public void Werte_sind_beim_Start_verborgen()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var modell = Erzeugen(engine, profil.ProfileName);

        Assert.False(modell.ValuesVisible);
        Assert.Empty(modell.Entries);

        // Anzahlen je Namensraum sind aber von Anfang an zu sehen.
        Assert.Equal(2, modell.Namespaces.Count);
        Assert.Contains(modell.Namespaces, n => n.Name == "email" && n.Count == 2);
        Assert.Contains(modell.Namespaces, n => n.Name == "iban" && n.Count == 1);
    }

    [Fact]
    public void Werte_anzeigen_fuellt_die_Liste()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var modell = Erzeugen(engine, profil.ProfileName);

        modell.ToggleValuesCommand.Execute(null);

        Assert.True(modell.ValuesVisible);
        Assert.Equal(3, modell.Entries.Count);
        Assert.Contains(modell.Entries, e => e.Plaintext == "max@beispiel.de" && e.Pseudonym == "TOK_1");
    }

    [Fact]
    public void Werte_ausblenden_leert_die_Liste_wieder()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var modell = Erzeugen(engine, profil.ProfileName);

        modell.ToggleValuesCommand.Execute(null);
        modell.ToggleValuesCommand.Execute(null);

        Assert.False(modell.ValuesVisible);
        Assert.Empty(modell.Entries);
    }

    // --------------------------------------------------------------- Suche

    [Fact]
    public void Die_Suche_filtert_ueber_Klartext_und_Pseudonym_ohne_Gross_Kleinschreibung()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var modell = Erzeugen(engine, profil.ProfileName);
        modell.ValuesVisible = true;

        modell.SearchText = "MAX";
        Assert.Single(modell.Entries);
        Assert.Equal("max@beispiel.de", modell.Entries[0].Plaintext);

        modell.SearchText = "tok_3";
        Assert.Single(modell.Entries);
        Assert.Equal("DE02120300000000202051", modell.Entries[0].Plaintext);

        modell.SearchText = "";
        Assert.Equal(3, modell.Entries.Count);
    }

    [Fact]
    public void Die_Namensraum_Auswahl_schraenkt_zusaetzlich_ein()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var modell = Erzeugen(engine, profil.ProfileName);
        modell.ValuesVisible = true;

        var emailFilter = modell.NamespaceFilterOptions.Single(o => o.Name == "email");
        modell.SelectedNamespaceFilter = emailFilter;

        Assert.Equal(2, modell.Entries.Count);
        Assert.All(modell.Entries, e => Assert.Equal("email", e.Namespace));
    }

    // ------------------------------------------------------------ Loeschen

    [Fact]
    public async Task Loeschen_mit_Bestaetigung_entfernt_die_ausgewaehlten_Eintraege()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var geaendert = false;
        var dialoge = new FakeDialogService { RemoveMappingEntriesConfirmed = true };
        var modell = Erzeugen(engine, profil.ProfileName, dialoge, () => geaendert = true);
        modell.ValuesVisible = true;

        var zuLoeschen = modell.Entries.Single(e => e.Plaintext == "max@beispiel.de");
        modell.UpdateSelection(new[] { zuLoeschen });

        Assert.Equal(1, modell.SelectedCount);
        await AusfuehrenUndWartenAsync(modell.RemoveSelectedCommand);

        Assert.True(geaendert);
        Assert.Equal((1, (string?)null), dialoge.LastRemoveMappingEntriesRequest);

        // Neu geladen: der Eintrag ist weg, die Tabelle bestaetigt es auch
        // nach dem Neuoeffnen.
        Assert.DoesNotContain(modell.Entries, e => e.Plaintext == "max@beispiel.de");

        using var wiederGeoeffnet = MappingStore.Open(profil.MappingStore!, profil.ProfileName, readOnly: true);
        Assert.False(wiederGeoeffnet.IsPlaintextKnown("email", "max@beispiel.de"));
        Assert.Equal(2, wiederGeoeffnet.TotalEntries);
    }

    [Fact]
    public async Task Loeschen_mit_Abbruch_aendert_nichts()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var geaendert = false;
        var dialoge = new FakeDialogService { RemoveMappingEntriesConfirmed = false };
        var modell = Erzeugen(engine, profil.ProfileName, dialoge, () => geaendert = true);
        modell.ValuesVisible = true;

        var zuLoeschen = modell.Entries.Single(e => e.Plaintext == "max@beispiel.de");
        modell.UpdateSelection(new[] { zuLoeschen });

        await AusfuehrenUndWartenAsync(modell.RemoveSelectedCommand);

        Assert.False(geaendert);
        Assert.Contains(modell.Entries, e => e.Plaintext == "max@beispiel.de");

        using var wiederGeoeffnet = MappingStore.Open(profil.MappingStore!, profil.ProfileName, readOnly: true);
        Assert.True(wiederGeoeffnet.IsPlaintextKnown("email", "max@beispiel.de"));
        Assert.Equal(3, wiederGeoeffnet.TotalEntries);
    }

    [Fact]
    public async Task Leeren_entfernt_den_ganzen_Namensraum()
    {
        var (profil, engine) = SitzungMitEintraegen();
        var dialoge = new FakeDialogService { RemoveMappingEntriesConfirmed = true };
        var modell = Erzeugen(engine, profil.ProfileName, dialoge);

        var namensraum = modell.Namespaces.Single(n => n.Name == "email");
        await AusfuehrenUndWartenAsync(namensraum.ClearCommand);

        Assert.Equal((2, "email"), dialoge.LastRemoveMappingEntriesRequest);
        Assert.DoesNotContain(modell.Namespaces, n => n.Name == "email");
        Assert.Contains(modell.Namespaces, n => n.Name == "iban");

        using var wiederGeoeffnet = MappingStore.Open(profil.MappingStore!, profil.ProfileName, readOnly: true);
        Assert.Equal(1, wiederGeoeffnet.TotalEntries);
    }
}
