using System.Runtime.InteropServices;
using System.Text.Json;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Die Zusagen, auf die sich der Datenschutz stuetzt. Faellt einer dieser Tests,
/// koennen Echtdaten dorthin gelangen, wo sie nicht hingehoeren.
/// </summary>
public class SafetyTests
{
    [Fact]
    public void Ein_Feld_ohne_Regel_bricht_den_Lauf_ab()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();

        var ex = Assert.Throws<UnhandledFieldException>(() => engine.Obfuscate(
            TestProfile.Utf8("Name;Geheimspalte\nMax Mustermann;streng vertraulich\n"),
            "a.csv",
            new RunOptions { Strict = true }));

        Assert.Contains("Geheimspalte", ex.FieldNames);
    }

    [Fact]
    public void Der_Abbruch_erfolgt_bevor_irgendetwas_verarbeitet_wurde()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();

        Assert.Throws<UnhandledFieldException>(() => engine.Obfuscate(
            TestProfile.Utf8("Name;Geheimspalte\nMax Mustermann;vertraulich\n"),
            "a.csv",
            new RunOptions { Strict = true }));

        // Kein Eintrag in der Ersetzungstabelle: der Lauf hat nichts angefasst.
        if (File.Exists(setup.Profile.MappingStore!))
        {
            using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName,
                readOnly: true);
            Assert.Equal(0, store.TotalEntries);
        }
    }

    [Fact]
    public void Alle_unentschiedenen_Felder_werden_auf_einmal_gemeldet()
    {
        // Nach "init" stehen alle Felder auf "error". Der Anwender soll die
        // vollstaendige Liste sehen und nicht Feld fuer Feld nachziehen muessen.
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Error)
            .WithField("IBAN", FieldAction.Error)
            .WithField("Betrag", FieldAction.Error);

        var ex = Assert.Throws<UnhandledFieldException>(() => setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Name;IBAN;Betrag\nMax Mustermann;DE02120300000000202051;1,00\n"),
            "a.csv",
            new RunOptions()));

        Assert.Equal(3, ex.FieldNames.Count);
        Assert.Contains("Name", ex.FieldNames);
        Assert.Contains("IBAN", ex.FieldNames);
        Assert.Contains("Betrag", ex.FieldNames);
    }

    [Fact]
    public void Der_strenge_Modus_uebersteuert_eine_nachlaessige_Vorgabe()
    {
        using var setup = new TestProfile(profile =>
            profile.Defaults.UnknownField = FieldAction.Passthrough);
        setup.WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();
        var eingabe = TestProfile.Utf8("Name;Rest\nMax Mustermann;Echtwert\n");

        // Ohne --strict wird durchgereicht, mit --strict wird abgebrochen.
        var ohneStrict = engine.Obfuscate(eingabe, "a.csv", new RunOptions());
        Assert.Contains("Echtwert", TestProfile.FromUtf8(ohneStrict.Content));

        Assert.Throws<UnhandledFieldException>(
            () => engine.Obfuscate(eingabe, "a.csv", new RunOptions { Strict = true }));
    }

    [Fact]
    public void Der_Bericht_enthaelt_keinen_einzigen_Eingabewert()
    {
        using var setup = new TestProfile()
            .WithField("Nummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("IBAN", FieldAction.Pseudonymize, "iban")
            .WithField("Notiz", FieldAction.Redact);

        var engine = setup.CreateEngine();

        var geheimwerte = new[]
        {
            "4711", "Max Mustermann", "DE02120300000000202051", "streng vertraulich",
        };

        var eingabe = "Nummer;Name;IBAN;Notiz\n" + string.Join(";", geheimwerte) + "\n";
        var ergebnis = engine.Obfuscate(TestProfile.Utf8(eingabe), "a.csv", new RunOptions { Strict = true });

        var berichtAlsJson = JsonSerializer.Serialize(ergebnis.Report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        foreach (var wert in geheimwerte)
            Assert.DoesNotContain(wert, berichtAlsJson);

        // Und auch kein erzeugtes Pseudonym: der Bericht traegt nur Zaehler.
        var pseudonyme = TestProfile.FromUtf8(ergebnis.Content).Split('\n')[1].TrimEnd('\r').Split(';');
        foreach (var pseudonym in pseudonyme.Where(p => p.Length >= 4 && p != "***"))
            Assert.DoesNotContain(pseudonym, berichtAlsJson);
    }

    [Fact]
    public void Der_Probelauf_veraendert_die_Ersetzungstabelle_nicht()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        var engine = setup.CreateEngine();
        var ergebnis = engine.Obfuscate(
            TestProfile.Utf8("Name\nMax Mustermann\n"), "a.csv",
            new RunOptions { Strict = true, DryRun = true });

        // Der Bericht entsteht vollstaendig ...
        Assert.Equal(1, ergebnis.Report.RuleHits["personName"]);

        // ... aber es wurde nichts festgehalten.
        Assert.Equal(0, ergebnis.Report.NewMappings);
        if (File.Exists(setup.Profile.MappingStore!))
        {
            using var store = MappingStore.Open(setup.Profile.MappingStore!, setup.Profile.ProfileName,
                readOnly: true);
            Assert.Equal(0, store.TotalEntries);
        }
    }

    [Fact]
    public void Die_Ersetzungstabelle_ist_nur_fuer_den_Eigentuemer_lesbar()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;  // Dateirechte gibt es dort in dieser Form nicht.

        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName");

        setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Name\nMax Mustermann\n"), "a.csv", new RunOptions { Strict = true });

        var mode = File.GetUnixFileMode(setup.Profile.MappingStore!);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Die_Tabelle_verweigert_die_Ablage_in_einem_Git_Verzeichnis()
    {
        var verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(verzeichnis, ".git"));

        try
        {
            var pfad = Path.Combine(verzeichnis, "unterordner", "mapping.json");

            var ex = Assert.Throws<MappingConflictException>(
                () => MappingStore.Open(pfad, "test"));

            Assert.Contains("Git-Arbeitsverzeichnis", ex.Message);

            // Mit ausdruecklicher Freigabe geht es trotzdem.
            using var store = MappingStore.Open(pfad, "test", allowInsideGitWorkingTree: true);
            Assert.NotNull(store);
        }
        finally
        {
            Directory.Delete(verzeichnis, recursive: true);
        }
    }

    [Fact]
    public void Eine_mehrdeutige_Tabelle_wird_beim_Laden_zurueckgewiesen()
    {
        using var setup = new TestProfile();

        // Von Hand einen Bestand erzeugen, in dem ein Pseudonym doppelt vergeben ist.
        var beschaedigt = """
            {
              "version": 1,
              "profileName": "test",
              "encryption": "none",
              "salt": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
              "namespaces": {
                "personName": {
                  "Max Mustermann": "Jonas Brehmer",
                  "Erika Musterfrau": "Jonas Brehmer"
                }
              }
            }
            """;

        File.WriteAllText(setup.Profile.MappingStore!, beschaedigt);

        var ex = Assert.Throws<MappingConflictException>(
            () => MappingStore.Open(setup.Profile.MappingStore!, "test"));

        Assert.Contains("mehrdeutig", ex.Message);
    }

    [Fact]
    public void Ein_zweiter_Lauf_kann_die_Tabelle_nicht_gleichzeitig_beschreiben()
    {
        using var setup = new TestProfile();

        using var ersterZugriff = MappingStore.Open(setup.Profile.MappingStore!, "test");

        Assert.Throws<MappingLockedException>(
            () => MappingStore.Open(setup.Profile.MappingStore!, "test"));

        // Lesender Zugriff bleibt trotzdem moeglich.
        using var lesend = MappingStore.Open(setup.Profile.MappingStore!, "test", readOnly: true);
        Assert.NotNull(lesend);
    }

    [Fact]
    public void Das_Feld_drop_verschwindet_vollstaendig_aus_der_Ausgabe()
    {
        using var setup = new TestProfile()
            .WithField("Name", FieldAction.Pseudonymize, "personName")
            .WithField("Geheim", FieldAction.Drop);

        var ergebnis = setup.CreateEngine().Obfuscate(
            TestProfile.Utf8("Name;Geheim\nMax Mustermann;streng vertraulich\n"),
            "a.csv", new RunOptions { Strict = true });

        var ausgabe = TestProfile.FromUtf8(ergebnis.Content);

        Assert.DoesNotContain("Geheim", ausgabe);
        Assert.DoesNotContain("streng vertraulich", ausgabe);
    }
}
