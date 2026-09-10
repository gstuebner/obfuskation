using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Startseite: drei Absichtskarten statt eines leeren Fensters, dazu ein
/// schneller Weg zum zuletzt benutzten Profil.
///
/// Kein eigener Zustand ausser der Anzeige des zuletzt benutzten Profils: alle
/// Befehle sind dieselben Befehlsobjekte, die auch die Kopfzeile verwendet
/// (<see cref="MainViewModel"/> erzeugt sie einmal und reicht sie hierher
/// durch), damit "Text säubern" auf der Startseite und ein spaeterer Weg zur
/// Textansicht nie auseinanderlaufen koennen.
/// </summary>
public sealed class StartViewModel : ObservableObject
{
    private readonly GuiSettings _settings;
    private readonly Func<string, Task> _openRecentProfile;

    public StartViewModel(
        GuiSettings settings,
        RelayCommand textCommand,
        RelayCommand replyCommand,
        AsyncRelayCommand filesCommand,
        RelayCommand helpCommand,
        Func<string, Task> openRecentProfile)
    {
        _settings = settings;
        _openRecentProfile = openRecentProfile;

        TextCommand = textCommand;
        ReplyCommand = replyCommand;
        FilesCommand = filesCommand;
        HelpCommand = helpCommand;

        OpenRecentProfileCommand = new AsyncRelayCommand(
            () => _recentProfilePath is null ? Task.CompletedTask : _openRecentProfile(_recentProfilePath),
            () => HasRecentProfile);

        Refresh();
    }

    /// <summary>Karte "Text säubern".</summary>
    public RelayCommand TextCommand { get; }

    /// <summary>Karte "Antwort zurückholen" -- dieselbe Ansicht, umgekehrte Richtung.</summary>
    public RelayCommand ReplyCommand { get; }

    /// <summary>Karte "Dateien pseudonymisieren".</summary>
    public AsyncRelayCommand FilesCommand { get; }

    public RelayCommand HelpCommand { get; }
    public AsyncRelayCommand OpenRecentProfileCommand { get; }

    private string? _recentProfilePath;

    public string? RecentProfileName { get; private set; }

    public bool HasRecentProfile => _recentProfilePath is not null;

    /// <summary>
    /// Liest das zuletzt benutzte, noch vorhandene Profil neu ein. Aufgerufen
    /// jedesmal, wenn die Startseite (wieder) angezeigt wird -- so faellt eine
    /// zwischenzeitliche Aenderung (ein neu angelegtes oder anderswo
    /// geoeffnetes Profil) nicht unter den Tisch, ohne dass diese Klasse
    /// selbst auf Aenderungen von <see cref="GuiSettings"/> horchen muesste.
    /// </summary>
    public void Refresh()
    {
        _recentProfilePath = _settings.RecentProfiles.FirstOrDefault(File.Exists);
        RecentProfileName = _recentProfilePath is null ? null : Path.GetFileNameWithoutExtension(_recentProfilePath);

        OnPropertyChanged(nameof(RecentProfileName));
        OnPropertyChanged(nameof(HasRecentProfile));
        OpenRecentProfileCommand.RaiseCanExecuteChanged();
    }
}
