using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Codebehind der Textansicht. Zwischenablage, Dateiauswahl, Drag&amp;Drop und
/// das Einfaerben gehoeren ausschliesslich hierher: <see cref="TextViewModel"/>
/// kennt nur <see cref="TextViewModel.InputText"/> als schlichten
/// Zeichenkettenwert und die Abschnitte als reine Daten und bleibt damit ohne
/// laufende Avalonia-Umgebung pruefbar (siehe <c>tests/Obfuskation.Gui.Tests/</c>,
/// das keine Kopfumgebung startet).
/// </summary>
public partial class TextView : UserControl
{
    private static readonly FilePickerFileType TextFiles = new("Textdateien")
    {
        Patterns = ["*.txt", "*.md", "*.log"],
    };

    private static readonly FilePickerFileType AllFiles = new("Alle Dateien")
    {
        Patterns = ["*"],
    };

    /// <summary>
    /// Die beiden Hervorhebungen, halbdurchsichtig, damit der Text darunter
    /// seine eigene Farbe behaelt. Deckkraft bewusst ueber der Haelfte: auf
    /// dunklem Fenster ging ein Drittel im Hintergrund schlicht unter, auf
    /// hellem traegt es immer noch nicht auf.
    /// </summary>
    private static readonly IBrush ReplacedBrush = new SolidColorBrush(Color.Parse("#8C3EA25B"));

    private static readonly IBrush ExcludedBrush = new SolidColorBrush(Color.Parse("#8CD97706"));

    /// <summary>
    /// Die Markierung, wie sie beim Oeffnen des Kontextmenues bestand. Wird
    /// dort festgehalten und nicht erst beim Klick auf den Eintrag gelesen:
    /// ob ein aufgehendes Menue den Fokus und damit die Markierung kostet, ist
    /// Sache der Fensterverwaltung. Genau daran ist der Vorgaenger dieses
    /// Weges gescheitert -- ein Knopf, der die Markierung beim eigenen Klick
    /// verlor und dann wirkungslos blieb.
    /// </summary>
    private string _selectionAtMenuOpen = "";

    private TextViewModel? _attached;

    // Die benannten Elemente stehen NICHT in den generierten Feldern: das
    // handgeschriebene InitializeComponent unten (wie in allen Views dieses
    // Projekts) laedt nur das XAML, die Feldverdrahtung des Name-Generators
    // entsteht damit nicht -- die Felder bleiben null. Deshalb wie ueberall
    // sonst im Projekt ueber FindControl aufloesen.
    private readonly SelectableTextBlock _inputBlock;
    private readonly SelectableTextBlock _resultBlock;
    private readonly TextBox _inputBox;
    private readonly MenuItem _alwaysReplaceItem;
    private readonly MenuItem _alwaysReplaceEditItem;

    public TextView()
    {
        InitializeComponent();

        _inputBlock = this.FindControl<SelectableTextBlock>("InputBlock")!;
        _resultBlock = this.FindControl<SelectableTextBlock>("ResultBlock")!;
        _inputBox = this.FindControl<TextBox>("InputBox")!;
        _alwaysReplaceItem = this.FindControl<MenuItem>("AlwaysReplaceItem")!;
        _alwaysReplaceEditItem = this.FindControl<MenuItem>("AlwaysReplaceEditItem")!;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ------------------------------------------------------ Hervorhebung

    private void AttachViewModel()
    {
        if (_attached is not null)
            _attached.PropertyChanged -= OnViewModelPropertyChanged;

        _attached = DataContext as TextViewModel;
        if (_attached is null)
            return;

        _attached.PropertyChanged += OnViewModelPropertyChanged;
        UpdateHighlights(_attached);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TextViewModel viewModel
            && e.PropertyName is nameof(TextViewModel.InputSegments) or nameof(TextViewModel.ResultSegments))
        {
            UpdateHighlights(viewModel);
        }
    }

    /// <summary>
    /// Baut beide Textseiten aus den Abschnitten des Ansichtsmodells neu auf.
    /// Eine Bindung ist das nicht: <c>Inlines</c> laesst sich nicht deklarativ
    /// an eine Auflistung binden, und das Ansichtsmodell soll fensterfrei
    /// pruefbar bleiben (siehe Klassenkopf).
    /// </summary>
    private void UpdateHighlights(TextViewModel viewModel)
    {
        Fill(_inputBlock, viewModel.InputSegments);
        Fill(_resultBlock, viewModel.ResultSegments);
    }

