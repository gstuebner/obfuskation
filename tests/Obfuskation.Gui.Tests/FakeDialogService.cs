using Obfuskation.Core.Configuration;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Tests;

/// <summary>
/// Testdoppel fuer <see cref="IDialogService"/>: liefert vorgegebene Antworten,
/// ohne dass dabei ein Fenster entsteht. Jede Antwort ist von aussen gesetzt --
/// ein Test, der eine bestimmte Rueckfrage nicht erwartet, laesst das
/// zugehoerige Feld einfach auf seiner sicheren Vorgabe stehen.
/// </summary>
internal sealed class FakeDialogService : IDialogService
{
    public string? DataFileToOpen { get; set; }
    public IReadOnlyList<string> DataFilesToOpen { get; set; } = Array.Empty<string>();
    public string? ProfileToOpen { get; set; }
    public string? SaveTarget { get; set; }

    /// <summary>Vorgabe ist die vorsichtige Richtung: nicht fortfahren.</summary>
    public SaveChoice SaveChangesChoice { get; set; } = SaveChoice.Cancel;

    public int AskSaveChangesCalls { get; private set; }

    public NewProfileResult? NewProfileResult { get; set; }
    public string? RenameResult { get; set; }
    public ProfileSummary? ProfilesResult { get; set; }

    /// <summary>Vorgabe ist die vorsichtige Richtung: nicht loeschen.</summary>
    public DeleteChoice DeleteChoice { get; set; } = DeleteChoice.Cancel;

    /// <summary>Vorgabe ist die vorsichtige Richtung: keinen Sammellauf starten.</summary>
    public bool BatchRunConfirmed { get; set; }

    public BatchRunProposal? LastBatchRunProposal { get; private set; }

    public Task<string?> OpenDataFileAsync(string? startDirectory = null) => Task.FromResult(DataFileToOpen);

    public Task<IReadOnlyList<string>> OpenDataFilesAsync(string? startDirectory = null)
        => Task.FromResult(DataFilesToOpen);

    public Task<string?> OpenProfileAsync(string? startDirectory = null) => Task.FromResult(ProfileToOpen);

    public Task<string?> SaveFileAsync(string suggestedName, string? startDirectory = null)
        => Task.FromResult(SaveTarget);

    public Task<SaveChoice> AskSaveChangesAsync(string profileName)
    {
        AskSaveChangesCalls++;
        return Task.FromResult(SaveChangesChoice);
    }

    public Task<NewProfileResult?> AskNewProfileAsync(NewProfileProposal proposal)
        => Task.FromResult(NewProfileResult);

    public Task<string?> AskRenameProfileAsync(RenameProposal proposal) => Task.FromResult(RenameResult);

    public Task<DeleteChoice> AskDeleteProfileAsync(DeleteProposal proposal) => Task.FromResult(DeleteChoice);

    public Task<bool> AskBatchRunAsync(BatchRunProposal proposal)
    {
        LastBatchRunProposal = proposal;
        return Task.FromResult(BatchRunConfirmed);
    }

    public Task<ProfileSummary?> ShowProfilesAsync(ProfilesViewModel viewModel) => Task.FromResult(ProfilesResult);
}
