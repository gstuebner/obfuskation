using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.Services;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

public partial class NewProfileWindow : Window
{
    public NewProfileWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// "Anderer Ort...": der Dateidialog gehoert der Ansicht, nicht dem
    /// fensterfreien Ansichtsmodell -- das traegt nur das Ergebnis ein.
    /// </summary>
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewProfileViewModel viewModel)
            return;

        var dialogs = new DialogService(this);
        var pfad = await dialogs.SaveFileAsync(
            Path.GetFileName(viewModel.TargetPath), Path.GetDirectoryName(viewModel.TargetPath));

        if (pfad is not null)
            viewModel.SetCustomTargetPath(pfad);
    }
}
