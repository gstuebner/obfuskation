using Avalonia;
using Avalonia.Styling;

namespace Obfuskation.Gui.Services;

/// <summary>
/// Schaltet zwischen heller und dunkler Ansicht um — im laufenden Betrieb,
/// ohne Neustart.
///
/// Avalonia erledigt die eigentliche Arbeit: beide Farbsaetze stehen
/// nebeneinander in <c>Themes/Colors.axaml</c> unter
/// <c>ResourceDictionary.ThemeDictionaries</c>, die Steuerelemente greifen ueber
/// <c>{DynamicResource ...}</c> zu und holen sich den neuen Wert selbst. Hier
/// wird nur die gewuenschte Variante gesetzt.
///
/// <see cref="AppTheme.System"/> bildet sich auf
/// <see cref="ThemeVariant.Default"/> ab: dann folgt die Anwendung der
/// Einstellung des Betriebssystems, unter GNOME also dem Dunkelmodus der
/// Systemeinstellungen.
/// </summary>
public static class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>Der jeweils naechste Zustand des Umschalters.</summary>
    public static AppTheme Next(AppTheme current) => current switch
    {
        AppTheme.System => AppTheme.Dark,
        AppTheme.Dark => AppTheme.Light,
        _ => AppTheme.System,
    };

    /// <summary>Beschriftung des Umschalters fuer den aktuellen Zustand.</summary>
    public static string Describe(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "Dunkel",
        AppTheme.Light => "Hell",
        _ => "System",
    };

    /// <summary>Sinnbild des Umschalters fuer den aktuellen Zustand.</summary>
    public static string Symbol(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "☾",   // Mond
        AppTheme.Light => "☀",  // Sonne
        _ => "◑",               // halb gefuellter Kreis
    };
}
