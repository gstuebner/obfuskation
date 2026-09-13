using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Der Dialog "Immer ersetzen…", geprueft ohne Fenster: die Reichweite
/// (woertlich vs. Form), die Trefferzahl der Vorschau, und wohin die Regel
/// tatsaechlich geschrieben wird -- Profil oder Erweiterungsdatei.
/// </summary>
public sealed class AlwaysReplaceViewModelTests : IDisposable
{
    private static Profile NeuesProfil() => new() { ProfileName = "test" };

    private static TextRulesViewModel Textregeln(Profile profil, ExtensionLibrary erweiterung)
        => new(profil, erweiterung, () => { });

    private static AlwaysReplaceViewModel Erzeugen(
        Profile profil, ExtensionLibrary erweiterung, string sample, string kontext = "",
        Action<bool>? onApplied = null)
        => new(profil, erweiterung, sample, kontext, onApplied ?? (_ => { }), () => Textregeln(profil, erweiterung));

    /// <summary>
    /// Ein Test schreibt bewusst in die (fuer den ganzen Testlauf gemeinsame)
    /// Erweiterungsdatei im Konfigurationsordner -- dieselbe Aufraeumung wie in
    /// <c>Core.Tests.ExtensionLibrarySaveTests</c>, damit kein anderer Test
    /// (etwa <see cref="MainViewModelTests"/>, das beim Konstruieren eines
    /// <see cref="MainViewModel"/> selbst laedt) eine liegengebliebene Datei
    /// vorfindet.
    /// </summary>
    public void Dispose()
    {
        foreach (var datei in new[]
                 {
                     Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName),
                     Path.Combine(PathHelper.ConfigDirectory, ExtensionLibrary.FileName + ".bak"),
                 })
        {
            if (File.Exists(datei))
                File.Delete(datei);
        }
    }

    [Fact]
    public void Ein_Wert_mit_Ziffern_bietet_beide_Reichweiten_an()
    {
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");

        Assert.True(modell.CanUseShape);
        Assert.Equal("genau „FW123456“", modell.LiteralDescription);
        Assert.Equal("„FW“ + 6 Ziffern", modell.ShapeDescription);
    }

    [Fact]
    public void Ohne_Ziffern_fehlt_alles_dieser_Form()
    {
        // Die Oberflaeche soll keine zweite Moeglichkeit anbieten, die
        // dasselbe taete wie die woertliche -- siehe PatternFromSample.Shape.
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "Mustermann");

        Assert.False(modell.CanUseShape);
        Assert.True(modell.UseLiteral);
        Assert.False(modell.UseShape);
    }

    [Fact]
    public void Die_Vorschau_zaehlt_die_Treffer_im_aktuellen_Text()
    {
        var profil = NeuesProfil();
        var text = "Auf FW123456 und FW987654 liegt die Regel, FW12 dagegen nicht.";

        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456", text);

        Assert.Equal(2, modell.Matches.Count);
        Assert.Contains("Trifft im aktuellen Text 2×", modell.MatchSummary);
    }

    [Fact]
    public void Ohne_Beispielwert_laesst_sich_nicht_uebernehmen()
    {
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "");

        Assert.False(modell.HasSample);
        Assert.False(modell.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Uebernehmen_legt_die_Regel_im_Profil_an()
    {
        var profil = NeuesProfil();
        var erweiterung = ExtensionLibrary.Empty;
        var profilGeaendert = false;

        var modell = Erzeugen(profil, erweiterung, "FW123456", onApplied: wentToProfile => profilGeaendert = wentToProfile);

        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.True(profilGeaendert);
        Assert.False(modell.UseExtension);

        var regel = Assert.Single(profil.TextRules);
        Assert.Equal(@"\bFW\d{6}\b", regel.Pattern);
        Assert.Empty(erweiterung.TextRules);

        // "token" mit Praefix bekommt automatisch einen eigenen Namensraum,
        // im selben Ort wie die Regel selbst.
        Assert.True(profil.Generators.ContainsKey(regel.Generator!));
        Assert.Equal("FW~", profil.Generators[regel.Generator!].Prefix);
    }

    [Fact]
    public void Uebernehmen_mit_immer_ueberall_schreibt_in_die_Erweiterungsdatei()
    {
        var profil = NeuesProfil();
        var erweiterung = ExtensionLibrary.Empty;
        var profilGeaendert = true;

        var modell = Erzeugen(profil, erweiterung, "FW123456", onApplied: wentToProfile => profilGeaendert = wentToProfile);
        modell.UseExtension = true;
        var ziel = modell.ExtensionPath;

        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.False(profilGeaendert);
        Assert.Empty(profil.TextRules);

        var regel = Assert.Single(erweiterung.TextRules);
        Assert.Equal(@"\bFW\d{6}\b", regel.Pattern);

        // Tatsaechlich auf die Platte geschrieben, nicht nur im Speicher --
        // eine neue ExtensionLibrary an demselben Pfad muss dieselbe Regel
        // wiederfinden.
        var wiederGelesen = ExtensionLibrary.Load(ziel);
        Assert.Single(wiederGelesen.TextRules);
    }

    [Fact]
    public void Regelnamen_werden_ueber_Profil_und_Erweiterung_hinweg_eindeutig_gemacht()
    {
        var profil = new Profile
        {
            ProfileName = "test",
            TextRules = { new TextRule { Name = "fw", Pattern = "x" } },
        };

        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");
        modell.ApplyCommand.Execute(null);

        Assert.Equal("fw2", modell.RuleName);
        Assert.Equal(2, profil.TextRules.Count);
    }

    [Fact]
    public void Muster_von_Hand_bearbeiten_liefert_die_Fachansicht_und_bricht_den_Dialog_ab()
    {
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");

        TextRulesViewModel? uebergeben = null;
        var geschlossen = false;
        modell.EditManuallyRequested += vm => uebergeben = vm;
        modell.CloseRequested += () => geschlossen = true;

        modell.EditManuallyCommand.Execute(null);

        Assert.NotNull(uebergeben);
        Assert.True(geschlossen);
        Assert.False(modell.Confirmed);
        Assert.Empty(profil.TextRules);
    }

    // ------------------------------------------------------------------
    // "Uebernehmen und weiter": mehrere Begriffe, ohne den Dialog je neu zu
    // oeffnen.
    // ------------------------------------------------------------------

    [Fact]
    public void Uebernehmen_und_weiter_legt_an_und_laesst_den_Dialog_offen()
    {
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");

        var geschlossen = false;
        modell.CloseRequested += () => geschlossen = true;

        modell.ApplyAndContinueCommand.Execute(null);

        Assert.False(geschlossen);
        Assert.True(modell.Confirmed);
        Assert.Equal("", modell.Sample);
        Assert.False(modell.HasSample);
        Assert.Single(profil.TextRules);
        Assert.Equal(new[] { "fw" }, modell.CreatedRuleNames);
        Assert.True(modell.HasCreatedRules);
        Assert.Equal("Angelegt: fw", modell.CreatedSummary);
    }

    [Fact]
    public void Zwei_Durchgaenge_ergeben_zwei_verschieden_benannte_Regeln()
    {
        // MakeUniqueRuleName liest Profil und Erweiterung bei jedem Durchgang
        // neu -- die eben angelegte Regel steht dabei schon drin.
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");

        modell.ApplyAndContinueCommand.Execute(null);
        modell.Sample = "FW987654";
        modell.ApplyCommand.Execute(null);

        Assert.Equal(2, profil.TextRules.Count);
        Assert.Equal(new[] { "fw", "fw2" }, modell.CreatedRuleNames);
        Assert.Equal("fw2", modell.RuleName);
    }

    [Fact]
    public void Abbrechen_nach_Uebernehmen_und_weiter_haelt_die_Bestaetigung()
    {
        // Sonst rechnete der Aufrufer die Vorschau nicht neu, und die bereits
        // angelegte Regel bliebe unsichtbar -- obwohl sie im Profil steht.
        var profil = NeuesProfil();
        var modell = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");

        modell.ApplyAndContinueCommand.Execute(null);
        modell.CancelCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.Single(profil.TextRules);
    }

    [Fact]
    public void Ohne_Eintrag_ist_kein_Anlegen_moeglich()
    {
        var modell = Erzeugen(NeuesProfil(), ExtensionLibrary.Empty, "");

        Assert.False(modell.HasSample);
        Assert.False(modell.ApplyCommand.CanExecute(null));
        Assert.False(modell.ApplyAndContinueCommand.CanExecute(null));
        Assert.False(modell.HasCreatedRules);
    }

    [Fact]
    public void Die_Beschriftung_richtet_sich_nach_dem_Aufrufer()
    {
        var profil = NeuesProfil();

        var ausDerDateiansicht = Erzeugen(profil, ExtensionLibrary.Empty, "FW123456");
        Assert.Equal("Immer ersetzen", ausDerDateiansicht.WindowTitle);
        Assert.False(ausDerDateiansicht.HasIntroText);

        var ausDerTextansicht = new AlwaysReplaceViewModel(
            profil, ExtensionLibrary.Empty, "FW123456", "", _ => { },
            () => Textregeln(profil, ExtensionLibrary.Empty),
            "Nicht erkannte vertrauliche Daten",
            "Diese Stelle wurde nicht automatisch erkannt.");

        Assert.Equal("Nicht erkannte vertrauliche Daten", ausDerTextansicht.WindowTitle);
        Assert.True(ausDerTextansicht.HasIntroText);
    }

    // ------------------------------------------------------------------
    // Der Absturzweg: ein Dialog ohne Beispielwert. Aus der Dateiansicht kam
    // er nie leer herein, aus der Textansicht war er unerreichbar -- der Fall
    // lief nie, bis ein Knopf ihn eroeffnete, und beendete dann den Prozess.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ein_Dialog_ohne_Beispielwert_traegt_jede_Abfrage(string beispiel)
    {
        var modell = Erzeugen(NeuesProfil(), ExtensionLibrary.Empty, beispiel);

        // Jede oeffentliche Abfrage einmal lesen: eine Ausnahme aus einer
        // Bindung beim Aufbau eines modalen Fensters beendet den Prozess, und
        // welche davon die Oberflaeche abfragt, entscheidet das XAML.
        var ex = Record.Exception(() =>
        {
            _ = modell.Sample;
            _ = modell.HasSample;
            _ = modell.CanUseShape;
            _ = modell.LiteralDescription;
            _ = modell.ShapeDescription;
            _ = modell.UseShape;
            _ = modell.UseLiteral;
            _ = modell.UseExtension;
            _ = modell.UseProfile;
            _ = modell.PrefixHint;
            _ = modell.HasPrefixHint;
            _ = modell.ExtensionPath;
            _ = modell.ExtensionHasComments;
            _ = modell.MatchSummary;
            _ = modell.HasMatches;
            _ = modell.EmptyHint;
            _ = modell.WindowTitle;
            _ = modell.HasIntroText;
            _ = modell.CreatedSummary;
            _ = modell.HasCreatedRules;
            _ = modell.ApplyCommand.CanExecute(null);
            _ = modell.ApplyAndContinueCommand.CanExecute(null);
        });

        Assert.Null(ex);
        Assert.False(modell.HasSample);
    }

    [Fact]
    public void Ein_geleertes_Feld_reisst_den_Dialog_nicht_mit()
    {
        // Die zweiseitige Bindung des Eingabefeldes schreibt beim Leeren einen
        // Wert zurueck, ueber dessen Beschaffenheit die Oberflaeche entscheidet
        // -- null eingeschlossen. Frueher riss das jeden Lesezugriff mit.
        var modell = Erzeugen(NeuesProfil(), ExtensionLibrary.Empty, "FW123456");

        modell.Sample = null!;

        Assert.Equal("", modell.Sample);
        Assert.False(modell.HasSample);
        Assert.Equal("", modell.LiteralDescription);
        Assert.False(modell.ApplyCommand.CanExecute(null));
    }
}
