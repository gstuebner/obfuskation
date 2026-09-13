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

    // ------------------------------------------------------------------
    // Markieren und ueber das Kontextmenue weiterreichen: der Weg fuer alles,
    // was die vier eingebauten Regeln nicht sehen.
    // ------------------------------------------------------------------

    [Fact]
    public void Ein_leerer_Wert_bleibt_wirkungslos()
    {
        // Die Ansicht sperrt den Menueeintrag schon ohne Markierung; dass hier
        // trotzdem nichts durchkommt, ist die zweite Sicherung. Der leere
        // Dialog dahinter war die Absturzstelle der Knopf-Fassung.
        var gerufen = false;
        var modell = new TextViewModel(
            ErzeugeSitzung(), debounceDelay: TimeSpan.Zero,
            onAlwaysReplaceRequested: _ => gerufen = true);

        modell.RequestAlwaysReplace("   ");

        Assert.False(gerufen);
    }

    [Fact]
    public void Nur_selbst_angelegte_Regeln_lassen_sich_aus_der_Fundliste_entfernen()
    {
        // Die vier eingebauten bleiben unantastbar: fuer sie ist das Haekchen
        // der richtige Weg, denn es gilt nur fuer diesen einen Durchgang.
        var sitzung = ErzeugeSitzung();
        sitzung.Profile.TextRules.Add(new TextRule
        {
            Name = "hostname",
            Priority = 60,
            Generator = "token",
            Pattern = @"\bFW\d{6}\b",
        });

        var modell = ErzeugeViewModel(sitzung);
        modell.InputText = "Server FW123456, Kontakt max.mustermann@beispiel.de";
        modell.RefreshPreview();

        var eigener = Assert.Single(modell.Matches, fund => fund.RuleName == "hostname");
        var eingebauter = Assert.Single(modell.Matches, fund => fund.RuleName == "email");

        Assert.True(eigener.IsUserRule);
        Assert.False(eingebauter.IsUserRule);
    }

    [Fact]
    public void Der_Rueckweg_meldet_den_Namen_der_zu_loeschenden_Regel()
    {
        var sitzung = ErzeugeSitzung();
        sitzung.Profile.TextRules.Add(new TextRule
        {
            Name = "hostname",
            Priority = 60,
            Generator = "token",
            Pattern = @"\bFW\d{6}\b",
        });

        string? zuLoeschen = null;
        var modell = new TextViewModel(
            sitzung, debounceDelay: TimeSpan.Zero,
            onRemoveRuleRequested: name => zuLoeschen = name);

        modell.InputText = "Server FW123456";
        modell.RefreshPreview();

        Assert.Single(modell.Matches).RemoveRuleCommand.Execute(null);

        Assert.Equal("hostname", zuLoeschen);
    }

    // ------------------------------------------------------------------
    // Der gemeldete Vermerk: was die eingebaute Erkennung sieht -- und vor
    // allem, was nicht. Genau diese Luecke ist der Grund, warum es das
    // Markieren von Hand ueberhaupt gibt.
    // ------------------------------------------------------------------

    private const string Vermerk = """
        Vertraulicher Vermerk – Vorgang: VK-2026-88194
        Bearbeiter: Markus Wellenbrink (Personalabteilung, Durchwahl -412)

        Akte von Frau Dr. Susanne E. Klenk-Brauer (geboren am 18.04.1981 in
        Wuppertal, Steuer-ID: 48 291 038 517, Sozialversicherungsnummer:
        65 180481 K 042).

        Meldeadresse: Lindenallee 47b, 40217 Düsseldorf
        Telefon privat: +49 211 98412-88
        E-Mail privat: susanne.klenk-brauer@altus-mail.de
        Neue Anschrift: Kranichweg 12, 60599 Frankfurt am Main
        Mobiltelefon: +49 171 4492810

        Altes Gehaltskonto (IBAN: DE89 3005 0000 0123 4567 89, BIC: DUSSDE33XXX).
        Neues Konto: IBAN DE42 5009 0000 9876 5432 10, BIC FFVBDEFFXXX,
        Kontoinhaber: Dr. Susanne Klenk-Brauer / Thorsten Brauer (geb. 03.11.1979).

        Firmenwagen Kennzeichen D-TK 8841, VIN WBA1A51080E912345,
        DKV-Kartennummer 7043 1109 4432 8819 über 142,80 EUR.
        """;

    [Fact]
    public void Der_Vermerk_verliert_Bankdaten_und_Kontaktwege()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());
        modell.InputText = Vermerk;
        modell.RefreshPreview();

        foreach (var wert in new[]
                 {
                     "DE89 3005 0000 0123 4567 89", "DE42 5009 0000 9876 5432 10",
                     "DUSSDE33XXX", "FFVBDEFFXXX",
                     "susanne.klenk-brauer@altus-mail.de",
                     "+49 211 98412-88", "+49 171 4492810",
                 })
        {
            Assert.DoesNotContain(wert, modell.ResultText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Der_Vermerk_behaelt_alles_Uebrige_im_Klartext()
    {
        // Kein Mangel dieses Tests, sondern seine Aussage: Namen, Anschriften,
        // Kennungen und Geburtsdaten faengt kein allgemeines Muster. Sie
        // bleiben stehen, bis jemand sie markiert -- deshalb muss die Ansicht
        // zeigen, was farblos geblieben ist.
        var modell = ErzeugeViewModel(ErzeugeSitzung());
        modell.InputText = Vermerk;
        modell.RefreshPreview();

        foreach (var wert in new[]
                 {
                     "Susanne E. Klenk-Brauer", "Markus Wellenbrink", "Thorsten Brauer",
                     "Lindenallee 47b", "40217 Düsseldorf", "Kranichweg 12",
                     "18.04.1981", "03.11.1979",
                     "48 291 038 517", "65 180481 K 042",
                     "D-TK 8841", "WBA1A51080E912345", "7043 1109 4432 8819",
                     "VK-2026-88194",
                 })
        {
            Assert.Contains(wert, modell.ResultText, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // Hervorhebung
    // ------------------------------------------------------------------

    [Fact]
    public void Die_Abschnitte_ergeben_wieder_genau_ihre_Seite()
    {
        // Die Invariante, auf die sich die Anzeige verlaesst: gingen Abschnitte
        // und Text auseinander, stuende die Farbe neben dem Wort.
        var modell = ErzeugeViewModel(ErzeugeSitzung());
        modell.InputText = Vermerk;
        modell.RefreshPreview();

        Assert.Equal(Vermerk, string.Concat(modell.InputSegments.Select(a => a.Text)));
        Assert.Equal(modell.ResultText, string.Concat(modell.ResultSegments.Select(a => a.Text)));
    }

    [Fact]
    public void Ein_Fund_ist_links_wie_rechts_hervorgehoben()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());
        modell.InputText = "Kontakt: max.mustermann@beispiel.de";
        modell.RefreshPreview();

        var fund = Assert.Single(modell.Matches);

        var links = Assert.Single(modell.InputSegments, a => a.Kind != TextSegmentKind.Normal);
        var rechts = Assert.Single(modell.ResultSegments, a => a.Kind != TextSegmentKind.Normal);

        Assert.Equal(TextSegmentKind.Replaced, links.Kind);
        Assert.Equal("max.mustermann@beispiel.de", links.Text);
        Assert.Equal(TextSegmentKind.Replaced, rechts.Kind);
        Assert.Equal(fund.Replacement, rechts.Text);
    }

    [Fact]
    public void Ein_abgewaehlter_Fund_wechselt_auf_beiden_Seiten_die_Farbe()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung());
        modell.InputText = "Kontakt: max.mustermann@beispiel.de";
        modell.RefreshPreview();

        modell.Matches[0].IsIncluded = false;

        var links = Assert.Single(modell.InputSegments, a => a.Kind != TextSegmentKind.Normal);
        var rechts = Assert.Single(modell.ResultSegments, a => a.Kind != TextSegmentKind.Normal);

        Assert.Equal(TextSegmentKind.Excluded, links.Kind);
        Assert.Equal(TextSegmentKind.Excluded, rechts.Kind);

        // Rechts steht jetzt wieder das Original -- es geht im Klartext hinaus,
        // und die Farbe sagt genau das.
        Assert.Equal("max.mustermann@beispiel.de", rechts.Text);
    }

    [Fact]
    public void Die_Rueckuebersetzung_kennt_keine_Hervorhebung()
    {
        var modell = ErzeugeViewModel(ErzeugeSitzung(), TextDirection.Reverse);
        modell.InputText = "Antwort ohne bekannte Pseudonyme.";
        modell.RefreshPreview();

        Assert.All(modell.InputSegments, a => Assert.Equal(TextSegmentKind.Normal, a.Kind));
        Assert.All(modell.ResultSegments, a => Assert.Equal(TextSegmentKind.Normal, a.Kind));
    }

    // ------------------------------------------------------------------
    // Umschalter Prüfen/Bearbeiten
    // ------------------------------------------------------------------

    [Fact]
    public void Ohne_Text_laesst_sich_geschrieben_werden()
    {
        // Vor einer leeren, nicht beschreibbaren Flaeche zu stehen waere das
        // denkbar schlechteste Willkommen.
        var modell = ErzeugeViewModel(ErzeugeSitzung());

        Assert.True(modell.IsEditing);
        Assert.Equal("Fertig", modell.EditToggleLabel);
    }

    [Fact]
    public void Eingefuegter_Text_steht_sofort_geprueft_da()
    {
        // Ohne Entprellung: hier hat niemand getippt, auf dessen naechsten
        // Tastenschlag zu warten waere.
        var modell = new TextViewModel(ErzeugeSitzung(), debounceDelay: TimeSpan.FromSeconds(30));

        modell.SetInputFromOutside("Kontakt: max.mustermann@beispiel.de");

        Assert.False(modell.IsEditing);
        Assert.Equal("Bearbeiten", modell.EditToggleLabel);
        Assert.Single(modell.Matches);
        Assert.NotEmpty(modell.InputSegments);
    }

    [Fact]
    public void Fertig_holt_eine_ausstehende_Entprellung_ein()
    {
        // Sonst gehoerten die Farben zum Stand vor der letzten Aenderung,
        // waehrend daneben schon der neue Text steht.
        var modell = new TextViewModel(ErzeugeSitzung(), debounceDelay: TimeSpan.FromSeconds(30));

        modell.SetInputFromOutside("noch nichts drin");
        modell.ToggleEditing();
        Assert.True(modell.IsEditing);

        modell.InputText = "Kontakt: max.mustermann@beispiel.de";
        Assert.Empty(modell.Matches);

        modell.ToggleEditing();

        Assert.False(modell.IsEditing);
        Assert.Single(modell.Matches);
        Assert.Equal(modell.InputText, string.Concat(modell.InputSegments.Select(a => a.Text)));
    }
}
