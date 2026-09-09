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
    private readonly GeneratorLibrary _library;

    private ProfileSession? _session;
    private string? _dataFilePath;
    private byte[]? _dataContent;
    private AnalysisResult? _analysis;
    private FieldRuleViewModel? _selectedField;
    private readonly List<FieldRuleViewModel> _selectedFields = new();
    private bool _bulkUpdate;
    private RunResultViewModel? _lastResult;
    private string _statusText = "Bereit.";
    private bool _isBusy;
    private bool _hasRunSinceOpen;
    private bool _dataFileIsNewToProfile;
    private bool _engineIsBroken;
    private MappingSummary? _mappingSummary;
    private CancellationTokenSource? _cancellation;
    private string _progressText = "";

    public MainViewModel(GuiSettings settings, Func<IDialogService> dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        // Einmal beim Start, nicht bei jedem Kern-Aufruf: alle Aufrufe (Engine,
        // Validierung, Geruesterzeugung) reichen dieselbe Bibliothek durch,
        // statt die Datei jedesmal erneut zu lesen. Eine kaputte Datei darf
        // die Oberflaeche nicht blockieren -- die Statuszeile nennt den Pfad
        // (siehe GeneratorLibrary.Load), und es geht ohne Bibliothek weiter.
        try
        {
            _library = GeneratorLibrary.Load();
        }
        catch (ConfigurationException ex)
        {
            _library = GeneratorLibrary.Empty;
            _statusText = ex.Message;
        }

        ShowProfilesCommand = new AsyncRelayCommand(ShowProfilesAsync);
        NewProfileCommand = new AsyncRelayCommand(NewProfileAsync);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => _session is not null);
        OpenDataFileCommand = new AsyncRelayCommand(OpenDataFileAsync, () => _session is not null);

        ObfuscateCommand = new AsyncRelayCommand(ObfuscateAsync, CanRun);
        DeobfuscateCommand = new AsyncRelayCommand(DeobfuscateAsync, CanRun);
        ScanCommand = new AsyncRelayCommand(ScanAsync, CanRun);

        ObfuscateAllCommand = new AsyncRelayCommand(() => RunBatchAsync(obfuscate: true), CanRunBatch);
        DeobfuscateAllCommand = new AsyncRelayCommand(() => RunBatchAsync(obfuscate: false), CanRunBatch);

        ShowPatternSuggestionsCommand = new AsyncRelayCommand(
            ShowPatternSuggestionsAsync, () => _session is not null && Fields.Count > 0);

        CancelCommand = new RelayCommand(Cancel, () => _isBusy);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        // Die Nebenfenster oeffnet die Ansicht; das Ansichtsmodell liefert nur
        // die Daten dafuer und kennt keine Fenster.
        ShowTextRulesCommand = new RelayCommand(
            () => TextRulesRequested?.Invoke(), () => _session is not null);
        ShowMappingCommand = new RelayCommand(
            () => MappingRequested?.Invoke(), () => _session is not null);
        ShowGeneratorOptionsCommand = new RelayCommand(
            () => GeneratorOptionsRequested?.Invoke(), () => _selectedField?.HasOptions == true);
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
    public AsyncRelayCommand ObfuscateAllCommand { get; }
    public AsyncRelayCommand DeobfuscateAllCommand { get; }
    public AsyncRelayCommand ShowPatternSuggestionsCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ShowTextRulesCommand { get; }
    public RelayCommand ShowMappingCommand { get; }
    public RelayCommand ShowGeneratorOptionsCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand ShowHelpCommand { get; }

    /// <summary>Bitten an die Ansicht, ein Nebenfenster zu oeffnen.</summary>
    public event Action? TextRulesRequested;
    public event Action? MappingRequested;
    public event Action? GeneratorOptionsRequested;
    public event Action? AboutRequested;
    public event Action? HelpRequested;

    /// <summary>Das Ansichtsmodell der Textregeln zum laufenden Profil.</summary>
    public TextRulesViewModel? CreateTextRulesViewModel()
        => _session is null ? null : new TextRulesViewModel(_session.Profile, _library, OnTextRulesChanged);

    /// <summary>
    /// Das Ansichtsmodell des Optionsdialogs fuer das fuehrende gewaehlte
    /// Feld. Der Dialog bezieht sich immer auf genau ein Feld -- bei
    /// Mehrfachauswahl bleibt die Schaltflaeche ohnehin verborgen (siehe
    /// <see cref="ShowGeneratorOptionsButton"/>).
    /// </summary>
    public GeneratorOptionsViewModel? CreateGeneratorOptionsViewModel()
        => _selectedField is null ? null : new GeneratorOptionsViewModel(_selectedField);

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
    public ObservableCollection<RecentFileViewModel> RecentDataFiles { get; } = new();

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

    /// <summary>Ob die Schnellwahl etwas anzuzeigen hat -- sonst bleibt "Zuletzt ▾" verborgen.</summary>
    public bool HasRecentDataFiles => RecentDataFiles.Count > 0;

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

    /// <summary>
    /// Das fuehrende Feld der Auswahl. Nach ihm richten sich Vorschau und
    /// Ueberschrift; bei Mehrfachauswahl ist es das erste gewaehlte.
    /// </summary>
    public FieldRuleViewModel? SelectedField
    {
        get => _selectedField;
        set
        {
            if (!SetProperty(ref _selectedField, value))
                return;

            // Wird das fuehrende Feld gesetzt, ohne dass die Liste eine
            // Mehrfachauswahl gemeldet hat, gilt genau dieses Feld als
            // gewaehlt. Ohne das liefe die Massenzuweisung unten auf einen
            // veralteten Stand.
            if (value is null || !_selectedFields.Contains(value))
            {
                _selectedFields.Clear();
                if (value is not null)
                    _selectedFields.Add(value);

                RaiseSelectionProperties();
            }

            RefreshPreview();
            OnPropertyChanged(nameof(HasSelectedField));
            OnPropertyChanged(nameof(SelectedFieldTitle));
            OnPropertyChanged(nameof(SelectedAction));
            OnPropertyChanged(nameof(SelectedGenerator));
            OnPropertyChanged(nameof(NeedsGenerator));
            OnPropertyChanged(nameof(ShowGeneratorOptionsButton));
            ShowGeneratorOptionsCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Alle gewaehlten Felder. Grosse Tabellen haben oft ganze Gruppen
    /// gleichartiger Spalten; die Behandlung wird darum auf einmal fuer die
    /// ganze Auswahl gesetzt, nicht Feld fuer Feld.
    /// </summary>
    public IReadOnlyList<FieldRuleViewModel> SelectedFields => _selectedFields;

    /// <summary>
    /// Meldet die Auswahl der Feldliste. Ruft die Ansicht bei jeder Aenderung;
    /// das Ansichtsmodell kennt die Liste selbst nicht.
    /// </summary>
    public void UpdateSelection(IEnumerable<FieldRuleViewModel> fields)
    {
        var gewaehlt = fields.ToList();

        _selectedFields.Clear();
        _selectedFields.AddRange(gewaehlt);

        // Das fuehrende Feld bleibt, solange es Teil der Auswahl ist — sonst
        // spraenge die Regelkarte bei jedem Erweitern der Auswahl um.
        if (_selectedField is null || !_selectedFields.Contains(_selectedField))
            SelectedField = _selectedFields.FirstOrDefault();

        RaiseSelectionProperties();
    }

    private void RaiseSelectionProperties()
    {
        OnPropertyChanged(nameof(SelectedFields));
        OnPropertyChanged(nameof(SelectedFieldTitle));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(IsSingleSelection));
        OnPropertyChanged(nameof(MultiSelectionHint));
        OnPropertyChanged(nameof(ShowGeneratorOptionsButton));
        ShowGeneratorOptionsCommand.RaiseCanExecuteChanged();
    }

    public bool HasSelectedField => _selectedField is not null;

    /// <summary>Ob mehr als ein Feld gewaehlt ist.</summary>
    public bool IsMultiSelection => _selectedFields.Count > 1;

    /// <summary>
    /// Ob genau ein Feld gewaehlt ist. Nur dann zeigt die Karte eine Vorschau:
    /// ein Beispielwert aus einem von zwoelf Feldern waere irrefuehrend.
    /// </summary>
    public bool IsSingleSelection => _selectedFields.Count <= 1;

    /// <summary>Sagt bei Mehrfachauswahl, worauf die Einstellung wirkt.</summary>
    public string MultiSelectionHint
        => $"Die Einstellung gilt für alle {_selectedFields.Count} gewählten Felder.";

    /// <summary>
    /// Die Behandlung der Auswahl. Gelesen wird sie am fuehrenden Feld,
    /// geschrieben auf alle gewaehlten.
    /// </summary>
    public ActionOption? SelectedAction
    {
        get => _selectedField?.SelectedAction;
        set
        {
            if (value is null)
                return;

            ApplyToSelection(feld => feld.SelectedAction = value);
            OnPropertyChanged(nameof(SelectedAction));
            OnPropertyChanged(nameof(SelectedGenerator));
            OnPropertyChanged(nameof(NeedsGenerator));
        }
    }

    /// <summary>Der Generator der Auswahl; wie <see cref="SelectedAction"/>.</summary>
    public GeneratorOption? SelectedGenerator
    {
        get => _selectedField?.SelectedGenerator;
        set
        {
            if (value is null)
                return;

            // Nur Felder, die ersetzt werden, tragen einen Generator. Ihn auf
            // ein durchgelassenes Feld zu schreiben, hiesse dessen Regel
            // stillschweigend zu veraendern.
            ApplyToSelection(feld =>
            {
                if (feld.NeedsGenerator)
                    feld.SelectedGenerator = value;
            });

            OnPropertyChanged(nameof(SelectedGenerator));
        }
    }

    /// <summary>Ob die Auswahl einen Generator braucht.</summary>
    public bool NeedsGenerator => _selectedField?.NeedsGenerator ?? false;

    /// <summary>
    /// Ob die Schaltflaeche zum Optionsdialog erscheint: nur bei
    /// Einzelauswahl eines Feldes, dessen Generator Optionen kennt. Bei
    /// Mehrfachauswahl koennten die gewaehlten Felder verschiedene
    /// Generatoren tragen, der Dialog bezieht sich aber immer auf genau
    /// einen Namensraum.
    /// </summary>
    public bool ShowGeneratorOptionsButton => IsSingleSelection && (_selectedField?.HasOptions ?? false);

    /// <summary>
    /// Setzt eine Einstellung auf die ganze Auswahl. Die Nacharbeit
    /// (Profilpruefung, Vorschau, Zaehler) laeuft einmal am Ende statt nach
    /// jedem einzelnen Feld — bei einer Tabelle mit hundert Spalten waere das
    /// sonst hundertmal dieselbe Pruefung.
    /// </summary>
    private void ApplyToSelection(Action<FieldRuleViewModel> aenderung)
    {
        _bulkUpdate = true;
        try
        {
            foreach (var feld in _selectedFields.ToList())
                aenderung(feld);
        }
        finally
        {
            _bulkUpdate = false;
        }

        OnFieldRuleChanged();
    }

    /// <summary>Ueberschrift der Regelkarte.</summary>
    public string SelectedFieldTitle
        => _selectedFields.Count > 1
            ? $"Regel: {_selectedFields.Count} Felder"
            : _selectedField is null ? "Regel" : $"Regel: {_selectedField.FieldName}";

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
                LoadProfile(ProfileSession.Load(pfad, _library));
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
            LoadProfile(ProfileSession.Load(gewaehlt.Path, _library));
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
            LoadProfile(ProfileSession.Load(newPath, _library));
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

        // Der uebliche Weg: aus einer oder mehreren Beispieldateien ein
        // Regelgeruest ableiten, genau wie "obfuskation init --from". Mehrere
        // zusammengehoerende Dateien (etwa Stammdaten und Adressen ueber die
        // gemeinsame Kundennummer) ergeben so ein einziges Profil mit den
        // Feldern aller Dateien statt eines pro Datei.
        var beispiele = await _dialogs().OpenDataFilesAsync(_settings.LastDataDirectory);
        if (beispiele.Count == 0)
            return;

        var erste = beispiele[0];
        var vorschlag = Path.GetFileNameWithoutExtension(erste);
        var antwort = await _dialogs().AskNewProfileAsync(new NewProfileProposal(vorschlag, erste));
        if (antwort is null)
            return;

        await GuardedAsync(async () =>
        {
            if (antwort.OpenExisting)
            {
                LoadProfile(ProfileSession.Load(antwort.TargetPath, _library));
                StatusText = $"Profil geladen: {Path.GetFileName(antwort.TargetPath)}";
            }
            else
            {
                // OK legt das Profil sofort an und speichert es: erst damit hat
                // es einen Pfad und erscheint in Index und Uebersicht.
                var session = ProfileSession.Create(antwort.Name, beispiele, antwort.Description, _library);
                session.Save(antwort.TargetPath);
                LoadProfile(session);

                StatusText = "Neues Profil angelegt. Jedes Feld braucht noch eine Entscheidung.";
            }

            // Alle gewaehlten Dateien sofort eintragen, nicht erst die erste
            // ueber LoadDataFileAsync unten -- sonst kennten Schnellwahl und
            // Sammellauf die restlichen Dateien erst, nachdem jede einzeln
            // geoeffnet wurde.
            if (_session?.Path is not null)
            {
                var index = ProfileIndex.Load();
                foreach (var datei in beispiele)
                    index.RecordDataFile(_session.Path, datei);
                index.Save();
            }

            await LoadDataFileAsync(erste);
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

        RefreshGenerators();
        RefreshIssues();
        RefreshMappingSummary();
        RefreshAnalysis();
        RefreshRecentDataFiles();

        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(ProfileTitle));
        OnPropertyChanged(nameof(HasBlockingIssues));
        RaiseCommandStates();
    }

    /// <summary>
    /// Baut die Auswahlliste der Generatoren aus dem Profil neu auf: die
    /// eingebauten, die im Profil selbst angelegten eigenen Namensraeume, und
    /// die der Generator-Bibliothek (mit Herkunftszusatz, siehe
    /// <see cref="GeneratorOption.For"/>). Eigene Methode statt Inline-Code,
    /// weil sie an zwei Stellen noetig ist -- beim Laden und jedesmal, wenn
    /// sich ueber das Praefix-Feld ein neuer Namensraum ergibt.
    ///
    /// Gleicht die Liste ab, statt sie mit <c>Clear()</c> zu leeren und neu zu
    /// befuellen: <c>Clear()</c> loest ein Reset aus, und waehrend die Liste
    /// kurzzeitig leer ist, faende eine ComboBox, deren <c>SelectedItem</c>
    /// genau auf einen ihrer Eintraege zeigt, keinen Treffer mehr und setzte
    /// die Auswahl auf <c>null</c> zurueck -- der Setter von SelectedGenerator
    /// schriebe dieses <c>null</c> sofort in die Regel. Genau das war Befund
    /// D-4, hier nur ausgeloest durch das Praefix-Feld statt durch einen von
    /// Hand editierten Namensraum.
    /// </summary>
    private void RefreshGenerators()
    {
        var ziel = _session is null
            ? Array.Empty<GeneratorOption>()
            : GeneratorOption.For(_session.Profile, _library);

        for (var i = 0; i < ziel.Count; i++)
        {
            if (i < Generators.Count)
            {
                if (!Generators[i].Equals(ziel[i]))
                    Generators[i] = ziel[i];
            }
            else
            {
                Generators.Add(ziel[i]);
            }
        }

        while (Generators.Count > ziel.Count)
            Generators.RemoveAt(Generators.Count - 1);
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

        RefreshRecentDataFiles();

        OnPropertyChanged(nameof(HasDataFile));
        OnPropertyChanged(nameof(DataFileName));
        OnPropertyChanged(nameof(ScanRecommended));
        OnPropertyChanged(nameof(ShowScanHint));
        RaiseCommandStates();
    }

    /// <summary>
    /// Baut die Schnellwahl aus dem Profilindex neu auf -- der Index wurde beim
    /// Laden des Profils oder unmittelbar zuvor in <see cref="LoadDataFileAsync"/>
    /// nachgefuehrt, steht also auf aktuellem Stand.
    ///
    /// Nicht mehr existierende Pfade werden ausgegraut (siehe
    /// <see cref="RecentFileViewModel.Exists"/>), nicht aus der Liste entfernt:
    /// wer eine Datei verschoben hat, soll das sehen, statt dass sie
    /// stillschweigend verschwindet und offenbleibt, ob das Programm sie je
    /// kannte.
    /// </summary>
    private void RefreshRecentDataFiles()
    {
        RecentDataFiles.Clear();

        if (_session?.Path is not null)
        {
            var index = ProfileIndex.Load();
            var eintrag = index.Profiles.FirstOrDefault(p =>
                string.Equals(p.Path, Path.GetFullPath(_session.Path), StringComparison.Ordinal));

            if (eintrag is not null)
            {
                var aktuellerPfad = _dataFilePath is null ? null : Path.GetFullPath(_dataFilePath);

                foreach (var datei in eintrag.Files.OrderByDescending(f => f.LastUsedUtc))
                {
                    var istAktuell = aktuellerPfad is not null
                        && string.Equals(datei.Path, aktuellerPfad, StringComparison.Ordinal);

                    // Keine Rueckfrage beim Wechsel: Eingabedateien werden nie
                    // geschrieben, es gibt nichts zu verlieren, und die
                    // Feldregeln greifen per Feldnamen-Abgleich auch auf der
                    // naechsten Datei. EnsureChangesHandledAsync gilt dem
                    // Profil, nicht der Datendatei, und wird deshalb hier
                    // bewusst nicht aufgerufen.
                    RecentDataFiles.Add(new RecentFileViewModel(
                        datei.Path, istAktuell, pfad => GuardedAsync(() => LoadDataFileAsync(pfad))));
                }
            }
        }

        OnPropertyChanged(nameof(HasRecentDataFiles));
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
            // Ohne offene Datei gibt es nichts zu analysieren -- ein zuvor
            // erkannter kaputter Zustand bezog sich auf die vorherige Datei
            // oder das vorherige Profil und gilt hier nicht mehr fort.
            _engineIsBroken = false;
            OnPropertyChanged(nameof(DataFileDetails));
            OnPropertyChanged(nameof(UndecidedCount));
            OnPropertyChanged(nameof(UndecidedText));
            OnPropertyChanged(nameof(HasUndecided));
            OnPropertyChanged(nameof(ShowEmptyHint));
            OnPropertyChanged(nameof(EmptyHint));
            OnPropertyChanged(nameof(NewFieldsHint));
            OnPropertyChanged(nameof(HasNewFieldsHint));
            OnPropertyChanged(nameof(HasBlockingIssues));
            return;
        }

        if (!_session.TryGetEngine(out var engine, out _) || engine is null)
        {
            StatusText = "Die Konfiguration ist fehlerhaft \u2014 siehe Hinweise.";
            _engineIsBroken = true;
            RefreshIssues();
            OnPropertyChanged(nameof(HasBlockingIssues));
            return;
        }

        _engineIsBroken = false;
        _analysis = engine.Analyze(_dataContent, _dataFilePath);

        if (_session.Path is not null && _dataFilePath is not null)
        {
            var index = ProfileIndex.Load();
            var eintrag = index.Profiles.FirstOrDefault(p =>
                string.Equals(p.Path, Path.GetFullPath(_session.Path), StringComparison.Ordinal));
            _dataFileIsNewToProfile = eintrag is null || !eintrag.Files.Any(f =>
                string.Equals(f.Path, Path.GetFullPath(_dataFilePath), StringComparison.Ordinal));
        }

        var beispiele = FieldSampler.Sample(_dataContent, _dataFilePath, _session.Profile.Input);

        foreach (var feld in _analysis.Fields)
        {
            beispiele.TryGetValue(feld.FieldName, out var beispiel);

            Fields.Add(new FieldRuleViewModel(
                _session.Profile, _library, feld, beispiel, OnFieldRuleChanged));
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
        OnPropertyChanged(nameof(HasBlockingIssues));
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

    private void OnFieldRuleChanged()
    {
        // Waehrend einer Massenzuweisung meldet jedes Feld seine Aenderung;
        // die Nacharbeit macht ApplyToSelection einmal am Ende.
        if (_bulkUpdate)
        {
            _session?.MarkChanged();
            return;
        }

        _session?.MarkChanged();

        // Ein neuer oder geaenderter Namensraum (etwa ueber das Praefix-Feld)
        // muss die Auswahlliste erreichen, bevor RefreshIssues laeuft --
        // sonst zeigte die ComboBox kurzzeitig den alten Stand.
        RefreshGenerators();
        RefreshIssues();
        RefreshPreview();

        OnPropertyChanged(nameof(ProfileTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UndecidedCount));
        OnPropertyChanged(nameof(UndecidedText));
        OnPropertyChanged(nameof(HasUndecided));
        // Eine Optionsaenderung kann einen neuen Namensraum angelegt haben --
        // das aendert nichts an HasOptions selbst, aber Sichtbarkeit und
        // Ausfuehrbarkeit der Schaltflaeche sollen mit dem aktuellen Stand
        // uebereinstimmen.
        OnPropertyChanged(nameof(ShowGeneratorOptionsButton));
        ShowGeneratorOptionsCommand.RaiseCanExecuteChanged();
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
        OnPropertyChanged(nameof(HasBlockingIssues));
    }

    public bool HasIssues => Issues.Count > 0;

    /// <summary>
    /// Ob sich aus dem Profil keine Engine aufbauen laesst und deshalb ein
    /// eigener Hinweisbereich oberhalb der Feldliste noetig ist -- ohne
    /// Felder gibt es dort kein gewaehltes Feld, also auch keinen sichtbaren
    /// Hinweisbereich im Regelbereich (siehe D-3 in A6).
    /// </summary>
    public bool HasBlockingIssues => _engineIsBroken && Issues.Count > 0;

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
    /// Ob ein Sammellauf ueberhaupt Arbeit haette: eine Sitzung muss geladen
    /// sein, kein Lauf darf laufen, und mindestens eine der dem Profil
    /// bekannten Dateien muss noch existieren -- sonst gaebe es nichts zu
    /// verarbeiten und die Rueckfrage waere fuer die Katz.
    /// </summary>
    private bool CanRunBatch()
        // Bewusst ein frischer File.Exists-Aufruf statt RecentFileViewModel.Exists:
        // das dort zwischengespeicherte Ergebnis stammt vom letzten Aufbau der
        // Schnellwahl und wird sonst nirgends aufgefrischt. Ein Sammellauf soll
        // aber genau wissen, was gerade auf der Platte liegt.
        => !IsBusy && _session is not null && RecentDataFiles.Any(f => File.Exists(f.FullPath));

    /// <summary>
    /// Erzeugt fuer jede dem Profil bekannte, noch vorhandene Datei eine
    /// Ausgabe -- "Alle Pseudodateien erzeugen…" beziehungsweise "Alle
    /// Klartextdateien erzeugen…". Anders als beim Einzellauf (<see cref="RunAsync"/>)
    /// gibt es nur eine einzige Rueckfrage vorab, danach laeuft die Liste ohne
    /// weitere Unterbrechung durch. Jede Datei wird erst vollstaendig
    /// verarbeitet und erst danach geschrieben -- ein Abbruch mitten in einer
    /// Datei hinterlaesst also nie eine halbe Ausgabedatei, nur eine fehlende.
    /// </summary>
    private async Task RunBatchAsync(bool obfuscate)
    {
        if (_session is null)
            return;

        var vorhandeneDateien = RecentDataFiles.Where(f => File.Exists(f.FullPath)).Select(f => f.FullPath).ToList();
        var fehlendeDateien = RecentDataFiles.Where(f => !File.Exists(f.FullPath)).Select(f => f.DisplayName).ToList();

        if (vorhandeneDateien.Count == 0)
            return;

        var marker = obfuscate ? "pseudo" : "klartext";

        // Ziel steht schon vor der Rueckfrage fest -- nur so laesst sich die
        // Anzahl der ueberschriebenen Dateien darin nennen.
        var ziele = vorhandeneDateien.ToDictionary(
            datei => datei,
            datei => System.IO.Path.Combine(
                Path.GetDirectoryName(datei) ?? "", DialogService.SuggestOutputName(datei, marker)));

        // Traegt eine Datei den Zusatz schon im Namen (kunden.pseudo.csv bei
        // "Alle Pseudodateien erzeugen"), haengt SuggestOutputName ihn bewusst
        // kein zweites Mal an -- der Vorschlag faellt dann auf den Namen der
        // Eingabe selbst zurueck, und der Lauf schriebe sein Ergebnis ueber
        // seine eigene Eingabedatei. Solche Dateien bleiben aussen vor und
        // werden in der Abschlussmeldung genannt.
        var selbstbezuegliche = ziele
            .Where(paar => string.Equals(
                Path.GetFullPath(paar.Key), Path.GetFullPath(paar.Value), StringComparison.Ordinal))
            .Select(paar => Path.GetFileName(paar.Key))
            .ToList();

        vorhandeneDateien.RemoveAll(datei => string.Equals(
            Path.GetFullPath(datei), Path.GetFullPath(ziele[datei]), StringComparison.Ordinal));

        if (vorhandeneDateien.Count == 0)
        {
            StatusText = BuildBatchStatusText(0, 0, Array.Empty<string>(), fehlendeDateien,
                selbstbezuegliche, abgebrochen: false);
            return;
        }

        var ueberschreibenAnzahl = vorhandeneDateien.Count(datei => File.Exists(ziele[datei]));

        var vorschlag = new BatchRunProposal(vorhandeneDateien.Count, marker, ueberschreibenAnzahl);
        if (!await _dialogs().AskBatchRunAsync(vorschlag))
            return;

        if (!_session.TryGetEngine(out var engine, out var befunde) || engine is null)
        {
            StatusText = "Die Konfiguration ist fehlerhaft — siehe Hinweise.";
            Issues.Clear();
            foreach (var befund in befunde)
                Issues.Add(befund);
            OnPropertyChanged(nameof(HasIssues));
            return;
        }

        IsBusy = true;
        StatusText = (obfuscate ? "Erzeugen der Pseudodateien" : "Erzeugen der Klartextdateien") + " läuft …";
        ProgressText = "";

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        var summe = new RunReport { Command = obfuscate ? "obfuscateAll" : "deobfuscateAll" };
        var geschriebenAnzahl = 0;
        var uebersprungeneDateien = new List<string>();
        var abgebrochen = false;

        try
        {
            for (var i = 0; i < vorhandeneDateien.Count; i++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();

                var eingabe = vorhandeneDateien[i];
                var ziel = ziele[eingabe];
                var dateiNummer = i + 1;

                var fortschritt = new Progress<RunProgress>(stand =>
                    ProgressText = $"Datei {dateiNummer} von {vorhandeneDateien.Count} · "
                                  + $"{stand.RowsProcessed} Datensätze");

                try
                {
                    var inhalt = await File.ReadAllBytesAsync(eingabe, _cancellation.Token);

                    var ergebnis = obfuscate
                        ? await engine.ObfuscateAsync(
                            inhalt, eingabe, new RunOptions { Strict = true }, _cancellation.Token, fortschritt)
                        : await engine.DeobfuscateAsync(
                            inhalt, eingabe, new RunOptions(), _cancellation.Token, fortschritt);

                    await File.WriteAllBytesAsync(ziel, ergebnis.Content, _cancellation.Token);

                    MergeReport(summe, ergebnis.Report);
                    geschriebenAnzahl++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is UnhandledFieldException
                                               or ConfigurationException
                                               or MappingConflictException
                                               or MappingLockedException
                                               or Core.Generation.GenerationException
                                               or IOException
                                               or UnauthorizedAccessException)
                {
                    // Nichts wird still uebergangen: diese eine Datei faellt
                    // aus, die uebrigen laufen weiter, und die Abschlussmeldung
                    // nennt Datei und Grund.
                    uebersprungeneDateien.Add($"{Path.GetFileName(eingabe)} ({ex.Message})");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Vor der naechsten Datei abgebrochen -- schon geschriebene
            // Dateien bleiben stehen und werden unten mitgezaehlt.
            abgebrochen = true;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }

        if (geschriebenAnzahl > 0)
        {
            LastResult = new RunResultViewModel(summe);
            RefreshMappingSummary();

            if (obfuscate)
            {
                _hasRunSinceOpen = true;
                OnPropertyChanged(nameof(ScanRecommended));
                OnPropertyChanged(nameof(ShowScanHint));
            }
        }

        StatusText = BuildBatchStatusText(
            geschriebenAnzahl, vorhandeneDateien.Count, uebersprungeneDateien, fehlendeDateien,
            selbstbezuegliche, abgebrochen);
    }

    /// <summary>Traegt den Bericht eines Einzellaufs in die Sammelsumme des Sammellaufs ein.</summary>
    private static void MergeReport(RunReport summe, RunReport einzelbericht)
    {
        summe.RowsProcessed += einzelbericht.RowsProcessed;
        summe.NewMappings += einzelbericht.NewMappings;

        // Die Tabelle waechst mit jedem Lauf -- der letzte Stand ist der
        // aktuelle, ein Aufsummieren wuerde ihn vervielfachen.
        summe.TotalMappings = einzelbericht.TotalMappings;

        foreach (var (regel, anzahl) in einzelbericht.RuleHits)
            summe.RuleHits[regel] = summe.RuleHits.GetValueOrDefault(regel) + anzahl;

        summe.Findings.AddRange(einzelbericht.Findings);
        summe.Warnings.AddRange(einzelbericht.Warnings);
    }

    private static string BuildBatchStatusText(
        int geschrieben, int versucht, IReadOnlyList<string> uebersprungen, IReadOnlyList<string> fehlend,
        IReadOnlyList<string> selbstbezueglich, bool abgebrochen)
    {
        var text = abgebrochen
            ? $"Abgebrochen: {geschrieben} von {versucht} Dateien geschrieben."
            : $"{geschrieben} von {versucht} Dateien geschrieben.";

        if (uebersprungen.Count > 0)
            text += " Übersprungen: " + string.Join("; ", uebersprungen) + ".";

        if (fehlend.Count > 0)
            text += " Nicht mehr vorhanden: " + string.Join(", ", fehlend) + ".";

        if (selbstbezueglich.Count > 0)
            text += " Ausgelassen, weil die Ausgabe die Eingabedatei selbst überschriebe: "
                  + string.Join(", ", selbstbezueglich) + ".";

        return text;
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

    // ------------------------------------------------------ Mustererkennung

    /// <summary>
    /// Baut das Ansichtsmodell fuer den Dialog "Muster erkennen…": ein
    /// Vorschlag je Feld, dessen Beispielwerte vollstaendig auf ein bekanntes
    /// Muster passen (eingebaute Muster und die Textregeln der
    /// Generator-Bibliothek, siehe <see cref="ValueSuggester"/>).
    ///
    /// Vorbelegt (Haekchen gesetzt) ist nur ein Feld, das im aktuellen Stand
    /// noch auf "offen" (<see cref="FieldAction.Error"/>) steht -- ein bereits
    /// entschiedenes Feld wird nie ohne ausdrueckliches Zutun ueberschrieben.
    /// </summary>
    private PatternSuggestionsViewModel? CreatePatternSuggestionsViewModel()
    {
        if (_session is null || _dataContent is null)
            return null;

        var beispiele = FieldSampler.Sample(_dataContent, _dataFilePath, _session.Profile.Input);
        var vorschlaege = ValueSuggester.Suggest(beispiele, _library, _session.Profile.Defaults);

        var items = vorschlaege
            .Select(vorschlag =>
            {
                var feld = Fields.FirstOrDefault(f =>
                    string.Equals(f.FieldName, vorschlag.FieldName, StringComparison.OrdinalIgnoreCase));
                var vorbelegt = feld is not null && !feld.IsDecided;
                return new PatternSuggestionItemViewModel(vorschlag, vorbelegt);
            })
            .ToList();

        return new PatternSuggestionsViewModel(items, GeneratorLibrary.DefaultPath);
    }

    private async Task ShowPatternSuggestionsAsync()
    {
        if (CreatePatternSuggestionsViewModel() is not { } viewModel)
            return;

        var akzeptiert = await _dialogs().ShowPatternSuggestionsAsync(viewModel);
        if (akzeptiert is null || akzeptiert.Count == 0)
            return;

        ApplyPatternSuggestions(akzeptiert);
    }

    /// <summary>
    /// Uebernimmt die angehakten Vorschlaege: Aktion auf "ersetzen" und den
    /// vorgeschlagenen Generator, ueber den vorhandenen Weg der einzelnen
    /// Feldregel -- das markiert das Profil wie jede andere Aenderung als
    /// ungespeichert veraendert.
    ///
    /// Wie <see cref="ApplyToSelection"/> gebuendelt in einer Massenzuweisung,
    /// damit die Nacharbeit (Profilpruefung, Vorschau, Zaehler) einmal am Ende
    /// laeuft statt einmal je uebernommenem Vorschlag.
    /// </summary>
    private void ApplyPatternSuggestions(IReadOnlyList<PatternSuggestionAcceptance> akzeptiert)
    {
        _bulkUpdate = true;
        try
        {
            foreach (var eintrag in akzeptiert)
            {
                var feld = Fields.FirstOrDefault(f =>
                    string.Equals(f.FieldName, eintrag.FieldName, StringComparison.OrdinalIgnoreCase));
                if (feld is null)
                    continue;

                feld.Action = FieldAction.Pseudonymize;
                feld.Generator = eintrag.Generator;
            }
        }
        finally
        {
            _bulkUpdate = false;
        }

        OnFieldRuleChanged();

        StatusText = akzeptiert.Count == 1
            ? "1 Feld aus der Mustererkennung übernommen."
            : $"{akzeptiert.Count} Felder aus der Mustererkennung übernommen.";
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
        ShowGeneratorOptionsCommand.RaiseCanExecuteChanged();
        ObfuscateCommand.RaiseCanExecuteChanged();
        DeobfuscateCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ObfuscateAllCommand.RaiseCanExecuteChanged();
        DeobfuscateAllCommand.RaiseCanExecuteChanged();
        ShowPatternSuggestionsCommand.RaiseCanExecuteChanged();
    }
}
