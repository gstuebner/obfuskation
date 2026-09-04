using Obfuskation.Core.Configuration;
using Obfuskation.Core.Detection;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die beiden Fallen der Textersetzung: Kettenersetzung beim Hinweg und die
/// Teilzeichenfolge beim Rueckweg. Beide zerstoeren Daten still, wenn sie nicht
/// behandelt werden.
/// </summary>
public class DetectionTests
{
    [Fact]
    public void Eine_Regel_greift_nicht_in_dem_was_eine_andere_gerade_einsetzte()
    {
        var engine = new TextRuleEngine();

        // Regel A ersetzt "alpha" durch etwas, das auf Regel B passen wuerde.
        var regeln = new List<TextRule>
        {
            new() { Name = "a", Priority = 100, Pattern = "alpha", Generator = "token" },
            new() { Name = "b", Priority = 90, Pattern = "beta", Generator = "token" },
        };

        var ergebnis = engine.Replace("alpha und gamma", regeln, match => "beta", out var anzahl);

        // Genau ein Treffer, und das eingesetzte "beta" bleibt unangetastet.
        Assert.Equal(1, anzahl);
        Assert.Equal("beta und gamma", ergebnis);
    }

    [Fact]
    public void Bei_ueberlappenden_Treffern_gewinnt_die_hoehere_Prioritaet()
    {
        var engine = new TextRuleEngine();

        var regeln = new List<TextRule>
        {
            new() { Name = "kurz", Priority = 10, Pattern = @"DE\d{2}", Generator = "token" },
            new() { Name = "lang", Priority = 100, Pattern = @"DE\d{20}", Generator = "token" },
        };

        var treffer = engine.FindMatches("DE02120300000000202051", regeln);

        Assert.Single(treffer);
        Assert.Equal("lang", treffer[0].Rule.Name);
    }

    [Fact]
    public void Bei_gleicher_Prioritaet_gewinnt_der_laengere_Treffer()
    {
        var engine = new TextRuleEngine();

        var regeln = new List<TextRule>
        {
            new() { Name = "kurz", Priority = 50, Pattern = @"AB\d{2}", Generator = "token" },
            new() { Name = "lang", Priority = 50, Pattern = @"AB\d{4}", Generator = "token" },
        };

        var treffer = engine.FindMatches("AB1234", regeln);

        Assert.Single(treffer);
        Assert.Equal(6, treffer[0].Length);
    }

    [Fact]
    public void Die_Rueckabbildung_verwechselt_kein_Pseudonym_mit_einem_laengeren()
    {
        // Die Teilzeichenfolgen-Falle: "Meier" steckt in "Meiersen".
        using var setup = new TestProfile();
        using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName);

        store.Add("personName", "Original Kurz", "Meier");
        store.Add("personName", "Original Lang", "Meiersen");
        store.Save();

        var generators = GeneratorRegistry.Build(setup.Profile, new SeedDeriver(store.Salt));
        var mapper = new ReverseTextMapper(store, generators);

        var ergebnis = mapper.Restore("Herr Meiersen und Frau Meier kamen.", out var anzahl);

        Assert.Equal(2, anzahl);
        Assert.Equal("Herr Original Lang und Frau Original Kurz kamen.", ergebnis);
    }

    [Fact]
    public void Wortartige_Pseudonyme_greifen_nicht_innerhalb_eines_Wortes()
    {
        using var setup = new TestProfile();
        using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName);

        store.Add("personName", "Echtname", "Berg");
        store.Save();

        var generators = GeneratorRegistry.Build(setup.Profile, new SeedDeriver(store.Salt));
        var mapper = new ReverseTextMapper(store, generators);

        // "Bergstrasse" darf nicht zu "Echtnamestrasse" werden.
        var ergebnis = mapper.Restore("Berg wohnt in der Bergstrasse.", out var anzahl);

        Assert.Equal(1, anzahl);
        Assert.Equal("Echtname wohnt in der Bergstrasse.", ergebnis);
    }

    [Fact]
    public void Strukturierte_Pseudonyme_brauchen_keine_Wortgrenze()
    {
        using var setup = new TestProfile();
        using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName);

        // Bei einer IBAN darf die Ersetzung auch direkt an einem Satzzeichen greifen.
        store.Add("iban", "DE02120300000000202051", "DE41500105177382719473");
        store.Save();

        var generators = GeneratorRegistry.Build(setup.Profile, new SeedDeriver(store.Salt));
        var mapper = new ReverseTextMapper(store, generators);

        var ergebnis = mapper.Restore("Konto:DE41500105177382719473.", out var anzahl);

        Assert.Equal(1, anzahl);
        Assert.Equal("Konto:DE02120300000000202051.", ergebnis);
    }

    [Theory]
    [InlineData("$.kunden[*].iban", "$.kunden[3].iban", true)]
    [InlineData("$.kunden[*].iban", "$.kunden[0].name", false)]
    [InlineData("$.kunden[*].iban", "$.lieferanten[0].iban", false)]
    [InlineData("$.*.iban", "$.kunden.iban", true)]
    [InlineData("$.a.b", "$.a.b.c", false)]
    public void Json_Pfade_werden_abschnittsweise_verglichen(string muster, string pfad, bool erwartet)
        => Assert.Equal(erwartet, JsonPathMatcher.Matches(muster, pfad));
}
