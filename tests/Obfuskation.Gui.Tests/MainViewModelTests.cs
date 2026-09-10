using System.Text;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Die Logik des Hauptfensters, geprueft ohne Fenster.
///
/// Das geht, weil das Ansichtsmodell keine Fenster kennt: Dateidialoge kommen
/// als Fabrik herein und werden hier nie aufgerufen, Nebenfenster werden nur
/// als Ereignis erbeten. Genau deshalb ist die Bedienlogik ueberhaupt pruefbar.
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _verzeichnis;
    private readonly GuiSettings _settings;

    public MainViewModelTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-gui-tests",
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
    /// Die Dialogfabrik wird in diesen Tests nie ausgewertet — alle geprueften
    /// Wege kommen ohne Dateiauswahl aus.
    /// </summary>
    private MainViewModel Erzeugen() => new(_settings,
        () => throw new InvalidOperationException("In diesem Test darf kein Dialog aufgehen."));

    /// <summary>Fuer Tests, die eine Rueckfrage erwarten -- das Doppel liefert vorgegebene Antworten.</summary>
    private MainViewModel Erzeugen(FakeDialogService dialoge) => new(_settings, () => dialoge);

    private string SchreibeCsv(string name = "kunden.csv")
    {
        var pfad = Path.Combine(_verzeichnis, name);
        File.WriteAllText(pfad,
            "Kundennummer;Kundenname;IBAN;Betrag\n"
            + "4711;Max Mustermann;DE02120300000000202051;1234,56\n"
            + "4712;Erika Musterfrau;DE02500105170137075030;-89,90\n",
            new UTF8Encoding(false));
        return pfad;
    }

    private string SchreibeProfil(Action<Profile>? anpassen = null)
    {
        var profil = new Profile
        {
            ProfileName = "test",
            MappingStore = Path.Combine(_verzeichnis, "mapping.json"),
        };

        anpassen?.Invoke(profil);

        var pfad = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        ProfileStore.Save(profil, pfad);
        return pfad;
    }

    /// <summary>Ein zweites Profil unter einem eigenen Dateinamen, fuer Profilwechsel-Tests.</summary>
    private string SchreibeZweitesProfil(string dateiname, string profilName)
    {
        var profil = new Profile
        {
            ProfileName = profilName,
            MappingStore = Path.Combine(_verzeichnis, dateiname + ".mapping.json"),
        };

        var pfad = Path.Combine(_verzeichnis, dateiname);
        ProfileStore.Save(profil, pfad);
        return pfad;
    }

    private static ProfileSummary NeueZusammenfassung(string pfad, string name) => new(
        pfad, name, Description: null, FieldCount: 0, TextRuleCount: 0,
        MappingStorePath: "", MappingStoreExists: false, ModifiedUtc: DateTimeOffset.UtcNow,
        LastUsedUtc: null, DataFiles: Array.Empty<DataFileUsage>(), Error: null);

    /// <summary>
    /// Fuehrt einen <see cref="AsyncRelayCommand"/> aus und wartet dessen Ende
    /// ab. <c>Execute</c> ist <c>async void</c> -- ohne diesen Umweg liefe der
    /// Test weiter, bevor das Laden der Datei (echtes Datei-IO) fertig ist.
    /// Statt eines Sleep-Polls wird das Ende ueber <c>CanExecuteChanged</c>
    /// abgewartet: <see cref="AsyncRelayCommand"/> loest es beim Start und beim
    /// Ende aus, <c>IsRunning</c> unterscheidet die beiden Faelle.
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

    [Fact]
    public async Task Beim_Start_wird_das_angegebene_Profil_geladen()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();

        await modell.InitializeAsync(profil, null);

        Assert.True(modell.HasProfile);
        Assert.Equal("test", modell.ProfileName);
    }

    [Fact]
    public async Task Beim_Start_wird_die_uebergebene_Datei_geoeffnet()
    {
        var profil = SchreibeProfil();
        var csv = SchreibeCsv();

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, csv);

        Assert.True(modell.HasDataFile);
        Assert.Equal("kunden.csv", modell.DataFileName);
        Assert.Contains("CSV", modell.DataFileDetails);
        Assert.Contains("';'", modell.DataFileDetails);
    }

    [Fact]
    public async Task Eine_Freitextdatei_zeigt_den_Freitexthinweis_statt_keine_Datei_geoeffnet()
    {
        // Teil D-1: Fields.Count == 0 allein unterscheidet nicht zwischen
        // "keine Datei offen" und "Datei offen, aber Fliesstext" -- beide
        // Faelle muessen unterschiedlichen Text und einen Weg in die
        // Textansicht zeigen.
        var profil = SchreibeProfil();
        var txt = Path.Combine(_verzeichnis, "antwort.txt");
        File.WriteAllText(txt, "Kontakt: max.mustermann@beispiel.de");

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, txt);

        Assert.True(modell.HasDataFile);
        Assert.True(modell.ShowEmptyHint);
        Assert.True(modell.IsFreeTextFile);
        Assert.Contains("Fließtext", modell.EmptyHint);
        Assert.DoesNotContain("Keine Datei geöffnet", modell.EmptyHint);
    }

    [Fact]
    public async Task In_der_Textansicht_oeffnen_uebernimmt_den_Inhalt_der_Freitextdatei()
    {
        var profil = SchreibeProfil();
        var txt = Path.Combine(_verzeichnis, "antwort.txt");
        File.WriteAllText(txt, "Kontakt: max.mustermann@beispiel.de");

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, txt);

        Assert.True(modell.OpenInTextViewCommand.CanExecute(null));
        modell.OpenInTextViewCommand.Execute(null);

        Assert.Equal(AppView.Text, modell.CurrentView);
        Assert.NotNull(modell.Text);
        Assert.Equal("Kontakt: max.mustermann@beispiel.de", modell.Text!.InputText);
    }

    [Fact]
    public async Task Immer_ersetzen_aus_der_Dateiansicht_legt_die_Regel_im_Profil_an()
    {
        // Teil C, zweiter Einstieg: der Knopf "Immer ersetzen…" neben "Felder
        // automatisch erkennen…" nutzt den Beispielwert des gewaehlten Feldes
        // als Vorbelegung.
        var profil = SchreibeProfil();
        var csv = SchreibeCsv();
        var dialoge = new FakeDialogService { AlwaysReplaceConfirmed = true };

        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv);

        modell.SelectedField = modell.Fields.Single(f => f.FieldName == "Kundennummer");

        await AusfuehrenUndWartenAsync(modell.ShowAlwaysReplaceCommand);

        Assert.NotNull(dialoge.LastAlwaysReplaceViewModel);
        Assert.Equal("4711", dialoge.LastAlwaysReplaceViewModel!.Sample);
        Assert.True(dialoge.LastAlwaysReplaceViewModel.Confirmed);
        Assert.Single(modell.Session!.Profile.TextRules);
        Assert.True(modell.Session.HasUnsavedChanges);
    }

    [Fact]
    public async Task Eine_Regel_fuer_alle_Projekte_wirkt_sofort()
    {
        // Der Hauptfall des Dialogs: ein hauseigenes Muster, das nicht im
        // Projekt, sondern in der Erweiterungsdatei landet. Die Engine fuehrt
        // Profil- und Erweiterungsregeln in ihrem Konstruktor zusammen -- wird
        // die zwischengespeicherte nicht verworfen, zeigt die Fundstellenliste
        // den Treffer zwar an (sie liest die Erweiterung unmittelbar), der
        // Ergebnistext liesse den Wert aber im Klartext stehen.
        var profil = SchreibeProfil();
        var dialoge = new FakeDialogService { AlwaysReplaceConfirmed = true, AlwaysReplaceUseExtension = true };

        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, null);

        modell.ShowTextCommand.Execute(null);
        Assert.NotNull(modell.Text);

        modell.Text!.InputText = "Zugriff über FW123456 gemeldet.";
        modell.Text.RefreshPreview();
        Assert.Contains("FW123456", modell.Text.ResultText, StringComparison.Ordinal);

        try
        {
            // Der Einstieg aus der Textansicht laeuft nebenlaeufig los; das
            // Doppel antwortet synchron, ein Durchlauf der Warteschlange
            // genuegt also.
            modell.Text.RequestAlwaysReplace("FW123456");
            await Task.Yield();

            Assert.True(dialoge.LastAlwaysReplaceViewModel!.Confirmed);

            // Die Regel steht in der Erweiterungsdatei, nicht im Profil.
            Assert.Empty(modell.Session!.Profile.TextRules);
            Assert.Single(modell.Session.Extensions.TextRules);

            modell.Text.RefreshPreview();
            Assert.DoesNotContain("FW123456", modell.Text.ResultText, StringComparison.Ordinal);
        }
        finally
        {
            // Die Erweiterungsdatei liegt im umgeleiteten Konfigurationsordner
            // (siehe TestUmgebung), gilt dort aber fuer die ganze Baugruppe --
            // sie darf keinem folgenden Test in die Quere kommen.
            var geschrieben = ExtensionLibrary.ResolveWritePath();
            if (File.Exists(geschrieben))
                File.Delete(geschrieben);
        }
    }

    [Fact]
    public async Task Ohne_Profil_bleibt_die_Feldliste_leer()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(null, SchreibeCsv());

        Assert.False(modell.HasProfile);
        Assert.Empty(modell.Fields);
        Assert.True(modell.ShowEmptyHint);
    }

    [Fact]
    public async Task Ohne_jedes_Profil_landet_der_Start_auf_der_Startseite()
    {
        // Der wirklich leere Fall (kein uebergebenes, kein gefundenes, kein
        // zuletzt benutztes Profil) ist der einzige, der auf der Startseite
        // landet -- siehe Teil A des Plans.
        var modell = Erzeugen();
        await modell.InitializeAsync(null, null);

        Assert.Equal(AppView.Start, modell.CurrentView);
        Assert.True(modell.IsStartView);
        Assert.False(modell.IsFilesView);
        Assert.False(modell.IsTextView);
    }

    [Fact]
    public async Task Mit_uebergebenem_Profil_geht_es_direkt_in_die_Dateiansicht()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, null);

        Assert.Equal(AppView.Files, modell.CurrentView);
        Assert.True(modell.IsFilesView);
        Assert.False(modell.IsStartView);
    }

    [Fact]
    public async Task Ueber_Profile_geoeffnet_wechselt_die_Ansicht_zu_Dateien()
    {
        // Auch von der Startseite aus (kein Profil geladen) erreichbar: die
        // Kopfzeile bietet "Profile…" in jeder Ansicht an.
        var profil = SchreibeProfil();
        var dialoge = new FakeDialogService
        {
            ProfilesResult = NeueZusammenfassung(profil, "test"),
        };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(null, null);
        Assert.Equal(AppView.Start, modell.CurrentView);

        await modell.ShowProfilesAsync();

        Assert.Equal(AppView.Files, modell.CurrentView);
        Assert.True(modell.HasProfile);
    }

    [Fact]
    public async Task Die_Karte_Dateien_pseudonymisieren_legt_ohne_Profil_ein_neues_an()
    {
        var csv = SchreibeCsv();
        var dialoge = new FakeDialogService
        {
            DataFilesToOpen = new[] { csv },
            NewProfileResult = new NewProfileResult(
                "test", null, Path.Combine(_verzeichnis, ProfileStore.DefaultFileName), OpenExisting: false),
        };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(null, null);
        Assert.Equal(AppView.Start, modell.CurrentView);

        await AusfuehrenUndWartenAsync(modell.Start.FilesCommand);

        Assert.Equal(AppView.Files, modell.CurrentView);
        Assert.True(modell.HasProfile);
        Assert.True(modell.HasDataFile);
    }

    [Fact]
    public async Task Alle_Felder_stehen_zunaechst_offen()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();

        await modell.InitializeAsync(profil, SchreibeCsv());

        Assert.Equal(4, modell.Fields.Count);
        Assert.All(modell.Fields, feld => Assert.False(feld.IsDecided));
        Assert.Equal(4, modell.UndecidedCount);
        Assert.Equal("4 Felder offen", modell.UndecidedText);

        // Ausgewaehlt wird das erste offene Feld: dort soll der Anwender
        // weitermachen.
        Assert.NotNull(modell.SelectedField);
        Assert.False(modell.SelectedField!.IsDecided);
    }

    [Fact]
    public async Task Eine_Entscheidung_legt_eine_Regel_an_und_zaehlt_herunter()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Kundenname");
        feld.Action = FieldAction.Pseudonymize;

        Assert.True(feld.IsDecided);
        Assert.Equal("●", feld.StatusSymbol);
        Assert.Equal(3, modell.UndecidedCount);

        // Die Regel muss im Profil angekommen sein, nicht nur im Ansichtsmodell.
        var regel = modell.Session!.Profile.Fields.Single(r => r.Match == "Kundenname");
        Assert.Equal(FieldAction.Pseudonymize, regel.Action);
        Assert.False(string.IsNullOrWhiteSpace(regel.Generator));

        Assert.True(modell.Session.HasUnsavedChanges);
        Assert.EndsWith("*", modell.ProfileTitle);
    }

    [Fact]
    public async Task Ohne_Erweiterungsdatei_bekommt_ein_Feld_keinen_Namensvorschlag_mehr()
    {
        // Vor der Umstellung auf FieldNameSuggester leitete ein fest
        // einkompiliertes Fragment ("iban" in "IBAN") den Generator aus dem
        // Feldnamen ab. Der Testlauf zeigt ueber XDG_CONFIG_HOME auf ein
        // eigenes, leeres Verzeichnis (siehe TestUmgebung) -- ohne
        // Erweiterungsdatei dort gibt es dieses Raten nicht mehr:
        // SuggestGenerator faellt wie jedes andere namentlich nicht
        // getroffene Feld auf das eingebaute "token" zurueck.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        modell.Fields.Single(f => f.FieldName == "IBAN").Action = FieldAction.Pseudonymize;

        Assert.Equal("token", modell.Fields.Single(f => f.FieldName == "IBAN").Generator);
    }

    [Fact]
    public async Task Ohne_Pseudonymisierung_braucht_es_keinen_Generator()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        feld.Action = FieldAction.Passthrough;

        Assert.False(feld.NeedsGenerator);
        Assert.Equal("unverändert", feld.Summary);

        var regel = modell.Session!.Profile.Fields.Single(r => r.Match == "Betrag");
        Assert.Null(regel.Generator);
    }

    [Fact]
    public async Task Ein_bestehendes_Regelwerk_wird_uebernommen()
    {
        var profil = SchreibeProfil(p =>
        {
            p.Fields.Add(new FieldRule
            {
                Match = "Kundenname",
                Action = FieldAction.Pseudonymize,
                Generator = "personName",
            });
            p.Fields.Add(new FieldRule { Match = "Betrag", Action = FieldAction.Passthrough });
        });

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        Assert.Equal(2, modell.UndecidedCount);   // Kundennummer und IBAN
        Assert.Equal("personName", modell.Fields.Single(f => f.FieldName == "Kundenname").Generator);
        Assert.False(modell.Session!.HasUnsavedChanges);
    }

    [Fact]
    public async Task Vor_dem_ersten_Lauf_ist_die_Vorschau_beispielhaft()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Kundenname");
        feld.Action = FieldAction.Pseudonymize;
        modell.SelectedField = feld;

        Assert.Equal("Max Mustermann", feld.SampleValue);
        Assert.True(feld.HasPreview);
        Assert.NotEqual(feld.SampleValue, feld.Preview);

        // Es gibt noch keine Tabelle — der Anwender muss erfahren, dass der
        // echte Lauf andere Werte liefern kann.
        Assert.True(feld.PreviewIsExample);
    }

    [Fact]
    public async Task Nach_dem_Oeffnen_traegt_ein_Feld_seine_Beispielwerte()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        // Zwei Datenzeilen in der Beispieldatei -- beide muessen ankommen,
        // nicht bloss die erste.
        var feld = modell.Fields.Single(f => f.FieldName == "Kundenname");
        Assert.True(feld.HasSampleValues);
        Assert.Equal(new[] { "Max Mustermann", "Erika Musterfrau" }, feld.SampleValues);
    }

    [Fact]
    public async Task Ein_offenes_Feld_zeigt_seinen_Inhalt_auch_ohne_Vorschau()
    {
        // Genau der Fall, den der Befund verlangt: ein Feld, das noch auf
        // "error" steht, hat keine Vorschau -- aber gerade dort ist die
        // Entscheidung offen, und der Inhalt muss trotzdem sichtbar sein.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Kundenname");

        Assert.Equal(FieldAction.Error, feld.Action);
        Assert.True(feld.HasSampleValues);
        Assert.False(feld.HasPreview);
    }

    [Fact]
    public async Task Eine_fehlerhafte_Regel_erscheint_als_Hinweis()
    {
        var profil = SchreibeProfil(p => p.Fields.Add(new FieldRule
        {
            Match = "Kundenname",
            Action = FieldAction.Pseudonymize,
            Generator = "gibtsNicht",
        }));

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        Assert.True(modell.HasIssues);
        Assert.Contains(modell.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task Eine_fehlerhafte_Konfiguration_zeigt_die_Hinweise_auch_ohne_gewaehltes_Feld()
    {
        // D-3 aus A6: der bestehende Hinweisbereich haengt an HasSelectedField,
        // aber ohne Engine bleibt die Feldliste leer -- es gibt also nie ein
        // gewaehltes Feld. HasBlockingIssues traegt den eigenen Bereich
        // oberhalb der Feldliste, der davon unabhaengig ist.
        var profil = SchreibeProfil(p => p.Fields.Add(new FieldRule
        {
            Match = "Kundenname",
            Action = FieldAction.Pseudonymize,
            Generator = "gibtsNicht",
        }));

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        Assert.True(modell.HasBlockingIssues);
        Assert.NotEmpty(modell.Issues);
    }

    [Fact]
    public async Task Ein_gueltiges_Profil_zeigt_keine_blockierenden_Hinweise()
    {
        var profil = SchreibeProfil();

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        Assert.False(modell.HasBlockingIssues);
    }

    [Fact]
    public async Task Der_Umschalter_geht_durch_alle_drei_Zustaende()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(null, null);

        // Ohne laufende Avalonia-Anwendung greift ThemeService.Apply ins Leere;
        // geprueft wird hier die Abfolge der Zustaende.
        Assert.Equal(AppTheme.System, _settings.Theme);

        modell.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.Dark, _settings.Theme);

        modell.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.Light, _settings.Theme);

        modell.ToggleThemeCommand.Execute(null);
        Assert.Equal(AppTheme.System, _settings.Theme);
    }

    [Fact]
    public async Task Die_Textregeln_lassen_sich_ohne_Fenster_bearbeiten()
    {
        var profil = SchreibeProfil(p => p.TextRules.AddRange(ProfileScaffolder.DefaultTextRules()));

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, null);

        var regeln = modell.CreateTextRulesViewModel();
        Assert.NotNull(regeln);
        Assert.Equal(4, regeln!.Rules.Count);

        // Der voreingestellte Erprobungstext enthaelt IBAN, E-Mail und Telefon.
        Assert.Contains(regeln.Matches, m => m.Rule == "iban");
        Assert.Contains(regeln.Matches, m => m.Rule == "email");
        Assert.Contains(regeln.Matches, m => m.Rule == "phone");

        // Und die Rechnungsnummer darf nicht als Telefonnummer durchgehen.
        Assert.DoesNotContain(regeln.Matches, m => m.Value.Contains("2024-0815"));
    }

    [Fact]
    public async Task Eine_neue_Textregel_landet_im_Profil()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, null);

        var regeln = modell.CreateTextRulesViewModel()!;
        regeln.AddCommand.Execute(null);

        Assert.Single(regeln.Rules);
        Assert.Single(modell.Session!.Profile.TextRules);
        Assert.True(modell.Session.HasUnsavedChanges);

        regeln.RemoveCommand.Execute(null);
        Assert.Empty(modell.Session.Profile.TextRules);
    }

    // ------------------------------------------------- Mustererkennung

    private string SchreibeCsvMitMustern()
    {
        var pfad = Path.Combine(_verzeichnis, "muster.csv");
        File.WriteAllText(pfad,
            "Kundennummer;E-Mail;Telefon\n"
            + "4711;max@beispiel.de;+49 30 12345678\n"
            + "4712;erika@beispiel.de;+49 40 87654321\n",
            new UTF8Encoding(false));
        return pfad;
    }

    [Fact]
    public async Task Muster_erkennen_belegt_nur_offene_Felder_vor_und_markiert_das_Profil_als_veraendert()
    {
        var profil = SchreibeProfil();
        var csv = SchreibeCsvMitMustern();

        var dialoge = new FakeDialogService();
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv);

        // "Telefon" wird vorab entschieden -- sein Vorschlag darf danach nicht
        // mehr vorbelegt sein, auch wenn der Wert weiter auf das Muster passt.
        modell.Fields.Single(f => f.FieldName == "Telefon").Action = FieldAction.Passthrough;

        await AusfuehrenUndWartenAsync(modell.ShowPatternSuggestionsCommand);

        var dialog = dialoge.LastPatternSuggestionsViewModel;
        Assert.NotNull(dialog);
        Assert.True(dialog!.HasSuggestions);

        var emailVorschlag = dialog.Items.Single(i => i.FieldName == "E-Mail");
        Assert.Equal("email", emailVorschlag.Generator);
        Assert.True(emailVorschlag.IsChecked, "ein noch offenes Feld muss vorbelegt sein");

        var telefonVorschlag = dialog.Items.Single(i => i.FieldName == "Telefon");
        Assert.False(telefonVorschlag.IsChecked, "ein bereits entschiedenes Feld darf nicht vorbelegt sein");

        // Jetzt tatsaechlich uebernehmen -- nur fuer "E-Mail".
        dialoge.PatternSuggestionsResult = new[] { new PatternSuggestionAcceptance("E-Mail", "email") };
        await AusfuehrenUndWartenAsync(modell.ShowPatternSuggestionsCommand);

        var emailFeld = modell.Fields.Single(f => f.FieldName == "E-Mail");
        Assert.Equal(FieldAction.Pseudonymize, emailFeld.Action);
        Assert.Equal("email", emailFeld.Generator);

        Assert.True(modell.Session!.HasUnsavedChanges);
        Assert.EndsWith("*", modell.ProfileTitle);
    }

    [Fact]
    public async Task Ohne_offene_Datei_liefert_Muster_erkennen_keinen_Dialog()
    {
        var profil = SchreibeProfil();

        var dialoge = new FakeDialogService();
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, null);

        Assert.False(modell.ShowPatternSuggestionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Die_Tabellenauskunft_nennt_Pfad_und_Rechte()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, null);

        var auskunft = modell.CreateMappingViewModel();

        Assert.NotNull(auskunft);
        Assert.Equal("test", auskunft!.ProfileName);
        Assert.EndsWith("mapping.json", auskunft.StorePath);

        // Noch kein Lauf: die Tabelle gibt es nicht.
        Assert.True(auskunft.IsEmpty);
    }

    [Fact]
    public async Task Eigene_Namensraeume_erscheinen_in_der_Generatorauswahl()
    {
        // Ein Eintrag unter "generators" schafft einen zweiten Namensraum —
        // der Weg, um Personennummer und Belegnummer zu trennen. Er muss in
        // der Auswahlliste auftauchen, sonst ist er von der Oberflaeche aus
        // nicht erreichbar.
        var profil = SchreibeProfil(p =>
            p.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" });

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var eigener = modell.Generators.SingleOrDefault(g => g.Name == "belegNummer");

        Assert.NotNull(eigener);
        Assert.Contains("numericId", eigener!.Description);
    }

    [Fact]
    public async Task Jeder_Generator_der_Auswahl_traegt_eine_Erklaerung()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        Assert.All(modell.Generators,
            option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));

        Assert.Equal("Straßenname mit Hausnummer",
            modell.Generators.Single(g => g.Name == "street").Description);
    }

    [Fact]
    public async Task Ein_Feld_mit_eigenem_Namensraum_zeigt_seinen_Generator_an()
    {
        // Befund D-4 der Testdurchfuehrung vom 4. September 2026: fuer ein Feld,
        // dessen Regel einen im Profil angelegten Generator nutzt, blieb das
        // Auswahlfeld leer. Ursache war, dass die Suche nur die eingebauten
        // Generatoren kannte und ersatzweise einen Eintrag mit abweichender
        // Erklaerung baute — als Datensatz mit Wertvergleich also nicht
        // derselbe wie der in der Auswahlliste.
        //
        // Die Gefahr lag nicht im Lauf, der arbeitete richtig, sondern in der
        // Anzeige: wer das leere Feld fuer einen Fehler haelt und einen
        // Generator aus der Liste waehlt, hebt die Trennung der Namensraeume
        // auf, und Belegnummer und Personennummer bekommen wieder dasselbe
        // Pseudonym.
        var profil = SchreibeProfil(p =>
        {
            p.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" };
            p.Fields.Add(new FieldRule
            {
                Match = "Kundennummer",
                Action = FieldAction.Pseudonymize,
                Generator = "belegNummer",
            });
        });

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        modell.SelectedField = modell.Fields.Single(f => f.FieldName == "Kundennummer");
        var gewaehlt = modell.SelectedField!.SelectedGenerator;

        Assert.NotNull(gewaehlt);
        Assert.Equal("belegNummer", gewaehlt!.Name);

        // Entscheidend: der Eintrag muss derselbe sein wie der in der
        // Auswahlliste, sonst findet das Auswahlfeld ihn nicht.
        Assert.Contains(gewaehlt, modell.Generators);
    }

    [Fact]
    public async Task Ein_Praefix_am_eingebauten_token_legt_einen_eigenen_Namensraum_an()
    {
        // A5: "Betrag" hat keinen ableitbaren Generator, SuggestGenerator
        // faellt deshalb auf das eingebaute "token" zurueck. Ein Praefix
        // darauf zu setzen traefe jedes andere Feld mit, das ebenfalls
        // schlicht "token" verwendet -- deshalb muss stattdessen ein neuer,
        // eigener Namensraum entstehen.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = feld;
        feld.Action = FieldAction.Pseudonymize;
        Assert.Equal("token", feld.Generator);

        feld.Prefix = "Betrag~";

        Assert.NotNull(feld.Generator);
        Assert.NotEqual("token", feld.Generator);

        var einstellungen = modell.Session!.Profile.Generators[feld.Generator!];
        Assert.Equal("token", einstellungen.Type);
        Assert.Equal("Betrag~", einstellungen.Prefix);

        var feldRegel = modell.Session.Profile.Fields.Single(r => r.Match == "Betrag");
        Assert.Equal(feld.Generator, feldRegel.Generator);
    }

    [Fact]
    public async Task Eine_Option_am_eingebauten_Generator_legt_einen_eigenen_Namensraum_an()
    {
        // Gegenstueck zum Praefix-Test oben, diesmal fuer einen der neuen
        // Generatoren mit Optionen: "Betrag" steht nach dem Wechsel auf
        // "pseudonymize" zunaechst auf dem eingebauten "token" (siehe oben),
        // wird hier aber von Hand auf den eingebauten "pattern" umgestellt.
        // Das Setzen der Maske darauf darf nicht jedes andere Feld mittreffen,
        // das ebenfalls schlicht "pattern" verwendet -- also muss auch hier
        // ein neuer, eigener Namensraum entstehen (FieldRuleViewModel.EnsureNamespace).
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = feld;
        feld.Action = FieldAction.Pseudonymize;
        feld.Generator = "pattern";
        Assert.Equal("pattern", feld.Generator);
        Assert.True(feld.HasOptions);
        Assert.True(feld.ShowPatternMask);

        feld.Pattern = "999-999";

        Assert.NotNull(feld.Generator);
        Assert.NotEqual("pattern", feld.Generator);

        var einstellungen = modell.Session!.Profile.Generators[feld.Generator!];
        Assert.Equal("pattern", einstellungen.Type);
        Assert.Equal("999-999", einstellungen.Pattern);

        var feldRegel = modell.Session.Profile.Fields.Single(r => r.Match == "Betrag");
        Assert.Equal(feld.Generator, feldRegel.Generator);

        // Wie beim Praefix: der neue Eintrag muss wertgleich in Generators
        // stehen, sonst faende ihn die ComboBox nicht.
        var gewaehlt = feld.SelectedGenerator;
        Assert.NotNull(gewaehlt);
        Assert.Contains(gewaehlt, modell.Generators);
    }

    [Fact]
    public async Task Ein_Setzer_ohne_Wertaenderung_legt_keinen_Namensraum_an()
    {
        // Avalonia schreibt bei NumericUpDown und ComboBox schon beim Oeffnen
        // des Dialogs den unveraenderten Wert zurueck. Entstuende dabei ein
        // Namensraum, haette der Anwender nach blossem Hinsehen ein
        // geaendertes Profil und beim Schliessen die Rueckfrage nach
        // ungespeicherten Aenderungen.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = feld;
        feld.Action = FieldAction.Pseudonymize;
        feld.Generator = "partialMask";

        var vorher = modell.Session!.Profile.Generators.Count;

        // Genau das, was die Bindings beim Oeffnen tun: den geltenden Wert
        // noch einmal setzen.
        feld.KeepFirst = feld.KeepFirst;
        feld.KeepLast = feld.KeepLast;
        feld.MaskChar = feld.MaskChar;

        // Dasselbe fuer die Auswahlliste des anderen Generators: auch ihr
        // Vorgabeeintrag darf beim blossen Anzeigen nicht ins Profil wandern.
        feld.Generator = "dateGeneralize";
        feld.SelectedGranularity = feld.SelectedGranularity;
        Assert.Equal("dateGeneralize", feld.Generator);

        feld.Generator = "partialMask";

        Assert.Equal("partialMask", feld.Generator);
        Assert.Equal(vorher, modell.Session.Profile.Generators.Count);

        // Eine echte Aenderung legt den Namensraum dann sehr wohl an.
        feld.KeepFirst = 3;
        Assert.NotEqual("partialMask", feld.Generator);
        Assert.Equal(3, modell.Session.Profile.Generators[feld.Generator!].KeepFirst);
    }

    [Fact]
    public async Task Die_Optionsschaltflaeche_erscheint_nur_bei_Generatoren_mit_Optionen()
    {
        // "IBAN" hat keine Optionen (nur Country, das nicht Teil dieser
        // Etappe ist), "Betrag" bekommt hier "pattern" mit einer Maske --
        // nur dort soll die Schaltflaeche erscheinen, und der Dialog soll
        // sich auf genau dieses Feld beziehen.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var iban = modell.Fields.Single(f => f.FieldName == "IBAN");
        modell.SelectedField = iban;
        iban.Action = FieldAction.Pseudonymize;
        iban.Generator = "iban";

        Assert.False(modell.ShowGeneratorOptionsButton);

        var betrag = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = betrag;
        betrag.Action = FieldAction.Pseudonymize;
        betrag.Generator = "pattern";

        Assert.True(modell.ShowGeneratorOptionsButton);

        var dialog = modell.CreateGeneratorOptionsViewModel();
        Assert.NotNull(dialog);
        Assert.Same(betrag, dialog!.Field);
    }

    [Fact]
    public async Task Der_ueber_das_Praefix_Feld_angelegte_Namensraum_bleibt_in_der_Auswahlliste()
    {
        // Schutz gegen einen Rueckfall in D-4, diesmal ausgeloest durch das
        // Praefix-Feld statt durch einen von Hand editierten Namensraum: der
        // frisch angelegte Eintrag muss wertgleich in Generators stehen,
        // sonst faende ihn die ComboBox nicht und setzte SelectedItem auf
        // null -- der Generator der Regel waere damit geloescht.
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = feld;
        feld.Action = FieldAction.Pseudonymize;
        feld.Prefix = "Betrag~";

        var gewaehlt = feld.SelectedGenerator;
        Assert.NotNull(gewaehlt);
        Assert.Contains(gewaehlt, modell.Generators);
    }

    [Fact]
    public async Task Ein_ungueltiges_Praefix_erzeugt_einen_Befund()
    {
        var profil = SchreibeProfil();
        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Betrag");
        modell.SelectedField = feld;
        feld.Action = FieldAction.Pseudonymize;
        feld.Prefix = "Artikel;";

        Assert.Contains(modell.Issues, i =>
            i.Severity == ValidationSeverity.Error && i.Path.EndsWith(".prefix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Auch_eine_Textregel_zeigt_einen_eigenen_Namensraum_an()
    {
        // Dieselbe Ursache traf das Fenster der Textregeln.
        var profil = SchreibeProfil(p =>
        {
            p.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" };
            p.TextRules.Add(new TextRule
            {
                Name = "beleg",
                Pattern = @"\bBEL-\d{6}\b",
                Generator = "belegNummer",
            });
        });

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var textregeln = modell.CreateTextRulesViewModel();
        Assert.NotNull(textregeln);

        var regel = textregeln!.Rules.Single(r => r.Name == "beleg");

        Assert.NotNull(regel.Generator);
        Assert.Equal("belegNummer", regel.Generator!.Name);
        Assert.Contains(regel.Generator, textregeln.Generators);
    }

    [Fact]
    public async Task Die_Namensraum_Warnung_zaehlt_alle_mitbetroffenen_Regeln()
    {
        // Ehemals "PrefixIsShared" -- ein Bool nur fuers Praefix. Jetzt eine
        // allgemeine Warnung, die genauso fuer die neuen Optionen (Maske,
        // Werteliste, ...) gilt und die Anzahl der mitbetroffenen Regeln
        // nennt, nicht nur ein Ja/Nein.
        var profil = SchreibeProfil(p =>
        {
            p.Generators["belegNummer"] = new GeneratorSettings { Type = "numericId" };
            p.Fields.Add(new FieldRule
            {
                Match = "Kundennummer", Action = FieldAction.Pseudonymize, Generator = "belegNummer",
            });
            p.TextRules.Add(new TextRule
            {
                Name = "beleg", Pattern = @"\bBEL-\d{6}\b", Generator = "belegNummer",
            });
        });

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "Kundennummer");
        modell.SelectedField = feld;

        // Die Textregel "beleg" nutzt denselben Namensraum -- eine
        // Optionsaenderung an "belegNummer" traefe sie mit.
        Assert.Equal(1, feld.SharedNamespaceCount);
        Assert.True(feld.NamespaceIsShared);
        Assert.Contains("1", feld.NamespaceSharedWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void Der_Testlauf_fasst_die_Einstellungen_des_Anwenders_nicht_an()
    {
        // Befund D-8 der Testdurchfuehrung vom 4. September 2026: die
        // Ansichtsmodelle rufen an mehreren Stellen GuiSettings.Save(), und der
        // Pfad loeste ohne gesetztes XDG_CONFIG_HOME auf das echte
        // Benutzerverzeichnis auf. Ein Testlauf ueberschrieb damit die
        // Einstellungen des angemeldeten Benutzers.
        var pfad = GuiSettings.FilePath;

        Assert.StartsWith(TestUmgebung.Verzeichnis, pfad, StringComparison.Ordinal);

        var heimat = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(Path.Combine(heimat, ".config"), pfad, StringComparison.Ordinal);
    }

    [Fact]
    public void Der_Name_der_Ausgabedatei_wird_erkennbar_vorbelegt()
    {
        Assert.Equal("kunden.pseudo.csv", DialogService.SuggestOutputName("/pfad/kunden.csv"));
        Assert.Equal("daten.klartext.json",
            DialogService.SuggestOutputName("/pfad/daten.json", "klartext"));

        // Ein schon vorhandener Zusatz wird nicht verdoppelt.
        Assert.Equal("kunden.pseudo.csv", DialogService.SuggestOutputName("/pfad/kunden.pseudo.csv"));
    }

    // --------------------------------------------------- Profilverwaltung

    [Fact]
    public async Task Profilwechsel_bei_ungespeicherten_Aenderungen_fragt_nach_und_bricht_bei_Abbrechen_ab()
    {
        var profil1 = SchreibeProfil();
        var profil2 = SchreibeZweitesProfil("zweites.json", "zweites");

        var dialoge = new FakeDialogService { SaveChangesChoice = SaveChoice.Cancel };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil1, SchreibeCsv());

        modell.Fields.Single(f => f.FieldName == "Betrag").Action = FieldAction.Passthrough;
        Assert.True(modell.HasUnsavedChanges);

        dialoge.ProfilesResult = NeueZusammenfassung(profil2, "zweites");
        await modell.ShowProfilesAsync();

        Assert.Equal(1, dialoge.AskSaveChangesCalls);

        // Abbrechen: das alte Profil bleibt geladen, mit seinen Aenderungen.
        Assert.Equal("test", modell.ProfileName);
        Assert.True(modell.HasUnsavedChanges);
    }

    [Fact]
    public async Task Profilwechsel_bei_ungespeicherten_Aenderungen_laedt_bei_Verwerfen_das_neue_Profil()
    {
        var profil1 = SchreibeProfil();
        var profil2 = SchreibeZweitesProfil("zweites.json", "zweites");

        var dialoge = new FakeDialogService { SaveChangesChoice = SaveChoice.Discard };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil1, SchreibeCsv());

        modell.Fields.Single(f => f.FieldName == "Betrag").Action = FieldAction.Passthrough;
        Assert.True(modell.HasUnsavedChanges);

        dialoge.ProfilesResult = NeueZusammenfassung(profil2, "zweites");
        await modell.ShowProfilesAsync();

        Assert.Equal("zweites", modell.ProfileName);
        Assert.False(modell.HasUnsavedChanges);
    }

    [Fact]
    public async Task Anlegen_speichert_das_Profil_am_gewaehlten_Pfad_mit_Namen_und_Beschreibung()
    {
        var csv = SchreibeCsv();
        var ziel = Path.Combine(_verzeichnis, "eigenerName.json");

        var dialoge = new FakeDialogService
        {
            DataFilesToOpen = new[] { csv },
            NewProfileResult = new NewProfileResult("eigenerName", "Testbeschreibung", ziel, OpenExisting: false),
        };

        var modell = Erzeugen(dialoge);
        await modell.NewProfileAsync();

        Assert.True(modell.HasProfile);
        Assert.Equal("eigenerName", modell.ProfileName);
        Assert.True(File.Exists(ziel));

        var geschrieben = ProfileStore.Load(ziel);
        Assert.Equal("eigenerName", geschrieben.ProfileName);
        Assert.Equal("Testbeschreibung", geschrieben.Description);
    }

    [Fact]
    public async Task NewFieldsHint_zaehlt_die_Felder_ohne_eigene_Regel()
    {
        var profil = SchreibeProfil(p => p.Fields.Add(new FieldRule
        {
            Match = "Kundenname",
            Action = FieldAction.Pseudonymize,
            Generator = "personName",
        }));

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, SchreibeCsv());

        // Vier Felder in der Beispieldatei, eines traegt eine eigene Regel --
        // die restlichen drei sind neu fuer dieses Profil.
        Assert.True(modell.HasNewFieldsHint);
        Assert.Contains("3 neue Felder", modell.NewFieldsHint);
    }

    [Fact]
    public async Task Eine_geoeffnete_Datei_landet_im_Index_des_Profils()
    {
        var profil = SchreibeProfil();
        var csv = SchreibeCsv();

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, csv);

        var index = ProfileIndex.Load();
        var eintrag = index.Profiles.Single(p =>
            string.Equals(p.Path, Path.GetFullPath(profil), StringComparison.Ordinal));

        Assert.Contains(eintrag.Files, f => string.Equals(f.Path, Path.GetFullPath(csv), StringComparison.Ordinal));
    }

    // ---------------------------------------------------- Schnellwahl (A3)

    [Fact]
    public async Task Eine_geoeffnete_Datei_erscheint_sofort_in_der_Schnellwahl()
    {
        var profil = SchreibeProfil();
        var csv = SchreibeCsv();

        var modell = Erzeugen();
        await modell.InitializeAsync(profil, csv);

        Assert.True(modell.HasRecentDataFiles);
        var eintrag = Assert.Single(modell.RecentDataFiles);

        Assert.Equal(Path.GetFullPath(csv), eintrag.FullPath);
        Assert.Equal("kunden.csv", eintrag.DisplayName);
        Assert.True(eintrag.Exists);
        Assert.True(eintrag.IsCurrent);

        // Die gerade geladene Datei laesst sich nicht nochmal "oeffnen".
        Assert.False(eintrag.OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task Die_Schnellwahl_wechselt_ohne_Dialog_zwischen_bekannten_Dateien()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");
        var csv2 = SchreibeCsv("kunden2.csv");

        var dialoge = new FakeDialogService { DataFileToOpen = csv2 };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);

        // Zweite Datei ueber den gewoehnlichen Weg oeffnen -- danach kennt der
        // Index beide, und die Schnellwahl zeigt beide an.
        await AusfuehrenUndWartenAsync(modell.OpenDataFileCommand);

        Assert.Equal("kunden2.csv", modell.DataFileName);
        Assert.Equal(2, modell.RecentDataFiles.Count);

        var ersteDatei = modell.RecentDataFiles.Single(f => f.DisplayName == "kunden.csv");
        Assert.False(ersteDatei.IsCurrent);
        Assert.True(ersteDatei.OpenCommand.CanExecute(null));

        // Zurueck zur ersten Datei -- ausschliesslich ueber die Schnellwahl,
        // ohne dass der (in diesem Test scharfe) Oeffnen-Dialog dafuer noetig
        // waere. Keine Rueckfrage: Eingabedateien werden nie geschrieben.
        await AusfuehrenUndWartenAsync(ersteDatei.OpenCommand);

        Assert.Equal("kunden.csv", modell.DataFileName);
        Assert.Equal(4, modell.Fields.Count);
        Assert.True(modell.RecentDataFiles.Single(f => f.DisplayName == "kunden.csv").IsCurrent);
    }

    [Fact]
    public async Task Ein_geloeschter_Eintrag_der_Schnellwahl_ist_ausgegraut_und_ungefaehrlich()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");
        var csv2 = SchreibeCsv("kunden2.csv");

        var dialoge = new FakeDialogService { DataFileToOpen = csv2 };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);

        // Die erste Datei verschwindet, bevor die Schnellwahl das naechste
        // Mal aufgebaut wird (beim Oeffnen der zweiten Datei).
        File.Delete(csv1);
        await AusfuehrenUndWartenAsync(modell.OpenDataFileCommand);

        var geloeschterEintrag = modell.RecentDataFiles.Single(f => f.DisplayName == "kunden.csv");
        Assert.False(geloeschterEintrag.Exists);
        Assert.False(geloeschterEintrag.OpenCommand.CanExecute(null));

        // Ein trotzdem erzwungener Aufruf (etwa ein veraltetes Tastaturkuerzel)
        // darf die Oberflaeche nicht werfen. AsyncRelayCommand.Execute prueft
        // CanExecute selbst und tut in diesem Fall nichts -- der Status bleibt
        // unveraendert, statt dass eine FileNotFoundException hochkaeme.
        var statusVorher = modell.StatusText;
        geloeschterEintrag.OpenCommand.Execute(null);
        Assert.Equal(statusVorher, modell.StatusText);
    }

    // ------------------------------------------------- Mehrfachauswahl

    [Fact]
    public async Task Anlegen_aus_einer_Datei_haelt_die_Beispieldatei_offen()
    {
        // Der Weg "Neu aus Datei…": nach dem Anlegen muss dieselbe Datei
        // geoeffnet sein, aus der das Regelgeruest stammt. Ein Fehler beim
        // Schreiben brach das frueher stumm ab, und die Oberflaeche blieb bei
        // "keine Datei geoeffnet" stehen.
        var csv = SchreibeCsv();
        var dialoge = new FakeDialogService
        {
            DataFilesToOpen = new[] { csv },
            NewProfileResult = new NewProfileResult(
                "kunden", null, Path.Combine(_verzeichnis, "kunden.json"), OpenExisting: false),
        };

        var modell = Erzeugen(dialoge);
        await modell.NewProfileAsync();

        Assert.True(modell.HasDataFile);
        Assert.Equal("kunden.csv", modell.DataFileName);
        Assert.Equal(4, modell.Fields.Count);
    }

    [Fact]
    public async Task Eine_Aktion_gilt_fuer_alle_gewaehlten_Felder()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        var auswahl = modell.Fields
            .Where(f => f.FieldName is "Kundenname" or "IBAN")
            .ToList();

        modell.UpdateSelection(auswahl);
        modell.SelectedAction = ActionOption.For(FieldAction.Redact);

        Assert.All(auswahl, feld => Assert.Equal(FieldAction.Redact, feld.Action));

        // Nicht gewaehlte Felder bleiben unberuehrt.
        Assert.Equal(FieldAction.Error, modell.Fields.Single(f => f.FieldName == "Betrag").Action);
    }

    [Fact]
    public async Task Ein_Generator_gilt_nur_fuer_die_ersetzten_Felder_der_Auswahl()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        var ersetzt = modell.Fields.Single(f => f.FieldName == "Kundenname");
        var durchgelassen = modell.Fields.Single(f => f.FieldName == "Betrag");

        ersetzt.Action = FieldAction.Pseudonymize;
        durchgelassen.Action = FieldAction.Passthrough;

        modell.UpdateSelection([ersetzt, durchgelassen]);
        modell.SelectedGenerator = new GeneratorOption("token", "");

        Assert.Equal("token", ersetzt.Generator);
        Assert.Null(durchgelassen.Generator);
    }

    [Fact]
    public async Task Die_Ueberschrift_nennt_die_Zahl_der_gewaehlten_Felder()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        modell.UpdateSelection(modell.Fields.Take(3));

        Assert.True(modell.IsMultiSelection);
        Assert.False(modell.IsSingleSelection);
        Assert.Equal("Regel: 3 Felder", modell.SelectedFieldTitle);
        Assert.Contains("3 gewählten Felder", modell.MultiSelectionHint);
    }

    [Fact]
    public async Task Ein_einzeln_gewaehltes_Feld_bleibt_die_Einzelauswahl()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        var feld = modell.Fields.Single(f => f.FieldName == "IBAN");
        modell.SelectedField = feld;

        Assert.True(modell.IsSingleSelection);
        Assert.Equal(feld, Assert.Single(modell.SelectedFields));
        Assert.Equal("Regel: IBAN", modell.SelectedFieldTitle);

        // Auch ohne Umweg ueber die Liste wirkt die Aktion auf dieses Feld.
        modell.SelectedAction = ActionOption.For(FieldAction.Drop);
        Assert.Equal(FieldAction.Drop, feld.Action);
    }

    [Fact]
    public async Task Das_fuehrende_Feld_bleibt_beim_Erweitern_der_Auswahl_stehen()
    {
        var modell = Erzeugen();
        await modell.InitializeAsync(SchreibeProfil(), SchreibeCsv());

        var zweites = modell.Fields[1];
        modell.SelectedField = zweites;
        modell.UpdateSelection([modell.Fields[0], zweites]);

        Assert.Equal(zweites, modell.SelectedField);
    }

    // ------------------------------------------- Mehrfachauswahl bei Dateien

    [Fact]
    public async Task Ein_Profil_aus_zwei_Dateien_kennt_die_Felder_beider_und_listet_beide_Dateien()
    {
        var stammdaten = Path.Combine(_verzeichnis, "kunden_stammdaten.csv");
        File.WriteAllText(stammdaten,
            "Kundennummer;Nachname;Vorname\nK1001;Altmaier;Anton\n", new UTF8Encoding(false));

        var adressen = Path.Combine(_verzeichnis, "kunden_adressen.csv");
        File.WriteAllText(adressen,
            "Kundennummer;Strasse;Ort\nK1001;Altenstraße 1;Altheim\n", new UTF8Encoding(false));

        var dialoge = new FakeDialogService
        {
            DataFilesToOpen = new[] { stammdaten, adressen },
            NewProfileResult = new NewProfileResult(
                "kunden", null, Path.Combine(_verzeichnis, "kunden.json"), OpenExisting: false),
        };

        var modell = Erzeugen(dialoge);
        await modell.NewProfileAsync();

        // Das Regelwerk des Profils kennt die Felder beider Dateien -- die
        // sichtbare Feldliste (modell.Fields) zeigt dagegen immer nur die
        // Spalten der gerade geoeffneten Datei, hier also nur die erste.
        Assert.Equal(
            new[] { "Kundennummer", "Nachname", "Vorname", "Strasse", "Ort" },
            modell.Session!.Profile.Fields.Select(f => f.Match));

        Assert.Equal(2, modell.RecentDataFiles.Count);
        Assert.Contains(modell.RecentDataFiles, f => f.DisplayName == "kunden_stammdaten.csv");
        Assert.Contains(modell.RecentDataFiles, f => f.DisplayName == "kunden_adressen.csv");
    }

    // -------------------------------------------------------- Sammellaeufe

    private static void EntscheideAlleFelder(MainViewModel modell)
    {
        foreach (var feld in modell.Fields)
            feld.Action = FieldAction.Passthrough;
    }

    [Fact]
    public async Task Ein_Sammellauf_schreibt_fuer_alle_bekannten_Dateien_eine_Ausgabe_daneben()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");
        var csv2 = SchreibeCsv("kunden2.csv");

        var index = ProfileIndex.Load();
        index.RecordDataFile(profil, csv1);
        index.RecordDataFile(profil, csv2);
        index.Save();

        var dialoge = new FakeDialogService { BatchRunConfirmed = true };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);
        EntscheideAlleFelder(modell);

        await AusfuehrenUndWartenAsync(modell.ObfuscateAllCommand);

        Assert.NotNull(dialoge.LastBatchRunProposal);
        Assert.Equal(2, dialoge.LastBatchRunProposal!.FileCount);

        var ziel1 = Path.Combine(_verzeichnis, "kunden.pseudo.csv");
        var ziel2 = Path.Combine(_verzeichnis, "kunden2.pseudo.csv");
        Assert.True(File.Exists(ziel1));
        Assert.True(File.Exists(ziel2));

        Assert.Contains("2 von 2", modell.StatusText);
        Assert.NotNull(modell.LastResult);
        Assert.Equal("Pseudodateien erzeugt", modell.LastResult!.Headline);
    }

    [Fact]
    public async Task Eine_abgelehnte_Rueckfrage_schreibt_nichts()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");

        var index = ProfileIndex.Load();
        index.RecordDataFile(profil, csv1);
        index.Save();

        var dialoge = new FakeDialogService { BatchRunConfirmed = false };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);
        EntscheideAlleFelder(modell);

        await AusfuehrenUndWartenAsync(modell.ObfuscateAllCommand);

        Assert.False(File.Exists(Path.Combine(_verzeichnis, "kunden.pseudo.csv")));
    }

    [Fact]
    public async Task Eine_fehlende_Datei_wird_im_Sammellauf_uebersprungen_und_gemeldet()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");
        var csv2 = SchreibeCsv("kunden2.csv");

        var index = ProfileIndex.Load();
        index.RecordDataFile(profil, csv1);
        index.RecordDataFile(profil, csv2);
        index.Save();

        var dialoge = new FakeDialogService { BatchRunConfirmed = true };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);
        EntscheideAlleFelder(modell);

        // Zweite Datei verschwindet, bevor der Sammellauf startet.
        File.Delete(csv2);

        await AusfuehrenUndWartenAsync(modell.ObfuscateAllCommand);

        Assert.Equal(1, dialoge.LastBatchRunProposal!.FileCount);
        Assert.True(File.Exists(Path.Combine(_verzeichnis, "kunden.pseudo.csv")));
        Assert.Contains("kunden2.csv", modell.StatusText);
    }

    [Fact]
    public async Task Ein_Sammellauf_der_Klartextdateien_liefert_die_Ausgangswerte()
    {
        var profil = SchreibeProfil();
        var csv1 = SchreibeCsv("kunden.csv");
        var csv2 = SchreibeCsv("kunden2.csv");

        var index = ProfileIndex.Load();
        index.RecordDataFile(profil, csv1);
        index.RecordDataFile(profil, csv2);
        index.Save();

        var dialoge = new FakeDialogService { BatchRunConfirmed = true };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv1);
        EntscheideAlleFelder(modell);

        await AusfuehrenUndWartenAsync(modell.ObfuscateAllCommand);

        var pseudo1 = Path.Combine(_verzeichnis, "kunden.pseudo.csv");
        var pseudo2 = Path.Combine(_verzeichnis, "kunden2.pseudo.csv");

        // Als Datendateien des Profils oeffnen -- wie im Handdurchgang des
        // Plans: die Pseudodateien selbst werden zu den bekannten Dateien.
        index = ProfileIndex.Load();
        index.RecordDataFile(profil, pseudo1);
        index.RecordDataFile(profil, pseudo2);
        index.Save();

        var modell2 = Erzeugen(dialoge);
        await modell2.InitializeAsync(profil, pseudo1);

        await AusfuehrenUndWartenAsync(modell2.DeobfuscateAllCommand);

        var klartext1 = Path.Combine(_verzeichnis, "kunden.pseudo.klartext.csv");
        var klartext2 = Path.Combine(_verzeichnis, "kunden2.pseudo.klartext.csv");

        Assert.True(File.Exists(klartext1));
        Assert.True(File.Exists(klartext2));
        Assert.Equal(await File.ReadAllTextAsync(csv1), await File.ReadAllTextAsync(klartext1));
        Assert.Equal(await File.ReadAllTextAsync(csv2), await File.ReadAllTextAsync(klartext2));
    }

    [Fact]
    public async Task Eine_Pseudodatei_wird_im_Sammellauf_nicht_ueber_sich_selbst_geschrieben()
    {
        var profil = SchreibeProfil();
        var csv = SchreibeCsv("kunden.csv");

        // Eine Datei, die den Zusatz schon traegt: SuggestOutputName haengt ihn
        // kein zweites Mal an, das Ziel waere also die Datei selbst.
        var pseudo = SchreibeCsv("kunden.pseudo.csv");
        var inhaltVorher = await File.ReadAllTextAsync(pseudo);

        var index = ProfileIndex.Load();
        index.RecordDataFile(profil, csv);
        index.RecordDataFile(profil, pseudo);
        index.Save();

        var dialoge = new FakeDialogService { BatchRunConfirmed = true };
        var modell = Erzeugen(dialoge);
        await modell.InitializeAsync(profil, csv);
        EntscheideAlleFelder(modell);

        await AusfuehrenUndWartenAsync(modell.ObfuscateAllCommand);

        // Die Rueckfrage zaehlt sie gar nicht erst mit, ihr Inhalt bleibt
        // unberuehrt, und die Abschlussmeldung nennt sie.
        Assert.Equal(1, dialoge.LastBatchRunProposal!.FileCount);
        Assert.Equal(inhaltVorher, await File.ReadAllTextAsync(pseudo));
        Assert.Contains("kunden.pseudo.csv", modell.StatusText);
    }
}
