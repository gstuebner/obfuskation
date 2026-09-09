using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Wertetreffer sollen streng bleiben: nur ein vollstaendiger Treffer auf
/// mehrere Beispielwerte zaehlt, sonst gibt es keinen Vorschlag.
/// </summary>
public class ValueSuggesterTests
{
    private static ExtensionLibrary AssetTagExtensions() => new()
    {
        Generators = { ["assetTag"] = new GeneratorSettings { Type = "pattern", Pattern = "INV999999" } },
        TextRules =
        [
            new TextRule { Name = "assetTag", Priority = 95, Pattern = @"\bINV\d{6}\b", Generator = "assetTag" },
        ],
    };

    private static Dictionary<string, IReadOnlyList<string>> Samples(string feld, params string[] werte)
        => new(StringComparer.OrdinalIgnoreCase) { [feld] = werte };

    [Fact]
    public void Zwei_passende_Inventarnummern_ergeben_einen_Vorschlag()
    {
        var samples = Samples("Zielsystem", "INV123456", "INV654321");

        var vorschlaege = ValueSuggester.Suggest(samples, AssetTagExtensions());

        var vorschlag = Assert.Single(vorschlaege);
        Assert.Equal("Zielsystem", vorschlag.FieldName);
        Assert.Equal("assetTag", vorschlag.Generator);
        Assert.Equal(2, vorschlag.MatchedSamples);
        Assert.Equal(2, vorschlag.TotalSamples);
        Assert.Contains(vorschlag.Evidence, new[] { "INV123456", "INV654321" });
    }

    [Fact]
    public void Ein_Teiltreffer_erzeugt_keinen_Vorschlag()
    {
        var samples = Samples("Notiz", "Neustart von INV123456 am Montag", "INV654321");

        Assert.Empty(ValueSuggester.Suggest(samples, AssetTagExtensions()));
    }

    [Fact]
    public void Ein_einziger_Beispielwert_erzeugt_keinen_Vorschlag()
    {
        var samples = Samples("Zielsystem", "INV123456");

        Assert.Empty(ValueSuggester.Suggest(samples, AssetTagExtensions()));
    }

    [Fact]
    public void Leerwerte_verhindern_keinen_Vorschlag()
    {
        var samples = Samples("Zielsystem", "INV123456", "INV654321", "", "   ");

        var vorschlag = Assert.Single(ValueSuggester.Suggest(samples, AssetTagExtensions()));

        Assert.Equal("assetTag", vorschlag.Generator);
        Assert.Equal(2, vorschlag.MatchedSamples);
        Assert.Equal(4, vorschlag.TotalSamples);
    }

    [Fact]
    public void Ohne_Erweiterungsdatei_schlagen_nur_die_eingebauten_Standardmuster_an()
    {
        var samples = Samples("IBAN", "DE02120300000000202051", "DE02500105170137075030");

        var vorschlag = Assert.Single(ValueSuggester.Suggest(samples, ExtensionLibrary.Empty));

        Assert.Equal("iban", vorschlag.Generator);
    }
}
