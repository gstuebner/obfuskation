using Avalonia;

namespace Obfuskation.Gui;

/// <summary>Einstiegspunkt der Oberflaeche.</summary>
internal static class Program
{
    // Vor dem Start von Avalonia darf nichts initialisiert werden, was auf
    // AvaloniaLocator zugreift - deshalb der schlanke Rumpf hier.
    /// <summary>Beim Start uebergebene Pfade, ausgewertet in <see cref="App"/>.</summary>
    public static string? StartProfilePath { get; private set; }

    public static string? StartDataPath { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        ParseArguments(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Bewusst schlank: die Oberflaeche nimmt hoechstens eine Konfiguration und
    /// eine Datendatei entgegen. Alles Weitere gehoert auf die Kommandozeile.
    /// </summary>
    private static void ParseArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" or "-c" when i + 1 < args.Length:
                    StartProfilePath = args[++i];
                    break;

                case "--help" or "-h" or "-?":
                    Console.WriteLine("Verwendung: obfuskation-gui [--config <datei>] [<datendatei>]");
                    Environment.Exit(0);
                    break;

                default:
                    if (!args[i].StartsWith('-'))
                        StartDataPath ??= args[i];
                    break;
            }
        }
    }

    /// <summary>Wird auch vom Vorschau-Werkzeug der Entwicklungsumgebung genutzt.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
