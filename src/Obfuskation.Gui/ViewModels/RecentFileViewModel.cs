namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Ein Eintrag der Schnellwahl: eine dem Profil laut <see cref="Core.Configuration.ProfileIndex"/>
/// bekannte Datendatei.
///
/// Eigener Befehl je Eintrag statt eines gemeinsamen Befehls mit Parameter:
/// <see cref="RelayCommand"/> und <see cref="AsyncRelayCommand"/> nehmen keinen
/// Parameter entgegen -- <c>Execute(object?)</c> verwirft ihn. Ein
/// <c>RelayCommand&lt;T&gt;</c> nur fuer diesen einen Zweck einzufuehren waere
/// mehr Umbau, als die Aufgabe verlangt, und ein Befehl je Listeneintrag passt
/// zum Bestand, der auch sonst kleine Ansichtsmodelle je Eintrag verwendet
/// (<see cref="NamedCount"/>, <see cref="GeneratorOption"/>).
/// </summary>
public sealed class RecentFileViewModel
{
    public RecentFileViewModel(string fullPath, bool isCurrent, Func<string, Task> openAsync)
    {
        FullPath = fullPath;
        DisplayName = Path.GetFileName(fullPath);
        Exists = File.Exists(fullPath);
        IsCurrent = isCurrent;

        // Der Rueckruf laedt ueber GuardedAsync -- eine zwischenzeitlich
        // verschobene oder gesperrte Datei soll sich melden, nicht die
        // Oberflaeche werfen.
        OpenCommand = new AsyncRelayCommand(() => openAsync(FullPath), () => Exists && !IsCurrent);
    }

    public string FullPath { get; }
    public string DisplayName { get; }

    /// <summary>
    /// Ob die Datei noch existiert. Wird nicht mehr existierende Eintraege
    /// stillschweigend ausgeblendet, wuesste niemand mehr, ob das Programm eine
    /// verschobene Datei je kannte -- deshalb wird hier nur ausgegraut
    /// (<see cref="OpenCommand"/> laesst sich nicht ausfuehren), nicht entfernt.
    /// </summary>
    public bool Exists { get; }

    public bool IsCurrent { get; }

    public AsyncRelayCommand OpenCommand { get; }
}
