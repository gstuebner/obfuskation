using System.Collections.ObjectModel;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Mapping;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Profiluebersicht: alle bekannten Profile, sortier- und durchsuchbar.
///
/// Fensterfrei wie die uebrigen Ansichtsmodelle -- Rueckfragen (Umbenennen,
/// Datei waehlen von Hand) kommen ueber <see cref="IDialogService"/>, ein
/// eigenes Fenster oeffnet diese Klasse nirgends. Bestaetigt der Anwender eine
/// Wahl, wird <see cref="CloseRequested"/> ausgeloest; der Aufrufer liest dann
/// <see cref="ChosenProfile"/>.
/// </summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly GuiSettings _settings;
    private readonly Func<IDialogService> _dialogs;
    private readonly string? _currentSessionPath;
    private readonly bool _currentSessionHasUnsavedChanges;
    private readonly ProfileIndex _index;

    private string _filterText = "";
    private ProfileRowViewModel? _selected;
    private string? _errorText;

    public ProfilesViewModel(
        GuiSettings settings,
        Func<IDialogService> dialogs,
        string? currentSessionPath,
        bool currentSessionHasUnsavedChanges)
    {
        _settings = settings;
        _dialogs = dialogs;
        _currentSessionPath = currentSessionPath;
        _currentSessionHasUnsavedChanges = currentSessionHasUnsavedChanges;

        _index = ProfileIndex.Load();
        _index.Prune();
        _index.Save();

        SortByNameCommand = new RelayCommand(() => ToggleSort(ProfileSortKey.Name));
        SortByLastUsedCommand = new RelayCommand(() => ToggleSort(ProfileSortKey.LastUsed));
        SortByModifiedCommand = new RelayCommand(() => ToggleSort(ProfileSortKey.Modified));

        OpenCommand = new RelayCommand(Open, () => _selected is { CanOpen: true });
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => _selected is { CanOpen: true });
        RemoveCommand = new RelayCommand(Remove, () => _selected is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => _selected is not null);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync);

        Refresh();
    }

    public ObservableCollection<ProfileRowViewModel> Rows { get; } = new();

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                Refresh();
        }
    }

    public ProfileRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                RaiseCommandStates();
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasErrorText));
        }
    }

    public bool HasErrorText => !string.IsNullOrEmpty(_errorText);

    /// <summary>Kopfzeilenbeschriftung samt Sortierpfeil, wo dieser Schluessel gerade greift.</summary>
    public string NameHeader => HeaderText("Name", ProfileSortKey.Name);
    public string LastUsedHeader => HeaderText("Zuletzt benutzt", ProfileSortKey.LastUsed);
    public string ModifiedHeader => HeaderText("Geändert", ProfileSortKey.Modified);

    public RelayCommand OpenCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand BrowseCommand { get; }
    public RelayCommand SortByNameCommand { get; }
    public RelayCommand SortByLastUsedCommand { get; }
    public RelayCommand SortByModifiedCommand { get; }

    /// <summary>Das zum Oeffnen gewaehlte Profil, gesetzt kurz vor <see cref="CloseRequested"/>.</summary>
    public ProfileSummary? ChosenProfile { get; private set; }

    /// <summary>Bittet die Ansicht, das Fenster zu schliessen.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Das gerade im Hauptfenster geoeffnete Profil wurde umbenannt; der neue
    /// Pfad der Profildatei. Die laufende Sitzung muss das nachziehen, ohne
    /// dass diese Klasse selbst etwas ueber Sitzungen wuesste.
    /// </summary>
    public event Action<string>? CurrentSessionRenamed;

    private string HeaderText(string label, ProfileSortKey key)
    {
        if (_settings.ProfileSortKey != key)
            return label;

        return label + (_settings.ProfileSortDescending ? " ▼" : " ▲");
    }

    private void ToggleSort(ProfileSortKey key)
    {
        if (_settings.ProfileSortKey == key)
            _settings.ProfileSortDescending = !_settings.ProfileSortDescending;
        else
        {
            _settings.ProfileSortKey = key;
            _settings.ProfileSortDescending = true;
        }

        _settings.Save();

        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(LastUsedHeader));
        OnPropertyChanged(nameof(ModifiedHeader));
        Refresh();
    }

    private void Refresh()
    {
        var summaries = ProfileCatalog.Collect(_index, _settings.RecentProfiles);

        // Ausgeblendete Profile zuerst heraussieben: ProfileCatalog.Collect
        // zaehlt den zentralen Profilordner immer mit auf und wuesste nichts
        // von einem zuvor "entfernten" Eintrag -- ohne diesen Schritt kaeme er
        // sofort wieder in die Liste, nur ohne Nutzungsdaten (der eigentliche
        // Fehlerbericht).
        IEnumerable<ProfileSummary> gefiltert = summaries.Where(s => !_settings.IsHidden(s.Path));
        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            var suchtext = _filterText.Trim();
            gefiltert = gefiltert.Where(s =>
                Contains(s.Name, suchtext) ||
                Contains(s.Description, suchtext) ||
                s.DataFiles.Any(f => Contains(f.Path, suchtext)));
        }

        var vorherGewaehlt = _selected?.Summary.Path;

        Rows.Clear();
        foreach (var summary in Sort(gefiltert))
            Rows.Add(new ProfileRowViewModel(summary));

        Selected = Rows.FirstOrDefault(r =>
            string.Equals(r.Summary.Path, vorherGewaehlt, StringComparison.Ordinal))
            ?? Rows.FirstOrDefault();
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<ProfileSummary> Sort(IEnumerable<ProfileSummary> summaries)
    {
        IOrderedEnumerable<ProfileSummary> sortiert = _settings.ProfileSortKey switch
        {
            ProfileSortKey.Name => _settings.ProfileSortDescending
                ? summaries.OrderByDescending(s => s.Name, StringComparer.OrdinalIgnoreCase)
                : summaries.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
            ProfileSortKey.Modified => _settings.ProfileSortDescending
                ? summaries.OrderByDescending(s => s.ModifiedUtc)
                : summaries.OrderBy(s => s.ModifiedUtc),
            _ => _settings.ProfileSortDescending
                ? summaries.OrderByDescending(s => s.LastUsedUtc ?? DateTimeOffset.MinValue)
                : summaries.OrderBy(s => s.LastUsedUtc ?? DateTimeOffset.MinValue),
        };

        return sortiert;
    }

    private void Open()
    {
        if (_selected is not { CanOpen: true } row)
            return;

        ChosenProfile = row.Summary;
        CloseRequested?.Invoke();
    }

    private async Task BrowseAsync()
    {
        var pfad = await _dialogs().OpenProfileAsync(PathHelper.ProfileDirectory);
        if (pfad is null)
            return;

        // Denselben Sammelweg wie die Uebersicht selbst nutzen, statt die
        // Erkennung eines gueltigen Profils ein zweites Mal nachzubauen --
        // liefert dabei auch gleich einen Fehlereintrag, falls die gewaehlte
        // Datei sich nicht laden laesst.
        var gefunden = ProfileCatalog.Collect(_index, new[] { pfad }).FirstOrDefault();
        if (gefunden is null)
            return;

        // Ein von Hand gewaehltes Profil, das zuvor ausgeblendet war, muss
        // wieder erreichbar sein -- sonst gaebe es fuer ein "entferntes" Profil
        // gar keinen Weg mehr zurueck in die Uebersicht.
        _settings.UnhideProfile(pfad);
        _settings.Save();

        ChosenProfile = gefunden;
        CloseRequested?.Invoke();
    }

    private void Remove()
    {
        if (_selected is not { } row)
            return;

        var pfad = row.Summary.Path;

        _index.Forget(pfad);
        _index.Save();

        _settings.RecentProfiles.RemoveAll(p =>
            string.Equals(Path.GetFullPath(p), Path.GetFullPath(pfad), StringComparison.Ordinal));

        // Der zentrale Profilordner wird von ProfileCatalog.Collect immer mit
        // aufgezaehlt -- ohne dieses dauerhafte Ausblenden kaeme der Eintrag
        // beim naechsten Refresh sofort wieder herein, nur ohne
        // Nutzungsdaten. Genau das war der gemeldete Fehler.
        _settings.HideProfile(pfad);
        _settings.Save();

        Refresh();
    }

    private async Task DeleteAsync()
    {
        if (_selected is not { } row)
            return;

        var summary = row.Summary;

        // Das gerade offene Profil zu loeschen waere gefaehrlich: die Sitzung
        // im Hauptfenster wuerde weiter mit einer verschwundenen Datei
        // arbeiten. Sicherer, den Vorgang ganz zu verweigern, statt eine
        // Rueckfrage zu bauen, die dort ohnehin niemand beantworten kann.
        if (IsCurrentSession(summary.Path))
        {
            ErrorText = "Das gerade geöffnete Profil lässt sich nicht löschen. " +
                        "Erst ein anderes Profil öffnen, dann erneut versuchen.";
            return;
        }

        var proposal = new DeleteProposal(
            summary.Name, summary.Path, summary.MappingStorePath, summary.MappingStoreExists);
        var wahl = await _dialogs().AskDeleteProfileAsync(proposal);
        if (wahl == DeleteChoice.Cancel)
            return;

        try
        {
            File.Delete(summary.Path);

            if (wahl == DeleteChoice.ProfileAndMapping && File.Exists(summary.MappingStorePath))
            {
                File.Delete(summary.MappingStorePath);

                // Die Sperrdatei ueberlebt einen Absturz waehrend eines Laufs --
                // ohne sie mitzuloeschen bliebe ein Geisterschloss zurueck, das
                // MappingStore.AcquireLock spaeter faelschlich als belegt sieht.
                var sperrdatei = summary.MappingStorePath + ".lock";
                if (File.Exists(sperrdatei))
                    File.Delete(sperrdatei);
            }

            _index.Forget(summary.Path);
            _index.Save();

            _settings.RecentProfiles.RemoveAll(p =>
                string.Equals(Path.GetFullPath(p), Path.GetFullPath(summary.Path), StringComparison.Ordinal));
            _settings.UnhideProfile(summary.Path);
            _settings.Save();

            ErrorText = null;
        }
        catch (Exception ex) when (ex is ConfigurationException
                                       or MappingConflictException
                                       or MappingLockedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            ErrorText = ex.Message;
        }

        Refresh();
    }

    private async Task RenameAsync()
    {
        if (_selected is not { CanOpen: true } row)
            return;

        var summary = row.Summary;

        // Ein Umbenennen, das die gerade offene Sitzung unter sich veraendert,
        // waere gefaehrlich, solange dort noch ungesicherte Regeln liegen: die
        // anschliessende Nachfuehrung (LoadProfile) wuerde diese Regeln
        // stillschweigend verwerfen.
        if (IsCurrentSession(summary.Path) && _currentSessionHasUnsavedChanges)
        {
            ErrorText = "Das gerade geöffnete Profil hat ungespeicherte Änderungen. " +
                        "Erst speichern oder verwerfen, dann umbenennen.";
            return;
        }

        var proposal = new RenameProposal(summary.Name, summary.Name, summary.MappingStorePath);
        var neuerName = await _dialogs().AskRenameProfileAsync(proposal);
        if (string.IsNullOrWhiteSpace(neuerName))
            return;

        try
        {
            var profile = ProfileStore.Load(summary.Path);
            var outcome = ProfileRenamer.Rename(profile, neuerName);
            ProfileStore.Save(profile, summary.Path);

            var zielPfad = DetermineFilePathAfterRename(summary.Path, outcome.OldName, outcome.NewName);

            if (!string.Equals(zielPfad, summary.Path, StringComparison.Ordinal))
            {
                File.Move(summary.Path, zielPfad);

                _index.MoveProfile(summary.Path, zielPfad);
                UpdateRecentProfilePath(summary.Path, zielPfad);

                if (IsCurrentSession(summary.Path))
                    CurrentSessionRenamed?.Invoke(zielPfad);
            }
            else if (IsCurrentSession(summary.Path))
            {
                // Nur der Name im Profil hat sich geaendert, nicht die Datei --
                // die Sitzung muss trotzdem neu geladen werden, sonst zeigt sie
                // weiter den alten Namen.
                CurrentSessionRenamed?.Invoke(summary.Path);
            }

            _index.RecordProfileUse(zielPfad);
            _index.Save();
            _settings.Save();

            ErrorText = null;
        }
        catch (Exception ex) when (ex is ConfigurationException
                                       or MappingConflictException
                                       or MappingLockedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            ErrorText = ex.Message;
        }

        Refresh();
    }

    private bool IsCurrentSession(string path)
        => _currentSessionPath is not null
           && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_currentSessionPath), StringComparison.Ordinal);

    /// <summary>
    /// Ob die Profildatei selbst mit umbenannt werden soll: nur wenn sie im
    /// zentralen Ordner liegt <b>und</b> ihr Dateiname aus dem alten Profilnamen
    /// abgeleitet war. Eine von Hand woanders abgelegte oder umbenannte Datei
    /// bleibt unangetastet -- nur ihr Inhalt aendert sich.
    /// </summary>
    private static string DetermineFilePathAfterRename(string oldPath, string oldName, string newName)
    {
        var directory = Path.GetDirectoryName(oldPath)!;
        var isCentral = string.Equals(
            Path.GetFullPath(directory), Path.GetFullPath(PathHelper.ProfileDirectory), StringComparison.Ordinal);

        var fileStem = Path.GetFileNameWithoutExtension(oldPath);
        var nameWasDerived = string.Equals(fileStem, PathHelper.SanitizeName(oldName), StringComparison.Ordinal);

        if (!isCentral || !nameWasDerived)
            return oldPath;

        var candidate = PathHelper.DefaultProfilePath(newName);

        // Zielname schon vergeben (ein anderes Profil): lieber die alte Datei
        // stehen lassen, als ein fremdes Profil zu ueberschreiben.
        return File.Exists(candidate) ? oldPath : candidate;
    }

    private void UpdateRecentProfilePath(string oldPath, string newPath)
    {
        var index = _settings.RecentProfiles.FindIndex(p =>
            string.Equals(Path.GetFullPath(p), Path.GetFullPath(oldPath), StringComparison.Ordinal));

        if (index >= 0)
            _settings.RecentProfiles[index] = newPath;
    }

    private void RaiseCommandStates()
    {
        OpenCommand.RaiseCanExecuteChanged();
        RenameCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Eine Zeile der Profiluebersicht.</summary>
public sealed class ProfileRowViewModel
{
    public ProfileRowViewModel(ProfileSummary summary) => Summary = summary;

    public ProfileSummary Summary { get; }

    public string Name => Summary.Name;

    public string? Description => Summary.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Summary.Description);

    public bool HasError => Summary.Error is not null;

    public string? ErrorMessage => Summary.Error;

    /// <summary>Ob die Schaltflaeche "Oeffnen" auf dieser Zeile Sinn ergibt.</summary>
    public bool CanOpen => !HasError;

    public string FilesText => Summary.DataFiles.Count switch
    {
        0 => "keine Dateien",
        1 => "1 Datei",
        var n => $"{n} Dateien",
    };

    public string LastUsedText => Summary.LastUsedUtc is { } zeitpunkt ? FormatRelative(zeitpunkt) : "nie";

    public string ModifiedText => Summary.Error is not null
        ? "—"
        : Summary.ModifiedUtc.ToLocalTime().ToString("dd.MM.yyyy");

    public string PathLine => $"Ausgewählt: {Summary.Path}";

    public bool HasMappingLine => !HasError;

    /// <summary>
    /// Erst hier, beim tatsaechlichen Anzeigen der ausgewaehlten Zeile, wird die
    /// Tabelle geoeffnet -- nicht schon beim Aufbau der Liste. Sonst wuerden
    /// beim Sammeln aller Profile gleich alle ihre Tabellen angefasst.
    /// </summary>
    public string MappingLine
    {
        get
        {
            if (HasError)
                return "";

            var summary = MappingSummary.For(Summary.MappingStorePath, Summary.Name);
            return $"Tabelle: {summary.StorePath} · {summary.Text}";
        }
    }

    public bool HasFilesLine => Summary.DataFiles.Count > 0;

    public string FilesLine
    {
        get
        {
            if (Summary.DataFiles.Count == 0)
                return "";

            var namen = string.Join("   ", Summary.DataFiles
                .OrderByDescending(f => f.LastUsedUtc)
                .Select(f => Path.GetFileName(f.Path)));

            var zuletzt = Summary.DataFiles.Max(f => f.LastUsedUtc).ToLocalTime().ToString("dd.MM.yyyy");
            return $"Dateien: {namen}   zuletzt {zuletzt}";
        }
    }

    private static string FormatRelative(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var heute = DateTime.Now.Date;

        if (local.Date == heute)
            return $"heute, {local:HH:mm}";
        if (local.Date == heute.AddDays(-1))
            return $"gestern, {local:HH:mm}";

        return local.ToString("dd.MM.yyyy");
    }
}
