using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Die Nebenfenster oeffnet die Ansicht. Das Ansichtsmodell bittet nur
        // darum und bleibt selbst frei von Fensterwissen — sonst waere es nicht
        // mehr fuer sich pruefbar.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            viewModel.TextRulesRequested += () => ShowTextRules(viewModel);
            viewModel.MappingRequested += () => ShowMapping(viewModel);
            viewModel.AboutRequested += () => ShowAbout(viewModel);
            viewModel.HelpRequested += ShowHelp;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Die Auswahl der Feldliste ans Ansichtsmodell weiterreichen.
    ///
    /// <c>SelectedItems</c> gehoert der Liste und laesst sich nicht binden wie
    /// ein einzelner Wert — die Ansicht meldet die Auswahl deshalb selbst. Das
    /// Ansichtsmodell bleibt frei von Fensterwissen und damit pruefbar.
    /// </summary>
    private void OnFieldSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not ListBox list)
            return;

        viewModel.UpdateSelection(
            list.SelectedItems?.OfType<FieldRuleViewModel>() ?? []);
    }

    private void ShowTextRules(MainViewModel viewModel)
    {
        if (viewModel.CreateTextRulesViewModel() is not { } inhalt)
            return;

        new TextRulesWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowMapping(MainViewModel viewModel)
    {
        if (viewModel.CreateMappingViewModel() is not { } inhalt)
            return;

        new MappingWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowAbout(MainViewModel viewModel)
        => new AboutWindow(viewModel.MappingStorePath).ShowDialog(this);

    private void ShowHelp() => new HelpWindow().ShowDialog(this);
}
