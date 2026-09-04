using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Muster, die <c>init</c> vorgibt. Ein zu weit gefasstes Muster ersetzt
/// harmlose Werte und beschaedigt damit die Testdaten; ein zu enges laesst
/// Echtdaten stehen. Beide Richtungen werden hier geprueft.
/// </summary>
public class DefaultTextRuleTests
{
    private static IReadOnlyList<TextMatch> Suchen(string text)
        => new TextRuleEngine().FindMatches(text, ProfileScaffolder.DefaultTextRules());

    [Theory]
    [InlineData("Konto DE02120300000000202051 belastet", "iban")]
    [InlineData("Bitte an max.mustermann@beispiel.de senden", "email")]
    [InlineData("Rueckfragen unter +49 30 12345678", "phone")]
    [InlineData("Tel. 030/1234-5678", "phone")]
    public void Erwartete_Muster_werden_gefunden(string text, string regelName)
    {
        var treffer = Suchen(text);
        Assert.Contains(treffer, t => t.Rule.Name == regelName);
    }

    [Theory]
    [InlineData("Rechnung 2024-0815 vom Vormonat")]
    [InlineData("Betrag 1234,56 EUR")]
    [InlineData("Buchung am 15.03.1980")]
    [InlineData("Bestellnummer 4711")]
    public void Harmlose_Werte_bleiben_unberuehrt(string text)
    {
        // Fehltreffer sind hier keine Kleinigkeit: sie beschaedigen die
        // Testdaten und untergraben das Vertrauen in die Ausgabe.
        Assert.Empty(Suchen(text));
    }

    [Fact]
    public void Eine_Rechnungsnummer_neben_einer_Telefonnummer_bleibt_stehen()
    {
        // Der Fall, an dem ein Muster ohne Blick nach hinten scheitert: es
        // faengt mitten in "2024-0815" an und erklaert den Rest zur Nummer.
        var treffer = Suchen("Rechnung 2024-0815, Rueckfragen unter +49 30 12345678");

        Assert.Single(treffer);
        Assert.Equal("phone", treffer[0].Rule.Name);
        Assert.Equal("+49 30 12345678", treffer[0].Value);
    }

    [Fact]
    public void Bei_einer_Iban_gewinnt_die_Iban_Regel_gegen_die_Telefonregel()
    {
        // In einer IBAN steckt eine lange Ziffernfolge. Die hoehere Prioritaet
        // der IBAN-Regel muss den Zuschlag bekommen.
        var treffer = Suchen("Konto DE02120300000000202051");

        Assert.Single(treffer);
        Assert.Equal("iban", treffer[0].Rule.Name);
    }

    [Fact]
    public void Die_Standardmuster_sind_gueltige_Ausdruecke()
    {
        foreach (var rule in ProfileScaffolder.DefaultTextRules())
        {
            var ex = Record.Exception(() => new System.Text.RegularExpressions.Regex(rule.Pattern));
            Assert.Null(ex);
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(string.IsNullOrWhiteSpace(rule.Generator));
        }
    }
}
