using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Obfuskation.Core.Configuration;
using Obfuskation.Gui.ViewModels;
using Obfuskation.Gui.Views;

namespace Obfuskation.Gui.Services;

/// <summary>Datei- und Rueckfragedialoge. Die einzige echte Umsetzung von <see cref="IDialogService"/>.</summary>
public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner;

    private static readonly FilePickerFileType DataFiles = new("Datendateien")
    {
        Patterns = ["*.csv", "*.tsv", "*.json", "*.txt", "*.md", "*.log"],
    };

    private static readonly FilePickerFileType ProfileFiles = new("Konfiguration")
    {
        Patterns = ["*.json"],
    };

    private static readonly FilePickerFileType AllFiles = new("Alle Dateien")
    {
        Patterns = ["*"],
    };

    public async Task<string?> OpenDataFileAsync(string? startDirectory = null)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Datei oeffnen",
            AllowMultiple = false,
            FileTypeFilter = [DataFiles, AllFiles],
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> OpenDataFilesAsync(string? startDirectory = null)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Dateien oeffnen",
            AllowMultiple = true,
            FileTypeFilter = [DataFiles, AllFiles],
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<string?> OpenProfileAsync(string? startDirectory = null)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Konfiguration oeffnen",
            AllowMultiple = false,
            FileTypeFilter = [ProfileFiles, AllFiles],
            SuggestedStartLocation = await FolderAsync(startDirectory),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string suggestedName, string? startDirectory = null)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Speichern unter",
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
            FileTypeChoices = [DataFiles, AllFiles],
            SuggestedStartLocation = await FolderAsync(startDirectory),
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    private async Task<IStorageFolder?> FolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            return await _owner.StorageProvider.TryGetFolderFromPathAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // -------------------------------------------------------- Rueckfragen

    public async Task<SaveChoice> AskSaveChangesAsync(string profileName)
    {
        var window = new ConfirmWindow(
            "Ungespeicherte Änderungen",
            $"Das Profil „{profileName}“ hat ungespeicherte Änderungen. Speichern, "
            + "bevor fortgefahren wird?",
            new (string Label, string Result)[]
            {
                ("Abbrechen", "cancel"),
                ("Verwerfen", "discard"),
                ("Speichern", "save"),
            });

        var ergebnis = await window.ShowDialog<string?>(_owner);

        // Ohne Auswahl (Titelleiste geschlossen) gilt die vorsichtige Richtung:
        // wie ausdrueckliches Abbrechen, nie wie Verwerfen.
        return ergebnis switch
        {
            "save" => SaveChoice.Save,
            "discard" => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    public async Task<NewProfileResult?> AskNewProfileAsync(NewProfileProposal proposal)
    {
        var viewModel = new NewProfileViewModel(proposal.SuggestedName, proposal.SampleFilePath);
        var window = new NewProfileWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => window.Close();

        await window.ShowDialog(_owner);

        if (!viewModel.Confirmed)
            return null;

        return new NewProfileResult(viewModel.Name, viewModel.Description, viewModel.TargetPath, viewModel.OpenExisting);
    }

    public async Task<string?> AskRenameProfileAsync(RenameProposal proposal)
    {
        var viewModel = new RenameProfileViewModel(proposal);
        var window = new RenameProfileWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => window.Close();

        await window.ShowDialog(_owner);

        return viewModel.Confirmed ? viewModel.NewName : null;
    }

    public async Task<DeleteChoice> AskDeleteProfileAsync(DeleteProposal proposal)
    {
        var tabellenHinweis = proposal.MappingStoreExists
            ? $"Die Ersetzungstabelle unter {proposal.MappingStorePath} enthält sämtliche Echtwerte. "
              + "Wird sie mitgelöscht, ist der Rückweg zu den Originaldaten unmöglich."
            : $"Unter {proposal.MappingStorePath} besteht noch keine Ersetzungstabelle.";

        var window = new ConfirmWindow(
            "Profil löschen",
            $"Profil „{proposal.Name}“ endgültig löschen? Das lässt sich nicht rückgängig machen.\n\n"
            + tabellenHinweis,
            new (string Label, string Result)[]
            {
                ("Abbrechen", "cancel"),
                ("Nur Profil", "profileOnly"),
                ("Profil und Tabelle", "profileAndMapping"),
            });

        var ergebnis = await window.ShowDialog<string?>(_owner);

        return ergebnis switch
        {
            "profileOnly" => DeleteChoice.ProfileOnly,
            "profileAndMapping" => DeleteChoice.ProfileAndMapping,
            _ => DeleteChoice.Cancel,
        };
    }

    public async Task<bool> AskBatchRunAsync(BatchRunProposal proposal)
    {
        var dateiwort = proposal.FileCount == 1 ? "1 Datei" : $"{proposal.FileCount} Dateien";
        var ueberschreibenHinweis = proposal.OverwriteCount switch
        {
            0 => "",
            1 => " Dabei wird 1 schon vorhandene Zieldatei überschrieben.",
            var n => $" Dabei werden {n} schon vorhandene Zieldateien überschrieben.",
        };

        var window = new ConfirmWindow(
            "Sammellauf",
            $"{dateiwort} werden verarbeitet, jede Ausgabe entsteht neben ihrer Eingabedatei mit dem "
            + $"Zusatz „.{proposal.Marker}“.{ueberschreibenHinweis} "
            + "Diese eine Rückfrage gilt für alle Dateien — danach folgt keine weitere.",
            new (string Label, string Result)[]
            {
                ("Abbrechen", "cancel"),
                ("Erzeugen", "create"),
            });

        var ergebnis = await window.ShowDialog<string?>(_owner);
        return ergebnis == "create";
    }

    public async Task<ProfileSummary?> ShowProfilesAsync(ProfilesViewModel viewModel)
    {
        var window = new ProfilesWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => window.Close();

        await window.ShowDialog(_owner);

        return viewModel.ChosenProfile;
    }

    /// <summary>
    /// Vorschlag fuer den Namen der Ausgabedatei. Der Zusatz macht auf einen
    /// Blick klar, welche der beiden Dateien das Pseudonymisat ist — die
    /// Verwechslung waere teuer.
    /// </summary>
    public static string SuggestOutputName(string inputPath, string marker = "pseudo")
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);

        // Einen schon vorhandenen Zusatz nicht ein zweites Mal anhaengen.
        if (name.EndsWith("." + marker, StringComparison.OrdinalIgnoreCase))
            return name + extension;

        return $"{name}.{marker}{extension}";
    }
}
