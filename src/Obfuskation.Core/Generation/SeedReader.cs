using System.Security.Cryptography;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Liest deterministisch Zahlen aus einem Seed. Sind die Bytes aufgebraucht,
/// wird per SHA-256 nachgezogen — der Ablauf bleibt dabei reproduzierbar.
/// </summary>
public ref struct SeedReader
{
    private byte[] _buffer;
    private int _position;

    public SeedReader(ReadOnlySpan<byte> seed)
    {
        _buffer = seed.ToArray();
        _position = 0;
    }

    private byte NextByte()
    {
        if (_position >= _buffer.Length)
        {
            _buffer = SHA256.HashData(_buffer);
            _position = 0;
        }
        return _buffer[_position++];
    }

    public uint NextUInt32()
    {
        uint value = 0;
        for (var i = 0; i < 4; i++)
            value = (value << 8) | NextByte();
        return value;
    }

    /// <summary>Gleichverteilte Zahl in <c>[0, exclusiveUpperBound)</c>.</summary>
    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));

        // Ablehnungsverfahren, damit keine Werte bevorzugt werden. Der Rest der
        // Division waere sonst fuer kleine Zahlen leicht haeufiger.
        var limit = uint.MaxValue - (uint.MaxValue % (uint)exclusiveUpperBound) - 1;
        uint value;
        do
        {
            value = NextUInt32();
        } while (value > limit);

        return (int)(value % (uint)exclusiveUpperBound);
    }

    public char NextDigit() => (char)('0' + NextInt(10));

    public char NextUpperLetter() => (char)('A' + NextInt(26));

    /// <summary>Waehlt ein Element aus einer Liste.</summary>
    public T Pick<T>(IReadOnlyList<T> items) => items[NextInt(items.Count)];
}
