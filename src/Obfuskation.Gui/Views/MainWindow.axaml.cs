using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Unterhalb dieser Fensterbreite wird die Kopfzeile schmaler: der
    /// Themen-Knopf zeigt nur sein Symbol ("System"/"Dunkel"/"Hell" entfaellt),
    /// der Knopf "⚙ Regeln & Generatoren" nur das Zahnrad -- der volle Name
    /// steht dann im Tooltip. Gemessen fuer 1.10.0 an der breitesten Kopfzeile
    /// (Dateiansicht mit Profil und ungespeicherten Aenderungen, also mit
    /// "Neu aus Datei…" und "Speichern"): sie braucht mit vollen Beschriftungen
    /// rund 880 Punkte, bei MinWidth 760 ueberdeckten die Knoepfe sonst den
    /// Programmnamen. Unter 900 werden rund 190 Punkte frei.
    /// </summary>
    private const double NarrowWidthThreshold = 900;

    private const string RulesButtonLabel = "⚙ Regeln & Generatoren";
    private const string RulesButtonSymbol = "⚙";

    private TextBlock? _themeNameText;
    private Button? _rulesButton;

    public MainWindow()
    {
        InitializeComponent();

        _themeNameText = this.FindControl<TextBlock>("ThemeNameText");
        _rulesButton = this.FindControl<Button>("RulesButton");
        SizeChanged += (_, e) => UpdateNarrowHeader(e.NewSize.Width);

        // Die Nebenfenster oeffnet die Ansicht. Das Ansichtsmodell bittet nur
        // darum und bleibt selbst frei von Fensterwissen — sonst waere es nicht
        // mehr fuer sich pruefbar.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            viewModel.MappingRequested += () => ShowMapping(viewModel);
            viewModel.GeneratorOptionsRequested += () => ShowGeneratorOptions(viewModel);
            viewModel.AboutRequested += () => ShowAbout(viewModel);
            viewModel.HelpRequested += ShowHelp;
        };
    }

    private void UpdateNarrowHeader(double width)
    {
        var breit = width >= NarrowWidthThreshold;

        if (_themeNameText is not null)
            _themeNameText.IsVisible = breit;

        if (_rulesButton is not null)
            _rulesButton.Content = breit ? RulesButtonLabel : RulesButtonSymbol;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ShowMapping(MainViewModel viewModel)
    {
        if (viewModel.CreateMappingViewModel() is not { } inhalt)
            return;

        new MappingWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowGeneratorOptions(MainViewModel viewModel)
    {
        if (viewModel.CreateGeneratorOptionsViewModel() is not { } inhalt)
            return;

        new GeneratorOptionsWindow { DataContext = inhalt }.ShowDialog(this);
    }

    private void ShowAbout(MainViewModel viewModel)
        => new AboutWindow(viewModel.MappingStorePath).ShowDialog(this);

    private void ShowHelp() => new HelpWindow().ShowDialog(this);
}
