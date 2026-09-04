using System.Collections.Concurrent;
using System.Reflection;

namespace Obfuskation.Core.Generation;

/// <summary>
/// Die eingebetteten Wortlisten, aus denen plausible Ersatzwerte gebaut werden.
/// Wird einmal geladen und danach nur noch gelesen.
/// </summary>
public static class WordLists
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> Cache = new();

    public static IReadOnlyList<string> FirstNames => Load("vornamen");
    public static IReadOnlyList<string> LastNames => Load("nachnamen");
    public static IReadOnlyList<string> Streets => Load("strassen");
    public static IReadOnlyList<string> Cities => Load("staedte");
    public static IReadOnlyList<string> CompanyWords => Load("firmenwoerter");
    public static IReadOnlyList<string> CompanySuffixes => Load("firmenzusaetze");
    public static IReadOnlyList<string> LegalForms => Load("rechtsformen");

    private static IReadOnlyList<string> Load(string name) => Cache.GetOrAdd(name, static key =>
    {
        var assembly = typeof(WordLists).GetTypeInfo().Assembly;
        var resourceName = $"Obfuskation.Core.Resources.{key}.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Eingebettete Wortliste fehlt: {resourceName}");
        using var reader = new StreamReader(stream);

        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                entries.Add(trimmed);
        }

        if (entries.Count == 0)
            throw new InvalidOperationException($"Wortliste ist leer: {resourceName}");

        return entries;
    });
}
