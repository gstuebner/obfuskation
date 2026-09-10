using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Die Textansicht, geprueft ohne Fenster.
///
/// Der Kernfall ist <see cref="Vorschau_und_echter_Lauf_liefern_denselben_Text"/>:
/// wer aus dieser Ansicht kopiert, kopiert genau das, was er sieht. Moeglich
/// ist das nur, weil <see cref="TextViewModel"/> im Konstruktor
/// <c>ObfuscationEngine.EnsureMappingStore</c> ruft (siehe dort und
/// <c>Core.Tests/MappingStoreTests.cs</c>) -- ohne das entstuende bei jedem
/// Probelauf ein fluechtiges Salt.
/// </summary>
public sealed class TextViewModelTests : IDisposable
{
    private readonly string _verzeichnis;

    public TextViewModelTests()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-textview-tests",
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

    /// <summary>
    /// Eine Sitzung mit den eingebauten Standardtextregeln (iban, email, bic,
    /// phone) und einer Ersetzungstabelle im Wegwerfverzeichnis -- niemals der
    /// Vorgabepfad unter <c>~/.local/share</c>, den ein Testlauf nicht anfassen darf.
    /// </summary>
    private ProfileSession ErzeugeSitzung()
    {
        var profil = new Profile
        {
            ProfileName = "text",
            MappingStore = Path.Combine(_verzeichnis, "mapping.json"),
            TextRules = ProfileScaffolder.DefaultTextRules(),
        };

        var pfad = Path.Combine(_verzeichnis, ProfileStore.DefaultFileName);
        ProfileStore.Save(profil, pfad);
        return ProfileSession.Load(pfad, ExtensionLibrary.Empty);
    }

    /// <summary>Ohne Entprellung, damit ein Test nicht auf einen Hintergrundtimer warten muss.</summary>
    private TextViewModel ErzeugeViewModel(ProfileSession sitzung, TextDirection richtung = TextDirection.Forward)
        => new(sitzung, richtung, debounceDelay: TimeSpan.Zero);

