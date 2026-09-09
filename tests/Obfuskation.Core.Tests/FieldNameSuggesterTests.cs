using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// <see cref="FieldNameSuggester"/> ersetzt das frueher fest einkompilierte
/// Namensraten (<c>ProfileScaffolder.Hints</c>) durch die Spaltenmuster der
/// Erweiterungsdatei (<see cref="ExtensionLibrary.FieldRules"/>). Kernpunkt
/// ist die Trefferregel "ganzer Feldname": ein Teiltreffer darf nicht mehr
/// zaehlen, das war der Fehler der alten Liste.
/// </summary>
public class FieldNameSuggesterTests
{
    private static ExtensionLibrary Erweiterung(params FieldNameRule[] regeln) => new() { FieldRules = [.. regeln] };

    [Fact]
    public void Ohne_Erweiterungsdatei_gibt_es_keinen_Vorschlag()
    {
        Assert.Null(FieldNameSuggester.Suggest("Zielsystem", ExtensionLibrary.Empty));
    }

    [Fact]
    public void Ein_ganzer_Feldname_trifft()
    {
        var extensions = Erweiterung(new FieldNameRule { Pattern = "zielsystem|assetTag", Generator = "assetTag" });

        Assert.Equal("assetTag", FieldNameSuggester.Suggest("Zielsystem", extensions));
    }

    [Fact]
    public void Ein_Teiltreffer_trifft_nicht()
    {
        // Der Fehler der alten Liste: das Fragment "ort" traf per Contains()
        // auch mitten in "Sortiment" und erklaerte die Spalte zum Ortsnamen.
        // Mit der Trefferregel "ganzer Feldname" darf das nicht mehr
        // passieren -- ".*ort" verlangt jetzt eine Endung auf "ort".
        var extensions = Erweiterung(new FieldNameRule { Pattern = ".*ort", Generator = "city" });

        Assert.Null(FieldNameSuggester.Suggest("Sortiment", extensions));
        Assert.Equal("city", FieldNameSuggester.Suggest("Wohnort", extensions));
    }

    [Fact]
    public void Die_erste_passende_Regel_gewinnt()
    {
        var extensions = Erweiterung(
            new FieldNameRule { Pattern = ".*name.*", Generator = "personName" },
            new FieldNameRule { Pattern = ".*name.*", Generator = "companyName" });

        Assert.Equal("personName", FieldNameSuggester.Suggest("Kundenname", extensions));
    }

    [Fact]
    public void ScanText_kommt_als_Sonderwert_durch()
    {
        var extensions = Erweiterung(new FieldNameRule { Pattern = "bemerkung|notiz", Generator = "scanText" });

        Assert.Equal("scanText", FieldNameSuggester.Suggest("Bemerkung", extensions));
    }

    [Fact]
    public void Die_Schreibweise_ist_standardmaessig_egal()
    {
        var extensions = Erweiterung(new FieldNameRule { Pattern = "zielsystem", Generator = "assetTag" });

        Assert.Equal("assetTag", FieldNameSuggester.Suggest("ZIELSYSTEM", extensions));
        Assert.Equal("assetTag", FieldNameSuggester.Suggest("zielsystem", extensions));
    }

    [Fact]
    public void IgnoreCase_false_verlangt_die_genaue_Schreibweise()
    {
        var extensions = Erweiterung(
            new FieldNameRule { Pattern = "Zielsystem", Generator = "assetTag", IgnoreCase = false });

        Assert.Equal("assetTag", FieldNameSuggester.Suggest("Zielsystem", extensions));
        Assert.Null(FieldNameSuggester.Suggest("ZIELSYSTEM", extensions));
    }

    [Fact]
    public void Ein_ungueltiges_Muster_wirft_nicht_und_liefert_einfach_keinen_Treffer()
    {
        var extensions = Erweiterung(
            new FieldNameRule { Pattern = "(unbalanciert", Generator = "assetTag" },
            new FieldNameRule { Pattern = "zielsystem", Generator = "personName" });

        var vorschlag = Record.Exception(() => FieldNameSuggester.Suggest("Zielsystem", extensions));

        Assert.Null(vorschlag);
        Assert.Equal("personName", FieldNameSuggester.Suggest("Zielsystem", extensions));
    }

    [Fact]
    public void Kein_Muster_passt_ergibt_null()
    {
        var extensions = Erweiterung(new FieldNameRule { Pattern = "zielsystem", Generator = "assetTag" });

        Assert.Null(FieldNameSuggester.Suggest("Artikelkategorie", extensions));
    }
}
