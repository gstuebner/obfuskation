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
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
}
