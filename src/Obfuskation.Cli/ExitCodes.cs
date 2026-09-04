namespace Obfuskation.Cli;

/// <summary>
/// Rueckgabewerte des Programms. Sie sind Teil der Schnittstelle: Skripte und
/// die spaetere Oberflaeche werten sie aus, sie duerfen sich also nicht
/// stillschweigend aendern.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>Allgemeiner Fehler.</summary>
    public const int Failure = 1;

    /// <summary>Die Konfiguration fehlt oder ist fehlerhaft.</summary>
    public const int ConfigurationError = 2;

    /// <summary>Ein Feld traegt keine Regel und die Vorgabe verlangt den Abbruch.</summary>
    public const int UnhandledField = 3;

    /// <summary>Die Pruefung hat Restbestaende gefunden.</summary>
    public const int ScanFindings = 4;

    /// <summary>Die Ersetzungstabelle ist widerspruechlich oder gesperrt.</summary>
    public const int MappingProblem = 5;
}
