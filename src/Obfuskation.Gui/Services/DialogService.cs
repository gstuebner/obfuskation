using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Obfuskation.Gui.Services;

/// <summary>Datei- und Rueckfragedialoge.</summary>
public sealed class DialogService
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
