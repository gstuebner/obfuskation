using System.Text;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Reporting;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Fortschritt und Abbruch. Beides zaehlt erst bei grossen Dateien — genau
/// dort, wo eine Oberflaeche sonst minutenlang stumm bliebe.
/// </summary>
public class ProgressTests
{
    private static TestProfile Setup() =>
        new TestProfile()
            .WithField("Nummer", FieldAction.Pseudonymize, "numericId")
            .WithField("Name", FieldAction.Pseudonymize, "personName");

    private static byte[] GrosseDatei(int zeilen)
    {
        var builder = new StringBuilder("Nummer;Name\n");
        for (var i = 0; i < zeilen; i++)
            builder.Append(100000 + i).Append(";Person ").Append(i).Append('\n');

        return new UTF8Encoding(false).GetBytes(builder.ToString());
    }

    [Fact]
    public void Der_Fortschritt_wird_waehrend_des_Laufs_gemeldet()
    {
        using var setup = Setup();

        var stände = new List<RunProgress>();
        var melder = new Progress<RunProgress>();

        // Progress<T> meldet sonst ueber den Synchronisierungskontext, den es
        // im Test nicht gibt. Deshalb hier der unmittelbare Weg.
        var direkt = new DirectProgress(stände.Add);
        _ = melder;

        setup.CreateEngine().Obfuscate(
            GrosseDatei(2500), "gross.csv", new RunOptions { Strict = true }, direkt);

        Assert.NotEmpty(stände);
        Assert.All(stände, stand => Assert.Equal("csv", stand.Stage));

        // Aufsteigend und am Ende der volle Umfang.
        Assert.Equal(stände.OrderBy(s => s.RowsProcessed).Select(s => s.RowsProcessed),
            stände.Select(s => s.RowsProcessed));
        Assert.Equal(2500, stände[^1].RowsProcessed);
    }

    [Fact]
    public void Bei_kleinen_Dateien_wird_nur_der_Abschluss_gemeldet()
    {
        using var setup = Setup();

        var stände = new List<RunProgress>();

        setup.CreateEngine().Obfuscate(
            GrosseDatei(10), "klein.csv", new RunOptions { Strict = true },
            new DirectProgress(stände.Add));

        // Zwischenmeldungen je Zeile waeren teurer als die Verarbeitung selbst.
        Assert.Single(stände);
        Assert.Equal(10, stände[0].RowsProcessed);
    }

    [Fact]
    public void Ein_Abbruch_wirkt_mitten_im_Lauf()
    {
        using var setup = Setup();
        using var abbruch = new CancellationTokenSource();

        // Nach den ersten Meldungen abbrechen.
        var melder = new DirectProgress(stand =>
        {
            if (stand.RowsProcessed >= 500)
                abbruch.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => setup.CreateEngine().Obfuscate(
            GrosseDatei(50000), "gross.csv", new RunOptions { Strict = true },
            melder, abbruch.Token));
    }

    [Fact]
    public async Task Ein_bereits_abgebrochener_Vorgang_startet_gar_nicht()
    {
        using var setup = Setup();
        using var abbruch = new CancellationTokenSource();
        abbruch.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            setup.CreateEngine().ObfuscateAsync(
                GrosseDatei(100), "gross.csv", new RunOptions { Strict = true }, abbruch.Token));
    }

    [Fact]
    public void Ein_Abbruch_hinterlaesst_keine_Eintraege_in_der_Tabelle()
    {
        using var setup = Setup();
        using var abbruch = new CancellationTokenSource();

        var melder = new DirectProgress(stand =>
        {
            if (stand.RowsProcessed >= 500)
                abbruch.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => setup.CreateEngine().Obfuscate(
            GrosseDatei(50000), "gross.csv", new RunOptions { Strict = true },
            melder, abbruch.Token));

        // Der Store wird erst nach der Verarbeitung gesichert. Ein Abbruch darf
        // deshalb keinen halben Bestand zuruecklassen.
        if (File.Exists(setup.Profile.MappingStore!))
        {
            using var store = Mapping.MappingStore.Open(
                setup.Profile.MappingStore!, setup.Profile.ProfileName, readOnly: true);
            Assert.Equal(0, store.TotalEntries);
        }
    }

    /// <summary>Meldet unmittelbar, ohne Umweg ueber einen Synchronisierungskontext.</summary>
    private sealed class DirectProgress : IProgress<RunProgress>
    {
        private readonly Action<RunProgress> _handler;

        public DirectProgress(Action<RunProgress> handler) => _handler = handler;

        public void Report(RunProgress value) => _handler(value);
    }
}
