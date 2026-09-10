using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Aus einem markierten Beispielwert ein Muster ableiten. Die Muster landen
/// unveraendert in einer <see cref="TextRule"/> und wirken damit auf echte
/// Daten -- gepruefte wird deshalb nicht nur die Zeichenkette, sondern auch,
/// was das entstandene Muster trifft und was nicht.
/// </summary>
public class PatternFromSampleTests
{
    [Fact]
    public void Ein_Hostname_ergibt_Kennung_plus_Ziffernzahl()
    {
        var muster = PatternFromSample.Shape("FW123456");

        Assert.NotNull(muster);
        Assert.Equal(@"\bFW\d{6}\b", muster.Pattern);
        Assert.Equal("„FW“ + 6 Ziffern", muster.Description);
    }

    [Fact]
    public void Die_Form_trifft_gleichartige_Werte_und_sonst_nichts()
    {
        var muster = PatternFromSample.Shape("FW123456")!;

        Assert.Matches(muster.Pattern, "Auf FW987654 liegt die Regel.");

        // Eine Stelle zu wenig, eine zu viel, und ein anderer Praefix duerfen
        // nicht mitgefangen werden -- ein zu weit gefasstes Muster beschaedigt
        // die Testdaten, ohne dass es beim Betrachten auffiele.
        Assert.DoesNotMatch(muster.Pattern, "FW12345");
        Assert.DoesNotMatch(muster.Pattern, "FW1234567");
        Assert.DoesNotMatch(muster.Pattern, "SW123456");
    }

    [Fact]
    public void Woertlich_trifft_nur_genau_diesen_Wert()
    {
        var muster = PatternFromSample.Literal("FW123456");

        Assert.Matches(muster.Pattern, "Host FW123456.");
        Assert.DoesNotMatch(muster.Pattern, "Host FW987654.");
    }

    [Fact]
    public void Sonderzeichen_werden_maskiert()
    {
        // Ohne Maskierung waere der Punkt ein beliebiges Zeichen und die
        // Klammern eine Gruppe -- das Muster traefe dann weit mehr als gemeint.
        var muster = PatternFromSample.Literal("Meier (a.b)");

        Assert.Matches(muster.Pattern, "von Meier (a.b) gemeldet");
        Assert.DoesNotMatch(muster.Pattern, "von Meier (axb) gemeldet");
    }

    [Fact]
    public void Mehrere_Ziffernlaeufe_bleiben_getrennt()
    {
        var muster = PatternFromSample.Shape("AB-12/34");

        Assert.NotNull(muster);
        Assert.Equal(@"\bAB-\d{2}/\d{2}\b", muster.Pattern);
        Assert.Equal("„AB-“ + 2 Ziffern + „/“ + 2 Ziffern", muster.Description);
    }

    [Fact]
    public void Ohne_Ziffern_gibt_es_keine_eigene_Form()
    {
        // "Mustermann" hat keine Form, die ueber den Wert hinausginge; die
        // Oberflaeche soll dann keine zweite Moeglichkeit anbieten, die
        // dasselbe tut wie die woertliche.
        Assert.Null(PatternFromSample.Shape("Mustermann"));
    }

    [Fact]
    public void Vor_einem_Nicht_Wortzeichen_steht_keine_Wortgrenze()
    {
        // '\b' vor '+' bedeutet etwas anderes, als man erwartet: die Grenze
        // verlangte dann links ein Wortzeichen, und das Muster fiele
        // stillschweigend aus.
        var muster = PatternFromSample.Shape("+49301234")!;

        Assert.StartsWith(@"\+", muster.Pattern, StringComparison.Ordinal);
        Assert.Matches(muster.Pattern, "Rufnummer +49301234 anrufen");
    }

    [Fact]
    public void Reine_Ziffern_ergeben_ein_Muster_ohne_woertlichen_Teil()
    {
        var muster = PatternFromSample.Shape("0815")!;

        Assert.Equal(@"\b\d{4}\b", muster.Pattern);
        Assert.Equal("4 Ziffern", muster.Description);
    }

    [Fact]
    public void Ein_leerer_Beispielwert_wird_abgewiesen()
    {
        Assert.Throws<ArgumentException>(() => PatternFromSample.Literal("   "));
        Assert.Throws<ArgumentException>(() => PatternFromSample.Shape(""));
    }

    [Fact]
    public void Das_Praefix_stammt_aus_der_Kennung()
    {
        Assert.Equal("FW~", PatternFromSample.SuggestPrefix("FW123456"));
        Assert.Equal("INV~", PatternFromSample.SuggestPrefix("INV-2024-0815"));
    }

    [Fact]
    public void Eine_reine_Nummer_traegt_kein_Praefix()
    {
        // Eine Zahl hat keinen Namen, den man voranstellen koennte -- dann
        // lieber gar kein Praefix als ein erfundenes.
        Assert.Null(PatternFromSample.SuggestPrefix("2024-0815"));
    }

    [Fact]
    public void Das_Praefix_haelt_den_erlaubten_Zeichenvorrat_ein()
    {
        var prefix = PatternFromSample.SuggestPrefix("Wärme-Zähler 12")!;

        // Derselbe Vorrat, den ProfileValidator durchsetzt: Buchstaben,
        // Ziffern, Unterstrich, Bindestrich, Abschluss mit '~'.
        Assert.Matches(@"^[A-Za-z0-9ÄÖÜäöüß_-]+~$", prefix);
        Assert.True(prefix.Length <= 32);
    }

    [Fact]
    public void Der_Regelname_ist_ein_kleingeschriebener_Stamm()
    {
        Assert.Equal("fw", PatternFromSample.SuggestRuleName("FW123456"));
        Assert.Equal("begriff", PatternFromSample.SuggestRuleName("2024-0815"));
    }
}
