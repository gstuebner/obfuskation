using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() : this(null) { }

    public AboutWindow(string? mappingStorePath)
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text =
            version is null ? "" : $"Fassung {version.Major}.{version.Minor}.{version.Build}";

        this.FindControl<TextBlock>("StoreText")!.Text =
            mappingStorePath ?? "(noch kein Profil geladen)";

        this.FindControl<TextBlock>("SettingsText")!.Text = GuiSettings.FilePath;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
