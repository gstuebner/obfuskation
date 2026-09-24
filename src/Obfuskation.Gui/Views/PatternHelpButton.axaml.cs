using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Der "?"-Knopf mit dem Spickzettel zu regulaeren Ausdruecken (Plan Teil B6):
/// Erklaerung, Kuerzel-Tabelle, Beispiele und ein KI-Hinweis samt Vorlage zum
/// Kopieren. Rein informativ und ohne eigenes Ansichtsmodell -- die
/// Zwischenablage wird wie in <c>TextView.axaml.cs</c> direkt im Codebehind
/// angesprochen.
/// </summary>
public partial class PatternHelpButton : UserControl
{
    /// <summary>
    /// Ob die Beispiele und die Kopiervorlage sich auf einen Spaltennamen
    /// beziehen (Spaltenmuster) statt auf Freitext (Textregel). Wirkt sich auf
    /// die gezeigten Beispiele und die Kopiervorlage aus (siehe
    /// <see cref="OnCopyRequest"/>).
    /// </summary>
    public static readonly StyledProperty<bool> IsFieldNameContextProperty =
        AvaloniaProperty.Register<PatternHelpButton, bool>(nameof(IsFieldNameContext));

    public bool IsFieldNameContext
    {
        get => GetValue(IsFieldNameContextProperty);
        set => SetValue(IsFieldNameContextProperty, value);
    }

    public PatternHelpButton() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private const string TextVorlage =
        "Ich brauche einen regulären Ausdruck in der .NET-Schreibweise (System.Text.RegularExpressions).\n\n"
        + "Format in Worten: [hier beschreiben, z. B. \"FW, dann sechs Ziffern\"]\n"
        + "Passende Beispiele (ausgedacht, keine echten Werte): [Beispiel 1], [Beispiel 2]\n"
        + "Nicht passende Beispiele: [Beispiel]\n\n"
        + "Bitte Wortgrenzen (\\b) setzen, wo sinnvoll, und ausschließlich den fertigen Ausdruck "
        + "ausgeben, ohne Erklärung.";

    private const string SpaltennameVorlage =
        "Ich brauche einen regulären Ausdruck in der .NET-Schreibweise (System.Text.RegularExpressions), "
        + "der auf den ganzen Spaltennamen passt (nicht nur einen Teil davon). Groß-/Kleinschreibung ist egal.\n\n"
        + "Format in Worten: [hier beschreiben, z. B. \"enthält 'iban'\" oder \"endet auf 'nr'\"]\n"
        + "Passende Beispiele (ausgedacht): [Beispiel 1], [Beispiel 2]\n"
        + "Nicht passende Beispiele: [Beispiel]\n\n"
        + "Bitte ausschließlich den fertigen Ausdruck ausgeben, ohne Erklärung.";

    /// <summary>
    /// "Anfrage für die KI kopieren": legt die passende Vorlage in die
    /// Zwischenablage und zeigt kurz "Kopiert." -- derselbe Weg wie
    /// <c>TextView.axaml.cs</c> (<c>TopLevel.GetTopLevel(this)?.Clipboard</c>),
    /// hier im Codebehind, weil das Ansichtsmodell fensterfrei bleiben soll.
    /// </summary>
    private async void OnCopyRequest(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            await clipboard.SetTextAsync(IsFieldNameContext ? SpaltennameVorlage : TextVorlage);

            if (CopiedHint is not null)
            {
                CopiedHint.IsVisible = true;

                // Kurz stehen lassen, dann wieder ausblenden -- die einzige
                // Rueckmeldung, dass das Kopieren geklappt hat.
                await Task.Delay(TimeSpan.FromSeconds(2));
                CopiedHint.IsVisible = false;
            }
        }
        catch (Exception)
        {
            // Keine Zwischenablage verfuegbar (Xvfb ohne xclip o.ae.) -- kein
            // Grund, den Spickzettel abzubrechen.
        }
    }
}
