using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die deutschen Erklärungen zu den Generatoren. Ein Generator ohne Erklärung
/// erscheint in der Oberfläche als nacktes englisches Wort — das soll nicht
/// unbemerkt passieren, wenn später einer dazukommt.
/// </summary>
public class GeneratorDescriptionTests
{
    [Fact]
    public void Jeder_eingebaute_Generator_hat_eine_Erklaerung()
    {
        var ohne = GeneratorRegistry.KnownNames
            .Where(name => string.IsNullOrWhiteSpace(GeneratorDescriptions.For(name)))
            .ToList();

        Assert.True(ohne.Count == 0,
            "Ohne deutsche Erklaerung: " + string.Join(", ", ohne));
    }

    [Theory]
    [InlineData("street", "Straßenname mit Hausnummer")]
    [InlineData("token", "allgemeine Kennung (TOK_…), mit Kennzeichnung davor")]
    [InlineData("dateShift", "Datum, um festen Betrag verschoben")]
    public void Die_Erklaerung_trifft_die_Sache(string generator, string erwartet)
        => Assert.Equal(erwartet, GeneratorDescriptions.For(generator));

    [Fact]
    public void Ein_unbekannter_Name_liefert_keine_erfundene_Erklaerung()
    {
        Assert.Equal("", GeneratorDescriptions.For("gibtsNicht"));
        Assert.Equal("gibtsNicht", GeneratorDescriptions.Label("gibtsNicht"));
    }

    [Fact]
    public void Name_und_Erklaerung_stehen_in_einer_Zeile()
        => Assert.Equal("street — Straßenname mit Hausnummer", GeneratorDescriptions.Label("street"));
}
