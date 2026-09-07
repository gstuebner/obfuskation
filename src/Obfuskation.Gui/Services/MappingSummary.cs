using Obfuskation.Core.Mapping;

namespace Obfuskation.Gui.Services;

/// <summary>
/// Kurzauskunft ueber eine Ersetzungstabelle: Pfad, Anzahl, Existenz.
///
/// Ausgelagert aus <see cref="ViewModels.MappingViewModel"/>, weil dieselbe
/// Auskunft auch in der Kopfzeile des Hauptfensters und in der Profiluebersicht
/// gebraucht wird -- die Logik soll nicht mehrfach dastehen.
/// </summary>
/// <param name="StorePath">Pfad der Mapping-Datei, wie er sich aus dem Profil ergibt.</param>
/// <param name="Exists">Ob unter dem Pfad schon eine Tabelle liegt.</param>
/// <param name="TotalEntries">Anzahl aller Eintraege, ueber alle Namensraeume.</param>
/// <param name="Text">Fertiger deutscher Kurztext zur Anzeige, etwa die Anzahl der Eintraege oder ein Platzhalter, wenn es noch keine Tabelle gibt.</param>
public sealed record MappingSummary(string StorePath, bool Exists, int TotalEntries, string Text)
{
    /// <summary>
    /// Liest die Kurzauskunft. Oeffnet die Tabelle dafuer lesend -- nur Anzahlen
    /// werden entnommen, niemals ein Wert. Ein Fehler beim Lesen (Sperre,
    /// beschaedigte Datei) fuehrt zu einem Text statt einer Ausnahme: diese
    /// Auskunft soll auch dann noch etwas anzuzeigen haben.
    /// </summary>
    public static MappingSummary For(string storePath, string profileName)
    {
        if (!File.Exists(storePath))
            return new MappingSummary(storePath, false, 0, "noch keine Tabelle");

        try
        {
            using var store = MappingStore.Open(
                storePath, profileName, readOnly: true, allowInsideGitWorkingTree: true);

            var total = store.TotalEntries;
            var text = total == 1 ? "1 Eintrag" : $"{total} Einträge";
            return new MappingSummary(storePath, true, total, text);
        }
        catch (Exception ex) when (ex is MappingConflictException
                                       or MappingLockedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            return new MappingSummary(storePath, true, 0, "nicht lesbar");
        }
    }
}
