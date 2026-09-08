namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Die Optionen eines einzelnen Feldes, fuer den Optionsdialog.
///
/// Traegt bewusst keinen eigenen Zustand: Namensraum-Anlage, Sichtbarkeit
/// der einzelnen Optionen und Vorschau kennt schon das uebergebene
/// <see cref="FieldRuleViewModel"/> -- derselbe Weg, den auch die inline
/// gebliebene Kennzeichnung an der Regelkarte nutzt (siehe
/// <c>MainWindow.axaml</c>). Der Dialog bindet direkt an <see cref="Field"/>
/// und braucht diese Klasse nur als Fassade mit einem Titel.
/// </summary>
public sealed class GeneratorOptionsViewModel
{
    public GeneratorOptionsViewModel(FieldRuleViewModel field) => Field = field;

    public FieldRuleViewModel Field { get; }

    /// <summary>Fenstertitel: das Feld, auf das sich der Dialog bezieht.</summary>
    public string Title => $"Generatoroptionen: {Field.FieldName}";

    public IReadOnlyList<GranularityOption> GranularityOptions { get; } = GranularityOption.All;
}
