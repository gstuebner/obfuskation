using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Ein schlichter Meldungsdialog mit bis zu drei Schaltflaechen. Avalonia
/// bringt keinen mit; dieser genuegt fuer alle Rueckfragen der Oberflaeche
/// (Speichern/Verwerfen/Abbrechen, Umbenennen-Bestaetigung).
///
/// Das Ergebnis kommt ueber <see cref="Window.ShowDialog{TResult}"/>: jede
/// Schaltflaeche schliesst das Fenster mit ihrem <c>Tag</c> als Ergebniswert.
/// Ohne Auswahl (Schliessen ueber die Titelleiste) liefert es <c>null</c> —
/// das muss der Aufrufer als "Abbrechen" werten, die vorsichtige Richtung.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    /// <param name="title">Fenstertitel.</param>
    /// <param name="message">Die Frage oder Erklaerung.</param>
    /// <param name="buttons">
    /// Beschriftung und Ergebniswert je Schaltflaeche, hoechstens drei. Die
    /// letzte gilt als die betonte Wahl.
    /// </param>
    public ConfirmWindow(string title, string message, IReadOnlyList<(string Label, string Result)> buttons)
        : this()
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;

        var slots = new[] { "Button1", "Button2", "Button3" };

        // Die Schaltflaechen stehen fest in der Ansicht; ueberzaehlige werden
        // ausgeblendet statt dynamisch erzeugt -- fuer hoechstens drei lohnt
        // sich das nicht.
        for (var i = 0; i < slots.Length; i++)
        {
            var button = this.FindControl<Button>(slots[i])!;
            if (i < buttons.Count)
            {
                button.Content = buttons[i].Label;
                button.Tag = buttons[i].Result;
                button.IsVisible = true;
            }
            else
            {
                button.IsVisible = false;
            }
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnButtonClick(object? sender, RoutedEventArgs e)
        => Close((sender as Button)?.Tag as string);
}
