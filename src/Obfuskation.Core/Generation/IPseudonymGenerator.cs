using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Erzeugt aus einem deterministischen Seed einen typgerechten Ersatzwert.
/// Implementierungen muessen frei von Zustand und frei von Zufall sein: derselbe
/// Seed muss immer denselben Wert liefern, sonst zerfaellt die Umkehrbarkeit.
/// </summary>
public interface IPseudonymGenerator
{
    string Name { get; }

    /// <summary>
    /// Ob sich der erzeugte Wert wieder auf das Original zurueckfuehren laesst.
    /// <c>false</c> etwa bei <c>redact</c>.
    /// </summary>
    bool IsReversible { get; }

    /// <summary>
    /// Ob der erzeugte Wert wortartig ist. Bestimmt, ob bei der Rueckabbildung
    /// in Freitext Wortgrenzen verlangt werden.
    /// </summary>
    bool IsWordLike { get; }

    /// <summary>
    /// Ob der Generator seinen eigenen Umkehrweg mitbringt und deshalb keinen
    /// Eintrag in der Mapping-Tabelle braucht (etwa die Datumsverschiebung).
    /// </summary>
    bool HasIntrinsicInverse => false;

    /// <param name="seed">Deterministische Bytes, mindestens 32.</param>
    /// <param name="original">Der Klartext, etwa fuer Laengen- und Formaterhalt.</param>
    string Generate(ReadOnlySpan<byte> seed, string original);

    /// <summary>
    /// Kehrt einen Wert ohne Mapping-Tabelle um. Nur aufgerufen, wenn
    /// <see cref="HasIntrinsicInverse"/> gilt.
    /// </summary>
    bool TryInvert(string pseudonym, out string original)
    {
        original = pseudonym;
        return false;
    }

    /// <summary>Uebernimmt Einstellungen aus dem Profil.</summary>
    void Configure(GeneratorSettings settings) { }
}

/// <summary>
/// Ein Generator mit kleinem einfachen Wertevorrat (etwa einer Wortliste),
/// der sich deterministisch erweitern laesst, sobald der einfache Vorrat fuer
/// einen Bestand erschoepft ist (Befund B6). Der
/// <see cref="Mapping.Pseudonymizer"/> fragt die Erweiterung erst ab, wenn die
/// gewoehnlichen Versuche kein freies Pseudonym ergeben haben -- was bisher
/// ein freies Pseudonym bekam, bekommt also weiterhin dasselbe.
/// </summary>
public interface IExpandableGenerator
{
    /// <param name="attempt">Laufende Nummer des Erweiterungsversuchs, ab 0.</param>
    /// <returns>Ein zusammengesetzter Wert, oder <c>null</c>, wenn es keine Erweiterung gibt.</returns>
    string? GenerateExpanded(ReadOnlySpan<byte> seed, string original, int attempt);
}
