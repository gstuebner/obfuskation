using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Anlegen und Bearbeiten eines eigenen Generators (Plan P3), geprueft ohne
/// Fenster: <see cref="GeneratorEditorViewModel"/> schreibt selbst nichts, der
/// Aufrufer liest <see cref="GeneratorEditorViewModel.ResultName"/> und
/// <see cref="GeneratorEditorViewModel.ResultSettings"/> erst nach
/// <see cref="GeneratorEditorViewModel.ApplyCommand"/>.
/// </summary>
public sealed class GeneratorEditorViewModelTests
{
    private static GeneratorEditorViewModel Neu(
        Profile? profile = null, ExtensionLibrary? extensions = null,
        bool canEditGlobal = true, RuleScope initialScope = RuleScope.Global,
        string? existingName = null, RuleScope? lockScopeTo = null,
        string? suggestedName = null, IReadOnlyList<string>? users = null)
        => new(profile, extensions ?? ExtensionLibrary.Empty, canEditGlobal, globalLockReason: null,
            initialScope, existingName, lockScopeTo, suggestedName, users: users);

    // ---------------------------------------------------------- Namensprüfung

    [Fact]
    public void Ein_leerer_Name_ist_ein_Fehler()
    {
        var modell = Neu();
        modell.Name = "   ";

        Assert.NotNull(modell.NameError);
        Assert.False(modell.CanApply);
    }

    [Fact]
    public void Ein_eingebauter_Name_ist_ein_Fehler()
    {
        var modell = Neu();
        modell.Name = "token";

        Assert.NotNull(modell.NameError);
    }

    [Fact]
    public void Ein_im_Zielbereich_schon_vergebener_Name_ist_ein_Fehler()
    {
        var extensions = new ExtensionLibrary();
        extensions.Generators["fw"] = new GeneratorSettings { Type = "token" };

        var modell = Neu(extensions: extensions);
        modell.Name = "fw";

        Assert.NotNull(modell.NameError);
    }

    [Fact]
    public void Ein_erlaubter_Name_hat_keinen_Fehler()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";

