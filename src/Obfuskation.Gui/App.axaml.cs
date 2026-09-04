using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;
using Obfuskation.Gui.Views;

namespace Obfuskation.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = GuiSettings.Load();
            ThemeService.Apply(settings.Theme);

            var window = new MainWindow();

            // Der Dialogdienst braucht das Fenster als Eigentuemer, das
            // Ansichtsmodell entsteht aber davor. Deshalb wird er nachgereicht.
            var viewModel = new MainViewModel(settings, () => new DialogService(window));
            window.DataContext = viewModel;

            // Erst nach dem Oeffnen: der Dateidialog und die Fehleranzeige
            // brauchen ein Fenster, das schon da ist.
            window.Opened += async (_, _) =>
                await viewModel.InitializeAsync(Program.StartProfilePath, Program.StartDataPath);

            window.Width = settings.WindowWidth;
            window.Height = settings.WindowHeight;

            window.Closing += (_, _) =>
            {
                settings.WindowWidth = window.Width;
                settings.WindowHeight = window.Height;
                settings.Save();
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
