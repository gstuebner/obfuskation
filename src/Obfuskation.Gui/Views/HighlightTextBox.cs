using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Obfuskation.Gui.ViewModels;

namespace Obfuskation.Gui.Views;

/// <summary>
/// Ein gewoehnliches Eingabefeld, das unter seinem Text farbige Bereiche
/// zeichnen kann -- dieselben Farben wie die Prueffassung der Textansicht.
///
/// Der Anlass: ein <see cref="TextBox"/> kann keine Hintergruende je
/// Textabschnitt tragen, darum zeigte die Textansicht ihre Farben bisher nur
/// in der schreibgeschuetzten Prueffassung. Wer tippt statt einfuegt, blieb
/// dadurch im Bearbeiten-Zustand und sah links gar nichts -- genau dort, wo
/// sich entscheidet, was noch im Klartext hinausginge.
///
/// Gezeichnet wird nicht im Feld selbst, sondern auf einer eigenen Ebene
/// (<see cref="TextHighlightLayer"/>), die beim Anwenden der Vorlage direkt
/// unter den Text-Presenter gelegt wird: in der Fluent-Vorlage steckt
/// <c>PART_TextPresenter</c> in einem <see cref="Panel"/> innerhalb des
/// ScrollViewers, und ein Geschwister davor liegt deckungsgleich darunter und
/// scrollt ohne eigenes Zutun mit. Die Rechtecke kommen aus dem
/// <see cref="Avalonia.Media.TextFormatting.TextLayout"/> des Presenters
/// selbst -- Umbruch, Schrift und Zeilenhoehe stimmen damit immer mit dem
/// ueberein, was tatsaechlich dasteht.
///
/// <see cref="StyleKeyOverride"/> sorgt dafuer, dass die Stile und die Vorlage
/// von <see cref="TextBox"/> unveraendert greifen: fuer das Auge ist das ein
/// ganz normales Eingabefeld.
/// </summary>
public class HighlightTextBox : TextBox
{
    /// <summary>
    /// Die Hervorhebung fuer ersetzte Funde. Halbdurchsichtig, damit der Text
    /// darueber seine eigene Farbe behaelt; Deckkraft bewusst ueber der
    /// Haelfte, weil ein Drittel auf dunklem Fenster schlicht unterging.
    /// Gemeinsam mit der Prueffassung (<c>TextView.Fill</c>) genutzt, damit
    /// beide Zustaende derselben Seite dieselbe Farbe zeigen.
    /// </summary>
    public static readonly IBrush ReplacedBrush = new SolidColorBrush(Color.Parse("#8C3EA25B"));

    /// <summary>Die Hervorhebung fuer bewusst behaltene Funde (Haekchen aus), siehe <see cref="ReplacedBrush"/>.</summary>
    public static readonly IBrush ExcludedBrush = new SolidColorBrush(Color.Parse("#8CD97706"));

    public static readonly StyledProperty<IReadOnlyList<TextHighlight>?> HighlightsProperty =
        AvaloniaProperty.Register<HighlightTextBox, IReadOnlyList<TextHighlight>?>(nameof(Highlights));

    private TextHighlightLayer? _layer;

    /// <summary>
    /// Die zu zeichnenden Bereiche, als Positionen in <see cref="TextBox.Text"/>.
    /// Wer sie liefert, sorgt dafuer, dass sie zum aktuellen Text gehoeren
    /// (siehe <see cref="TextViewModel.InputHighlights"/>); was ueber das
    /// Textende hinausreicht, wird hier zur Sicherheit uebergangen.
    /// </summary>
    public IReadOnlyList<TextHighlight>? Highlights
    {
        get => GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _layer?.Detach();
        _layer = null;

        // Ohne den erwarteten Aufbau (eine kuenftige Vorlage koennte ihn
        // aendern) bleibt das Feld ein gewoehnliches Eingabefeld ohne Farben
        // -- die Prueffassung zeigt sie weiterhin. Ein Absturz waere fuer ein
        // reines Anzeigemittel der falsche Preis.
        if (e.NameScope.Find<TextPresenter>("PART_TextPresenter") is not { Parent: Panel panel } presenter)
            return;

        _layer = new TextHighlightLayer(this, presenter);
        panel.Children.Insert(panel.Children.IndexOf(presenter), _layer);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HighlightsProperty || change.Property == TextProperty)
            _layer?.InvalidateVisual();
    }
}

/// <summary>
/// Die Zeichenebene unter dem Text eines <see cref="HighlightTextBox"/>. Sie
/// nimmt keine Eingaben an und belegt nur so viel Platz wie ihr Panel; ihre
/// Rechtecke rechnet sie bei jedem Zeichnen neu aus dem Layout des Presenters
/// aus, statt sie zwischenzuspeichern -- nach einem Umbruch oder einer
/// Groessenaenderung waeren gespeicherte Rechtecke sofort falsch.
/// </summary>
internal sealed class TextHighlightLayer : Control
{
    private readonly HighlightTextBox _owner;
    private readonly TextPresenter _presenter;

    public TextHighlightLayer(HighlightTextBox owner, TextPresenter presenter)
    {
        _owner = owner;
        _presenter = presenter;

        IsHitTestVisible = false;
        Focusable = false;

        // Das Layout des Presenters aendert sich auch ohne neuen Text:
        // Fensterbreite, Umbruch, Schriftgroesse. Nach jedem Layoutdurchgang
        // neu zeichnen ist billig -- InvalidateVisual loest keinen weiteren
        // Layoutdurchgang aus, nur ein Neuzeichnen.
        _presenter.LayoutUpdated += OnPresenterLayoutUpdated;
    }

    public void Detach()
    {
        _presenter.LayoutUpdated -= OnPresenterLayoutUpdated;

        if (Parent is Panel panel)
            panel.Children.Remove(this);
    }

    private void OnPresenterLayoutUpdated(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var highlights = _owner.Highlights;
        if (highlights is not { Count: > 0 })
            return;

        var textLength = _owner.Text?.Length ?? 0;
        var offset = _presenter.TranslatePoint(default, this) ?? default;
        var layout = _presenter.TextLayout;

        foreach (var highlight in highlights)
        {
            if (highlight.Length <= 0 || highlight.Start < 0 || highlight.Start + highlight.Length > textLength)
                continue;

            var brush = highlight.Kind == TextSegmentKind.Excluded
                ? HighlightTextBox.ExcludedBrush
                : HighlightTextBox.ReplacedBrush;

            foreach (var rect in layout.HitTestTextRange(highlight.Start, highlight.Length))
                context.FillRectangle(brush, rect.Translate(offset));
        }
    }
}
