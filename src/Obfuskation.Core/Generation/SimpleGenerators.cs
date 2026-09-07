using System.Text;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>Generischer Ersatzwert fuer alles ohne spezielleren Typ.</summary>
public sealed class TokenGenerator : IPseudonymGenerator
{
    private string _prefix = "";

    public string Name => "token";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Prefix))
            _prefix = settings.Prefix;
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
        => _prefix + "TOK_" + Convert.ToHexString(seed[..4]);
}

/// <summary>Fester Platzhalter. Der Wert ist danach unwiederbringlich verloren.</summary>
public sealed class RedactGenerator : IPseudonymGenerator
{
    private string _placeholder = "***";

    public string Name => "redact";
    public bool IsReversible => false;
    public bool IsWordLike => false;

    public string Generate(ReadOnlySpan<byte> seed, string original) => _placeholder;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Domain))
            _placeholder = settings.Domain;
    }

    public void SetPlaceholder(string placeholder) => _placeholder = placeholder;
}

/// <summary>Vor- und Nachname aus den eingebetteten Wortlisten.</summary>
public sealed class PersonNameGenerator : IPseudonymGenerator
{
    public string Name => "personName";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var first = reader.Pick(WordLists.FirstNames);
        var last = reader.Pick(WordLists.LastNames);

        // Schreibweise "Nachname, Vorname" beibehalten, wenn das Original sie nutzt.
        return original.Contains(", ", StringComparison.Ordinal)
            ? $"{last}, {first}"
            : $"{first} {last}";
    }
}

/// <summary>Nur der Vorname.</summary>
public sealed class FirstNameGenerator : IPseudonymGenerator
{
    public string Name => "firstName";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        return reader.Pick(WordLists.FirstNames);
    }
}

/// <summary>Nur der Nachname.</summary>
public sealed class LastNameGenerator : IPseudonymGenerator
{
    public string Name => "lastName";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        return reader.Pick(WordLists.LastNames);
    }
}

/// <summary>Firmenname aus Wortliste, Zusatz und Rechtsform.</summary>
public sealed class CompanyNameGenerator : IPseudonymGenerator
{
    public string Name => "companyName";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var word = reader.Pick(WordLists.CompanyWords);
        var suffix = reader.Pick(WordLists.CompanySuffixes);
        var legalForm = reader.Pick(WordLists.LegalForms);
        return $"{word} {suffix} {legalForm}";
    }
}

/// <summary>Strassenname mit Hausnummer.</summary>
public sealed class StreetGenerator : IPseudonymGenerator
{
    public string Name => "street";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var street = reader.Pick(WordLists.Streets);

        // Nur eine Hausnummer anhaengen, wenn das Original auch eine trug.
        if (!original.Any(char.IsDigit))
            return street;

        var number = reader.NextInt(180) + 1;
        return $"{street} {number}";
    }
}

/// <summary>Ortsname.</summary>
public sealed class CityGenerator : IPseudonymGenerator
{
    public string Name => "city";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        return reader.Pick(WordLists.Cities);
    }
}

/// <summary>Deutsche Postleitzahl, fuenfstellig und nicht mit 00 beginnend.</summary>
public sealed class PostalCodeGenerator : IPseudonymGenerator
{
    public string Name => "postalCode";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var value = reader.NextInt(98999 - 1067 + 1) + 1067;
        return value.ToString("D5");
    }
}

/// <summary>
/// Zahlenkennung mit erhaltener Stellenzahl, fuehrende Nullen eingeschlossen.
/// Nichtziffern des Originals bleiben an ihrer Stelle stehen.
/// </summary>
public sealed class NumericIdGenerator : IPseudonymGenerator
{
    public string Name => "numericId";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var builder = new StringBuilder(original.Length);

        foreach (var character in original)
            builder.Append(char.IsDigit(character) ? reader.NextDigit() : character);

        // Original ohne jede Ziffer: eine achtstellige Kennung erzeugen.
        if (!original.Any(char.IsDigit))
        {
            builder.Clear();
            for (var i = 0; i < 8; i++)
                builder.Append(reader.NextDigit());
        }

        return builder.ToString();
    }
}

/// <summary>
/// Telefonnummer mit erhaltener Struktur: Ziffern werden ersetzt, Trennzeichen,
/// Klammern und ein fuehrendes Plus bleiben stehen.
/// </summary>
public sealed class PhoneGenerator : IPseudonymGenerator
{
    public string Name => "phone";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var builder = new StringBuilder(original.Length);
        var digitIndex = 0;

        foreach (var character in original)
        {
            if (!char.IsDigit(character))
            {
                builder.Append(character);
                continue;
            }

            // Laendervorwahl und die fuehrende Null der Ortsvorwahl beibehalten,
            // damit die Nummer als deutsche Nummer erkennbar bleibt.
            if (digitIndex < 2 && (character == '4' || character == '9' || character == '0'))
                builder.Append(character);
            else
                builder.Append(reader.NextDigit());

            digitIndex++;
        }

        return builder.Length == 0 ? "+49 30 " + reader.NextInt(90000000 - 10000000) : builder.ToString();
    }
}

/// <summary>
/// E-Mail-Adresse unter einer reservierten Domain. <c>.invalid</c> ist per
/// RFC 2606 fuer genau diesen Zweck vorgesehen und kann keine echte Adresse sein.
/// </summary>
public sealed class EmailGenerator : IPseudonymGenerator
{
    private string _domain = "example.invalid";

    public string Name => "email";
    public bool IsReversible => true;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Domain))
            _domain = settings.Domain.TrimStart('@');
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        var reader = new SeedReader(seed);
        var first = reader.Pick(WordLists.FirstNames).ToLowerInvariant();
        var last = reader.Pick(WordLists.LastNames).ToLowerInvariant();
        var discriminator = reader.NextInt(1000);
        return $"{Normalize(first)}.{Normalize(last)}{discriminator:D3}@{_domain}";
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(character);
            else if (character is '-' or '.')
                builder.Append(character);
        }
        return builder.Length == 0 ? "x" : builder.ToString();
    }
}
