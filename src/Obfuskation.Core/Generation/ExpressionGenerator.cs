using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Erzeugt Werte nach einem <see cref="GeneratorExpression"/>, mit eigenen
/// Tabellen fuer Tabellenverweise wie <c>{kreis}</c>. Anders als
/// <see cref="PatternGenerator"/> ist der Generator damit in sich
/// vollstaendig -- Ausdruck und Tabellen stehen im selben
/// <c>generators</c>-Eintrag und lassen sich unveraendert in ein anderes
/// Projekt uebernehmen.
///
/// Als wortartig eingestuft (<see cref="IsWordLike"/>), obwohl die erzeugten
/// Werte kein Wort im ueblichen Sinn sind: sie sind kurz und unterschiedlich
/// lang (ein KFZ-Kennzeichen mal mit, mal ohne "E"), und ohne Wortgrenze
/// wuerde <c>ReverseTextMapper</c> "M-AB 12" auch mitten in "M-AB 123"
/// zurueckfuehren. Die Grenze greift ohnehin nur an Raendern aus Buchstaben
/// oder Ziffern und schadet darum nicht, wo sie nicht noetig ist.
/// </summary>
public sealed class ExpressionGenerator : IPseudonymGenerator
{
    private GeneratorExpression? _expression;
    private ExpressionError? _parseError;
    private Dictionary<string, IReadOnlyList<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private int _maxLength;

    public string Name => "expression";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Expression))
        {
            if (GeneratorExpression.TryParse(settings.Expression, out var expression, out var error))
            {
                _expression = expression;
                _parseError = null;
            }
            else
            {
                _expression = null;
                _parseError = error;
            }
        }

        // Ohne Ruecksicht auf Gross-/Kleinschreibung, damit {Kreis} im
        // Ausdruck dieselbe Tabelle trifft wie ein unter "kreis" angelegter Eintrag.
        var tables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (settings.Tables is not null)
        {
            foreach (var (name, values) in settings.Tables)
                tables[name] = values;
        }
        _tables = tables;
        _maxLength = _expression?.MaxLength(_tables) ?? 0;
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        if (_parseError is not null)
        {
            throw new GenerationException(Name,
                $"Der Ausdruck ist fehlerhaft (Stelle {_parseError.Position}): {_parseError.Message}");
        }

        if (_expression is null)
        {
            throw new GenerationException(Name,
                "Generator 'expression' braucht einen Ausdruck unter 'expression' im zugehoerigen " +
                "generators-Eintrag; ohne ihn gibt es nichts zu erzeugen.");
        }

        // Die Pruefung meldet das schon vorher, aber die Vorschau im
        // Generator-Dialog erzeugt bei jedem Tastenschlag, auch vor jeder
        // Pruefung -- ein Ausdruck wie ((a{64}){64}){64} haette sie sonst
        // mit einem Wert von 262 144 Zeichen aufgehalten.
        if (_maxLength > GeneratorExpression.MaxValueLength)
        {
            throw new GenerationException(Name,
                $"Der Ausdruck kann Werte bis {_maxLength} Zeichen erzeugen, erlaubt sind höchstens " +
                $"{GeneratorExpression.MaxValueLength}.");
        }

        var reader = new SeedReader(seed);
        return _expression.Generate(ref reader, _tables);
    }
}
