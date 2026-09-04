using System.Text.Json;
using System.Text.Json.Serialization;
using Obfuskation.Core.Generation;

namespace Obfuskation.Core.Mapping;

/// <summary>
/// Verwaltet die Ersetzungstabelle: haelt sie im Speicher, sichert sie atomar auf
/// Platte und setzt die Zugriffsrechte durch.
/// </summary>
public sealed class MappingStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MappingDocument _document;

    /// <summary>Rueckwaertsindex je Namensraum: Pseudonym auf Klartext.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _reverse = new(StringComparer.Ordinal);

    private FileStream? _lock;
    private bool _dirty;

    private MappingStore(string path, MappingDocument document, FileStream? lockStream)
    {
        Path = path;
        _document = document;
        _lock = lockStream;
        BuildReverseIndex();
    }

    public string Path { get; }

    public string ProfileName => _document.ProfileName;

    public byte[] Salt => Convert.FromBase64String(_document.Salt);

    /// <summary>Ob seit dem Laden Eintraege hinzugekommen sind.</summary>
    public bool HasUnsavedChanges => _dirty;

    /// <summary>Hinweise, die beim Oeffnen aufgefallen sind (etwa zu weite Dateirechte).</summary>
    public IReadOnlyList<string> OpenWarnings { get; private init; } = Array.Empty<string>();

    public int TotalEntries => _document.Namespaces.Values.Sum(n => n.Count);

    public int NewEntries { get; private set; }

    /// <summary>
    /// Oeffnet den Store, legt ihn bei Bedarf an und sperrt ihn gegen parallele Laeufe.
    /// </summary>
    /// <param name="path">Pfad zur Mapping-Datei.</param>
    /// <param name="profileName">Erwarteter Profilname.</param>
    /// <param name="readOnly">Ohne Sperre oeffnen, etwa fuer reine Auskunft.</param>
    /// <param name="allowInsideGitWorkingTree">
    /// Die Sicherung gegen eine Ablage im Git-Arbeitsverzeichnis aufheben.
    /// </param>
    public static MappingStore Open(
        string path,
        string profileName,
        bool readOnly = false,
        bool allowInsideGitWorkingTree = false)
    {
        path = System.IO.Path.GetFullPath(PathHelper.ExpandHome(path));
        var warnings = new List<string>();

        if (!allowInsideGitWorkingTree && PathHelper.IsInsideGitWorkingTree(path))
        {
            throw new MappingConflictException(
                $"Der Mapping-Store soll unter {path} abgelegt werden, das liegt in einem " +
                "Git-Arbeitsverzeichnis. Die Datei enthält sämtliche Echtdaten und darf dort " +
                "nicht liegen. Pfad in der Konfiguration ändern oder --allow-unsafe-store setzen.");
        }

        var directory = System.IO.Path.GetDirectoryName(path)!;

        // Ein frisch angelegtes Verzeichnis entsteht mit den Rechten der umask
        // und wird sofort eingeschraenkt — das ist kein Befund, ueber den zu
        // berichten waere. Gemeldet wird nur, was vorher schon zu offen stand.
        var existedBefore = Directory.Exists(directory);
        Directory.CreateDirectory(directory);
        FilePermissions.RestrictDirectory(directory, existedBefore ? warnings : new List<string>());

        FileStream? lockStream = null;
        if (!readOnly)
            lockStream = AcquireLock(path + ".lock");

        try
        {
            MappingDocument document;
            if (File.Exists(path))
            {
                FilePermissions.CheckFile(path, warnings);
                document = Read(path);

                if (!string.Equals(document.ProfileName, profileName, StringComparison.Ordinal))
                {
                    warnings.Add(
                        $"Der Mapping-Store gehört zum Profil '{document.ProfileName}', " +
                        $"verarbeitet wird aber '{profileName}'.");
                }

                if (document.Version > MappingDocument.CurrentVersion)
                {
                    throw new MappingConflictException(
                        $"Der Mapping-Store hat Version {document.Version}, dieses Programm kennt " +
                        $"hoechstens Version {MappingDocument.CurrentVersion}.");
                }
            }
            else
            {
                document = new MappingDocument
                {
                    ProfileName = profileName,
                    Salt = Convert.ToBase64String(SeedDeriver.CreateSalt()),
                };
            }

            return new MappingStore(path, document, lockStream) { OpenWarnings = warnings };
        }
        catch
        {
            lockStream?.Dispose();
            TryDeleteLock(path + ".lock");
            throw;
        }
    }

    private static MappingDocument Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<MappingDocument>(stream, JsonOptions)
                   ?? throw new MappingConflictException($"Der Mapping-Store ist leer: {path}");
        }
        catch (JsonException ex)
        {
            throw new MappingConflictException(
                $"Der Mapping-Store ist beschädigt und kein gültiges JSON: {path} ({ex.Message})");
        }
    }

    private static FileStream AcquireLock(string lockPath)
    {
        try
        {
            return new FileStream(lockPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            throw new MappingLockedException(lockPath);
        }
    }

    private static void TryDeleteLock(string lockPath)
    {
        try
        {
            if (File.Exists(lockPath))
                File.Delete(lockPath);
        }
        catch (IOException)
        {
            // Die Sperrdatei wird ohnehin beim Schliessen entfernt.
        }
    }

    private void BuildReverseIndex()
    {
        _reverse.Clear();
        foreach (var (namespaceName, entries) in _document.Namespaces)
        {
            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (plaintext, pseudonym) in entries)
            {
                if (reverse.TryGetValue(pseudonym, out var existing))
                {
                    throw new MappingConflictException(
                        $"Im Namensraum '{namespaceName}' verweist dasselbe Pseudonym auf zwei " +
                        $"verschiedene Klartexte ('{existing}' und '{plaintext}'). Die " +
                        "Rückabbildung wäre mehrdeutig; der Store muss von Hand bereinigt werden.");
                }
                reverse[pseudonym] = plaintext;
            }
            _reverse[namespaceName] = reverse;
        }
    }

    /// <summary>Sucht das Pseudonym zu einem Klartext.</summary>
    public bool TryGetPseudonym(string namespaceName, string plaintext, out string pseudonym)
    {
        pseudonym = "";
        return _document.Namespaces.TryGetValue(namespaceName, out var entries)
               && entries.TryGetValue(plaintext, out pseudonym!);
    }

    /// <summary>Sucht den Klartext zu einem Pseudonym.</summary>
    public bool TryGetPlaintext(string namespaceName, string pseudonym, out string plaintext)
    {
        plaintext = "";
        return _reverse.TryGetValue(namespaceName, out var entries)
               && entries.TryGetValue(pseudonym, out plaintext!);
    }

    /// <summary>Ob der Wert in diesem Namensraum bereits als Pseudonym vergeben ist.</summary>
    public bool IsPseudonymTaken(string namespaceName, string candidate)
        => _reverse.TryGetValue(namespaceName, out var entries) && entries.ContainsKey(candidate);

    /// <summary>Ob der Wert in diesem Namensraum als Klartext vorkommt.</summary>
    public bool IsPlaintextKnown(string namespaceName, string candidate)
        => _document.Namespaces.TryGetValue(namespaceName, out var entries) && entries.ContainsKey(candidate);

    /// <summary>Traegt eine neue Zuordnung ein.</summary>
    public void Add(string namespaceName, string plaintext, string pseudonym)
    {
        if (!_document.Namespaces.TryGetValue(namespaceName, out var entries))
        {
            entries = new Dictionary<string, string>(StringComparer.Ordinal);
            _document.Namespaces[namespaceName] = entries;
        }

        if (!_reverse.TryGetValue(namespaceName, out var reverse))
        {
            reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            _reverse[namespaceName] = reverse;
        }

        entries[plaintext] = pseudonym;
        reverse[pseudonym] = plaintext;
        _dirty = true;
        NewEntries++;
    }

    /// <summary>Alle Zuordnungen eines Namensraums.</summary>
    public IReadOnlyDictionary<string, string> Entries(string namespaceName)
        => _document.Namespaces.TryGetValue(namespaceName, out var entries)
            ? entries
            : new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> NamespaceNames => _document.Namespaces.Keys;

    /// <summary>Alle Pseudonyme mit ihrem Namensraum, fuer die Rueckabbildung von Freitext.</summary>
    public IEnumerable<(string Namespace, string Pseudonym, string Plaintext)> AllReverseEntries()
    {
        foreach (var (namespaceName, entries) in _reverse)
            foreach (var (pseudonym, plaintext) in entries)
                yield return (namespaceName, pseudonym, plaintext);
    }

    /// <summary>Alle Klartexte, fuer die Nachpruefung durch <c>scan</c>.</summary>
    public IEnumerable<(string Namespace, string Plaintext)> AllPlaintexts()
    {
        foreach (var (namespaceName, entries) in _document.Namespaces)
            foreach (var plaintext in entries.Keys)
                yield return (namespaceName, plaintext);
    }

    /// <summary>
    /// Schreibt den Store atomar: erst in eine Nebendatei im selben Verzeichnis,
    /// dann umbenennen. Ein Abbruch darf niemals einen halben Store hinterlassen.
    /// </summary>
    public void Save()
    {
        if (!_dirty)
            return;

        _document.UpdatedUtc = DateTimeOffset.UtcNow;

        var temporary = Path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _document, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        FilePermissions.RestrictFile(temporary);
        File.Move(temporary, Path, overwrite: true);
        FilePermissions.RestrictFile(Path);

        _dirty = false;
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }
}
