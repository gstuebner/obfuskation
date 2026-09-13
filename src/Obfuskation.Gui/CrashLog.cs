using Obfuskation.Core;

namespace Obfuskation.Gui;

/// <summary>
/// Letzte Rettung bei unbehandelten Ausnahmen: ein Eintrag im Protokoll, damit
/// sich ein Absturz im Nachhinein zuordnen laesst.
///
/// Der Anlass sind gemeldete Abstuerze ohne jede Fehlermeldung: ohne diesen
/// Weg bleibt nur Raten. Das Protokollieren selbst darf dabei nie etwas
/// ausloesen -- es laeuft an Stellen, an denen bereits etwas schiefgegangen
/// ist, und eine zweite Ausnahme von hier riss bisher alles mit.
/// </summary>
internal static class CrashLog
{
    /// <summary>
    /// Neben <c>gui.json</c> und der Erweiterungsdatei, nicht an einem zweiten,
    /// eigenen Ort: wer eine davon findet, findet auch dieses Protokoll.
    /// </summary>
    public static string Path => System.IO.Path.Combine(PathHelper.ConfigDirectory, "absturz.log");

    /// <param name="origin">
    /// Woher der Fehler kam ("Dispatcher", "Task", "AppDomain", "Start") --
    /// die Herkunft entscheidet, ob die Anwendung weiterlaufen konnte, und
    /// grenzt beim Nachlesen die Suche erheblich ein.
    /// </param>
    public static void Write(string origin, Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(PathHelper.ConfigDirectory);
            System.IO.File.AppendAllText(
                Path,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {origin} ==={Environment.NewLine}"
                + exception + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception)
        {
            // Bewusst geschluckt und bewusst breit gefangen: ein volles
            // Dateisystem oder ein schreibgeschuetzter Ordner darf die
            // Anwendung nicht zusaetzlich zum urspruenglichen Fehler beenden.
        }
    }
}
