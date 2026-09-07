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
    /// Die Profiluebersicht. <paramref name="viewModel"/> traegt bereits alle
    /// Daten und Befehle; hier entsteht nur das Fenster darum. Liefert das zum
    /// Oeffnen gewaehlte Profil, oder <c>null</c>, wenn das Fenster ohne Wahl
    /// geschlossen wurde.
    /// </summary>
    Task<ProfileSummary?> ShowProfilesAsync(ProfilesViewModel viewModel);
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
