namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Stellt eindeutige Textregelnamen her -- gebraucht an mehreren Stellen, die
/// sich nicht auseinanderentwickeln duerfen: <see cref="AlwaysReplaceViewModel"/>
/// beim Anlegen und <see cref="SettingsViewModel"/> beim Verschieben einer
/// Regel zwischen Projekt und Erweiterung. Eine gleichnamige Regel am
/// jeweils anderen Ort wuerde sich sonst beim naechsten Zusammenfuehren
/// gegenseitig verdraengen (siehe <c>ExtensionLibrary.MergeTextRules</c>).
/// </summary>
internal static class TextRuleNaming
{
    /// <summary>
    /// Liefert <paramref name="baseName"/> unveraendert, wenn er noch frei ist,
    /// sonst mit angehaengter, hochgezaehlter Nummer.
    /// </summary>
    public static string MakeUnique(string baseName, IEnumerable<string> takenNames)
    {
        var vergeben = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);

        if (!vergeben.Contains(baseName))
            return baseName;

        var zaehler = 2;
        while (vergeben.Contains(baseName + zaehler))
            zaehler++;

        return baseName + zaehler;
    }
}
