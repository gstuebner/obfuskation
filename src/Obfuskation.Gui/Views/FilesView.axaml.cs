using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

public partial class FilesView : UserControl
{
    public FilesView() => InitializeComponent();

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
}
