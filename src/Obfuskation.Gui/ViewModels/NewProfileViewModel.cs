using Obfuskation.Core;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Das Anlegen-Formular: Name, Beschreibung, Ablageort.
///
/// Fensterfrei wie die uebrigen Ansichtsmodelle -- den eigentlichen
/// "Anderer Ort..."-Dateidialog oeffnet die Ansicht und traegt das Ergebnis
/// ueber <see cref="SetCustomTargetPath"/> ein. Bestaetigt wird ueber
/// <see cref="CloseRequested"/>, das Ergebnis liest der Aufrufer danach aus
/// <see cref="Confirmed"/>, <see cref="OpenExisting"/> und den Feldern.
/// </summary>
public sealed class NewProfileViewModel : ObservableObject
{
    private string _name;
    private string? _description;
    private string _targetPath;
    private bool _customTargetPath;

    public NewProfileViewModel(string suggestedName, string sampleFilePath)
    {
        SampleFilePath = sampleFilePath;
        _name = suggestedName;
        _targetPath = PathHelper.DefaultProfilePath(_name);

        // Existiert die Zieldatei schon, darf "Anlegen" sie nicht stillschweigend
        // ueberschreiben -- dafuer gibt es "Stattdessen oeffnen". Ohne diese
        // Sperre wuerde ein Klick auf Anlegen ein bestehendes Regelwerk loeschen.
        OkCommand = new RelayCommand(Confirm, () => !string.IsNullOrWhiteSpace(_name) && !TargetExists);
        OpenExistingCommand = new RelayCommand(ConfirmOpenExisting);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public string SampleFilePath { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
                return;

            // Der Ablageort laeuft live mit, solange der Anwender ihn nicht von
            // Hand ueberschrieben hat -- ansonsten wuerde eine spaetere
            // Namensaenderung eine bewusst gewaehlte Ablage stillschweigend
            // wieder verwerfen.
            if (!_customTargetPath)
                TargetPath = PathHelper.DefaultProfilePath(_name);

            OnPropertyChanged(nameof(MappingStoreHint));
            OkCommand.RaiseCanExecuteChanged();
        }
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string TargetPath
    {
        get => _targetPath;
        private set
        {
            if (SetProperty(ref _targetPath, value))
            {
                OnPropertyChanged(nameof(TargetExists));
                OkCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Setzt den Ablageort von Hand; folgt danach nicht mehr dem Namen.</summary>
    public void SetCustomTargetPath(string path)
    {
        _customTargetPath = true;
        TargetPath = path;
    }

    /// <summary>Erklaerzeile, wo die Ersetzungstabelle entstehen wird.</summary>
    public string MappingStoreHint
        => $"Die Ersetzungstabelle entsteht unter {PathHelper.DefaultMappingStorePath(_name)}";

    public bool TargetExists => File.Exists(_targetPath);

    /// <summary>Ob der Anwender bestaetigt hat -- ueber OK oder "Stattdessen oeffnen".</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Statt anzulegen: das bestehende Profil am Zielort oeffnen.</summary>
    public bool OpenExisting { get; private set; }

    public RelayCommand OkCommand { get; }
    public RelayCommand OpenExistingCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action? CloseRequested;

    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    private void ConfirmOpenExisting()
    {
        Confirmed = true;
        OpenExisting = true;
        CloseRequested?.Invoke();
    }
}
