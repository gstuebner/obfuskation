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
}
