using System.Text;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Struktur lesen, Behandlung zuordnen, Wert vorschauen — die drei Dinge, die
/// eine Oberflaeche braucht, bevor sie irgendetwas verarbeitet.
/// </summary>
public class InspectionTests
{
    /// <summary>Die mitgelieferte Beispieldatei: Windows-1252, Semikolon, acht Spalten.</summary>
    private static byte[] BeispielCsv()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "testdata", "kunden.csv");
        return File.ReadAllBytes(Path.GetFullPath(pfad));
    }

    [Fact]
    public void Die_Beispieldatei_wird_richtig_erkannt()
    {
        var ergebnis = FieldInspector.Inspect(BeispielCsv(), "kunden.csv");

        Assert.Equal(DataFormat.Csv, ergebnis.Format);
        Assert.Equal("windows-1252", ergebnis.Encoding);
        Assert.Equal(";", ergebnis.Delimiter);
        Assert.Equal(8, ergebnis.FieldNames.Count);
        Assert.Equal("Kundennummer", ergebnis.FieldNames[0]);
        Assert.Contains("Verwendungszweck", ergebnis.FieldNames);
    }

    [Fact]
    public void Json_Eigenschaften_werden_rekursiv_gesammelt()
    {
        var json = """
            { "kunden": [ { "name": "Max", "adresse": { "ort": "Berlin" } } ] }
            """;

        var ergebnis = FieldInspector.Inspect(TestProfile.Utf8(json), "kunden.json");

        Assert.Equal(DataFormat.Json, ergebnis.Format);
        Assert.Null(ergebnis.Delimiter);
        Assert.Equal(new[] { "kunden", "name", "adresse", "ort" }, ergebnis.FieldNames);
    }

    [Fact]
    public void Fliesstext_hat_keine_Felder()
    {
        var ergebnis = FieldInspector.Inspect(TestProfile.Utf8("Nur ein Satz.\n"), "notiz.txt");

        Assert.Equal(DataFormat.Text, ergebnis.Format);
        Assert.Empty(ergebnis.FieldNames);
    }

    [Fact]
    public void Ein_Komma_getrenntes_Csv_wird_ebenso_erkannt()
    {
        var csv = "Name,IBAN,Betrag\nMax Mustermann,DE02120300000000202051,1.00\n";
        var ergebnis = FieldInspector.Inspect(TestProfile.Utf8(csv), "a.csv");

        Assert.Equal(",", ergebnis.Delimiter);
        Assert.Equal(new[] { "Name", "IBAN", "Betrag" }, ergebnis.FieldNames);
    }

    [Fact]
    public void Analyze_meldet_offene_Felder_ohne_abzubrechen()
    {
        // Ein frisch angelegtes Profil steht ueberall auf "error". Genau diesen
        // Zustand muss die Oberflaeche anzeigen koennen — ein Abbruch waere
        // hier das Gegenteil von hilfreich.
        using var setup = new TestProfile();
        var profil = ProfileScaffolder.Create("test", null);
        foreach (var name in new[] { "Kundennummer", "Kundenname", "IBAN" })
            profil.Fields.Add(new FieldRule { Match = name, Action = FieldAction.Error });
        profil.MappingStore = setup.Profile.MappingStore;

        var ergebnis = new ObfuscationEngine(profil).Analyze(
            TestProfile.Utf8("Kundennummer;Kundenname;IBAN\n4711;Max Mustermann;DE02\n"), "a.csv");

        Assert.Equal(3, ergebnis.Fields.Count);
        Assert.All(ergebnis.Fields, feld => Assert.False(feld.IsDecided));
        Assert.Equal(3, ergebnis.Undecided.Count);
    }

    [Fact]
    public void Analyze_unterscheidet_eigene_Regel_und_Vorgabe()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var ergebnis = setup.CreateEngine().Analyze(
            TestProfile.Utf8("Name;Unbekannt\nMax Mustermann;x\n"), "a.csv");

        var name = ergebnis.Fields.Single(f => f.FieldName == "Name");
        Assert.True(name.IsDecided);
        Assert.False(name.IsFromDefault);
        Assert.Equal("personName", name.Generator);

        var unbekannt = ergebnis.Fields.Single(f => f.FieldName == "Unbekannt");
        Assert.True(unbekannt.IsFromDefault);
        Assert.False(unbekannt.IsDecided);   // Vorgabe ist "error"
    }

    [Fact]
    public void Analyze_liefert_auch_die_Formatangaben()
    {
        using var setup = new TestProfile()
            .WithField("Kundenname", FieldAction.Pseudonymize, "personName");

        var ergebnis = setup.CreateEngine().Analyze(BeispielCsv(), "kunden.csv");

        Assert.Equal("windows-1252", ergebnis.File.Encoding);
        Assert.Equal(";", ergebnis.File.Delimiter);
    }

    [Fact]
    public void Die_Vorschau_entspricht_dem_spaeteren_echten_Lauf()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();

        // Erst einen Bestand anlegen, damit das Salt festliegt.
        engine.Obfuscate(TestProfile.Utf8("Name\nErika Musterfrau\n"), "a.csv",
            new RunOptions { Strict = true });

        var vorschau = engine.PreviewValue("personName", "Max Mustermann");

        var lauf = engine.Obfuscate(TestProfile.Utf8("Name\nMax Mustermann\n"), "b.csv",
            new RunOptions { Strict = true });
        var tatsaechlich = TestProfile.FromUtf8(lauf.Content).Split('\n')[1].TrimEnd('\r');

        Assert.Equal(tatsaechlich, vorschau);
    }

    [Fact]
    public void Die_Vorschau_haelt_nichts_fest()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();
        engine.Obfuscate(TestProfile.Utf8("Name\nErika Musterfrau\n"), "a.csv",
            new RunOptions { Strict = true });

        using (var vorher = Mapping.MappingStore.Open(setup.Profile.MappingStore!, "test", readOnly: true))
        {
            var stand = vorher.TotalEntries;
            vorher.Dispose();

            engine.PreviewValue("personName", "Max Mustermann");
            engine.PreviewValue("personName", "Anna Beispiel");

            using var nachher = Mapping.MappingStore.Open(setup.Profile.MappingStore!, "test", readOnly: true);
            Assert.Equal(stand, nachher.TotalEntries);
        }
    }

    [Fact]
    public void Ohne_Tabelle_ist_die_Vorschau_nur_beispielhaft()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();

        // Noch kein Bestand: das Salt entsteht fluechtig, der Wert kann also
        // vom spaeteren Lauf abweichen. Die Oberflaeche muss das kenntlich
        // machen — hier wird nur geprueft, dass sie es erfragen kann.
        Assert.False(engine.MappingStoreExists);
        Assert.False(string.IsNullOrEmpty(engine.PreviewValue("personName", "Max Mustermann")));

        engine.Obfuscate(TestProfile.Utf8("Name\nMax Mustermann\n"), "a.csv",
            new RunOptions { Strict = true });

        Assert.True(engine.MappingStoreExists);
    }

    [Fact]
    public void Die_Vorschau_eines_leeren_Wertes_bleibt_leer()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        Assert.Equal("", setup.CreateEngine().PreviewValue("personName", ""));
    }
}
