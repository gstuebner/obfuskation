using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Obfuskation.Core;
using Obfuskation.Core.Mapping;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Auskunft über die Ersetzungstabelle -- und, seit Plan Teil D, auch das
/// Loeschen einzelner Eintraege oder ganzer Namensraeume.
///
/// Werte bleiben verdeckt, bis sie ausdruecklich eingeblendet werden (immer
/// aus beim Oeffnen) -- die Tabelle enthaelt saemtliche Echtdaten, und wer sie
/// hier ausbreitet, tut das bewusst, nicht als Vorgabe. Pseudonyme lassen
/// sich nicht aendern: nur loeschen, nie bearbeiten -- ein geaendertes
/// Pseudonym haette mit bereits erzeugten Pseudodateien nichts mehr zu tun.
///
/// Fensterfrei wie jedes Ansichtsmodell dieses Projekts: Rueckfragen laufen
/// ueber <see cref="IDialogService"/>, ein eigenes Fenster entsteht hier nicht.
/// </summary>
public sealed class MappingViewModel : ObservableObject
{
    private readonly string _profileName;
    private readonly Func<IDialogService> _dialogs;
    private readonly Action _onChanged;
    private readonly List<MappingEntryViewModel> _selectedEntries = new();

    private bool _valuesVisible;
    private string _searchText = "";
    private MappingNamespaceOption? _selectedNamespaceFilter;
    private string _statusMessage = "";
    private string _totalText = "";
    private string _emptyText = "";

    public MappingViewModel(ObfuscationEngine engine, string profileName, Func<IDialogService> dialogs, Action onChanged)
    {
        _profileName = profileName;
        _dialogs = dialogs;
        _onChanged = onChanged;

        // Pfad und Existenz kommen aus der gemeinsamen Hilfe, damit sie nicht
        // ein zweites Mal berechnet werden muessen -- dieselbe Auskunft steht
        // auch in der Kopfzeile des Hauptfensters.
        StorePath = MappingSummary.For(engine.ResolveMappingStorePath(), profileName).StorePath;

        ToggleValuesCommand = new RelayCommand(() => ValuesVisible = !ValuesVisible);
        RemoveSelectedCommand = new AsyncRelayCommand(
            () => RemoveEntriesAsync(_selectedEntries.ToList(), namespaceName: null),
            () => _selectedEntries.Count > 0);

        if (!File.Exists(StorePath))
        {
            EmptyText = "Es gibt noch keine Ersetzungstabelle. Sie entsteht beim ersten Lauf.";
            Permissions = "—";
            return;
        }

        Permissions = ReadPermissions(StorePath);
        Load();
    }

    public string ProfileName => _profileName;
    public string StorePath { get; }
    public string Permissions { get; } = "";

    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    public string EmptyText
    {
        get => _emptyText;
        private set => SetProperty(ref _emptyText, value);
    }

    public ObservableCollection<MappingNamespaceViewModel> Namespaces { get; } = new();

    public bool IsEmpty => Namespaces.Count == 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasStatusMessage => _statusMessage.Length > 0;

    // -------------------------------------------------------- Werte anzeigen

    /// <summary>
    /// Ob Klartext und Pseudonym eingeblendet sind. Immer <c>false</c> beim
    /// Oeffnen -- die Tabelle enthaelt saemtliche Echtdaten.
    /// </summary>
    public bool ValuesVisible
    {
        get => _valuesVisible;
        set
        {
            if (!SetProperty(ref _valuesVisible, value))
                return;

            RefreshEntries();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
                RefreshEntries();
        }
    }

    public ObservableCollection<MappingNamespaceOption> NamespaceFilterOptions { get; } = new();

    public MappingNamespaceOption? SelectedNamespaceFilter
    {
        get => _selectedNamespaceFilter;
        set
        {
            if (SetProperty(ref _selectedNamespaceFilter, value))
                RefreshEntries();
        }
    }

