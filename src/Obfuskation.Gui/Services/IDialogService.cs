using Obfuskation.Core.Configuration;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Services;

/// <summary>
/// Datei- und Rueckfragedialoge, hinter einer Schnittstelle statt einer
/// konkreten Klasse: nur so laesst sich <see cref="ViewModels.MainViewModel"/>
/// mit einem Doppel pruefen, ohne dass dabei ein echtes Fenster entsteht.
/// <see cref="DialogService"/> ist die einzige echte Umsetzung.
/// </summary>
public interface IDialogService
{
    Task<string?> OpenDataFileAsync(string? startDirectory = null);

    /// <summary>
    /// Wie <see cref="OpenDataFileAsync"/>, aber mit Mehrfachauswahl -- fuer
    /// "Neu aus Datei…", wenn ein Profil aus mehreren zusammengehoerenden
    /// Dateien auf einmal entstehen soll. Eine leere Liste bedeutet Abbruch.
    /// </summary>
    Task<IReadOnlyList<string>> OpenDataFilesAsync(string? startDirectory = null);

    Task<string?> OpenProfileAsync(string? startDirectory = null);

    Task<string?> SaveFileAsync(string suggestedName, string? startDirectory = null);

    /// <summary>Rueckfrage bei ungespeicherten Aenderungen, bevor eine Sitzung ersetzt wird.</summary>
    Task<SaveChoice> AskSaveChangesAsync(string profileName);

    /// <summary>Der Anlegen-Dialog: Name, Beschreibung, Ablageort.</summary>
    Task<NewProfileResult?> AskNewProfileAsync(NewProfileProposal proposal);

    /// <summary>
    /// Rueckfrage vorm Umbenennen, die zuerst sagt, was mit der Ersetzungstabelle
    /// geschieht. Liefert den neuen Namen, oder <c>null</c> bei Abbruch.
    /// </summary>
    Task<string?> AskRenameProfileAsync(RenameProposal proposal);

    /// <summary>
    /// Rueckfrage vorm endgueltigen Loeschen. Sagt ausdruecklich, dass die
    /// Ersetzungstabelle alle Echtwerte enthaelt und ihr Verlust den Rueckweg zu
    /// den Originaldaten unmoeglich macht. Liefert <see cref="DeleteChoice.Cancel"/>
    /// ohne Auswahl -- die vorsichtige Richtung.
    /// </summary>
    Task<DeleteChoice> AskDeleteProfileAsync(DeleteProposal proposal);

    /// <summary>
    /// Die eine Rueckfrage vor einem Sammellauf ("Alle Pseudodateien
    /// erzeugen…" / "Alle Klartextdateien erzeugen…"). Liefert <c>true</c>, wenn
    /// der Anwender fortfahren will.
    /// </summary>
    Task<bool> AskBatchRunAsync(BatchRunProposal proposal);

    /// <summary>
    /// Die Profiluebersicht. <paramref name="viewModel"/> traegt bereits alle
    /// Daten und Befehle; hier entsteht nur das Fenster darum. Liefert das zum
    /// Oeffnen gewaehlte Profil, oder <c>null</c>, wenn das Fenster ohne Wahl
    /// geschlossen wurde.
    /// </summary>
    Task<ProfileSummary?> ShowProfilesAsync(ProfilesViewModel viewModel);

    /// <summary>
    /// Der Dialog "Muster erkennen…": zeigt wertbasierte Generatorvorschlaege
    /// fuer die Felder der offenen Datei (<paramref name="viewModel"/> traegt
    /// sie bereits). Liefert die beim Bestaetigen angehakten Vorschlaege, oder
    /// <c>null</c> bei Abbruch.
    /// </summary>
    Task<IReadOnlyList<PatternSuggestionAcceptance>?> ShowPatternSuggestionsAsync(
        PatternSuggestionsViewModel viewModel);
}

/// <summary>Antwort auf die Rueckfrage vorm Loeschen eines Profils.</summary>
public enum DeleteChoice
{
    Cancel,
    ProfileOnly,
    ProfileAndMapping,
}

/// <summary>Antwort auf die Rueckfrage bei ungespeicherten Aenderungen.</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <param name="SuggestedName">Vorbelegung fuer das Namensfeld, aus dem Dateinamen der Beispieldatei.</param>
/// <param name="SampleFilePath">Die Beispieldatei, aus der das Regelgeruest entsteht.</param>
public sealed record NewProfileProposal(string SuggestedName, string SampleFilePath);

/// <param name="Name">Der vom Anwender gewaehlte Profilname.</param>
/// <param name="Description">Freitext, kann leer sein.</param>
/// <param name="TargetPath">Wohin das Profil geschrieben werden soll.</param>
/// <param name="OpenExisting">
/// Der Anwender hat statt anzulegen "Stattdessen oeffnen" gewaehlt, weil unter
/// <see cref="TargetPath"/> schon ein Profil liegt.
/// </param>
public sealed record NewProfileResult(string Name, string? Description, string TargetPath, bool OpenExisting);

/// <param name="OldName">Der bisherige Name, zur Anzeige.</param>
/// <param name="SuggestedName">Vorbelegung fuer das Namensfeld.</param>
/// <param name="MappingStorePath">
/// Pfad der Ersetzungstabelle, damit die Rueckfrage sagen kann, dass sie dort
/// unveraendert stehen bleibt.
/// </param>
public sealed record RenameProposal(string OldName, string SuggestedName, string MappingStorePath);

/// <param name="Name">Name des zu loeschenden Profils, zur Anzeige.</param>
/// <param name="ProfilePath">Pfad der Profildatei.</param>
/// <param name="MappingStorePath">Pfad der Ersetzungstabelle, zur Anzeige.</param>
/// <param name="MappingStoreExists">
/// Ob unter <see cref="MappingStorePath"/> ueberhaupt schon eine Tabelle liegt
/// -- sonst waere die Warnung vor dem Verlust der Echtwerte gegenstandslos.
/// </param>
public sealed record DeleteProposal(string Name, string ProfilePath, string MappingStorePath, bool MappingStoreExists);

/// <param name="FileCount">Anzahl der Dateien, die verarbeitet werden.</param>
/// <param name="Marker">Namenszusatz der Ausgabe, "pseudo" oder "klartext".</param>
/// <param name="OverwriteCount">
/// Anzahl schon vorhandener Zieldateien, die dabei ueberschrieben wuerden --
/// muss die Rueckfrage ausdruecklich nennen.
/// </param>
public sealed record BatchRunProposal(int FileCount, string Marker, int OverwriteCount);

/// <summary>Ein beim "Muster erkennen…"-Dialog angehakter Vorschlag.</summary>
/// <param name="FieldName">Das Feld, auf das der Vorschlag zutrifft.</param>
/// <param name="Generator">Der vorgeschlagene Generator, der uebernommen werden soll.</param>
public sealed record PatternSuggestionAcceptance(string FieldName, string Generator);
