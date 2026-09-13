using Obfuskation.Gui.Views;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Die Aufschrift des Kontextmenue-Eintrags. Sie nennt den markierten Wert --
/// ein Eintrag, der nicht sagt, worauf er wirkt, war das Verstaendnisproblem
/// der Knopf-Fassung. Geprueft wird hier nur die reine Rechnung; dass sie am
/// richtigen Ereignis haengt, entscheidet die Ansicht.
/// </summary>
public class TextViewLabelTests
{
    [Theory]
    [InlineData("FW123456", "FW123456")]
    [InlineData("  Klenk-Brauer  ", "Klenk-Brauer")]
    public void Kurze_Markierungen_bleiben_unveraendert(string markierung, string erwartet)
        => Assert.Equal(erwartet, TextView.Shorten(markierung));

    [Fact]
    public void Lange_Markierungen_werden_mit_Auslassung_gekuerzt()
    {
        var lang = "Lindenallee 47b, 40217 Düsseldorf, Deutschland";

        var gekuerzt = TextView.Shorten(lang);

        Assert.True(gekuerzt.Length <= 30);
        Assert.EndsWith("…", gekuerzt, StringComparison.Ordinal);
        Assert.StartsWith("Lindenallee 47b", gekuerzt, StringComparison.Ordinal);
    }

    [Fact]
    public void Mehrzeilige_Markierungen_werden_zu_einer_Zeile()
    {
        // Ein Menueeintrag mit Zeilenumbruch zerreisst das Menue.
        var gekuerzt = TextView.Shorten("Lindenallee 47b\n40217 Düsseldorf");

        Assert.DoesNotContain('\n', gekuerzt);
        Assert.DoesNotContain('\r', gekuerzt);
    }
}
