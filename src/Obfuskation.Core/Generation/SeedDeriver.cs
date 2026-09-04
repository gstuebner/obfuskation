using System.Security.Cryptography;
using System.Text;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Leitet Pseudonym-Seeds deterministisch ab. Gleicher Klartext ergibt im selben
/// Profil immer dasselbe Pseudonym, ueber alle Dateien und Laeufe hinweg — das
/// ist es, was Verknuepfungen ueber Kundennummern intakt haelt.
/// </summary>
public sealed class SeedDeriver
{
    public const int SaltLength = 32;

    private readonly byte[] _salt;

    public SeedDeriver(byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < 16)
            throw new ArgumentException("Das Salt muss mindestens 16 Byte lang sein.", nameof(salt));
        _salt = salt;
    }

    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    /// <summary>
    /// HMAC-SHA256 ueber Generatorname, Klartext und Zaehler. Der Zaehler wird
    /// bei einer Pseudonym-Kollision erhoeht, um einen anderen Wert zu erhalten.
    /// </summary>
    public byte[] Derive(string generatorName, string plaintext, int counter)
    {
        // Die Bestandteile werden laengenpraefigiert verkettet, damit
        // ("ab", "c") und ("a", "bc") nicht denselben Seed ergeben.
        using var buffer = new MemoryStream();
        WriteComponent(buffer, generatorName);
        WriteComponent(buffer, plaintext);
        Span<byte> counterBytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(counterBytes, counter);
        buffer.Write(counterBytes);

        return HMACSHA256.HashData(_salt, buffer.ToArray());
    }

    /// <summary>Eine profilweite Kennzahl, etwa der Datums-Offset.</summary>
    public byte[] DeriveProfileConstant(string purpose)
        => HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes("profile " + purpose));

    private static void WriteComponent(Stream target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BitConverter.TryWriteBytes(length, bytes.Length);
        target.Write(length);
        target.Write(bytes);
    }
}
