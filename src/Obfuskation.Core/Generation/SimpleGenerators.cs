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
        if (!string.IsNullOrEmpty(settings.Placeholder))
            _placeholder = settings.Placeholder;
    }

    public void SetPlaceholder(string placeholder) => _placeholder = placeholder;
}

/// <summary>Waehlt deterministisch aus einer eigenen Werteliste.</summary>
public sealed class WordlistGenerator : IPseudonymGenerator
{
    private List<string>? _values;

    public string Name => "wordlist";
    public bool IsReversible => true;
    public bool IsWordLike => true;

    public void Configure(GeneratorSettings settings)
    {
        if (settings.Values is { Count: > 0 })
            _values = settings.Values;
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        // Die einzige Pflichtoption unter den Generatoren: ohne eigene Werte
        // gibt es nichts, woraus gewaehlt werden koennte. Ein Schein-Default
        // wuerde das nur verschleiern.
        if (_values is not { Count: > 0 })
            throw new GenerationException(Name,
                "Generator 'wordlist' braucht eine eigene Werteliste unter 'values' im " +
                "zugehoerigen generators-Eintrag; ohne sie gibt es nichts zur Auswahl.");

        var reader = new SeedReader(seed);
        return reader.Pick(_values);
    }
}

/// <summary>
/// Behaelt <c>keepFirst</c> Zeichen am Anfang und <c>keepLast</c> am Ende,
/// ersetzt den Rest durch <c>maskChar</c>. Nicht umkehrbar: aus dem
/// maskierten Rest laesst sich der Klartext nicht zurueckgewinnen.
/// </summary>
public sealed class PartialMaskGenerator : IPseudonymGenerator
{
    private int _keepFirst;
    private int _keepLast = 4;
    private char _maskChar = '*';

    public string Name => "partialMask";
    public bool IsReversible => false;
    public bool IsWordLike => false;

    public void Configure(GeneratorSettings settings)
    {
        // Die Vorgabe "hinten vier Zeichen" gilt nur, solange gar nichts
        // eingestellt ist. Sobald eine der beiden Seiten gesetzt wird, zaehlt
        // allein das Eingestellte -- sonst gaebe es keinen Weg, ausschliesslich
        // den Anfang stehen zu lassen: 'keepFirst: 3' zoege die vier Zeichen am
        // Ende stillschweigend mit.
        if (settings.KeepFirst > 0 || settings.KeepLast > 0)
        {
            _keepFirst = settings.KeepFirst;
            _keepLast = settings.KeepLast;
        }

        if (!string.IsNullOrEmpty(settings.MaskChar))
            _maskChar = settings.MaskChar[0];
    }

    public string Generate(ReadOnlySpan<byte> seed, string original)
    {
        // Ist der Wert kuerzer als die Summe der behaltenen Zeichen, wuerde
        // ein ueberlappender Ausschnitt einen Teil des Klartexts trotzdem
        // zeigen -- der einzige wirklich gefaehrliche Fehler an dieser
        // Stelle. Deshalb dann vollstaendige Maskierung statt eines
        // Ausschnitts.
        if (original.Length <= _keepFirst + _keepLast)
            return new string(_maskChar, original.Length);

        var visibleStart = original[.._keepFirst];
        var visibleEnd = original[^_keepLast..];
        var maskedLength = original.Length - _keepFirst - _keepLast;

        return visibleStart + new string(_maskChar, maskedLength) + visibleEnd;
    }
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
