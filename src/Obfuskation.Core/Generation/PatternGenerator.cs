using System.Text;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Erzeugt einen Ersatzwert nach einer Zeichenmaske: 'A' Grossbuchstabe, 'a'
/// Kleinbuchstabe, '9' Ziffer, 'X' alphanumerisch, '\' escaped das
/// Folgezeichen als woertlich, alles Uebrige bleibt woertlich stehen.
///
/// Ohne gesetzte Maske leitet der Generator sie aus dem Original ab (Ziffer
/// -&gt; 9, Gross- -&gt; A, Klein- -&gt; a, Rest woertlich) und ist damit
/// unkonfiguriert ein allgemeiner formaterhaltender Generator fuer Vertrags-,
/// Beleg- und Auftragsnummern.
/// </summary>
public sealed class PatternGenerator : IPseudonymGenerator
{
    private string? _mask;

    public string Name => "pattern";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Pattern))
            _mask = settings.Pattern;
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);

        // Ohne konfigurierte Maske wird sie direkt am Original angewandt, ohne
        // den Umweg ueber eine abgeleitete Maskenzeichenkette: der Umweg waere
        // fehleranfaellig, weil ein woertlicher Rueckwaertsschraegstrich im
        // Original in der abgeleiteten Maske wie ein Escape-Zeichen aussaehe.
        return _mask is null
            ? ApplyDerivedMask(original, ref reader)
            : ApplyMask(_mask, ref reader);
    }

    private static string ApplyDerivedMask(string original, ref SeedReader reader)
    {
        var builder = new StringBuilder(original.Length);
        foreach (var character in original)
        {
            if (char.IsDigit(character))
                builder.Append(reader.NextDigit());
            else if (char.IsUpper(character))
                builder.Append(reader.NextUpperLetter());
            else if (char.IsLower(character))
                builder.Append(char.ToLowerInvariant(reader.NextUpperLetter()));
            else
                builder.Append(character);
        }
        return builder.ToString();
    }

    private static string ApplyMask(string mask, ref SeedReader reader)
    {
        var builder = new StringBuilder(mask.Length);
        for (var i = 0; i < mask.Length; i++)
        {
            var symbol = mask[i];

            if (symbol == '\\' && i + 1 < mask.Length)
            {
                builder.Append(mask[++i]);
                continue;
            }

            builder.Append(symbol switch
            {
                'A' => reader.NextUpperLetter(),
                'a' => char.ToLowerInvariant(reader.NextUpperLetter()),
                '9' => reader.NextDigit(),
                'X' => AlphanumericChar(reader.NextInt(62)),
                _ => symbol,
            });
        }
        return builder.ToString();
    }

    /// <summary>26 Grossbuchstaben + 26 Kleinbuchstaben + 10 Ziffern = 62 moegliche Zeichen.</summary>
    private static char AlphanumericChar(int value) => value switch
    {
        < 26 => (char)('A' + value),
        < 52 => (char)('a' + (value - 26)),
        _ => (char)('0' + (value - 52)),
    };
}
