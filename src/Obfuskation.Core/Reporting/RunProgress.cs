namespace Obfuskation.Core.Reporting;

/// <summary>
/// Zwischenstand eines laufenden Vorgangs.
///
/// Enthaelt wie der Bericht ausschliesslich Zaehler und Bezeichnungen, niemals
/// verarbeitete Werte — ein Fortschrittsbalken ist kein Ort fuer Echtdaten.
/// </summary>
/// <param name="RowsProcessed">Bisher verarbeitete Datensaetze.</param>
/// <param name="Stage">Was gerade geschieht, fuer die Anzeige.</param>
public sealed record RunProgress(int RowsProcessed, string Stage);

/// <summary>
/// Meldet den Fortschritt, aber nicht bei jedem Datensatz.
///
/// Bei einer Datei mit hunderttausend Zeilen waere eine Meldung je Zeile
/// teurer als die Verarbeitung selbst — jede davon muesste in den
/// Oberflaechenfaden zurueck. Gemeldet wird deshalb geblockt.
/// </summary>
public sealed class ProgressReporter
{
    private const int ReportEvery = 500;

    private readonly IProgress<RunProgress>? _progress;
    private readonly string _stage;
    private int _lastReported;

    public ProgressReporter(IProgress<RunProgress>? progress, string stage)
    {
        _progress = progress;
        _stage = stage;
    }

    public void Report(int rowsProcessed)
    {
        if (_progress is null || rowsProcessed - _lastReported < ReportEvery)
            return;

        _lastReported = rowsProcessed;
        _progress.Report(new RunProgress(rowsProcessed, _stage));
    }

    /// <summary>Abschliessende Meldung mit dem endgueltigen Stand.</summary>
    public void Complete(int rowsProcessed)
        => _progress?.Report(new RunProgress(rowsProcessed, _stage));
}
