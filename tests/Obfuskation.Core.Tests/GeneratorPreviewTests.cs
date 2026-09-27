using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// <see cref="GeneratorPreview"/>: das Beispiel im Generator-Dialog (Plan P3),
/// bevor der Entwurf irgendwo gespeichert ist.
/// </summary>
public class GeneratorPreviewTests
{
    private static SeedDeriver Deriver() => new(new byte[32]);

    [Fact]
    public void Ein_Token_mit_Praefix_liefert_einen_Wert_mit_diesem_Praefix()
    {
        var settings = new GeneratorSettings { Type = "token", Prefix = "FW~" };

        var erfolg = GeneratorPreview.TryExample("fw", settings, "Beispiel 4711", Deriver(), out var example, out var error);

        Assert.True(erfolg);
        Assert.Null(error);
        Assert.StartsWith("FW~", example, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Werteliste_ohne_Werte_liefert_false_und_eine_Fehlermeldung()
    {
        var settings = new GeneratorSettings { Type = "wordlist" };

        var erfolg = GeneratorPreview.TryExample("liste", settings, "Beispiel 4711", Deriver(), out var example, out var error);

        Assert.False(erfolg);
        Assert.Equal("", example);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryExamples_liefert_bei_token_drei_verschiedene_Werte()
    {
        var settings = new GeneratorSettings { Type = "token" };

        var erfolg = GeneratorPreview.TryExamples(
            "fw", settings, "Beispiel 4711", Deriver(), count: 3, out var examples, out var error);

        Assert.True(erfolg);
        Assert.Null(error);
        Assert.Equal(3, examples.Count);
        Assert.Equal(examples.Count, examples.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TryExamples_liefert_bei_redact_genau_einen_Wert()
    {
        var settings = new GeneratorSettings { Type = "redact" };

        var erfolg = GeneratorPreview.TryExamples(
            "geschwaerzt", settings, "Beispiel 4711", Deriver(), count: 3, out var examples, out var error);

        Assert.True(erfolg);
        Assert.Null(error);
        Assert.Single(examples);
    }
}
