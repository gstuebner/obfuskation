using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

public partial class GeneratorEditorWindow : Window
{
    public GeneratorEditorWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
