using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

public partial class ProfilesWindow : Window
{
    public ProfilesWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
