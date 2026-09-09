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
    private readonly GeneratorLibrary _library;
    private ObfuscationEngine? _engine;

    private ProfileSession(Profile profile, string? path, GeneratorLibrary library)
    {
        Profile = profile;
        Path = path;
        _library = library;
    }

    public Profile Profile { get; }

    /// <summary>Pfad der Konfigurationsdatei; <c>null</c> bei einem neuen Profil.</summary>
    public string? Path { get; private set; }

    public bool HasUnsavedChanges { get; private set; }

    public string DisplayName => Profile.ProfileName;

    /// <summary>
    /// Die Generator-Bibliothek, mit der diese Sitzung arbeitet -- von der
    /// Oberflaeche einmal beim Start geladen und hierher durchgereicht, damit
    /// Engine, Pruefung und Geruesterzeugung dieselbe Bibliothek sehen statt
    /// jede fuer sich die Datei erneut zu lesen (siehe <see cref="MainViewModel"/>).
    /// </summary>
    public GeneratorLibrary Library => _library;

    /// <summary>
    /// Ohne <paramref name="library"/> gilt <see cref="GeneratorLibrary.Load"/>
    /// -- fuer Aufrufer, denen die einmalig geladene Bibliothek der Oberflaeche
    /// nicht vorliegt (etwa Tests).
    /// </summary>
    public static ProfileSession Load(string path, GeneratorLibrary? library = null)
        => new(ProfileStore.Load(PathHelper.ExpandHome(path)), System.IO.Path.GetFullPath(path),
            library ?? GeneratorLibrary.Load());

    public static ProfileSession Create(
        string profileName, string? sampleFilePath, string? description = null, GeneratorLibrary? library = null)
    {
        library ??= GeneratorLibrary.Load();
        return new(ProfileScaffolder.Create(profileName, sampleFilePath, description, library), null, library)
        {
            HasUnsavedChanges = true,
        };
    }

    /// <summary>Wie die Einzelfassung, aber das Regelgeruest entsteht aus mehreren Dateien auf einmal.</summary>
    public static ProfileSession Create(
        string profileName, IEnumerable<string> sampleFilePaths, string? description = null,
        GeneratorLibrary? library = null)
    {
        library ??= GeneratorLibrary.Load();
        return new(ProfileScaffolder.Create(profileName, sampleFilePaths, description, library), null, library)
        {
            HasUnsavedChanges = true,
        };
    }

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
    public ObfuscationEngine Engine => _engine ??= new ObfuscationEngine(Profile, _library);

    /// <summary>
    /// Die Engine, sofern das Profil fehlerfrei ist — sonst <c>null</c> samt
    /// Befunden. Fuer alles, was im Hintergrund laeuft und den Anwender nicht
    /// mit einer Ausnahme unterbrechen soll.
    /// </summary>
    public bool TryGetEngine(out ObfuscationEngine? engine, out IReadOnlyList<ValidationIssue> issues)
    {
        issues = ProfileValidator.Validate(Profile, _library);

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            engine = null;
            return false;
        }

        engine = Engine;
        return true;
    }

    public IReadOnlyList<ValidationIssue> Validate() => ProfileValidator.Validate(Profile, _library);
}
