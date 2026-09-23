using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

public partial class MappingWindow : Window
{
    public MappingWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Die Auswahl der Eintragsliste ans Ansichtsmodell weiterreichen.
    ///
    /// <c>SelectedItems</c> gehoert der Liste und laesst sich nicht binden wie
    /// ein einzelner Wert (dieselbe Ueberlegung wie bei
    /// <c>FilesView.OnFieldSelectionChanged</c>) -- die Ansicht meldet die
    /// Auswahl deshalb selbst. Das Ansichtsmodell bleibt frei von
    /// Fensterwissen und damit pruefbar.
    /// </summary>
    private void OnEntrySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MappingViewModel viewModel || sender is not ListBox list)
            return;

        viewModel.UpdateSelection(list.SelectedItems?.OfType<MappingEntryViewModel>() ?? []);
    }
}
