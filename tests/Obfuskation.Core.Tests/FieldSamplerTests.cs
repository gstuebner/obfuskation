using System.Text;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Formats;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Stichprobe fuer die Feldvorschau: robust gegen Trennzeichen und
/// Anfuehrungszeichen im Wert, gegen fremde Zeichensaetze und gegen Spalten,
/// die durchgaengig leer sind.
/// </summary>
public class FieldSamplerTests
{
    [Fact]
    public void Ein_Trennzeichen_im_Wert_zerlegt_das_Feld_nicht()
    {
        var csv = "Name;Ort\n\"Meier; Sohn\";Berlin\n";

        var ergebnis = FieldSampler.Sample(TestProfile.Utf8(csv), "a.csv");

        Assert.Equal("Meier; Sohn", ergebnis["Name"][0]);
        Assert.Equal("Berlin", ergebnis["Ort"][0]);
    }

    [Fact]
    public void Verdoppelte_Anfuehrungszeichen_werden_richtig_entpackt()
    {
        var csv = "Zitat\n\"Er sagte \"\"Hallo\"\"\"\n";

        var ergebnis = FieldSampler.Sample(TestProfile.Utf8(csv), "a.csv");

        Assert.Equal("Er sagte \"Hallo\"", ergebnis["Zitat"][0]);
    }

    [Fact]
    public void Windows1252_Umlaute_kommen_richtig_an()
    {
        // Der Kern des Befundes: die alte Lesung nahm fest UTF-8 an. Eine
        // Ausfuhr in Windows-1252 kam damit als kaputte Umlaute an.
        TextFormatDetector.RegisterCodePages();
        var windows1252 = Encoding.GetEncoding(1252);

        var csv = "Name;Ort\nJürgen Groß;München\n";
        var content = windows1252.GetBytes(csv);

        var ergebnis = FieldSampler.Sample(content, "a.csv");

        Assert.Equal("Jürgen Groß", ergebnis["Name"][0]);
        Assert.Equal("München", ergebnis["Ort"][0]);
    }

    [Fact]
    public void Bis_zu_drei_Werte_je_Feld_werden_gesammelt()
    {
        var csv = "Name\nEins\nZwei\nDrei\nVier\nFünf\n";

        var ergebnis = FieldSampler.Sample(TestProfile.Utf8(csv), "a.csv");

        Assert.Equal(new[] { "Eins", "Zwei", "Drei" }, ergebnis["Name"]);
    }

    [Fact]
    public void Eine_durchgaengig_leere_Spalte_liest_nicht_den_ganzen_Bestand()
    {
        var zeilen = Enumerable.Range(1, 60).Select(i => $"Wert{i};");
        var csv = "Gefuellt;Leer\n" + string.Join("\n", zeilen) + "\n";

        var ergebnis = FieldSampler.Sample(TestProfile.Utf8(csv), "a.csv");

        Assert.Equal(new[] { "Wert1", "Wert2", "Wert3" }, ergebnis["Gefuellt"]);
        Assert.False(ergebnis.TryGetValue("Leer", out var leereWerte) && leereWerte.Count > 0);
    }

    [Fact]
    public void Json_Werte_werden_je_Eigenschaftsname_ueber_verschachtelte_Objekte_hinweg_gesammelt()
    {
        var json = """
            { "kunden": [
                { "name": "Max", "adresse": { "ort": "Berlin" } },
                { "name": "Erika", "adresse": { "ort": "Hamburg" } }
            ] }
            """;

        var ergebnis = FieldSampler.Sample(TestProfile.Utf8(json), "a.json");

        Assert.Equal(new[] { "Max", "Erika" }, ergebnis["name"]);
        Assert.Equal(new[] { "Berlin", "Hamburg" }, ergebnis["ort"]);

        // "kunden" traegt selbst kein Blattwert, nur ein Array -- dafuer gibt
        // es keinen Beispielwert.
        Assert.False(ergebnis.ContainsKey("kunden"));
    }

    [Fact]
    public void Freitext_liefert_keine_Beispielwerte()
    {
        var ergebnis = FieldSampler.Sample(TestProfile.Utf8("Nur ein Satz.\n"), "notiz.txt");

        Assert.Empty(ergebnis);
    }
}
