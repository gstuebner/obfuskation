using System.Collections.ObjectModel;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;
using Obfuskation.Core.Generation;
using Obfuskation.Core.Mapping;
using Obfuskation.Core.Reporting;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Das Hauptfenster: geoeffnetes Profil, geoeffnete Datei, Feldregeln und die
/// drei Vorgaenge.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly GuiSettings _settings;
    private readonly Func<IDialogService> _dialogs;

    private ProfileSession? _session;
    private string? _dataFilePath;
    private byte[]? _dataContent;
    private AnalysisResult? _analysis;
    private FieldRuleViewModel? _selectedField;
    private RunResultViewModel? _lastResult;
    private string _statusText = "Bereit.";
    private bool _isBusy;
    private bool _hasRunSinceOpen;
    private bool _dataFileIsNewToProfile;
    private MappingSummary? _mappingSummary;
    private CancellationTokenSource? _cancellation;
    private string _progressText = "";

    public MainViewModel(GuiSettings settings, Func<IDialogService> dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        ShowProfilesCommand = new AsyncRelayCommand(ShowProfilesAsync);
        NewProfileCommand = new AsyncRelayCommand(NewProfileAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => _session is not null);
        OpenDataFileCommand = new AsyncRelayCommand(OpenDataFileAsync, () => _session is not null);

        ObfuscateCommand = new AsyncRelayCommand(ObfuscateAsync, CanRun);
        DeobfuscateCommand = new AsyncRelayCommand(DeobfuscateAsync, CanRun);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanRun);

        CancelCommand = new RelayCommand(Cancel, () => _isBusy);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        // Die Nebenfenster oeffnet die Ansicht; das Ansichtsmodell liefert nur
        // die Daten dafuer und kennt keine Fenster.
        ShowTextRulesCommand = new RelayCommand(
            () => TextRulesRequested?.Invoke(), () => _session is not null);
        ShowMappingCommand = new RelayCommand(
            () => MappingRequested?.Invoke(), () => _session is not null);
        ShowAboutCommand = new RelayCommand(() => AboutRequested?.Invoke());
        ShowHelpCommand = new RelayCommand(() => HelpRequested?.Invoke());

        Actions = new ObservableCollection<ActionOption>(ActionOption.All);

        Generators = new ObservableCollection<GeneratorOption>(GeneratorOption.BuiltIn);
    }

    // ------------------------------------------------------------- Befehle

    public AsyncRelayCommand ShowProfilesCommand { get; }
    public AsyncRelayCommand NewProfileCommand { get; }
    public AsyncRelayCommand SaveProfileCommand { get; }
    public AsyncRelayCommand OpenDataFileCommand { get; }
    public AsyncRelayCommand ObfuscateCommand { get; }
    public AsyncRelayCommand DeobfuscateCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ShowTextRulesCommand { get; }
    public RelayCommand ShowMappingCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand ShowHelpCommand { get; }

    /// <summary>Bitten an die Ansicht, ein Nebenfenster zu oeffnen.</summary>
    public event Action? TextRulesRequested;
    public event Action? MappingRequested;
    public event Action? AboutRequested;
    public event Action? HelpRequested;

    /// <summary>Das Ansichtsmodell der Textregeln zum laufenden Profil.</summary>
    public TextRulesViewModel? CreateTextRulesViewModel()
        => _session is null ? null : new TextRulesViewModel(_session.Profile, OnTextRulesChanged);

    /// <summary>Die Auskunft ueber die Ersetzungstabelle.</summary>
    public MappingViewModel? CreateMappingViewModel()
    {
        if (_session is null || !_session.TryGetEngine(out var engine, out _) || engine is null)
            return null;

        return new MappingViewModel(engine, _session.Profile.ProfileName);
    }

    public string? MappingStorePath
    {
        get
        {
            if (_session is null)
                return null;

            return _session.TryGetEngine(out var engine, out _) && engine is not null
                ? engine.ResolveMappingStorePath()
                : null;
        }
    }

    private void OnTextRulesChanged()
    {
        _session?.MarkChanged();
        RefreshIssues();
        OnPropertyChanged(nameof(ProfileTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    // -------------------------------------------------------------- Listen

    public ObservableCollection<FieldRuleViewModel> Fields { get; } = new();
    public ObservableCollection<ActionOption> Actions { get; }
    public ObservableCollection<GeneratorOption> Generators { get; }
    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    // ------------------------------------------------------------ Zustand

    public ProfileSession? Session => _session;

    public string ProfileName => _session?.DisplayName ?? "kein Profil";

    public string ProfileTitle
        => _session is null ? "Obfuskation" : $"Obfuskation — {_session.DisplayName}"
           + (_session.HasUnsavedChanges ? " *" : "");

    /// <summary>Untertitel der Kopfzeile: Beschreibung und Tabellenauskunft.</summary>
    public string ProfileSubtitle
    {
        get
        {
            if (_session is null)
                return "";

            var teile = new List<string>();
            if (!string.IsNullOrWhiteSpace(_session.Profile.Description))
                teile.Add(_session.Profile.Description!.Trim());

            if (_mappingSummary is not null)
                teile.Add($"Tabelle: {_mappingSummary.StorePath} · {_mappingSummary.Text}");

            return string.Join(" · ", teile);
        }
    }

    public bool HasProfileSubtitle => ProfileSubtitle.Length > 0;

    public bool HasProfile => _session is not null;

    /// <summary>Ob eine Rueckfrage noetig ist, bevor die Sitzung ersetzt wird.</summary>
    public bool HasUnsavedChanges => _session?.HasUnsavedChanges == true;

    public bool HasDataFile => _dataContent is not null;

    public string DataFileName
        => _dataFilePath is null ? "keine Datei geöffnet" : Path.GetFileName(_dataFilePath);

    /// <summary>Format, Zeichensatz und Trennzeichen der geoeffneten Datei.</summary>
    public string DataFileDetails
    {
        get
        {
            if (_analysis is null)
                return "";

            var file = _analysis.File;
            var teile = new List<string> { file.Format.ToString().ToUpperInvariant(), file.Encoding };

            if (!string.IsNullOrEmpty(file.Delimiter))
                teile.Add($"'{file.Delimiter}'");

            return string.Join(" · ", teile);
        }
    }

    public FieldRuleViewModel? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
            {
                RefreshPreview();
                OnPropertyChanged(nameof(HasSelectedField));
                OnPropertyChanged(nameof(SelectedFieldTitle));
            }
        }
    }

    public bool HasSelectedField => _selectedField is not null;

    /// <summary>Ueberschrift der Regelkarte.</summary>
    public string SelectedFieldTitle
        => _selectedField is null ? "Regel" : $"Regel: {_selectedField.FieldName}";

    /// <summary>Wegweiser, solange noch nichts geoeffnet ist.</summary>
    public string EmptyHint => _session is null
        ? "Noch kein Profil geladen.\n\nMit \u201eNeu aus Datei\u2026\u201c ein Regelgerüst aus einer "
          + "vorhandenen Datei ableiten, oder unter \u201eProfile\u2026\u201c ein bestehendes Profil "
          + "wählen. Ein Profil bündelt Feldregeln und Ersetzungstabelle für zusammengehörende Dateien."
          + "\n\nWer zum ersten Mal hier ist: „Mehr ▾“ → „Kurzhilfe…“ "
          + "erklärt das Nötige auf einer Seite."
        : "Keine Datei geöffnet.\n\nMit \u201eÖffnen\u2026\u201c eine CSV-, JSON- oder Textdatei wählen; "
          + "die Felder erscheinen dann hier.";

    public bool ShowEmptyHint => Fields.Count == 0;

    public int UndecidedCount => Fields.Count(entry => !entry.IsDecided);

    /// <summary>Fusszeile: was einem Lauf noch im Weg steht.</summary>
    public string UndecidedText => UndecidedCount switch
    {
        0 when Fields.Count == 0 => "",
        0 => "alle Felder entschieden",
        1 => "1 Feld offen",
        var n => $"{n} Felder offen",
    };

    public bool HasUndecided => UndecidedCount > 0;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowScanHint));
                CancelCommand.RaiseCanExecuteChanged();
                RaiseCommandStates();
            }
        }
    }

    /// <summary>Zwischenstand des laufenden Vorgangs.</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    private void Cancel() => _cancellation?.Cancel();

    public RunResultViewModel? LastResult
    {
        get => _lastResult;
        private set
        {
            if (SetProperty(ref _lastResult, value))
                OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => _lastResult is not null;

    /// <summary>
    /// Ob nach dem Erzeugen einer Pseudodatei die Nachpruefung noch aussteht. Der Schritt vor
    /// der Weitergabe soll nicht untergehen.
    /// </summary>
    public bool ScanRecommended => _hasRunSinceOpen;

    /// <summary>
    /// Ob der Hinweis auf die ausstehende Pruefung angezeigt wird. Er teilt
    /// sich die dehnbare Spalte der Aktionsleiste mit der Fortschrittsanzeige;
    /// beide gleichzeitig sichtbar hiesse, sie laegen uebereinander. Waehrend
    /// eines Laufs hat der Fortschritt Vorrang.
    /// </summary>
    public bool ShowScanHint => _hasRunSinceOpen && !_isBusy;

    public string ThemeSymbol => ThemeService.Symbol(_settings.Theme);
    public string ThemeName => ThemeService.Describe(_settings.Theme);

    // --------------------------------------------------------------- Start

    /// <summary>
    /// Zustand beim Start herstellen: ein ausdruecklich genanntes Profil, sonst
    /// eine <c>obfuskation.json</c> im aktuellen Verzeichnis (wie es die
    /// Kommandozeile auch tut), sonst das zuletzt benutzte Profil.
    /// </summary>
    public async Task InitializeAsync(string? profilePath, string? dataPath)
    {
        var pfad = profilePath
                   ?? ProfileStore.Discover(Directory.GetCurrentDirectory())
                   ?? _settings.RecentProfiles.FirstOrDefault(File.Exists);

        if (pfad is not null && File.Exists(pfad))
        {
            await GuardedAsync(() =>
            {
                LoadProfile(ProfileSession.Load(pfad));
                StatusText = $"Profil geladen: {pfad}";
                return Task.CompletedTask;
            });
        }

        if (dataPath is not null && File.Exists(dataPath) && _session is not null)
            await GuardedAsync(() => LoadDataFileAsync(dataPath));
    }

    // ------------------------------------------------------------- Profile

    /// <summary>
    /// Fragt bei ungespeicherten Aenderungen nach, bevor die Sitzung ersetzt
    /// wird. Liefert <c>false</c>, wenn der Anwender abbricht -- dann bleibt
    /// das alte Profil unangetastet geladen.
    /// </summary>
    public async Task<bool> EnsureChangesHandledAsync()
    {
        if (!HasUnsavedChanges)
            return true;

        var wahl = await _dialogs().AskSaveChangesAsync(_session!.DisplayName);

        switch (wahl)
        {
            case SaveChoice.Save:
                await SaveProfileAsync();
                // Ein abgebrochener Speichern-Dialog hinterlaesst weiter
                // ungespeicherte Aenderungen -- dann nicht fortfahren.
                return !HasUnsavedChanges;

            case SaveChoice.Discard:
                return true;

            default:
                return false;
        }
    }

    public async Task ShowProfilesAsync()
    {
        var viewModel = new ProfilesViewModel(_settings, _dialogs, _session?.Path, HasUnsavedChanges);
        viewModel.CurrentSessionRenamed += OnCurrentSessionRenamed;

        var gewaehlt = await _dialogs().ShowProfilesAsync(viewModel);
        if (gewaehlt is null)
            return;

        if (gewaehlt.Error is not null)
        {
            StatusText = gewaehlt.Error;
            return;
        }

        if (!await EnsureChangesHandledAsync())
            return;

        await GuardedAsync(() =>
        {
            LoadProfile(ProfileSession.Load(gewaehlt.Path));
            StatusText = $"Profil geladen: {gewaehlt.Name}";
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Das gerade offene Profil wurde in der Uebersicht umbenannt -- Name und
    /// Pfad der laufenden Sitzung nachziehen. Wird nur aufgerufen, wenn keine
    /// ungespeicherten Aenderungen vorlagen (die Uebersicht prueft das vor dem
    /// Umbenennen selbst), das Neuladen verwirft also nichts.
    /// </summary>
    private void OnCurrentSessionRenamed(string newPath)
    {
        if (_session is null)
            return;

        try
        {
            LoadProfile(ProfileSession.Load(newPath));
            StatusText = "Das offene Profil wurde umbenannt.";
        }
        catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException)
        {
            StatusText = ex.Message;
        }
    }

    public async Task NewProfileAsync()
    {
        if (!await EnsureChangesHandledAsync())
            return;

        // Der uebliche Weg: aus einer Beispieldatei ein Regelgeruest ableiten,
        // genau wie "obfuskation init --from".
        var beispiel = await _dialogs().OpenDataFileAsync(_settings.LastDataDirectory);
        if (beispiel is null)
            return;

        var vorschlag = Path.GetFileNameWithoutExtension(beispiel);
        var antwort = await _dialogs().AskNewProfileAsync(new NewProfileProposal(vorschlag, beispiel));
        if (antwort is null)
            return;

        await GuardedAsync(async () =>
        {
            if (antwort.OpenExisting)
            {
                LoadProfile(ProfileSession.Load(antwort.TargetPath));
                StatusText = $"Profil geladen: {Path.GetFileName(antwort.TargetPath)}";
            }
            else
            {
                // OK legt das Profil sofort an und speichert es: erst damit hat
                // es einen Pfad und erscheint in Index und Uebersicht.
                var session = ProfileSession.Create(antwort.Name, beispiel, antwort.Description);
                session.Save(antwort.TargetPath);
                LoadProfile(session);

                StatusText = "Neues Profil angelegt. Jedes Feld braucht noch eine Entscheidung.";
            }

            await LoadDataFileAsync(beispiel);
        });
    }

    private async Task SaveProfileAsync()
    {
        if (_session is null)
            return;

        var ziel = _session.Path;
        if (ziel is null)
        {
            ziel = await _dialogs().SaveFileAsync(
                ProfileStore.DefaultFileName,
                _settings.LastDataDirectory);

            if (ziel is null)
                return;
        }

        await GuardedAsync(() =>
        {
            _session.Save(ziel);
            _settings.RememberProfile(ziel);
            _settings.Save();

            var index = ProfileIndex.Load();
            index.RecordProfileUse(ziel);
            index.Save();

            StatusText = $"Gespeichert: {ziel}";
            OnPropertyChanged(nameof(ProfileTitle));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            return Task.CompletedTask;
        });
    }

    private void LoadProfile(ProfileSession session)
    {
        _session = session;

        if (session.Path is not null)
        {
            _settings.RememberProfile(session.Path);
            _settings.Save();

            var index = ProfileIndex.Load();
            index.RecordProfileUse(session.Path);
            index.Save();
        }

        // Die Auswahlliste haengt am Profil: eigene Namensraeume stehen dort.
        Generators.Clear();
        foreach (var option in GeneratorOption.For(session.Profile))
            Generators.Add(option);

        RefreshIssues();
        RefreshMappingSummary();
        RefreshAnalysis();

        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(ProfileTitle));
        RaiseCommandStates();
    }

    /// <summary>
    /// Auskunft ueber die Ersetzungstabelle fuer die Kopfzeile. Neu berechnet
    /// wird sie nur beim Profilladen und nach einem Lauf -- nicht bei jeder
    /// Regelaenderung, sonst wuerde jede Eingabe die Tabelle anfassen.
    /// </summary>
    private void RefreshMappingSummary()
    {
        _mappingSummary = _session is null
            ? null
            : MappingSummary.For(PathHelper.ResolveMappingStore(_session.Profile), _session.Profile.ProfileName);

        OnPropertyChanged(nameof(ProfileSubtitle));
        OnPropertyChanged(nameof(HasProfileSubtitle));
    }

    // --------------------------------------------------------------- Datei

    private async Task OpenDataFileAsync()
    {
        var pfad = await _dialogs().OpenDataFileAsync(_settings.LastDataDirectory);
        if (pfad is null)
            return;

        await GuardedAsync(() => LoadDataFileAsync(pfad));
    }

    private async Task LoadDataFileAsync(string pfad)
    {
        _dataFilePath = pfad;
        _dataContent = await File.ReadAllBytesAsync(pfad);

        _settings.LastDataDirectory = Path.GetDirectoryName(pfad);
        _settings.Save();

        LastResult = null;
        _hasRunSinceOpen = false;

        // Erst die Analyse -- die liest ueber RefreshAnalysis noch den alten
        // Indexstand, um zu erkennen, ob diese Datei neu fuer das Profil ist.
        // Erst danach wird der Index selbst nachgefuehrt, sonst faende sich die
        // eben geoeffnete Datei schon als "bekannt" und der Hinweis unten
        // verschwaende, kaum dass er erscheinen koennte.
        RefreshAnalysis();

        if (_session?.Path is not null)
        {
            var index = ProfileIndex.Load();
            index.RecordDataFile(_session.Path, pfad);
            index.Save();
        }

        OnPropertyChanged(nameof(HasDataFile));
        OnPropertyChanged(nameof(DataFileName));
        OnPropertyChanged(nameof(ScanRecommended));
        OnPropertyChanged(nameof(ShowScanHint));
        RaiseCommandStates();
    }

    /// <summary>
    /// Liest die Feldliste neu ein. Nutzt <see cref="ObfuscationEngine.Analyze"/>,
    /// das die Struktur liest, ohne die Datei zu verarbeiten — und ohne bei
    /// einem offenen Feld abzubrechen.
    /// </summary>
    private void RefreshAnalysis()
    {
        Fields.Clear();
        _analysis = null;
        _dataFileIsNewToProfile = false;

        if (_session is null || _dataContent is null)
        {
            OnPropertyChanged(nameof(DataFileDetails));
            OnPropertyChanged(nameof(UndecidedCount));
            OnPropertyChanged(nameof(UndecidedText));
            OnPropertyChanged(nameof(HasUndecided));
            OnPropertyChanged(nameof(ShowEmptyHint));
            OnPropertyChanged(nameof(EmptyHint));
            OnPropertyChanged(nameof(NewFieldsHint));
            OnPropertyChanged(nameof(HasNewFieldsHint));
            return;
        }

        if (!_session.TryGetEngine(out var engine, out _) || engine is null)
        {
            StatusText = "Die Konfiguration ist fehlerhaft \u2014 siehe Hinweise.";
            RefreshIssues();
            return;
        }

        _analysis = engine.Analyze(_dataContent, _dataFilePath);

        if (_session.Path is not null && _dataFilePath is not null)
        {
            var index = ProfileIndex.Load();
            var eintrag = index.Profiles.FirstOrDefault(p =>
                string.Equals(p.Path, Path.GetFullPath(_session.Path), StringComparison.Ordinal));
            _dataFileIsNewToProfile = eintrag is null || !eintrag.Files.Any(f =>
                string.Equals(f.Path, Path.GetFullPath(_dataFilePath), StringComparison.Ordinal));
        }

        var beispiele = ReadSampleValues(_analysis.File);

        foreach (var feld in _analysis.Fields)
        {
            beispiele.TryGetValue(feld.FieldName, out var beispiel);

            Fields.Add(new FieldRuleViewModel(
                _session.Profile, feld, beispiel, OnFieldRuleChanged));
        }

        SelectedField = Fields.FirstOrDefault(f => !f.IsDecided) ?? Fields.FirstOrDefault();

        OnPropertyChanged(nameof(DataFileDetails));
        OnPropertyChanged(nameof(UndecidedCount));
        OnPropertyChanged(nameof(UndecidedText));
        OnPropertyChanged(nameof(HasUndecided));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(NewFieldsHint));
        OnPropertyChanged(nameof(HasNewFieldsHint));
        RaiseCommandStates();
    }

    /// <summary>
    /// Hinweis, wenn die offene Datei noch nicht Teil des Profils war: neue
    /// Felder ohne eigene Regel fallen sonst leicht unter den Tisch.
    /// </summary>
    public string? NewFieldsHint
    {
        get
        {
            if (_analysis is null || !_dataFileIsNewToProfile)
                return null;

            var neu = _analysis.Fields.Count(f => f.IsFromDefault);
            if (neu == 0)
                return null;

            return neu == 1
                ? "Diese Datei war bisher nicht Teil des Profils \u2014 1 neues Feld."
                : $"Diese Datei war bisher nicht Teil des Profils \u2014 {neu} neue Felder.";
        }
    }

    public bool HasNewFieldsHint => NewFieldsHint is not null;

    /// <summary>
    /// Ein Beispielwert je Feld aus der ersten Datenzeile, damit die Vorschau
    /// mit echten Werten arbeitet statt mit erfundenen.
    /// </summary>
    private Dictionary<string, string> ReadSampleValues(InspectedFile file)
    {
        var beispiele = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_dataContent is null || file.Format != DataFormat.Csv || file.FieldNames.Count == 0)
            return beispiele;

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(_dataContent);
            using var reader = new StringReader(text);

            _ = reader.ReadLine();                       // Kopfzeile
            if (reader.ReadLine() is not { } zeile)
                return beispiele;

            // Bewusst einfach gehalten: fuer eine Vorschau genuegt eine grobe
            // Zerlegung. Die richtige Verarbeitung macht ohnehin CsvHelper.
            var trenner = file.Delimiter == "\\t" ? "\t" : file.Delimiter ?? ";";
            var werte = zeile.Split(trenner);

            for (var i = 0; i < file.FieldNames.Count && i < werte.Length; i++)
            {
                var wert = werte[i].Trim().Trim('"');
                if (wert.Length > 0)
                    beispiele[file.FieldNames[i]] = wert;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Ohne Beispielwerte gibt es eben keine Vorschau.
        }

        return beispiele;
    }

    private void OnFieldRuleChanged()
    {
        _session?.MarkChanged();

        RefreshIssues();
        RefreshPreview();

        OnPropertyChanged(nameof(ProfileTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UndecidedCount));
        OnPropertyChanged(nameof(UndecidedText));
        OnPropertyChanged(nameof(HasUndecided));
        RaiseCommandStates();
    }

    private void RefreshPreview()
    {
        if (_selectedField is null || _session is null)
            return;

        _session.TryGetEngine(out var engine, out _);
        _selectedField.RefreshPreview(engine);
    }

    private void RefreshIssues()
    {
        Issues.Clear();

        if (_session is null)
            return;

        foreach (var issue in _session.Validate())
            Issues.Add(issue);

        OnPropertyChanged(nameof(HasIssues));
    }

    public bool HasIssues => Issues.Count > 0;

    // ------------------------------------------------------------ Vorgaenge

    private bool CanRun() => !IsBusy && _session is not null && _dataContent is not null;

    private async Task ObfuscateAsync()
    {
        if (_session is null || _dataContent is null || _dataFilePath is null)
            return;

        // Erst pruefen, dann fragen: einen Speichern-Dialog zu zeigen und
        // danach am ersten offenen Feld zu scheitern waere Zeitverschwendung.
        if (UndecidedCount > 0)
        {
            StatusText = $"{UndecidedText} — jedes Feld braucht eine Entscheidung, "
                       + "bevor die Pseudodatei erzeugt werden kann.";
            SelectedField = Fields.FirstOrDefault(f => !f.IsDecided);
            return;
        }

        var ziel = await _dialogs().SaveFileAsync(
            DialogService.SuggestOutputName(_dataFilePath),
            Path.GetDirectoryName(_dataFilePath));

        if (ziel is null)
            return;

        await RunAsync("Erzeugen der Pseudodatei", async (engine, token, fortschritt) =>
        {
            var ergebnis = await engine.ObfuscateAsync(
                _dataContent, _dataFilePath, new RunOptions { Strict = true }, token, fortschritt);

            await File.WriteAllBytesAsync(ziel, ergebnis.Content, token);

            _hasRunSinceOpen = true;
            OnPropertyChanged(nameof(ScanRecommended));
        OnPropertyChanged(nameof(ShowScanHint));

            return (ergebnis.Report, $"Geschrieben: {ziel}");
        });
    }

    private async Task DeobfuscateAsync()
    {
        if (_session is null || _dataContent is null || _dataFilePath is null)
            return;

        var ziel = await _dialogs().SaveFileAsync(
            DialogService.SuggestOutputName(_dataFilePath, "klartext"),
            Path.GetDirectoryName(_dataFilePath));

        if (ziel is null)
            return;

        await RunAsync("Erzeugen der Klartextdatei", async (engine, token, fortschritt) =>
        {
            var ergebnis = await engine.DeobfuscateAsync(
                _dataContent, _dataFilePath, new RunOptions(), token, fortschritt);

            await File.WriteAllBytesAsync(ziel, ergebnis.Content, token);

            return (ergebnis.Report, $"Geschrieben: {ziel}");
        });
    }

    private async Task ScanAsync()
    {
        if (_session is null || _dataContent is null)
            return;

        await RunAsync("Prüfen", async (engine, token, fortschritt) =>
        {
            var ergebnis = await engine.ScanAsync(
                _dataContent, _dataFilePath, new RunOptions(), token, fortschritt);

            _hasRunSinceOpen = false;
            OnPropertyChanged(nameof(ScanRecommended));
        OnPropertyChanged(nameof(ShowScanHint));

            var meldung = ergebnis.Report.Findings.Count == 0
                ? "Keine Restbestände gefunden."
                : $"{ergebnis.Report.Findings.Count} Verdachtsfälle — die Datei nicht "
                  + "weitergeben, bevor sie geklärt sind.";

            return (ergebnis.Report, meldung);
        });
    }

    /// <summary>
    /// Gemeinsamer Rahmen der drei Vorgaenge: Engine holen, Fehler abfangen,
    /// Bericht anzeigen.
    /// </summary>
    private async Task RunAsync(
        string bezeichnung,
        Func<ObfuscationEngine, CancellationToken, IProgress<RunProgress>,
            Task<(RunReport Report, string Message)>> vorgang)
    {
        if (_session is null)
            return;

        if (!_session.TryGetEngine(out var engine, out var befunde) || engine is null)
        {
            StatusText = "Die Konfiguration ist fehlerhaft \u2014 siehe Hinweise.";
            Issues.Clear();
            foreach (var befund in befunde)
                Issues.Add(befund);
            OnPropertyChanged(nameof(HasIssues));
            return;
        }

        IsBusy = true;
        StatusText = $"{bezeichnung} läuft …";
        ProgressText = "";

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        // Progress<T> meldet im Faden, in dem es erzeugt wurde — also hier im
        // Oberflaechenfaden. Ohne das duerfte die Anzeige gar nicht angefasst
        // werden.
        var fortschritt = new Progress<RunProgress>(stand =>
            ProgressText = $"{stand.RowsProcessed} Datensätze …");

        try
        {
            var (bericht, meldung) = await vorgang(engine, _cancellation.Token, fortschritt);

            LastResult = new RunResultViewModel(bericht);
            StatusText = meldung;

            // Ein Lauf kann neue Eintraege in der Tabelle hinterlassen haben --
            // die Auskunft in der Kopfzeile soll das ohne weiteres Zutun zeigen.
            RefreshMappingSummary();
        }
        catch (OperationCanceledException)
        {
            // Ein Abbruch beim Erzeugen hinterlaesst keine halbe Ausgabedatei:
            // geschrieben wird erst, wenn die Verarbeitung fertig ist.
            StatusText = $"{bezeichnung} abgebrochen. Es wurde nichts geschrieben.";
        }
        catch (UnhandledFieldException ex)
        {
            // Kein Stoerfall: nach dem Anlegen eines Profils ist das der
            // Normalzustand. Statt einer Fehlermeldung wird das erste offene
            // Feld ausgewaehlt, damit der Anwender dort weitermachen kann.
            StatusText = ex.Message;
            SelectedField = Fields.FirstOrDefault(f =>
                ex.FieldNames.Contains(f.FieldName, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ConfigurationException
                                       or MappingConflictException
                                       or MappingLockedException
                                       or Core.Generation.GenerationException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    // -------------------------------------------------------------- Ansicht

    private void ToggleTheme()
    {
        _settings.Theme = ThemeService.Next(_settings.Theme);
        ThemeService.Apply(_settings.Theme);
        _settings.Save();

        OnPropertyChanged(nameof(ThemeSymbol));
        OnPropertyChanged(nameof(ThemeName));
    }

    /// <summary>Fuehrt einen Vorgang aus und faengt die bekannten Fehler ab.</summary>
    private async Task GuardedAsync(Func<Task> vorgang)
    {
        try
        {
            await vorgang();
        }
        catch (Exception ex) when (ex is ConfigurationException
                                       or MappingConflictException
                                       or MappingLockedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            StatusText = ex.Message;
        }
    }

    private void RaiseCommandStates()
    {
        SaveProfileCommand.RaiseCanExecuteChanged();
        OpenDataFileCommand.RaiseCanExecuteChanged();
        ShowTextRulesCommand.RaiseCanExecuteChanged();
        ShowMappingCommand.RaiseCanExecuteChanged();
        ObfuscateCommand.RaiseCanExecuteChanged();
        DeobfuscateCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
    }
}
