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
}