    /// <summary>Die gefilterten Eintraege -- nur befuellt, waehrend <see cref="ValuesVisible"/> gilt.</summary>
    public ObservableCollection<MappingEntryViewModel> Entries { get; } = new();

    public RelayCommand ToggleValuesCommand { get; }
    public AsyncRelayCommand RemoveSelectedCommand { get; }

    /// <summary>
    /// Meldet die Auswahl der Eintragsliste. <c>ListBox.SelectedItems</c>
    /// gehoert der Liste und laesst sich nicht binden wie ein einzelner Wert
    /// (dieselbe Ueberlegung wie bei <c>MainViewModel.UpdateSelection</c> fuer
    /// die Feldliste) -- die Ansicht meldet die Auswahl deshalb selbst.
    /// </summary>
    public void UpdateSelection(IEnumerable<MappingEntryViewModel> entries)
    {
        _selectedEntries.Clear();
        _selectedEntries.AddRange(entries);

        OnPropertyChanged(nameof(SelectedCount));
        RemoveSelectedCommand.RaiseCanExecuteChanged();
    }

    public int SelectedCount => _selectedEntries.Count;

    // ------------------------------------------------------------- Laden

    /// <summary>Liest Namensraeume und Anzahlen neu ein -- beim Aufbau und nach jedem Loeschen.</summary>
    private void Load()
    {
        Namespaces.Clear();
        NamespaceFilterOptions.Clear();
        NamespaceFilterOptions.Add(MappingNamespaceOption.All);

        try
        {
            using var store = MappingStore.Open(StorePath, _profileName, readOnly: true, allowInsideGitWorkingTree: true);

            foreach (var name in store.NamespaceNames.OrderBy(n => n, StringComparer.Ordinal))
            {
                var anzahl = store.Entries(name).Count;
                Namespaces.Add(new MappingNamespaceViewModel(name, anzahl, () => RemoveNamespaceAsync(name)));
                NamespaceFilterOptions.Add(new MappingNamespaceOption(name, name));
            }

            var summary = MappingSummary.For(StorePath, _profileName);
            TotalText = summary.Text + " insgesamt";
            EmptyText = "Die Tabelle ist noch leer.";
        }
        catch (Exception ex) when (ex is MappingConflictException or MappingLockedException
                                       or IOException or UnauthorizedAccessException)
        {
            EmptyText = ex.Message;
            TotalText = "";
        }

        OnPropertyChanged(nameof(IsEmpty));

        _selectedNamespaceFilter = NamespaceFilterOptions[0];
        OnPropertyChanged(nameof(SelectedNamespaceFilter));

        RefreshEntries();
    }

