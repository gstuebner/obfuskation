using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

public partial class PatternSuggestionsWindow : Window
{
    public PatternSuggestionsWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
