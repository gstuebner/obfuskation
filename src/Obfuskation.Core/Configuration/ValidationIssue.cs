namespace Obfuskation.Core.Configuration;

/// <summary>
/// Ein Befund der Profilpruefung. <see cref="Path"/> zeigt auf das betroffene
/// Feld, damit die GUI den Hinweis direkt am Eingabefeld anzeigen kann.
/// </summary>
/// <param name="Path">Pfad im Profil, z.B. <c>fields[3].generator</c>.</param>
/// <param name="Severity">Schweregrad.</param>
/// <param name="Message">Meldung fuer den Menschen.</param>
public sealed record ValidationIssue(string Path, ValidationSeverity Severity, string Message);