    private static void Fill(SelectableTextBlock target, IReadOnlyList<TextSegment> segments)
    {
        var inlines = new InlineCollection();

        foreach (var segment in segments)
        {
            inlines.Add(new Run(segment.Text)
            {
                Background = segment.Kind switch
                {
                    TextSegmentKind.Replaced => ReplacedBrush,
                    TextSegmentKind.Excluded => ExcludedBrush,
                    _ => null,
                },
            });
        }

        target.Inlines = inlines;
    }

    // ------------------------------------------------------- Kontextmenue

    /// <summary>
    /// Beschriftet den Eintrag, bevor das Menue aufgeht. Er nennt den
    /// markierten Wert: ein Eintrag, der nicht sagt, worauf er wirkt, war
    /// genau das Verstaendnisproblem der vorigen Fassung.
    /// </summary>
    private void OnInputContextRequested(object? sender, ContextRequestedEventArgs e)
        => PrepareAlwaysReplaceItem(_alwaysReplaceItem, _inputBlock.SelectedText ?? "");

    /// <summary>
    /// Dasselbe fuer das Eingabefeld: "immer ersetzen…" muss an beiden
    /// Stellen liegen, an denen markiert wird -- ein Anfaenger bleibt nach
    /// dem Einfuegen im Bearbeiten-Zustand und sucht den Eintrag genau dort.
    /// </summary>
    private void OnEditContextRequested(object? sender, ContextRequestedEventArgs e)
        => PrepareAlwaysReplaceItem(_alwaysReplaceEditItem, _inputBox.SelectedText ?? "");

    /// <summary>
    /// Haelt die Markierung fest und beschriftet damit den Menueeintrag --
    /// gemeinsam fuer beide Kontextmenues. Festgehalten wird sie hier und
    /// nicht erst beim Klick auf den Eintrag gelesen (siehe Kommentar an
    /// <see cref="_selectionAtMenuOpen"/>).
    /// </summary>
    private void PrepareAlwaysReplaceItem(MenuItem item, string selection)
    {
        _selectionAtMenuOpen = selection.Trim();

        var hasSelection = _selectionAtMenuOpen.Length > 0;

        item.IsEnabled = hasSelection;
        item.Header = hasSelection
            ? $"»{Shorten(_selectionAtMenuOpen)}« immer ersetzen…"
            : "Markiertes immer ersetzen…";
    }

    /// <summary>
    /// Kuerzt eine lange Markierung fuer die Aufschrift. Der Dialog bekommt
    /// unverkuerzt, was markiert wurde -- gekuerzt wird nur, was im Menue
    /// steht, damit es nicht ueber den Bildschirm laeuft.
    /// </summary>
    /// <remarks>Oeffentlich und ohne Zustand, damit sich die Randfaelle einzeln pruefen lassen.</remarks>
    public static string Shorten(string value, int maxLength = 30)
    {
        var einzeilig = value.ReplaceLineEndings(" ").Trim();

        return einzeilig.Length <= maxLength
            ? einzeilig
            : einzeilig[..(maxLength - 1)].TrimEnd() + "…";
    }

    private void OnCopySelection(object? sender, RoutedEventArgs e) => _inputBlock.Copy();

    private void OnSelectAll(object? sender, RoutedEventArgs e) => _inputBlock.SelectAll();

    // Die vier Standardeintraege des Eingabefelds: ein eigenes Menue verdraengt
    // das eingebaute, also werden sie nachgebaut -- ueber dieselben
    // TextBox-Methoden, die auch das native Menue ruft.
    private void OnEditCut(object? sender, RoutedEventArgs e) => _inputBox.Cut();

    private void OnEditCopy(object? sender, RoutedEventArgs e) => _inputBox.Copy();

    private void OnEditPaste(object? sender, RoutedEventArgs e) => _inputBox.Paste();

    private void OnEditSelectAll(object? sender, RoutedEventArgs e) => _inputBox.SelectAll();

