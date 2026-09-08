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

    /// <summary>
    /// dateGeneralize ist ausdruecklich nicht umkehrbar (viele-zu-eins) und
    /// bekommt deshalb keinen Eintrag in der Ersetzungstabelle -- wie redact
    /// ohne Tabelleneintrag. Die Rueckabbildung findet folgerichtig nichts und
    /// meldet das als nichtWiederherstellbar, statt den gerundeten Wert
    /// stillschweigend als Original auszugeben. Die Kategorie ist bewusst
    /// nicht "unbekanntesPseudonym": das legte nahe, ein anderer Bestand
    /// koennte den Wert noch hergeben.
    /// </summary>
    [Fact]
    public void DateGeneralize_meldet_beim_Zurueckuebersetzen_dass_nichts_wiederherstellbar_ist()
    {
        using var setup = new TestProfile()
            .WithField("Geburtsdatum", FieldAction.Pseudonymize, "dateGeneralize");
        var engine = setup.CreateEngine();

        var obfuskiert = engine.Obfuscate(
            TestProfile.Utf8("Geburtsdatum\n15.03.1980\n"), "a.csv", new RunOptions { Strict = true });
        var zurueck = engine.Deobfuscate(obfuskiert.Content, "a.csv", new RunOptions());

        Assert.Contains(zurueck.Report.Findings,
            f => f.Kind == "nichtWiederherstellbar" && f.Rule == "dateGeneralize");

        // Und ausdruecklich nicht die Kategorie, die auf ein fremdes Profil deutet.
        Assert.DoesNotContain(zurueck.Report.Findings, f => f.Kind == "unbekanntesPseudonym");

        // Der gerundete Wert steht unveraendert da -- er wird nicht faelschlich
        // als wiederhergestelltes Original ausgegeben.
        Assert.Equal("Geburtsdatum\n01.03.1980\n", TestProfile.FromUtf8(zurueck.Content));
    }

    /// <summary>Gegenstueck fuer partialMask, ebenfalls nicht umkehrbar.</summary>
    [Fact]
    public void PartialMask_meldet_beim_Zurueckuebersetzen_dass_nichts_wiederherstellbar_ist()
    {
        using var setup = new TestProfile()
            .WithField("Telefon", FieldAction.Pseudonymize, "partialMask");
        var engine = setup.CreateEngine();

        var obfuskiert = engine.Obfuscate(
            TestProfile.Utf8("Telefon\n01701234567\n"), "a.csv", new RunOptions { Strict = true });
        var zurueck = engine.Deobfuscate(obfuskiert.Content, "a.csv", new RunOptions());

        Assert.Contains(zurueck.Report.Findings,
            f => f.Kind == "nichtWiederherstellbar" && f.Rule == "partialMask");
    }
}
