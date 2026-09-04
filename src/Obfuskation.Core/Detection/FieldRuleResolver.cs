using System.Text.RegularExpressions;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Detection;

/// <summary>Die auf ein konkretes Feld angewandte Regel.</summary>
/// <param name="Rule">Die getroffene Regel, oder <c>null</c> bei der Vorgabe.</param>
/// <param name="Action">Die auszufuehrende Behandlung.</param>
/// <param name="Generator">Generatorname, falls ersetzt wird.</param>
/// <param name="TextRules">Textregeln, falls der Inhalt durchsucht wird.</param>
public sealed record ResolvedFieldRule(
    FieldRule? Rule,
    FieldAction Action,
    string? Generator,
    IReadOnlyList<TextRule> TextRules)
{
    public bool IsFromDefault => Rule is null;
}

/// <summary>
/// Ordnet Feldnamen ihre Regel zu. Das Ergebnis wird je Feldname zwischenge-
/// speichert, damit die Zuordnung nicht fuer jede Zeile neu bestimmt wird.
/// </summary>
public sealed class FieldRuleResolver
{
    private readonly Profile _profile;
    private readonly List<(FieldRule Rule, Regex? Pattern)> _rules = new();
    private readonly Dictionary<string, ResolvedFieldRule> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextRule> _textRulesByName;

    public FieldRuleResolver(Profile profile)
    {
        _profile = profile;
        _textRulesByName = profile.TextRules.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in profile.Fields)
        {
            Regex? pattern = null;
            if (rule.MatchType == FieldMatchType.Regex)
            {
                try
                {
                    pattern = new Regex(rule.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new ConfigurationException(
                        $"Ungültiger regulärer Ausdruck in der Feldregel '{rule.Match}': {ex.Message}", ex);
                }
            }
            _rules.Add((rule, pattern));
        }
    }

    /// <summary>
    /// Bestimmt die Behandlung eines Feldes. Die erste passende Regel gewinnt,
    /// die Reihenfolge in der Konfiguration ist also die Prioritaet.
    /// </summary>
    /// <param name="fieldName">Spaltenname oder JSON-Eigenschaft.</param>
    /// <param name="jsonPath">Vollstaendiger JSON-Pfad, falls vorhanden.</param>
    public ResolvedFieldRule Resolve(string fieldName, string? jsonPath = null)
    {
        var cacheKey = jsonPath ?? fieldName;
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resolved = ResolveUncached(fieldName, jsonPath);
        _cache[cacheKey] = resolved;
        return resolved;
    }

    private ResolvedFieldRule ResolveUncached(string fieldName, string? jsonPath)
    {
        foreach (var (rule, pattern) in _rules)
        {
            var matches = rule.MatchType switch
            {
                FieldMatchType.Exact => string.Equals(rule.Match, fieldName, StringComparison.OrdinalIgnoreCase),
                FieldMatchType.Regex => pattern!.IsMatch(fieldName),
                FieldMatchType.JsonPath => jsonPath is not null && JsonPathMatcher.Matches(rule.Match, jsonPath),
                _ => false,
            };

            if (!matches)
                continue;

            return new ResolvedFieldRule(rule, rule.Action, rule.Generator, ResolveTextRules(rule));
        }

        return new ResolvedFieldRule(null, _profile.Defaults.UnknownField, "token", _profile.TextRules);
    }

    private IReadOnlyList<TextRule> ResolveTextRules(FieldRule rule)
    {
        if (rule.Action != FieldAction.ScanText)
            return Array.Empty<TextRule>();

        // Ohne Angabe gelten alle Textregeln des Profils.
        if (rule.TextRules is not { Count: > 0 })
            return _profile.TextRules;

        var selected = new List<TextRule>(rule.TextRules.Count);
        foreach (var name in rule.TextRules)
        {
            if (!_textRulesByName.TryGetValue(name, out var textRule))
                throw new ConfigurationException(
                    $"Die Feldregel '{rule.Match}' verweist auf die unbekannte Textregel '{name}'.");
            selected.Add(textRule);
        }
        return selected;
    }
}
