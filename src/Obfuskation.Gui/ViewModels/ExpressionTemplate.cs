namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Eine Tabelle innerhalb einer <see cref="ExpressionTemplate"/>.
/// </summary>
/// <param name="Name">Der Tabellenname, wie er im Ausdruck als <c>{name}</c> auftaucht.</param>
/// <param name="Values">Die Werte der Tabelle.</param>
public sealed record ExpressionTemplateTable(string Name, IReadOnlyList<string> Values);

/// <summary>
/// Eine Vorlage fuer den Knopf "Beispiel einsetzen ▾" im Ausdruck-Block des
/// Generator-Dialogs (Plan docs/plan-ausdruck-generator.md, P2b) -- ersetzt
/// Ausdruck und Tabellen des Entwurfs auf einen Schlag. Reine Daten, kein
/// eigenes Ansichtsmodell je Vorlage noetig: der Knopf nutzt
/// <see cref="RelayCommand{T}"/> mit dem Schluessel der Vorlage als Parameter.
/// </summary>
public sealed record ExpressionTemplate(
    string Key, string Title, string Expression, IReadOnlyList<ExpressionTemplateTable> Tables)
{
    /// <summary>Die drei Vorlagen aus dem Plan, in der dort genannten Reihenfolge.</summary>
    public static IReadOnlyList<ExpressionTemplate> All { get; } =
    [
        new("kfz", "KFZ-Kennzeichen", @"{kreis}-[A-Z]{2} \d{2,3}E?",
        [
            new ExpressionTemplateTable("kreis",
            [
                "B", "HH", "M", "K", "F", "S", "D", "DO", "E", "L",
                "HB", "DD", "H", "N", "DU", "BO", "W", "BI", "BN", "MS",
            ]),
        ]),
        new("kundennummer", "Kundennummer", @"KD-\d{6}", []),
        new("artikelnummer", "Artikelnummer", @"[A-Z]{3}-\d{4}(-[A-Z])?", []),
    ];

    public static ExpressionTemplate? Find(string? key)
        => key is null
            ? null
            : All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
