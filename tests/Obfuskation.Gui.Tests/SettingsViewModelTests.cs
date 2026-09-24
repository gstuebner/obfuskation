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
///
/// Seit Plan Teil B gibt es keine getrennten Reiter "Projekt"/"Global" mehr:
/// <see cref="SettingsViewModel.TextRules"/> ist eine einzige Liste, jede
/// Regel traegt ihren <see cref="RuleScope"/> selbst.
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

    private static ExtensionResolution LeereAufloesung(string pfad)
        => new(null, null, [new ExtensionCandidate(pfad, ExtensionOrigin.ConfigDirectory, Exists: false, SkippedAsProfile: false)]);

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

    private SettingsViewModel Modell(
        ProfileSession? session, ExtensionLibrary extensions, ExtensionWriteState? zustand = null,
        string? ladeFehler = null, SettingsTab tab = SettingsTab.TextRules, string? regelName = null,
        RuleScope? bereich = null)
        => new(session, extensions, zustand ?? SchreibbarerZustand(), ladeFehler,
            LeereAufloesung(ExtensionPfad), tab, regelName, bereich);

    // -------------------------------------------------------- Uebernehmen

    [Fact]
    public void Uebernehmen_schreibt_Profil_und_globale_Erweiterung()
    {
        var session = Sitzung();
        var extensions = ExtensionLibrary.Empty;

        var modell = Modell(session, extensions);

        modell.TextRules.AddCommand.Execute(null);
        modell.TextRules.Selected!.Pattern = @"\bPROJEKT\d+\b";

        modell.TextRules.AddCommand.Execute(null);
        modell.TextRules.Selected!.IsGlobalScope = true;
        modell.TextRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

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
        var modell = Modell(session, extensions);

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
        var modell = Modell(session, ExtensionLibrary.Empty);

        // Eine frisch angelegte Regel hat ein leeres Muster -- das ist ein
        // Fehler (ProfileValidator), kein bloss unfertiger Zwischenstand.
        modell.TextRules.AddCommand.Execute(null);

        modell.ApplyCommand.Execute(null);

        Assert.False(modell.Applied);
        Assert.NotEmpty(modell.ValidationErrors);
        Assert.False(session.HasUnsavedChanges);
    }

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

        var modell = Modell(session, extensions);

        modell.TextRules.AddCommand.Execute(null);
        modell.TextRules.Selected!.IsGlobalScope = true;
        modell.TextRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

        modell.ApplyCommand.Execute(null);

        Assert.Empty(modell.ValidationErrors);
        Assert.True(modell.Applied);
        Assert.Single(extensions.TextRules);
    }

    // ----------------------------------------------------------- Abbrechen

    [Fact]
    public void Abbrechen_aendert_weder_Profil_noch_Erweiterung()
    {
        var session = Sitzung();
        var extensions = ExtensionLibrary.Empty;
        var modell = Modell(session, extensions);

        modell.TextRules.AddCommand.Execute(null);
        modell.TextRules.Selected!.Pattern = @"\bPROJEKT\d+\b";
        modell.TextRules.AddCommand.Execute(null);
        modell.TextRules.Selected!.IsGlobalScope = true;
        modell.TextRules.Selected!.Pattern = @"\bGLOBAL\d+\b";

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
    public void Eine_schreibgeschuetzte_Erweiterungsdatei_sperrt_die_globalen_Regeln()
    {
        var zustand = new ExtensionWriteState(
            ExtensionPfad, CanWrite: false, Reason: "Die geltende Datei ist schreibgeschützt.");
        var extensions = new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "hauseigen", Pattern = @"\bINV\d{6}\b" } },
        };

        var modell = Modell(null, extensions, zustand);

        Assert.False(modell.CanEditGlobal);
        Assert.True(modell.HasGlobalWarning);
        Assert.True(modell.ShowLockBar);
        Assert.Equal("Die geltende Datei ist schreibgeschützt.", modell.GlobalStateText);
        Assert.False(modell.TextRules.CanAddRule);
        Assert.All(modell.TextRules.Rules, r => Assert.False(r.IsEditable));
    }

    [Fact]
    public void Eine_kaputte_Erweiterungsdatei_sperrt_die_globalen_Regeln_mit_der_Ladefehlermeldung()
    {
        // Fehler 1 des Plans: auch wenn die Datei rein technisch beschreibbar
        // waere, darf ein kaputtes JSON nicht kommentarlos ueberschrieben
        // werden.
        var zustand = SchreibbarerZustand();
        var ladeFehler = "Erweiterungsdatei ist kein gültiges JSON: " + ExtensionPfad;

        var modell = Modell(null, ExtensionLibrary.Empty, zustand, ladeFehler);

        Assert.False(modell.CanEditGlobal);
        Assert.Equal(ladeFehler, modell.GlobalStateText);
        Assert.False(modell.TextRules.CanAddRule);
    }

    [Fact]
    public void Ohne_Profil_ist_der_Filter_ausgeblendet_und_neue_Regeln_landen_global()
    {
        var modell = Modell(null, ExtensionLibrary.Empty);

        Assert.False(modell.HasProfile);
        Assert.False(modell.TextRules.ShowFilter);

        modell.TextRules.AddCommand.Execute(null);

        Assert.Equal(RuleScope.Global, modell.TextRules.Selected!.Scope);
    }

    // ----------------------------------------------------------- Bereich

    [Fact]
    public void Bereich_wechseln_nimmt_eine_Regel_samt_eigenem_Generator_in_die_Erweiterung_mit()
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

        var modell = Modell(session, extensions);

        var regel = modell.TextRules.Rules.Single(r => r.Name == "beleg");
        modell.TextRules.Selected = regel;

        Assert.True(regel.CanChangeScope);

        regel.IsGlobalScope = true;

        // Nach dem Wechsel steht die Regel bearbeitbar im globalen Bereich.
        var verschoben = modell.TextRules.Rules.Single(r => r.Name == "beleg");
        Assert.Equal(RuleScope.Global, verschoben.Scope);
        Assert.Equal(@"\bBEL-\d{6}\b", verschoben.Pattern);
        Assert.Equal("belegNummer", verschoben.Generator!.Name);

        // Die Seite "Eigene Generatoren" zeigt den mitkopierten Generator
        // sofort, nicht erst nach einem Neustart des Fensters.
        Assert.Contains(modell.GlobalGenerators, g => g.Name == "belegNummer");

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
    public void Bereich_wechseln_macht_einen_doppelten_Namen_im_Ziel_eindeutig()
    {
        var session = Sitzung(p =>
        {
            p.TextRules.Add(new TextRule { Name = "gleich", Pattern = @"\bA\d+\b" });
        });
        var extensions = new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "gleich", Pattern = @"\bB\d+\b" } },
        };

        var modell = Modell(session, extensions);

        var regel = modell.TextRules.Rules.Single(r => r.Name == "gleich" && r.Scope == RuleScope.Project);
        regel.IsGlobalScope = true;

        Assert.Contains(modell.TextRules.Rules, r => r.Name == "gleich2" && r.Scope == RuleScope.Global);
    }

    // ---------------------------------------------------- Erfassungsmodus

    [Fact]
    public void Beispielwert_mit_Ziffern_ergibt_eine_Form_mit_vorgeschlagenem_Namen()
    {
        var session = Sitzung();
        var modell = Modell(session, ExtensionLibrary.Empty);

        modell.TextRules.AddCommand.Execute(null);
        var regel = modell.TextRules.Selected!;

        // Ohne Ziffern im Beispiel steht "Genau dieser Wert" gewaehlt da --
        // die Form greift von selbst, sobald ein Ziffernlauf auftaucht.
        Assert.True(regel.IsExactMode);
        regel.Sample = "FW123456";

        Assert.True(regel.IsShapeMode);
        Assert.False(regel.IsExactMode);
        Assert.Equal(@"\bFW\d{6}\b", regel.Pattern);
        Assert.Equal("fw", regel.Name);
    }

    [Fact]
    public void Umschalten_auf_Exact_ergibt_das_Literal_Muster()
    {
        var session = Sitzung();
        var modell = Modell(session, ExtensionLibrary.Empty);

        modell.TextRules.AddCommand.Execute(null);
        var regel = modell.TextRules.Selected!;
        regel.Sample = "FW123456";

        regel.IsExactMode = true;

        Assert.Equal(@"\bFW123456\b", regel.Pattern);
    }

    [Fact]
    public void Eine_vorhandene_erzeugte_Regel_oeffnet_im_Shape_Modus_mit_Beschreibung()
    {
        var session = Sitzung(p => p.TextRules.Add(
            new TextRule { Name = "fw", Pattern = @"\bFW\d{6}\b" }));

        var modell = Modell(session, ExtensionLibrary.Empty);
        var regel = modell.TextRules.Rules.Single(r => r.Name == "fw");

        Assert.True(regel.IsShapeMode);
        Assert.Equal("„FW“ + 6 Ziffern", regel.ShapeDescription);
    }

    [Fact]
    public void Ein_freier_Regex_oeffnet_im_Custom_Modus()
    {
        var session = Sitzung(p => p.TextRules.Add(
            new TextRule { Name = "frei", Pattern = @"FW\d+" }));

        var modell = Modell(session, ExtensionLibrary.Empty);
        var regel = modell.TextRules.Rules.Single(r => r.Name == "frei");

        Assert.True(regel.IsCustomMode);
        Assert.Equal(@"FW\d+", regel.Pattern);
    }

    [Fact]
    public void Ein_Klick_auf_einen_Erfassungsmodus_loescht_einen_eigenen_Ausdruck_nicht()
    {
        var session = Sitzung(p => p.TextRules.Add(
            new TextRule { Name = "frei", Pattern = @"FW\d+" }));

        var modell = Modell(session, ExtensionLibrary.Empty);
        var regel = modell.TextRules.Rules.Single(r => r.Name == "frei");

        regel.IsExactMode = true;

        Assert.Equal(@"FW\d+", regel.Pattern);

        // Erst ein getippter Beispielwert ersetzt den Ausdruck.
        regel.Sample = "FW7";
        Assert.Equal(PatternFromSample.Literal("FW7").Pattern, regel.Pattern);
    }

    [Fact]
    public void Ein_manuell_geaenderter_Name_bleibt_beim_Tippen_im_Beispielfeld_erhalten()
    {
        var session = Sitzung();
        var modell = Modell(session, ExtensionLibrary.Empty);

        modell.TextRules.AddCommand.Execute(null);
        var regel = modell.TextRules.Selected!;

        regel.Name = "eigenerName";
        regel.Sample = "FW123456";

        Assert.Equal("eigenerName", regel.Name);
    }

    [Fact]
    public void Fuer_dieses_Projekt_anpassen_ergibt_eine_gleichnamige_Projektregel()
    {
        var session = Sitzung();
        var extensions = new ExtensionLibrary
        {
            TextRules = { new TextRule { Name = "hauseigen", Pattern = @"\bINV\d{6}\b" } },
        };

        var zustand = new ExtensionWriteState(ExtensionPfad, CanWrite: false, Reason: "gesperrt");
        var modell = Modell(session, extensions, zustand);

        var regel = modell.TextRules.Rules.Single(r => r.Name == "hauseigen");
        Assert.True(regel.IsLocked);
        Assert.True(regel.CanAdjustForProject);

        regel.AdjustForProjectCommand.Execute(null);

        var projektRegel = modell.TextRules.Rules.Single(r => r.Name == "hauseigen" && r.Scope == RuleScope.Project);
        Assert.Equal(@"\bINV\d{6}\b", projektRegel.Pattern);

        var globaleRegel = modell.TextRules.Rules.Single(r => r.Name == "hauseigen" && r.Scope == RuleScope.Global);
        Assert.True(globaleRegel.IsOverridden);
    }

    // -------------------------------------------------------- Spaltenmuster

    [Fact]
    public void Spaltenmuster_lassen_sich_entfernen_und_umsortieren()
    {
        var extensions = new ExtensionLibrary
        {
            FieldRules =
            {
                new FieldNameRule { Pattern = "eins", Generator = "token" },
                new FieldNameRule { Pattern = "zwei", Generator = "token" },
                new FieldNameRule { Pattern = "drei", Generator = "token" },
            },
        };

        var modell = Modell(null, extensions);

        Assert.Equal(3, modell.FieldRules.Count);

        // Entfernen: die mittlere Zeile faellt weg -- zunaechst nur in der
        // Kopie des Einstellungsfensters.
        modell.FieldRules[1].RemoveCommand.Execute(null);
        Assert.Equal(["eins", "drei"], modell.FieldRules.Select(f => f.Pattern));

        // Hochschieben: die zweite (jetzt "drei") wandert nach vorn.
        modell.FieldRules[1].MoveUpCommand.Execute(null);
        Assert.Equal(["drei", "eins"], modell.FieldRules.Select(f => f.Pattern));

        Assert.True(modell.HasUnsavedChanges);

        // Erst "Übernehmen" schreibt die Kopie in die geteilte Instanz zurueck.
        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Applied);
        Assert.Equal(["drei", "eins"], extensions.FieldRules.Select(f => f.Pattern));
    }

    // ---------------------------------------------------------- Bezeichnung

    private static FieldRule FreitextFeld(params string[] regeln)
        => new() { Match = "Verwendungszweck", Action = FieldAction.ScanText, TextRules = regeln.ToList() };

    [Fact]
    public void Umbenennen_zieht_die_Regelauswahl_der_Freitextfelder_nach()
    {
        var session = Sitzung(p =>
        {
            p.TextRules.Add(new TextRule { Name = "begriff", Pattern = @"\b\d{4}\b", Generator = "token" });
            p.TextRules.Add(new TextRule { Name = "email", Pattern = "@", Generator = "token" });
            p.Fields.Add(FreitextFeld("begriff", "email"));
        });

        var modell = Modell(session, ExtensionLibrary.Empty);
        var regel = modell.TextRules.Rules.Single(r => r.Name == "begriff");

        // Ueber einen Zwischenstand, der kurz wie die andere Regel heisst --
        // die Auswahl darf dabei nicht auf "email" umgebogen werden.
        regel.Name = "email";
        Assert.True(regel.HasNameHint);
        regel.Name = "Kreditkartennummer";
        Assert.False(regel.HasNameHint);

        modell.ApplyCommand.Execute(null);

        Assert.False(modell.HasValidationErrors, string.Join("; ", modell.ValidationErrors));
        Assert.True(modell.Applied);
        Assert.Equal(["Kreditkartennummer", "email"], session.Profile.Fields.Single().TextRules);
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public void Ein_Bereichswechsel_mit_Umbenennung_zieht_die_Regelauswahl_nach()
    {
        var erweiterung = new ExtensionLibrary();
        erweiterung.TextRules.Add(new TextRule { Name = "inventar", Pattern = "INV", Generator = "token" });

        var session = Sitzung(p =>
        {
            p.TextRules.Add(new TextRule { Name = "inventar", Pattern = @"\bINV\d{6}\b", Generator = "token" });
            p.Fields.Add(FreitextFeld("inventar"));
        });

        var modell = Modell(session, erweiterung);
        var projektRegel = modell.TextRules.Rules.Single(r => r.Name == "inventar" && r.Scope == RuleScope.Project);

        projektRegel.IsGlobalScope = true;   // wird dabei zu "inventar2"
        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Applied, string.Join("; ", modell.ValidationErrors));
        Assert.Equal(["inventar2"], session.Profile.Fields.Single().TextRules);
    }

    [Fact]
    public void Eine_gleichnamige_Regel_im_anderen_Bereich_wird_beim_Tippen_angesagt()
    {
        var erweiterung = new ExtensionLibrary();
        erweiterung.TextRules.Add(new TextRule { Name = "inventar", Pattern = "INV", Generator = "token" });
        var session = Sitzung(p => p.TextRules.Add(new TextRule { Name = "fw", Pattern = "FW", Generator = "token" }));

        var modell = Modell(session, erweiterung);
        var regel = modell.TextRules.Rules.Single(r => r.Name == "fw");

        regel.Name = "Inventar";

        Assert.True(regel.HasNameHint);
        Assert.Contains("In diesem Projekt gilt dann nur diese hier", regel.NameHint);
    }
}