    [Fact]
    public void Text_einfuegen_laesst_Fundstellen_erscheinen()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());

        modell.InputText = "Kontakt: max.mustermann@beispiel.de";
        modell.RefreshPreview();

        Assert.True(modell.HasMatches);
        var fund = Assert.Single(modell.Matches);
        Assert.Equal("email", fund.RuleName);
        Assert.Equal("max.mustermann@beispiel.de", fund.Original);
        Assert.NotEqual(fund.Original, fund.Replacement);
        Assert.Contains("1× email", modell.MatchSummary);
    }

    [Fact]
    public void Vorschau_und_echter_Lauf_liefern_denselben_Text()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());

        modell.InputText = "Kontakt: max.mustermann@beispiel.de, IBAN DE02120300000000202051.";
        modell.RefreshPreview();

        var vorschau = modell.ResultText;
        var echterLauf = modell.RunReal();

        Assert.Equal(vorschau, echterLauf);

        // Nicht nur zufaellig gleich, weil nichts ersetzt wurde: es muss
        // tatsaechlich etwas passiert sein.
        Assert.NotEqual(modell.InputText, echterLauf);
    }

    [Fact]
    public void Richtungswechsel_stellt_den_Originaltext_wieder_her()
    {
        var sitzung = ErzeugeSitzung();
        var modell = ErzeugeViewModel(sitzung);

        var original = "Kontakt: max.mustermann@beispiel.de";
        modell.InputText = original;
        modell.RefreshPreview();
        var pseudonymisiert = modell.RunReal();

        Assert.NotEqual(original, pseudonymisiert);

        modell.IsReverse = true;
        modell.InputText = pseudonymisiert;
        modell.RefreshPreview();

        Assert.Equal(original, modell.ResultText);

        // Die Rueckuebersetzung laeuft ueber die Tabelle, nicht ueber
        // Textregeln -- keine einzeln abwaehlbare Fundstellenliste.
        Assert.Empty(modell.Matches);
    }

    [Fact]
    public void Abgewaehlter_Fund_bleibt_im_Ergebnis_Klartext()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());

        const string email = "max.mustermann@beispiel.de";
        const string iban = "DE02120300000000202051";
        modell.InputText = $"Kontakt: {email}, IBAN {iban}.";
        modell.RefreshPreview();

        Assert.Equal(2, modell.Matches.Count);
        var emailFund = modell.Matches.Single(m => m.RuleName == "email");
        var ibanFund = modell.Matches.Single(m => m.RuleName == "iban");
        Assert.True(emailFund.CanToggle);
        Assert.True(ibanFund.CanToggle);

        emailFund.IsIncluded = false;

        Assert.Contains(email, modell.ResultText);
        Assert.DoesNotContain(iban, modell.ResultText);
        Assert.Contains(ibanFund.Replacement, modell.ResultText);
    }

    [Fact]
    public void Kopieren_holt_eine_ausstehende_Entprellung_ein()
    {
        // Wer Text einfuegt und sofort auf "Kopieren" klickt, darf nicht den
        // Stand von davor bekommen: RunReal bricht die Entprellung ab und
        // rechnet die Vorschau neu, bevor es den echten Lauf ausloest.
        var modell = new TextViewModel(ErzeugeSitzung(), debounceDelay: TimeSpan.FromSeconds(30));

        const string email = "max.mustermann@beispiel.de";
        modell.InputText = $"Kontakt: {email}";

        // Die Entprellung laeuft noch, es gibt also noch kein Ergebnis.
        Assert.Equal("", modell.ResultText);

        var kopiert = modell.RunReal();

        Assert.DoesNotContain(email, kopiert);
        Assert.Contains("Kontakt: ", kopiert, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_Haekchen_nach_geaendertem_Text_wirft_nicht()
    {
        // Die Funde tragen Positionen im Text des Laufs. Wird waehrend der
        // Entprellung gekuerzt und in derselben Zeitspanne ein Haekchen
        // umgeschaltet, zeigten diese Positionen frueher hinter das Ende des
        // aktuellen Textes.
        var modell = new TextViewModel(ErzeugeSitzung(), debounceDelay: TimeSpan.FromSeconds(30));

        modell.InputText = "Kontakt: max.mustermann@beispiel.de, IBAN DE02120300000000202051.";
        modell.RefreshPreview();
        Assert.Equal(2, modell.Matches.Count);

        // Kuerzen, ohne die Vorschau nachziehen zu lassen.
        modell.InputText = "kurz";

        var fund = modell.Matches[0];
        fund.IsIncluded = false;

        // Der Ergebnistext gehoert weiterhin zum Lauf und bleibt in sich
        // stimmig -- entscheidend ist, dass hier nichts fliegt.
        Assert.Contains(fund.Original, modell.ResultText, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmittelbar_angrenzende_Funde_sperren_das_Abwaehlen()
    {
        // Grenzen zwei Funde ohne Zeichen dazwischen aneinander, ist nicht zu
        // entscheiden, wo der eine Ersatzwert endet und der naechste beginnt.
        // Dann muss die Zuordnung ausfallen -- eine geratene zerlegte beim
        // Abwaehlen den Text.
        var funde = new[]
        {
            new TextMatch(0, 3, "abc", new TextRule { Name = "eins" }),
            new TextMatch(3, 3, "def", new TextRule { Name = "zwei" }),
        };

        Assert.Null(TextViewModel.AlignReplacements("abcdef", "XXYY", funde));
    }

    [Fact]
    public void Ohne_Text_bleibt_das_Ergebnis_leer()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());

        modell.InputText = "";
        modell.RefreshPreview();

        Assert.Equal("", modell.ResultText);
        Assert.Empty(modell.Matches);
    }
}