    /// <summary>Baut die gefilterte Eintragsliste neu auf -- nur mit Wirkung, waehrend <see cref="ValuesVisible"/> gilt.</summary>
    private void RefreshEntries()
    {
        Entries.Clear();
        _selectedEntries.Clear();
        OnPropertyChanged(nameof(SelectedCount));
        RemoveSelectedCommand.RaiseCanExecuteChanged();

        if (!_valuesVisible || Namespaces.Count == 0)
            return;

        var suchtext = _searchText.Trim();
        var namensraum = _selectedNamespaceFilter?.Name;

        try
        {
            using var store = MappingStore.Open(StorePath, _profileName, readOnly: true, allowInsideGitWorkingTree: true);

            foreach (var name in store.NamespaceNames.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (namensraum is not null && !string.Equals(name, namensraum, StringComparison.Ordinal))
                    continue;

                foreach (var (plaintext, pseudonym) in store.Entries(name).OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    if (suchtext.Length > 0
                        && plaintext.IndexOf(suchtext, StringComparison.OrdinalIgnoreCase) < 0
                        && pseudonym.IndexOf(suchtext, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Entries.Add(new MappingEntryViewModel(name, plaintext, pseudonym));
                }
            }
        }
        catch (Exception ex) when (ex is MappingConflictException or MappingLockedException
                                       or IOException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    // ------------------------------------------------------------ Loeschen

    private async Task RemoveEntriesAsync(IReadOnlyList<MappingEntryViewModel> entries, string? namespaceName)
    {
        if (entries.Count == 0)
            return;

        var bestaetigt = await _dialogs().AskRemoveMappingEntriesAsync(entries.Count, namespaceName);
        if (!bestaetigt)
            return;

        if (!TrySave(store =>
            {
                foreach (var entry in entries)
                    store.Remove(entry.Namespace, entry.Plaintext);
            }))
        {
            return;
        }

        Load();
        _onChanged();
    }

    private async Task RemoveNamespaceAsync(string namespaceName)
    {
        var anzahl = Namespaces.FirstOrDefault(n => string.Equals(n.Name, namespaceName, StringComparison.Ordinal))?.Count ?? 0;
        if (anzahl == 0)
            return;

        var bestaetigt = await _dialogs().AskRemoveMappingEntriesAsync(anzahl, namespaceName);
        if (!bestaetigt)
            return;

        if (!TrySave(store => store.RemoveNamespace(namespaceName)))
            return;

        Load();
        _onChanged();
    }

    /// <summary>
    /// Oeffnet die Tabelle schreibend (sperrt sie damit gegen einen
    /// parallelen Lauf), fuehrt <paramref name="aendern"/> aus und speichert.
    /// Eine <see cref="MappingLockedException"/> bekommt die vom Plan
    /// vorgesehene eigene Meldung, alles andere die Meldung der Ausnahme
    /// selbst.
    /// </summary>
    private bool TrySave(Action<MappingStore> aendern)
    {
        try
        {
            using var store = MappingStore.Open(StorePath, _profileName, readOnly: false, allowInsideGitWorkingTree: true);
            aendern(store);
            store.Save();
            return true;
        }
        catch (MappingLockedException)
        {
            StatusMessage = "Die Tabelle ist gerade durch einen Lauf gesperrt.";
            OnPropertyChanged(nameof(HasStatusMessage));
            return false;
        }
        catch (Exception ex) when (ex is MappingConflictException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
            OnPropertyChanged(nameof(HasStatusMessage));
            return false;
        }
    }

    private static string ReadPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "unter Windows nicht anwendbar";

        try
        {
            var mode = File.GetUnixFileMode(path);

            var owner = ((int)(mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite
                                       | UnixFileMode.UserExecute)) >> 6) & 7;
            var group = ((int)(mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                                       | UnixFileMode.GroupExecute)) >> 3) & 7;
            var others = (int)(mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite
                                       | UnixFileMode.OtherExecute)) & 7;

            var text = $"0{owner}{group}{others}";
            return group == 0 && others == 0
                ? text + "  (nur für Sie lesbar)"
                : text + "  — ACHTUNG: auch für andere lesbar";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "nicht lesbar";
        }
    }
}

/// <summary>Ein Namensraum in der Kopfliste, mit Anzahl und dem Knopf "Leeren…".</summary>
public sealed class MappingNamespaceViewModel : ObservableObject
{
    public MappingNamespaceViewModel(string name, int count, Func<Task> onClear)
    {
        Name = name;
        Count = count;
        ClearCommand = new AsyncRelayCommand(onClear, () => Count > 0);
    }

    public string Name { get; }
    public int Count { get; }
    public AsyncRelayCommand ClearCommand { get; }
}

/// <summary>Ein Eintrag der Namensraum-Auswahl -- <see cref="Name"/> <c>null</c> bedeutet "alle".</summary>
public sealed record MappingNamespaceOption(string? Name, string Label)
{
    public static MappingNamespaceOption All { get; } = new(null, "alle");

    public override string ToString() => Label;
}

/// <summary>Eine Zeile der eingeblendeten Ersetzungstabelle.</summary>
public sealed record MappingEntryViewModel(string Namespace, string Plaintext, string Pseudonym);
