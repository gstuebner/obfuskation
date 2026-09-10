using System.Collections.ObjectModel;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Auskunft ueber die Erweiterungsdatei fuer den Menueintrag "Hauseigene
/// Muster…" -- rein lesend, wie <see cref="MappingViewModel"/> fuer die
/// Ersetzungstabelle. Vor diesem Fenster liess sich der Fundort nur ueber
/// <c>obfuskation extensions path</c> auf der Kommandozeile erfahren
/// (Teil D-4 des Plans).
/// </summary>
public sealed class ExtensionsViewModel
{
    public ExtensionsViewModel(string path, ExtensionLibrary extensions)
    {
        Path = path;
        Exists = File.Exists(path);

        foreach (var name in extensions.Generators.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            GeneratorNames.Add(name);

        foreach (var rule in extensions.TextRules.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            TextRuleNames.Add(rule.Name);

        FieldRuleCount = extensions.FieldRules.Count;
    }

    public string Path { get; }

    /// <summary>Ob unter <see cref="Path"/> ueberhaupt eine Datei liegt -- eine Erweiterung ist optional.</summary>
    public bool Exists { get; }

    public ObservableCollection<string> GeneratorNames { get; } = new();
    public ObservableCollection<string> TextRuleNames { get; } = new();
    public int FieldRuleCount { get; }

    public bool HasGenerators => GeneratorNames.Count > 0;
    public bool HasTextRules => TextRuleNames.Count > 0;

    public bool IsEmpty => GeneratorNames.Count == 0 && TextRuleNames.Count == 0 && FieldRuleCount == 0;

    public string FieldRuleSummary => FieldRuleCount switch
    {
        0 => "keine Spaltenmuster",
        1 => "1 Spaltenmuster",
        var n => $"{n} Spaltenmuster",
    };
}
