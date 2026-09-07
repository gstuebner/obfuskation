using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // Closing kann nicht auf eine Task warten: beim ersten Aufruf wird
            // abgebrochen (e.Cancel = true) und die Rueckfrage gestartet; faellt
            // sie zu "weitermachen" aus, wird ueber den Merker ein zweites Mal
            // Close() gerufen, das dann tatsaechlich schliesst. Der Merker muss
            // vor diesem zweiten Aufruf gesetzt sein, sonst haengt sich die
            // Rueckfrage in einer Schleife auf.
            var closeConfirmed = false;

            window.Closing += (_, e) =>
            {
                settings.WindowWidth = window.Width;
                settings.WindowHeight = window.Height;
                settings.Save();

                if (closeConfirmed)
                    return;

                e.Cancel = true;

                // Bewusst ueber den Dispatcher statt direkt: ohne ungespeicherte
                // Aenderungen liefert EnsureChangesHandledAsync sofort, ohne je
                // zu warten. Das zweite Close() liefe dann noch waehrend dieses
                // Closing-Aufrufs -- ein Wiedereintritt, nach dem der Abbruch
                // oben fuer ein bereits geschlossenes Fenster gaelte. So laeuft
                // die Rueckfrage erst, wenn dieser Aufruf abgeschlossen ist.
                Dispatcher.UIThread.Post(async () =>
                {
                    if (!await viewModel.EnsureChangesHandledAsync())
                        return;

                    closeConfirmed = true;
                    window.Close();
                });
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
