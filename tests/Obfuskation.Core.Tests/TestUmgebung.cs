using System.Runtime.CompilerServices;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Haelt den Testlauf von der echten Konfiguration und den echten
/// Ersetzungstabellen des Anwenders fern.
///
/// <see cref="PathHelper.ConfigDirectory"/> loest ueber <c>XDG_CONFIG_HOME</c>
/// auf, <see cref="PathHelper.DefaultMappingStorePath"/> ueber
/// <c>XDG_DATA_HOME</c>. Ohne diese Umleitung wuerden die Index- und
/// Umbenenn-Tests (die absichtlich den Vorgabepfad benutzen, um genau diesen
/// Mechanismus zu pruefen) in <c>~/.config/obfuskation</c> und
/// <c>~/.local/share/obfuskation</c> schreiben.
///
/// Der Modulinitialisierer laeuft vor dem ersten Test und gilt fuer alle
/// Testklassen dieser Baugruppe.
/// </summary>
internal static class TestUmgebung
{
    private static string? _basisVerzeichnis;
    private static string? _konfigVerzeichnis;
    private static string? _datenVerzeichnis;

    [ModuleInitializer]
    internal static void Einrichten()
    {
        _basisVerzeichnis = Path.Combine(Path.GetTempPath(), "obfuskation-testkonfiguration",
            Guid.NewGuid().ToString("N"));
        _konfigVerzeichnis = Path.Combine(_basisVerzeichnis, "config");
        _datenVerzeichnis = Path.Combine(_basisVerzeichnis, "data");

        Directory.CreateDirectory(_konfigVerzeichnis);
        Directory.CreateDirectory(_datenVerzeichnis);

        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _konfigVerzeichnis);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _datenVerzeichnis);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Aufraeumen();
    }

    private static void Aufraeumen()
    {
        try
        {
            if (_basisVerzeichnis is not null && Directory.Exists(_basisVerzeichnis))
                Directory.Delete(_basisVerzeichnis, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis stoert nicht.
        }
    }

    /// <summary>Das Wegwerfverzeichnis, auf das XDG_CONFIG_HOME zeigt.</summary>
    internal static string KonfigVerzeichnis =>
        _konfigVerzeichnis ?? throw new InvalidOperationException("Der Modulinitialisierer lief nicht.");

    /// <summary>Das Wegwerfverzeichnis, auf das XDG_DATA_HOME zeigt.</summary>
    internal static string DatenVerzeichnis =>
        _datenVerzeichnis ?? throw new InvalidOperationException("Der Modulinitialisierer lief nicht.");
}
