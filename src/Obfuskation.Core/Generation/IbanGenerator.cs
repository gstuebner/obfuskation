using System.Text;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Erzeugt formal gueltige IBANs. Laendercode und Gesamtlaenge des Originals
/// bleiben erhalten und die Pruefziffer wird nach ISO 7064 Mod-97-10 korrekt
/// berechnet — sonst waeren die Testdaten fuer jede Anwendung wertlos, die die
/// Pruefziffer validiert.
/// </summary>
public sealed class IbanGenerator : IPseudonymGenerator
{
    private string _defaultCountry = "DE";

    public string Name => "iban";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Country) && settings.Country.Length == 2)
            _defaultCountry = settings.Country.ToUpperInvariant();
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var compact = Compact(original);

        var country = compact.Length >= 2 && char.IsAsciiLetter(compact[0]) && char.IsAsciiLetter(compact[1])
            ? compact[..2].ToUpperInvariant()
            : _defaultCountry;

        var totalLength = compact.Length >= 6 ? compact.Length : DefaultLengthFor(country);

        // Der Rumpf sind alle Stellen nach Laendercode und Pruefziffer.
        var bodyLength = totalLength - 4;
        var body = new StringBuilder(bodyLength);
        for (var i = 0; i < bodyLength; i++)
            body.Append(reader.NextDigit());

        var checkDigits = ComputeCheckDigits(country, body.ToString());
        var iban = country + checkDigits + body;

        return RestoreGrouping(original, iban);
    }

    /// <summary>Pruefziffer nach ISO 7064 Mod-97-10.</summary>
    public static string ComputeCheckDigits(string country, string body)
    {
        // Rechenschema: Land und "00" ans Ende, Buchstaben in Zahlen, dann
        // 98 minus Rest der Division durch 97.
        var rearranged = body + country + "00";
        var remainder = Mod97(rearranged);
        return (98 - remainder).ToString("D2");
    }

    /// <summary>Prueft eine IBAN nach ISO 7064 Mod-97-10.</summary>
    public static bool IsValid(string iban)
    {
        var compact = Compact(iban);
        if (compact.Length is < 5 or > 34)
            return false;
        if (!char.IsAsciiLetter(compact[0]) || !char.IsAsciiLetter(compact[1]))
            return false;
        if (!char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3]))
            return false;

        var rearranged = compact[4..] + compact[..4];
        return Mod97(rearranged) == 1;
    }

    private static int Mod97(string value)
    {
        // Stellenweise rechnen, weil die Zahl fuer jeden eingebauten Typ zu gross waere.
        var remainder = 0;
        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                remainder = (remainder * 10 + (character - '0')) % 97;
            }
            else if (char.IsAsciiLetter(character))
            {
                var numeric = char.ToUpperInvariant(character) - 'A' + 10;
                remainder = (remainder * 100 + numeric) % 97;
            }
            else
            {
                throw new ArgumentException($"Ungültiges Zeichen in der IBAN: '{character}'", nameof(value));
            }
        }
        return remainder;
    }

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        return builder.ToString();
    }

    /// <summary>
    /// Uebernimmt die Gruppierung des Originals, damit eine mit Leerzeichen
    /// geschriebene IBAN auch als solche zurueckkommt.
    /// </summary>
    private static string RestoreGrouping(string original, string compactIban)
    {
        if (!original.Contains(' '))
            return compactIban;

        var builder = new StringBuilder(compactIban.Length + compactIban.Length / 4);
        for (var i = 0; i < compactIban.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                builder.Append(' ');
            builder.Append(compactIban[i]);
        }
        return builder.ToString();
    }

    private static int DefaultLengthFor(string country) => country switch
    {
        "DE" => 22,
        "AT" => 20,
        "CH" => 21,
        "NL" => 18,
        "BE" => 16,
        "FR" => 27,
        "IT" => 27,
        "ES" => 24,
        "LU" => 20,
        "GB" => 22,
        _ => 22,
    };
}

/// <summary>Erzeugt formal gueltige BICs im Muster <c>AAAADEFFXXX</c>.</summary>
public sealed class BicGenerator : IPseudonymGenerator
{
    private string _defaultCountry = "DE";

    public string Name => "bic";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Country) && settings.Country.Length == 2)
            _defaultCountry = settings.Country.ToUpperInvariant();
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var trimmed = original.Trim().ToUpperInvariant();

        var country = trimmed.Length >= 6 && char.IsAsciiLetter(trimmed[4]) && char.IsAsciiLetter(trimmed[5])
            ? trimmed[4..6]
            : _defaultCountry;

        var builder = new StringBuilder(11);
        for (var i = 0; i < 4; i++)
            builder.Append(reader.NextUpperLetter());
        builder.Append(country);
        for (var i = 0; i < 2; i++)
            builder.Append(reader.NextUpperLetter());

        // Die Filialkennung nur anhaengen, wenn das Original elfstellig war.
        if (trimmed.Length == 11)
            for (var i = 0; i < 3; i++)
                builder.Append(reader.NextUpperLetter());

        return builder.ToString();
    }
}
