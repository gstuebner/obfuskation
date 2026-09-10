using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Codebehind der Textansicht. Zwischenablage, Dateiauswahl und Drag&amp;Drop
/// gehoeren ausschliesslich hierher: <see cref="TextViewModel"/> kennt nur
/// <see cref="TextViewModel.InputText"/> als schlichten Zeichenkettenwert und
/// bleibt damit ohne laufende Avalonia-Umgebung pruefbar (siehe
/// <c>tests/Obfuskation.Gui.Tests/</c>, das keine Kopfumgebung startet).
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

    public TextView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
            viewModel.InputText = text;
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

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

    /// <summary>
    /// Eine gezogene Datei ersetzt den Text wie "Einfügen" -- die Ansicht ist
    /// bewusst auf genau eine Quelle je Vorgang ausgelegt, mehrere fallen
    /// gelassene Dateien werden nicht zusammengefuehrt.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        var dateien = e.DataTransfer.TryGetFiles();
        if (dateien is { Length: > 0 })
        {
            await LadeDateiAsync(dateien[0], viewModel);
            return;
        }

        if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
            viewModel.InputText = text;
    }

    private static async Task LadeDateiAsync(IStorageItem datei, TextViewModel viewModel)
    {
        var pfad = datei.TryGetLocalPath();
        if (pfad is null || !File.Exists(pfad))
            return;

        viewModel.InputText = await File.ReadAllTextAsync(pfad);
    }

    /// <summary>
    /// Liest die Markierung direkt aus dem TextBox -- das Ansichtsmodell
    /// bekommt nur die Zeichenkette, nie die Markierung selbst (siehe
    /// Klassenkopf). Ohne Markierung passiert nichts, statt mit einem leeren
    /// "Was?"-Feld einen Dialog zu oeffnen, den niemand ausgefuellt hat.
    /// </summary>
    private void OnAlwaysReplace(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        viewModel.RequestAlwaysReplace(InputBox.SelectedText);
    }

    private async void OnCopyResult(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

        var text = viewModel.RunReal();

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private async void OnSaveResult(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextViewModel viewModel)
            return;

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
}