    private void OnAlwaysReplaceSelection(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TextViewModel viewModel)
            viewModel.RequestAlwaysReplace(_selectionAtMenuOpen);
    }

    /// <summary>
    /// Strg+M als Tastaturweg zum selben Dialog. Tunnelt von der Ansicht aus,
    /// damit es unabhaengig davon greift, welche der beiden Textseiten gerade
    /// den Fokus haelt.
    /// </summary>
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.M || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        if (DataContext is not TextViewModel viewModel)
            return;

        var markierung = (viewModel.IsEditing ? _inputBox.SelectedText : _inputBlock.SelectedText) ?? "";
        if (markierung.Trim().Length == 0)
            return;

        e.Handled = true;
        viewModel.RequestAlwaysReplace(markierung.Trim());
    }

    // ------------------------------------------------ Text hereinbekommen

    private void OnToggleEditing(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TextViewModel viewModel)
            viewModel.ToggleEditing();
    }

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
                viewModel.SetInputFromOutside(text);
        }
        catch (Exception ex)
        {
            // Ein async void-Behandler ohne Schutz beendet bei jedem Fehler den
            // Prozess. Das globale Netz faengt ihn inzwischen zwar, aber hier
            // laesst sich die Ursache benennen statt nur zu protokollieren.
            viewModel.ReportViewError("Die Zwischenablage ließ sich nicht lesen: " + ex.Message);
        }
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Textdatei öffnen",
                AllowMultiple = false,
                FileTypeFilter = [TextFiles, AllFiles],
            });

            if (files.Count == 0)
                return;

            await LadeDateiAsync(files[0], viewModel);
        }
        catch (Exception ex)
        {
            viewModel.ReportViewError("Die Datei ließ sich nicht öffnen: " + ex.Message);
        }
    }

    /// <summary>
    /// Eine gezogene Datei ersetzt den Text wie "Einfügen" -- die Ansicht ist
    /// bewusst auf genau eine Quelle je Vorgang ausgelegt, mehrere fallen
    /// gelassene Dateien werden nicht zusammengefuehrt.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        try
        {
            var dateien = e.DataTransfer.TryGetFiles();
            if (dateien is { Length: > 0 })
            {
                await LadeDateiAsync(dateien[0], viewModel);
                return;
            }

            if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
                viewModel.SetInputFromOutside(text);
        }
        catch (Exception ex)
        {
            viewModel.ReportViewError("Das Gezogene ließ sich nicht übernehmen: " + ex.Message);
        }
    }

    /// <summary>
    /// Groessenschranke: der Text wird bei jeder Aenderung vollstaendig neu
    /// gescannt und in Abschnitte zerlegt. Was darueber hinausgeht, gehoert in
    /// den Dateimodus, nicht in diese Ansicht.
    /// </summary>
    private const long MaxFileSize = 5_000_000;

    private static async Task LadeDateiAsync(IStorageItem datei, TextViewModel viewModel)
    {
        var pfad = datei.TryGetLocalPath();
        if (pfad is null || !File.Exists(pfad))
            return;

        if (new FileInfo(pfad).Length > MaxFileSize)
        {
            viewModel.ReportViewError(
                "Die Datei ist zu groß für die Textansicht (höchstens 5 MB). Bitte den Dateimodus verwenden.");
            return;
        }

        viewModel.SetInputFromOutside(await File.ReadAllTextAsync(pfad));
    }

    // ---------------------------------------------------- Text hinausgeben

    private async void OnCopyResult(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        try
        {
            var text = viewModel.RunReal();

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            viewModel.ReportViewError("Das Ergebnis ließ sich nicht kopieren: " + ex.Message);
        }
    }

    private async void OnSaveResult(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
                return;

            var text = viewModel.RunReal();

            var ziel = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Ergebnis speichern",
                SuggestedFileName = "ergebnis.txt",
                DefaultExtension = "txt",
                FileTypeChoices = [TextFiles, AllFiles],
                ShowOverwritePrompt = true,
            });

            var pfad = ziel?.TryGetLocalPath();
            if (pfad is not null)
                await File.WriteAllTextAsync(pfad, text);
        }
        catch (Exception ex)
        {
            viewModel.ReportViewError("Das Ergebnis ließ sich nicht speichern: " + ex.Message);
        }
    }
}
