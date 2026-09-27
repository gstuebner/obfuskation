using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Tests;

/// <summary>
/// <see cref="GeneratorExpression"/> und <see cref="ExpressionGenerator"/>:
/// der RegEx-aehnliche Ausdruck zum Erzeugen (nicht Suchen) zufaelliger Werte
/// (siehe docs/plan-ausdruck-generator.md).
/// </summary>
public class ExpressionGeneratorTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> KeineTabellen =
        new Dictionary<string, IReadOnlyList<string>>();

    private static byte[] Seed(string text, int counter = 0) => new SeedDeriver(new byte[32]).Derive("expression", text, counter);

    private static string Generate(string ausdruck, IReadOnlyDictionary<string, IReadOnlyList<string>> tabellen, byte[] seed)
    {
        Assert.True(GeneratorExpression.TryParse(ausdruck, out var expression, out var fehler),
            $"Ausdruck sollte parsen, Fehler: {fehler}");
        var reader = new SeedReader(seed);
        return expression!.Generate(ref reader, tabellen);
    }

    // ------------------------------------------------------------------ KFZ

    [Fact]
    public void Ein_KFZ_Ausdruck_mit_kurzer_Tabelle_passt_immer_auf_das_Muster()
    {
        var tabellen = new Dictionary<string, IReadOnlyList<string>> { ["kreis"] = ["B", "HH", "M"] };
        Assert.True(GeneratorExpression.TryParse(@"{kreis}-[A-Z]{2} \d{2,3}E?", out var expression, out _));

        var regel = new Regex(@"^(B|HH|M)-[A-Z]{2} \d{2,3}E?$");
        var sahZweiZiffern = false;
        var sahDreiZiffern = false;
        var sahE = false;

        for (var i = 0; i < 200; i++)
        {
            var reader = new SeedReader(Seed("Kennzeichen", i));
            var wert = expression!.Generate(ref reader, tabellen);

            Assert.Matches(regel, wert);

            var ziffernteil = wert.Split(' ')[1].TrimEnd('E');
            if (ziffernteil.Length == 2) sahZweiZiffern = true;
            if (ziffernteil.Length == 3) sahDreiZiffern = true;
            if (wert.EndsWith('E')) sahE = true;
        }

        Assert.True(sahZweiZiffern, "Zwei Ziffern kamen in 200 Versuchen nie vor.");
        Assert.True(sahDreiZiffern, "Drei Ziffern kamen in 200 Versuchen nie vor.");
        Assert.True(sahE, "\"E\" kam in 200 Versuchen nie vor.");
    }

    [Fact]
    public void Derselbe_Seed_ergibt_denselben_Wert()
    {
        var tabellen = new Dictionary<string, IReadOnlyList<string>> { ["kreis"] = ["B", "HH", "M"] };
        Assert.True(GeneratorExpression.TryParse(@"{kreis}-[A-Z]{2} \d{2,3}E?", out var expression, out _));

        var seed = Seed("gleicherWert");
        var reader1 = new SeedReader(seed);
        var reader2 = new SeedReader(seed);

        Assert.Equal(expression!.Generate(ref reader1, tabellen), expression.Generate(ref reader2, tabellen));
    }

    // ---------------------------------------------------------- Schreibweisen

    [Fact]
    public void Ein_escaptes_Sonderzeichen_ist_woertlich()
    {
        Assert.Equal("a.b{c", Generate(@"a\.b\{c", KeineTabellen, Seed("x")));
    }

    [Fact]
    public void Eine_nicht_einfangende_Gruppe_wird_akzeptiert()
    {
        Assert.True(GeneratorExpression.TryParse("(?:a|b)", out var expression, out var fehler));
        Assert.Null(fehler);

        var reader = new SeedReader(Seed("nicht-einfangend"));
        var wert = expression!.Generate(ref reader, KeineTabellen);
        Assert.True(wert is "a" or "b");
    }

    [Fact]
    public void Zirkumflex_und_Dollar_am_Rand_werden_ignoriert()
    {
        Assert.Equal("abc", Generate(@"^abc$", KeineTabellen, Seed("y")));
    }

    [Fact]
    public void Umlaute_in_einer_Zeichenklasse_funktionieren()
    {
        Assert.True(GeneratorExpression.TryParse("[ÄÖÜ]", out var expression, out _));

        var reader = new SeedReader(Seed("umlaut"));
        var wert = expression!.Generate(ref reader, KeineTabellen);
        Assert.Contains(wert, new[] { "Ä", "Ö", "Ü" });
    }

    // ------------------------------------------------------------ Parserfehler

    [Theory]
    [InlineData("[A-Z", 1)]
    [InlineData("(ab", 1)]
    [InlineData("ab)", 3)]
    [InlineData("a{2,}", 2)]
    [InlineData("a{3,2}", 2)]
    [InlineData("a{0}", 2)]
    [InlineData("a*", 2)]
    [InlineData("a+", 2)]
    [InlineData(".", 1)]
    [InlineData("[^a]", 2)]
    [InlineData("[Z-A]", 2)]
    [InlineData(@"\q", 1)]
    [InlineData("{2}", 1)]
    [InlineData("a{2,65}", 2)]
    [InlineData("x^", 2)]
    [InlineData("$x", 1)]
    [InlineData("[a😀]", 3)]
    [InlineData("a{٣}", 2)]
    public void Ein_fehlerhafter_Ausdruck_wird_mit_Stelle_gemeldet(string ausdruck, int stelle)
    {
        var erfolg = GeneratorExpression.TryParse(ausdruck, out var expression, out var fehler);

        Assert.False(erfolg);
        Assert.Null(expression);
        Assert.NotNull(fehler);
        Assert.Equal(stelle, fehler!.Position);
        Assert.False(string.IsNullOrWhiteSpace(fehler.Message));
    }

    [Fact]
    public void Ein_Bereich_bis_zum_letzten_Zeichen_endet_trotzdem()
    {
        // Mit char gezaehlt liefe die Schleife nach U+FFFF auf 0 ueber und
        // kaeme nie zum Ende.
        Assert.True(GeneratorExpression.TryParse("[\uFFF0-\uFFFF]", out var expression, out _));

        Assert.Equal(16, expression!.ValuePool(KeineTabellen, cap: 1000));
    }

    [Fact]
    public void Ein_leerer_Ausdruck_ist_ein_Fehler()
    {
        Assert.False(GeneratorExpression.TryParse("", out _, out var fehler));
        Assert.NotNull(fehler);
    }

    // --------------------------------------------------------- Vorrat und Laenge

    [Fact]
    public void Zwei_Grossbuchstaben_und_drei_Ziffern_ergeben_676000_Werte()
    {
        Assert.True(GeneratorExpression.TryParse(@"[A-Z]{2}\d{3}", out var expression, out _));

        Assert.Equal(676_000, expression!.ValuePool(KeineTabellen, cap: 10_000_000));
    }

    [Fact]
    public void Ein_optionales_E_verdoppelt_den_Wertevorrat()
    {
        Assert.True(GeneratorExpression.TryParse(@"[A-Z]{2}\d{3}E?", out var expression, out _));

        Assert.Equal(1_352_000, expression!.ValuePool(KeineTabellen, cap: 10_000_000));
    }

    [Fact]
    public void Eine_Tabelle_zaehlt_ihre_Eintraege_als_Wertevorrat()
    {
        var tabellen = new Dictionary<string, IReadOnlyList<string>> { ["kreis"] = ["B", "HH", "M", "K"] };
        Assert.True(GeneratorExpression.TryParse("{kreis}", out var expression, out _));

        Assert.Equal(4, expression!.ValuePool(tabellen, cap: 1000));
    }

    [Fact]
    public void Tief_geschachtelte_Anzahlen_laufen_bei_der_Laenge_nicht_ueber()
    {
        // 64^6 passt nicht in int -- ohne Saettigung kaeme eine negative
        // Laenge heraus, und die Laengenpruefung liesse den Ausdruck durch.
        Assert.True(GeneratorExpression.TryParse("((((((a{64}){64}){64}){64}){64}){64})", out var expression, out _));

        Assert.Equal(int.MaxValue, expression!.MaxLength(KeineTabellen));
    }

    [Fact]
    public void Ein_Ausdruck_mit_zu_langen_Werten_erzeugt_nichts()
    {
        // Die Vorschau im Generator-Dialog laeuft vor jeder Pruefung; sie darf
        // an einem solchen Ausdruck nicht haengen bleiben.
        var settings = new GeneratorSettings { Type = "expression", Expression = "((a{64}){64}){64}" };

        var erfolg = GeneratorPreview.TryExample(
            "lang", settings, "x", new SeedDeriver(new byte[32]), out _, out var fehler);

        Assert.False(erfolg);
        Assert.Contains("höchstens 256", fehler);
    }

    [Fact]
    public void MinLength_und_MaxLength_von_optionalem_Zeichen_sind_0_und_1()
    {
        Assert.True(GeneratorExpression.TryParse("A?", out var expression, out _));

        Assert.Equal(0, expression!.MinLength(KeineTabellen));
        Assert.Equal(1, expression.MaxLength(KeineTabellen));
    }

    // -------------------------------------------------------------- Rundlauf

    [Fact]
    public void Eine_CSV_Spalte_mit_Ausdruck_Generator_laesst_sich_zurueckfuehren()
    {
        using var setup = new TestProfile(profile =>
        {
            profile.Generators["kfz"] = new GeneratorSettings
            {
                Type = "expression",
                Expression = @"{kreis}-[A-Z]{2} \d{2,3}",
                Tables = new Dictionary<string, List<string>> { ["kreis"] = ["B", "HH", "M"] },
            };
        }).WithField("Kennzeichen", FieldAction.Pseudonymize, "kfz");

        var engine = setup.CreateEngine();
        var original = TestProfile.Utf8("Kennzeichen\nM-XY 123\n");

        var pseudonymisiert = engine.Obfuscate(original, "a.csv", new RunOptions { Strict = true });
        var wiederhergestellt = engine.Deobfuscate(pseudonymisiert.Content, "a.csv", new RunOptions());

        Assert.Equal(original, wiederhergestellt.Content);
    }

    // ------------------------------------------------------------------ JSON

    [Fact]
    public void Tabellennamen_bleiben_nach_Speichern_und_Laden_unveraendert()
    {
        using var setup = new TestProfile();
        setup.Profile.Generators["kfz"] = new GeneratorSettings
        {
            Type = "expression",
            Expression = "{Kreis}-{kreis2}",
            Tables = new Dictionary<string, List<string>>
            {
                ["Kreis"] = ["B"],
                ["kreis2"] = ["M"],
            },
        };

        var pfad = Path.Combine(setup.Directory, "obfuskation-projekt.json");
        ProfileStore.Save(setup.Profile, pfad);
        var geladen = ProfileStore.Load(pfad);

        var tabellen = geladen.Generators["kfz"].Tables!;
        Assert.Contains("Kreis", tabellen.Keys);
        Assert.Contains("kreis2", tabellen.Keys);
    }
}
