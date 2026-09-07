using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Obfuskation.Core;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Die Kurzhilfe. Beantwortet die Fragen, die vor der ersten Benutzung
/// aufkommen — allen voran, wozu ein Profil ueberhaupt da ist.
///
/// Sie steht in der Oberflaeche, weil die Dokumentation neben dem Programm
/// liegt und damit erfahrungsgemaess ungelesen bleibt. Der Text ist bewusst
/// knapp; fuer alles Weitere verweist er auf die Anwenderdokumentation.
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();

        // Der Ablageort wird zur Laufzeit ermittelt statt fest im Text zu
        // stehen: er haengt von XDG_CONFIG_HOME ab und lautet unter Windows
        // ohnehin anders.
        this.FindControl<TextBlock>("ProfileDirText")!.Text = PathHelper.ProfileDirectory;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
