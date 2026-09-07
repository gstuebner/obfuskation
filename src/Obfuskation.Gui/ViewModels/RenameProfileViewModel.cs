using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Rueckfrage vorm Umbenennen: zeigt zuerst, was mit der Ersetzungstabelle
/// geschieht, dann ein Namensfeld. Fensterfrei wie die uebrigen Ansichtsmodelle.
/// </summary>
public sealed class RenameProfileViewModel : ObservableObject
{
    private string _newName;

    public RenameProfileViewModel(RenameProposal proposal)
    {
        Proposal = proposal;
        _newName = proposal.SuggestedName;

        OkCommand = new RelayCommand(Confirm, () => !string.IsNullOrWhiteSpace(_newName));
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public RenameProposal Proposal { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value))
                OkCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Was beim Bestaetigen mit der Ersetzungstabelle geschieht.</summary>
    public string Explanation =>
        $"Die Ersetzungstabelle bleibt unter {Proposal.MappingStorePath} und behält alle Einträge. " +
        "Die Profildatei wird entsprechend umbenannt.";

    public bool Confirmed { get; private set; }

    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action? CloseRequested;

    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }
}
