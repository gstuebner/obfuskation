using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// D-2 aus A6: mit einem falschen Profil rechnet <c>dateShift</c> ohne
/// Tabelleneintrag ueber den profilweiten Offset zurueck und liefert ein
/// plausibles, aber falsches Datum, waehrend andere Spalten korrekt als
/// unbekanntesPseudonym auffallen. Das Verhalten selbst bleibt (siehe
/// Kommentar in Pseudonymizer), aber der Bericht muss darauf hinweisen.
/// </summary>
public class DeobfuscateWarningTests
{
    private static TestProfile Profil() =>
        new TestProfile()
            .WithField("Geburtsdatum", FieldAction.Pseudonymize, "dateShift")
            .WithField("Name", FieldAction.Pseudonymize, "personName");

    [Fact]
    public void Fremdes_Profil_bei_der_Rueckabbildung_warnt_vor_verschobenen_Daten()
    {
        // Zwei getrennte Wegwerfverzeichnisse -- jedes bekommt beim ersten
        // Oeffnen seiner mapping.json ein eigenes, zufaelliges Salt (siehe
        // MappingStore.Open). Der dateShift-Offset haengt an diesem Salt,
        // ist also je Profil ein anderer.
        using var richtig = Profil();
        using var fremd = Profil();

        var obfuskiert = richtig.CreateEngine().Obfuscate(
            TestProfile.Utf8("Geburtsdatum;Name\n15.03.1980;Max Mustermann\n"),
            "a.csv", new RunOptions { Strict = true });

        var zurueck = fremd.CreateEngine().Deobfuscate(
            obfuskiert.Content, "a.csv", new RunOptions());

        Assert.Contains(zurueck.Report.Warnings, w => w.Code == "datumMitFremdemProfil");
    }

    [Fact]
    public void Beim_richtigen_Profil_bleibt_die_Warnung_aus()
    {
        using var setup = Profil();
        var engine = setup.CreateEngine();

        var obfuskiert = engine.Obfuscate(
            TestProfile.Utf8("Geburtsdatum;Name\n15.03.1980;Max Mustermann\n"),
            "a.csv", new RunOptions { Strict = true });

        var zurueck = engine.Deobfuscate(obfuskiert.Content, "a.csv", new RunOptions());

        Assert.DoesNotContain(zurueck.Report.Warnings, w => w.Code == "datumMitFremdemProfil");
    }
}
