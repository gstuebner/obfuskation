using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Das Einstellungsfenster, geprueft ohne Fenster: <see cref="SettingsViewModel"/>
/// arbeitet auf Kopien von Profil und Erweiterung (siehe Klassenkopf dort) und
/// schreibt erst bei "Übernehmen" zurueck. Jeder Test uebergibt
/// <see cref="ExtensionWriteState"/> selbst statt <c>ExtensionLibrary.GetWriteState()</c>
/// zu rufen -- so bleibt jeder Test unabhaengig vom mit der ganzen Baugruppe
/// geteilten Konfigurationsordner (siehe TestUmgebung).
/// </summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _verzeichnis;

    public SettingsViewModelTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-settings-tests",
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

    private string ExtensionPfad => Path.Combine(_verzeichnis, ExtensionLibrary.FileName);

    private ExtensionWriteState SchreibbarerZustand() => new(ExtensionPfad, CanWrite: true, Reason: null);

    private ProfileSession Sitzung(Action<Profile>? anpassen = null)
    {
        var profil = new Profile
        {
            ProfileName = "test",
            MappingStore = Path.Combine(_verzeichnis, "mapping.json"),
        };
        anpassen?.Invoke(profil);

        var pfad = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        ProfileStore.Save(profil, pfad);
        return ProfileSession.Load(pfad, ExtensionLibrary.Empty);
    }

    // -------------------------------------------------------- Uebernehmen

    [Fact]
    public void Uebernehmen_schreibt_Profil_und_globale_Erweiterung()
    {
        var session = Sitzung();
        var extensions = ExtensionLibrary.Empty;
        var schreibzustand = SchreibbarerZustand();

        var modell = new SettingsViewModel(session, extensions, schreibzustand, extensionLoadError: null);

        modell.ProjectRules!.AddCommand.Execute(null);
        modell.ProjectRules.Selected!.Pattern = @"\bPROJEKT\d+\b";

        modell.GlobalRules.AddCommand.Execute(null);
        modell.GlobalRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

        modell.ApplyCommand.Execute(null);

        Assert.Empty(modell.ValidationErrors);
        Assert.True(modell.Applied);
        Assert.True(modell.ProjectChanged);
        Assert.True(modell.GlobalChanged);

        // Das Profil ist tatsaechlich in der Sitzung angekommen, nicht nur in
        // der Kopie.
        Assert.Single(session.Profile.TextRules);
        Assert.Equal(@"\bPROJEKT\d+\b", session.Profile.TextRules[0].Pattern);
        Assert.True(session.HasUnsavedChanges);

        // Die geteilte ExtensionLibrary-Instanz wurde per ReplaceWith
        // aktualisiert (nicht durch eine neue ersetzt).
        Assert.Single(extensions.TextRules);
        Assert.Equal(@"\bGLOBAL\d+\b", extensions.TextRules[0].Pattern);

        // Und tatsaechlich auf die Platte geschrieben.
        var wiederGelesen = ExtensionLibrary.Load(ExtensionPfad);
        Assert.Single(wiederGelesen.TextRules);
    }

    [Fact]
    public void Uebernehmen_ohne_Aenderung_schreibt_nichts()
    {
        var session = Sitzung();
        var extensions = ExtensionLibrary.Empty;
        var modell = new SettingsViewModel(session, extensions, SchreibbarerZustand(), extensionLoadError: null);

        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Applied);
        Assert.False(modell.ProjectChanged);
        Assert.False(modell.GlobalChanged);
        Assert.False(session.HasUnsavedChanges);
        Assert.False(File.Exists(ExtensionPfad));
    }

    [Fact]
    public void Ein_ungueltiges_Muster_blockiert_das_Uebernehmen()
    {
        var session = Sitzung();
        var modell = new SettingsViewModel(session, ExtensionLibrary.Empty, SchreibbarerZustand(), extensionLoadError: null);

        // Eine frisch angelegte Regel hat ein leeres Muster -- das ist ein
        // Fehler (ProfileValidator), kein bloss unfertiger Zwischenstand.
        modell.ProjectRules!.AddCommand.Execute(null);

        modell.ApplyCommand.Execute(null);

        Assert.False(modell.Applied);
        Assert.NotEmpty(modell.ValidationErrors);
        Assert.False(session.HasUnsavedChanges);
    }

    // ----------------------------------------------------------- Abbrechen

    [Fact]
    public void Ein_schon_vorher_bestehender_Profilfehler_blockiert_das_Uebernehmen_nicht()
    {
        // Ein halb eingerichtetes Feld der Dateiansicht: Generator, den es
        // nicht gibt. Das Einstellungsfenster hat ihn nicht verursacht und
        // soll eine globale Aenderung deswegen nicht verweigern.
        var session = Sitzung(profil => profil.Fields.Add(new FieldRule
        {
            Match = "kunde",
            Action = FieldAction.Pseudonymize,
            Generator = "gibtsnicht",
        }));
        var extensions = ExtensionLibrary.Empty;

        var modell = new SettingsViewModel(session, extensions, SchreibbarerZustand(), extensionLoadError: null);

        modell.GlobalRules.AddCommand.Execute(null);
        modell.GlobalRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

        modell.ApplyCommand.Execute(null);

        Assert.Empty(modell.ValidationErrors);
        Assert.True(modell.Applied);
        Assert.Single(extensions.TextRules);
    }

    [Fact]
    public void Abbrechen_aendert_weder_Profil_noch_Erweiterung()
    {
        var session = Sitzung();
        var extensions = ExtensionLibrary.Empty;
        var modell = new SettingsViewModel(session, extensions, SchreibbarerZustand(), extensionLoadError: null);

        modell.ProjectRules!.AddCommand.Execute(null);
        modell.ProjectRules.Selected!.Pattern = @"\bPROJEKT\d+\b";
        modell.GlobalRules.AddCommand.Execute(null);
        modell.GlobalRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

        var geschlossen = false;
        modell.CloseRequested += () => geschlossen = true;

        modell.CancelCommand.Execute(null);

        Assert.True(geschlossen);
        Assert.False(modell.Applied);
        Assert.Empty(session.Profile.TextRules);
        Assert.Empty(extensions.TextRules);
        Assert.False(session.HasUnsavedChanges);
        Assert.False(File.Exists(ExtensionPfad));
    }

    // ------------------------------------------------------ Schreibschutz

    [Fact]
    public void Eine_schreibgeschuetzte_Erweiterungsdatei_sperrt_den_globalen_Reiter()
    {
        var zustand = new ExtensionWriteState(
            ExtensionPfad, CanWrite: false, Reason: "Die geltende Datei ist schreibgeschützt.");

        var modell = new SettingsViewModel(null, ExtensionLibrary.Empty, zustand, extensionLoadError: null);

        Assert.False(modell.CanEditGlobal);
        Assert.True(modell.HasGlobalWarning);
        Assert.Equal("Die geltende Datei ist schreibgeschützt.", modell.GlobalStateText);
        Assert.True(modell.GlobalRules.IsReadOnly);
        Assert.False(modell.GlobalRules.AddCommand.CanExecute(null));
    }

    [Fact]
    public void Eine_kaputte_Erweiterungsdatei_sperrt_den_globalen_Reiter_mit_der_Ladefehlermeldung()
    {
        // Fehler 1 des Plans: auch wenn die Datei rein technisch beschreibbar
        // waere, darf ein kaputtes JSON nicht kommentarlos ueberschrieben
        // werden.
        var zustand = SchreibbarerZustand();
        var ladeFehler = "Erweiterungsdatei ist kein gültiges JSON: " + ExtensionPfad;

        var modell = new SettingsViewModel(null, ExtensionLibrary.Empty, zustand, ladeFehler);

        Assert.False(modell.CanEditGlobal);
        Assert.Equal(ladeFehler, modell.GlobalStateText);
        Assert.True(modell.GlobalRules.IsReadOnly);
    }

    [Fact]
    public void Ohne_Profil_ist_der_Projektreiter_nicht_vorhanden()
    {
        var modell = new SettingsViewModel(null, ExtensionLibrary.Empty, SchreibbarerZustand(), extensionLoadError: null);

        Assert.False(modell.HasProfile);
        Assert.Null(modell.ProjectRules);
        Assert.Equal(SettingsTab.Global, modell.SelectedTab);
    }

    // ----------------------------------------------------------- Verschieben

    [Fact]
    public void Verschieben_nimmt_eine_Regel_samt_eigenem_Generator_in_die_Erweiterung_mit()
    {
        var session = Sitzung(p =>
        {
            p.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" };
            p.TextRules.Add(new TextRule
            {
                Name = "beleg", Pattern = @"\bBEL-\d{6}\b", Generator = "belegNummer",
            });
        });
        var extensions = ExtensionLibrary.Empty;

        var modell = new SettingsViewModel(session, extensions, SchreibbarerZustand(), extensionLoadError: null);

        var regel = modell.ProjectRules!.Rules.Single(r => r.Name == "beleg");
        modell.ProjectRules.Selected = regel;

        Assert.True(regel.HasMoveLabel);
        Assert.True(regel.MoveCommand.CanExecute(null));

        regel.MoveCommand.Execute(null);

        // Nach dem Verschieben zeigt der globale Reiter die Regel bearbeitbar an.
        Assert.Equal(SettingsTab.Global, modell.SelectedTab);
        Assert.DoesNotContain(modell.ProjectRules.Rules, r => r.Name == "beleg" && !r.IsOtherArea);
        var verschoben = modell.GlobalRules.Rules.Single(r => r.Name == "beleg" && !r.IsOtherArea);
        Assert.Equal(@"\bBEL-\d{6}\b", verschoben.Pattern);
        Assert.Equal("belegNummer", verschoben.Generator!.Name);

        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Applied);
        Assert.Empty(session.Profile.TextRules);
        Assert.Single(extensions.TextRules);
        Assert.True(extensions.Generators.ContainsKey("belegNummer"));

        // "Mitkopiert, nicht verschoben" (Plan Teil B): der Generator bleibt
        // auch im Profil bestehen, falls dort noch etwas anderes ihn braucht.
        Assert.True(session.Profile.Generators.ContainsKey("belegNummer"));
    }

    [Fact]
    public void Verschieben_macht_einen_doppelten_Namen_im_Ziel_eindeutig()
    {
        var session = Sitzung(p =>
        {
            p.TextRules.Add(new TextRule { Name = "gleich", Pattern = @"\bA\d+\b" });
        });
        var extensions = new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "gleich", Pattern = @"\bB\d+\b" } },
        };

        var modell = new SettingsViewModel(session, extensions, SchreibbarerZustand(), extensionLoadError: null);

        var regel = modell.ProjectRules!.Rules.Single(r => r.Name == "gleich" && !r.IsOtherArea);
        regel.MoveCommand.Execute(null);

        Assert.Contains(modell.GlobalRules.Rules, r => r.Name == "gleich2" && !r.IsOtherArea);
    }
}
