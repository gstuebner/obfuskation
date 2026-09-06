using System.Runtime.CompilerServices;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Haelt den Testlauf von den Einstellungen des Anwenders fern.
///
/// <see cref="Gui.Services.GuiSettings.FilePath"/> loest ueber
/// <c>XDG_CONFIG_HOME</c> auf und faellt ohne diese Variable auf
/// <c>~/.config/obfuskation/gui.json</c> zurueck. Da die Ansichtsmodelle an
/// mehreren Stellen <c>Save()</c> rufen, ueberschrieb ein Testlauf zuvor die
/// echte Datei des angemeldeten Benutzers samt seiner zuletzt geoeffneten
/// Profile. Ein Testlauf muss rueckwirkungsfrei sein.
///
/// Der Modulinitialisierer laeuft, bevor der erste Test angefasst wird, und
/// gilt damit fuer alle Testklassen dieser Baugruppe.
/// </summary>
internal static class TestUmgebung
{
    private static string? _verzeichnis;

    [ModuleInitializer]
    internal static void Einrichten()
    {
        _verzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-testkonfiguration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_verzeichnis);

        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _verzeichnis);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Aufraeumen();
    }

    private static void Aufraeumen()
    {
        try
        {
            if (_verzeichnis is not null && Directory.Exists(_verzeichnis))
                Directory.Delete(_verzeichnis, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis stoert nicht.
        }
    }

    /// <summary>Das Wegwerfverzeichnis, auf das die Einstellungen zeigen.</summary>
    internal static string Verzeichnis =>
        _verzeichnis ?? throw new InvalidOperationException("Der Modulinitialisierer lief nicht.");
}