        Assert.Null(modell.NameError);
    }

    // ------------------------------------------------- Sichtbarkeit je Art

    [Fact]
    public void Die_Optionssichtbarkeit_folgt_der_gewaehlten_Art()
    {
        var modell = Neu();

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "token");
        Assert.True(modell.ShowPrefix);
        Assert.False(modell.ShowPatternMask);
        Assert.False(modell.ShowWordlist);

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "pattern");
        Assert.False(modell.ShowPrefix);
        Assert.True(modell.ShowPatternMask);

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "wordlist");
        Assert.False(modell.ShowPatternMask);
        Assert.True(modell.ShowWordlist);
    }

    [Fact]
    public void Ein_Wechsel_von_token_zu_pattern_laesst_das_Praefix_aus_dem_Ergebnis_fallen()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";
        modell.Prefix = "FW~";

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "pattern");
        modell.Pattern = "AA-9999";

        Assert.True(modell.CanApply);
        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.Equal("pattern", modell.ResultSettings.Type);
        Assert.Equal("AA-9999", modell.ResultSettings.Pattern);
        Assert.Null(modell.ResultSettings.Prefix);
    }

    // -------------------------------------------------------------- Bereich

    [Fact]
    public void Ohne_Profil_ist_der_Bereich_fest_auf_alle_Projekte()
    {
        // initialScope=Project wird bewusst uebergeben, um zu zeigen, dass
        // das Fehlen eines Profils staerker wiegt als jeder Vorschlag.
        var modell = Neu(profile: null, initialScope: RuleScope.Project);

        Assert.Equal(RuleScope.Global, modell.Scope);
        Assert.False(modell.CanChooseScope);
    }

    // ----------------------------------------------------- Sperren beim Bearbeiten

    [Fact]
    public void Im_Bearbeiten_Modus_mit_Verwendern_ist_CanRename_falsch()
    {
        var profil = new Profile();
        profil.Generators["fw"] = new GeneratorSettings { Type = "token", Prefix = "FW~" };

        var modell = Neu(
            profile: profil, initialScope: RuleScope.Project,
            existingName: "fw", users: new[] { "Regel „fw“" });

        Assert.False(modell.CanRename);
        Assert.False(modell.CanChangeType);
        Assert.NotNull(modell.UsersHint);
    }

    // -------------------------------------------------------------- Vorschau

    [Fact]
    public void Die_Vorschau_liefert_einen_Wert()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";
        modell.Prefix = "FW~";

        Assert.Null(modell.PreviewError);
        Assert.NotNull(modell.PreviewText);
        Assert.StartsWith("FW~", modell.PreviewText, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- Ausdruck

    [Fact]
    public void Die_Namen_in_BaseTypes_entsprechen_den_Basistypen_aus_OptionOwnership()
    {
        var erwartet = new HashSet<string>(
            ProfileValidator.OptionOwnership.Select(o => o.BaseType), StringComparer.OrdinalIgnoreCase);

        var modell = Neu();
        var tatsaechlich = new HashSet<string>(
            modell.BaseTypes.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);

        Assert.True(erwartet.SetEquals(tatsaechlich));
        Assert.DoesNotContain(modell.BaseTypes, o => string.Equals(o.Name, "firstName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ein_eingebauter_Name_mit_eigenem_type_behaelt_diesen_type_als_Grundlage()
    {
        // Wie GeneratorRegistry.Build: "type" geht vor dem Schluessel. Ein von
        // Hand angelegtes "email": { "type": "wordlist" } ist eine Werteliste.
        var extensions = new ExtensionLibrary();
        extensions.Generators["email"] = new GeneratorSettings { Type = "wordlist", Values = ["a", "b", "c", "d", "e"] };

        var modell = Neu(extensions: extensions, existingName: "email");

        Assert.Equal("wordlist", modell.SelectedBaseType.Name);
        Assert.False(modell.CanRename);
        Assert.False(modell.CanChangeType);
    }

    [Fact]
    public void Bearbeiten_eines_numericId_Generators_behaelt_numericId()
    {
        var profil = new Profile();
        profil.Generators["kundenId"] = new GeneratorSettings { Type = "numericId" };

        var modell = Neu(profile: profil, initialScope: RuleScope.Project, existingName: "kundenId");

        Assert.Equal("numericId", modell.SelectedBaseType.Name);
        Assert.True(modell.HasKindHint);
    }

    [Fact]
    public void Die_Vorlage_KFZ_fuellt_Ausdruck_und_Tabelle()
    {
        var modell = Neu();
        modell.Name = "kfz";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "expression");

        modell.ApplyTemplateCommand.Execute("kfz");

        Assert.False(string.IsNullOrEmpty(modell.Expression));
        Assert.Single(modell.Tables);
        Assert.Equal("kreis", modell.Tables[0].Name);
        Assert.NotEmpty(modell.Tables[0].Values);

        Assert.Null(modell.PreviewError);
        Assert.NotNull(modell.PreviewText);
        Assert.Matches(new Regex(@"[A-Z]{1,2}-[A-Z]{2} \d{2,3}E?"), modell.PreviewText);
    }

    [Fact]
    public void PlusTabelle_verwendet_den_im_Ausdruck_fehlenden_Tabellennamen()
    {
        var modell = Neu();
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "expression");
        modell.Expression = "{kreis}-9999";

        Assert.True(modell.HasMissingTables);

        modell.AddTableCommand.Execute(null);

        Assert.Single(modell.Tables);
        Assert.Equal("kreis", modell.Tables[0].Name);
        Assert.False(modell.HasMissingTables);
    }

    [Fact]
    public void Anlegen_bei_einer_fehlenden_Tabelle_legt_sie_an()
    {
        var modell = Neu();
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "expression");
        modell.Expression = "{kreis}-9999";

        var hinweis = Assert.Single(modell.MissingTableHints);
        Assert.Equal("kreis", hinweis.Name);

        hinweis.AddCommand.Execute(null);

        Assert.Single(modell.Tables);
        Assert.Equal("kreis", modell.Tables[0].Name);
        Assert.False(modell.HasMissingTables);
    }

    [Fact]
    public void Doppelte_Tabellennamen_sperren_CanApply()
    {
        var modell = Neu();
        modell.Name = "kfz";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "expression");
        modell.Expression = "{a}{b}";

        modell.AddTableCommand.Execute(null);
        modell.Tables[0].ValuesText = "X";

        modell.AddTableCommand.Execute(null);
        modell.Tables[1].Name = "a";
        modell.Tables[1].ValuesText = "Y";

        Assert.False(modell.CanApply);
        Assert.Contains(modell.Errors, e => e.Contains("mehrfach vergeben"));
    }

    // --------------------------------------------------------- Verschiebung

    [Fact]
    public void Die_Hoechstverschiebung_landet_im_Ergebnis()
    {
        var modell = Neu();
        modell.Name = "datumKurz";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");

        Assert.True(modell.ShowMaxDays);
        Assert.Equal(GeneratorSettings.DefaultMaxDays, modell.MaxDays);

        modell.MaxDays = 30;
        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.Equal("dateShift", modell.ResultSettings.Type);
        Assert.Equal(30, modell.ResultSettings.MaxDays);
    }

    [Fact]
    public void Ein_Wechsel_von_dateShift_zu_token_setzt_maxDays_zurueck()
    {
        var modell = Neu();
        modell.Name = "datumKurz";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");
        modell.MaxDays = 30;

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "token");

        Assert.False(modell.ShowMaxDays);
        Assert.True(modell.CanApply);
        modell.ApplyCommand.Execute(null);
        Assert.Equal(GeneratorSettings.DefaultMaxDays, modell.ResultSettings.MaxDays);
    }

    [Fact]
    public void Beim_Bearbeiten_warnt_eine_geaenderte_Hoechstverschiebung()
    {
        var profil = new Profile();
        profil.Generators["datumKurz"] = new GeneratorSettings { Type = "dateShift", MaxDays = 30 };

        var modell = Neu(profile: profil, initialScope: RuleScope.Project, existingName: "datumKurz");

        Assert.Equal(30, modell.MaxDays);
        Assert.False(modell.HasMaxDaysChangeWarning);

        modell.MaxDays = 60;
        Assert.True(modell.HasMaxDaysChangeWarning);
        Assert.Contains("Bisher 30 Tage", modell.MaxDaysChangeWarning);

        modell.MaxDays = 30;
        Assert.False(modell.HasMaxDaysChangeWarning);
    }

    [Fact]
    public void Ein_neuer_Generator_warnt_nicht_vor_einer_geaenderten_Hoechstverschiebung()
    {
        var modell = Neu();
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");

        modell.MaxDays = 30;

        Assert.False(modell.HasMaxDaysChangeWarning);
    }

    [Fact]
    public void Fuer_Datumsarten_ist_das_vorgegebene_Beispiel_ein_Datum()
    {
        var modell = Neu();
        modell.Name = "datumKurz";

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");

        Assert.Equal("15.03.2024", modell.SampleInput);
        Assert.False(modell.HasPreviewError);
        Assert.Matches(new Regex(@"^\d{2}\.\d{2}\.\d{4}$"), modell.PreviewText);

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "token");
        Assert.Equal("Beispiel 4711", modell.SampleInput);
    }

    [Fact]
    public void Ein_selbst_eingetipptes_Beispiel_bleibt_beim_Artwechsel_stehen()
    {
        var modell = Neu();
        modell.SampleInput = "Muster 0815";

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");

        Assert.Equal("Muster 0815", modell.SampleInput);
    }

    [Fact]
    public void Wechsel_von_expression_zu_token_laesst_Ausdruck_und_Tabellen_fallen()
    {
        var modell = Neu();
        modell.Name = "kfz";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "expression");
        modell.Expression = @"[A-Z]{3}-\d{4}";

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "token");

        Assert.True(modell.CanApply);
        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.Equal("token", modell.ResultSettings.Type);
        Assert.Null(modell.ResultSettings.Expression);
        Assert.Null(modell.ResultSettings.Tables);
    }

    // ------------------------------------- Umstellung eines eingebauten Generators (Plan P3c)

    [Fact]
    public void Bearbeiten_einer_Umstellung_oeffnet_mit_der_Art_des_umgestellten_Generators()
    {
        var extensions = new ExtensionLibrary
        {
            Generators = { ["email"] = new GeneratorSettings { Domain = "firma.test" } },
        };

        var modell = Neu(extensions: extensions, existingName: "email");

        Assert.Equal("email", modell.SelectedBaseType.Name);
        Assert.True(modell.CanApply);
        Assert.Null(modell.NameError);
    }

    [Fact]
    public void Uebernehmen_einer_Umstellung_aendert_die_Domain_aber_nicht_den_Typ()
    {
        var extensions = new ExtensionLibrary
        {
            Generators = { ["email"] = new GeneratorSettings { Domain = "firma.test" } },
        };

        var modell = Neu(extensions: extensions, existingName: "email");
        modell.Domain = "andere.test";

        modell.ApplyCommand.Execute(null);

        Assert.True(modell.Confirmed);
        Assert.Null(modell.ResultSettings.Type);
        Assert.Equal("andere.test", modell.ResultSettings.Domain);
    }

    // --------------------------------------------------- Neue Arten und Felder

    [Fact]
    public void Domain_landet_im_Ergebnis_und_ein_Wechsel_der_Art_leert_sie()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "email");
        modell.Domain = "firma.test";

        modell.ApplyCommand.Execute(null);
        Assert.Equal("firma.test", modell.ResultSettings.Domain);

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "pattern");
        modell.Pattern = "AA-9999";

        modell.ApplyCommand.Execute(null);
        Assert.Null(modell.ResultSettings.Domain);
    }

    [Fact]
    public void Country_und_formats_landen_im_Ergebnis_und_ein_Wechsel_der_Art_leert_sie()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "iban");
        modell.Country = "at";

        Assert.Equal("AT", modell.Country);

        modell.ApplyCommand.Execute(null);
        Assert.Equal("AT", modell.ResultSettings.Country);

        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "dateShift");
        modell.FormatsText = "dd.MM.yy";

        modell.ApplyCommand.Execute(null);
        Assert.Equal(new List<string> { "dd.MM.yy" }, modell.ResultSettings.Formats);
        Assert.Null(modell.ResultSettings.Country);
    }

    [Fact]
    public void Country_wird_bei_der_Eingabe_zu_Grossbuchstaben()
    {
        var modell = Neu();
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "iban");

        modell.Country = "at";

        Assert.Equal("AT", modell.Country);
    }

    [Fact]
    public void DomainWarning_erscheint_bei_firma_de_nicht_bei_firma_test()
    {
        var modell = Neu();
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "email");

        modell.Domain = "firma.de";
        Assert.True(modell.HasDomainWarning);

        modell.Domain = "firma.test";
        Assert.False(modell.HasDomainWarning);
    }

    [Fact]
    public void Die_Vorschau_fuer_email_mit_eigener_Domain_endet_darauf()
    {
        var modell = Neu();
        modell.Name = "meinGenerator";
        modell.SelectedBaseType = modell.BaseTypes.Single(o => o.Name == "email");
        modell.Domain = "firma.test";

        Assert.Null(modell.PreviewError);
        Assert.NotNull(modell.PreviewText);
        Assert.All(modell.PreviewText!.Split('\n'),
            zeile => Assert.EndsWith("@firma.test", zeile, StringComparison.Ordinal));
    }
}
