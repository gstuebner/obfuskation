using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    public async Task<IReadOnlyList<PatternSuggestionAcceptance>?> ShowPatternSuggestionsAsync(
        PatternSuggestionsViewModel viewModel)
    {
        var window = new PatternSuggestionsWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => window.Close();

        await window.ShowDialog(_owner);

        return viewModel.Confirmed ? viewModel.Accepted : null;
    }

    public async Task<bool> ShowAlwaysReplaceAsync(AlwaysReplaceViewModel viewModel)
    {
        var window = new AlwaysReplaceWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => window.Close();

        // "In den Einstellungen bearbeiten…" schliesst diesen schlichten
        // Dialog; MainViewModel oeffnet danach die Einstellungen selbst
        // (siehe AlwaysReplaceViewModel.EditRulesRequested) -- hier ist nichts
        // weiter zu tun.
        await window.ShowDialog(_owner);

        return viewModel.Confirmed;
    }

    public async Task<bool> AskRemoveTextRuleAsync(string ruleName, string? extensionPath)
    {
        var geltung = extensionPath is null
            ? "Sie gilt danach in diesem Projekt nicht mehr."
            : $"Sie gilt danach in keinem Projekt mehr (entfällt aus {extensionPath}).";

        var window = new ConfirmWindow(
            "Regel löschen",
            $"Die Regel „{ruleName}“ wird gelöscht. {geltung} "
            + "Bereits vergebene Pseudonyme bleiben in der Ersetzungstabelle — "
            + "zurückübersetzen lässt sich weiterhin alles.",
            new (string Label, string Result)[]
            {
                ("Abbrechen", "cancel"),
                ("Löschen", "delete"),
            });

        // Ohne Auswahl (Titelleiste geschlossen) gilt die vorsichtige Richtung.
        return await window.ShowDialog<string?>(_owner) == "delete";
    }

    public async Task<bool> AskRemoveMappingEntriesAsync(int count, string? namespaceName)
    {
        var mengenwort = namespaceName is not null
            ? $"Alle {count} Einträge im Namensraum „{namespaceName}“"
            : count == 1 ? "1 ausgewählter Eintrag" : $"{count} ausgewählte Einträge";

        var window = new ConfirmWindow(
            "Einträge löschen",
            $"{mengenwort} werden gelöscht. Bereits erzeugte Pseudodateien, die diese Pseudonyme enthalten, "
            + "lassen sich an diesen Stellen nicht mehr zurückübersetzen. Ein neuer Lauf vergibt für den "
            + "Klartext voraussichtlich wieder dasselbe Pseudonym.",
            new (string Label, string Result)[]
            {
                ("Abbrechen", "cancel"),
                ("Löschen", "delete"),
            });

        return await window.ShowDialog<string?>(_owner) == "delete";
    }

    public async Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };

        viewModel.OpenFolderRequested += path => OpenWithShell(path);
        viewModel.OpenEditorRequested += path => OpenWithShell(path);

        // Dasselbe Vorgehen wie beim Hauptfenster (siehe App.OnFrameworkInitializationCompleted):
        // Closing kann nicht auf eine Task warten, darum wird beim ersten
        // Aufruf abgebrochen und die Rueckfrage ueber den Dispatcher
        // nachgeholt; faellt sie nicht auf "verwerfen" oder "übernehmen" aus,
        // bleibt das Fenster offen.
        var schliessenBestaetigt = false;

        viewModel.CloseRequested += () =>
        {
            schliessenBestaetigt = true;
            window.Close();
        };

        window.Closing += (_, e) =>
        {
            if (schliessenBestaetigt || !viewModel.HasUnsavedChanges)
                return;

            e.Cancel = true;

            Dispatcher.UIThread.Post(async () =>
            {
                var confirm = new ConfirmWindow(
                    "Ungespeicherte Änderungen",
                    "Die Einstellungen haben ungespeicherte Änderungen. Übernehmen, bevor das Fenster schließt?",
                    new (string Label, string Result)[]
                    {
                        ("Weiter bearbeiten", "continue"),
                        ("Verwerfen", "discard"),
                        ("Übernehmen", "apply"),
                    });

                var ergebnis = await confirm.ShowDialog<string?>(window);

                switch (ergebnis)
                {
                    case "apply":
                        viewModel.ApplyCommand.Execute(null);
                        // Schlaegt die Pruefung fehl, bleibt HasUnsavedChanges
                        // wahr und CloseRequested wurde nicht ausgeloest -- das
                        // Fenster bleibt dann offen, mit der Fehlerliste.
                        break;

                    case "discard":
                        schliessenBestaetigt = true;
                        window.Close();
                        break;
                }
            });
        };

        await window.ShowDialog(_owner);

        return viewModel.Applied;
    }

    /// <summary>
    /// Oeffnet einen Pfad mit dem, was das Betriebssystem dafuer vorsieht --
    /// den Ordner der Erweiterungsdatei im Dateimanager, die Datei selbst im
    /// hinterlegten Editor. Ein Fehlschlag (kein grafischer Handler
    /// eingerichtet) ist kein Programmfehler und wird nur in der Statuszeile
    /// des Einstellungsfensters sichtbar -- hier gibt es keine bessere
    /// Reaktion als ihn zu schlucken.
    /// </summary>
    private static void OpenWithShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // Kein Handler eingerichtet, oder der Pfad existiert (noch) nicht --
            // beides kein Grund, das Fenster abzuwuergen.
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
