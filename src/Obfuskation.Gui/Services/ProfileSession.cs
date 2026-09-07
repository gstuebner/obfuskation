using Obfuskation.Core;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Gui.Services;

/// <summary>
/// Das geoeffnete Profil samt Aenderungsstand.
///
/// Die <see cref="ObfuscationEngine"/> prueft das Profil in ihrem
/// Konstruktor und lehnt ein fehlerhaftes ab. Sie wird deshalb erst bei Bedarf
/// erzeugt und nach jeder Regelaenderung verworfen — so arbeitet die
/// Oberflaeche nie mit einem veralteten Regelwerk, und ein noch unfertiges
/// Profil laesst sich trotzdem weiter bearbeiten.
/// </summary>
public sealed class ProfileSession
{
    private ObfuscationEngine? _engine;

    private ProfileSession(Profile profile, string? path)
    {
        Profile = profile;
        Path = path;
    }

    public Profile Profile { get; }

    /// <summary>Pfad der Konfigurationsdatei; <c>null</c> bei einem neuen Profil.</summary>
    public string? Path { get; private set; }

    public bool HasUnsavedChanges { get; private set; }

    public string DisplayName => Profile.ProfileName;

    public static ProfileSession Load(string path)
        => new(ProfileStore.Load(PathHelper.ExpandHome(path)), System.IO.Path.GetFullPath(path));

    public static ProfileSession Create(string profileName, string? sampleFilePath, string? description = null)
        => new(ProfileScaffolder.Create(profileName, sampleFilePath, description), null) { HasUnsavedChanges = true };

    /// <summary>
    /// Meldet eine Aenderung am Regelwerk. Die Engine wird verworfen, damit der
    /// naechste Zugriff sie mit den neuen Regeln aufbaut.
    /// </summary>
    public void MarkChanged()
    {
        HasUnsavedChanges = true;
        _engine = null;
    }

    public void Save(string? path = null)
    {
        var target = path ?? Path
            ?? throw new InvalidOperationException("Für dieses Profil ist noch kein Pfad festgelegt.");

        ProfileStore.Save(Profile, target);
        Path = System.IO.Path.GetFullPath(target);
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Die Engine zum aktuellen Regelwerk. Wirft
    /// <see cref="ConfigurationException"/>, solange das Profil fehlerhaft ist.
    /// </summary>
    public ObfuscationEngine Engine => _engine ??= new ObfuscationEngine(Profile);

    /// <summary>
    /// Die Engine, sofern das Profil fehlerfrei ist — sonst <c>null</c> samt
    /// Befunden. Fuer alles, was im Hintergrund laeuft und den Anwender nicht
    /// mit einer Ausnahme unterbrechen soll.
    /// </summary>
    public bool TryGetEngine(out ObfuscationEngine? engine, out IReadOnlyList<ValidationIssue> issues)
    {
        issues = ProfileValidator.Validate(Profile);

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            engine = null;
            return false;
        }

        engine = Engine;
        return true;
    }

    public IReadOnlyList<ValidationIssue> Validate() => ProfileValidator.Validate(Profile);
}
