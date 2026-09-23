using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

/// <summary>Codebehind ohne eigenes Verhalten -- reine Bindungen, siehe TextRulesPanel.axaml.</summary>
public partial class TextRulesPanel : UserControl
{
    public TextRulesPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
