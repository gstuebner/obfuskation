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

            viewModel.MappingRequested += () => ShowMapping(viewModel);
            viewModel.GeneratorOptionsRequested += () => ShowGeneratorOptions(viewModel);
            viewModel.AboutRequested += () => ShowAbout(viewModel);
            viewModel.HelpRequested += ShowHelp;
            viewModel.ExtensionsRequested += () => ShowExtensions(viewModel);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ShowMapping(MainViewModel viewModel)
    {
        if (viewModel.CreateMappingViewModel() is not { } inhalt)
            return;

        new MappingWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowGeneratorOptions(MainViewModel viewModel)
    {
        if (viewModel.CreateGeneratorOptionsViewModel() is not { } inhalt)
            return;

        new GeneratorOptionsWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowAbout(MainViewModel viewModel)
        => new AboutWindow(viewModel.MappingStorePath).ShowDialog(this);

    private void ShowHelp() => new HelpWindow().ShowDialog(this);

    private void ShowExtensions(MainViewModel viewModel)
        => new ExtensionsWindow { DataContext = viewModel.CreateExtensionsViewModel() }.ShowDialog(this);
}
