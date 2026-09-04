namespace Obfuskation.Core.Configuration;

/// <summary>Das Profil ist fehlerhaft oder nicht lesbar.</summary>
public class ConfigurationException : Exception
{
    public ConfigurationException(string message, IReadOnlyList<ValidationIssue>? issues = null)
        : base(message)
        => Issues = issues ?? Array.Empty<ValidationIssue>();

    public ConfigurationException(string message, Exception inner)
        : base(message, inner)
        => Issues = Array.Empty<ValidationIssue>();

    /// <summary>Einzelbefunde, falls die Ausnahme aus der Profilpruefung stammt.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }
}

/// <summary>
/// Ein Feld traegt keine Regel und die Vorgabe verlangt einen Abbruch. Wird
/// geworfen, bevor irgendeine Ausgabe entsteht.
/// </summary>
public sealed class UnhandledFieldException : Exception
{
    public UnhandledFieldException(IReadOnlyList<string> fieldNames)
        : base("Für folgende Felder steht noch keine Entscheidung fest: " +
               string.Join(", ", fieldNames))
        => FieldNames = fieldNames;

    public IReadOnlyList<string> FieldNames { get; }
}
