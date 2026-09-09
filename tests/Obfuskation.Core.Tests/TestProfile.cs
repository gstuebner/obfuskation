using System.Text;
using Obfuskation.Core;
using Obfuskation.Core.Configuration;

namespace Obfuskation.Core.Tests;

/// <summary>
/// Ein Profil samt eigener Ersetzungstabelle in einem Wegwerfverzeichnis.
/// Jeder Test bekommt seinen eigenen Bestand, damit sich die Tests nicht ueber
/// gemeinsame Zustaende beeinflussen.
/// </summary>
public sealed class TestProfile : IDisposable
{
    public TestProfile(Action<Profile>? configure = null)
    {
        Directory = Path.Combine(Path.GetTempPath(), "obfuskation-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        Profile = new Profile
        {
            ProfileName = "test",
            MappingStore = Path.Combine(Directory, "mapping.json"),
            TextRules =
            [
                new TextRule
                {
                    Name = "iban",
                    Priority = 100,
                    Generator = "iban",
                    Pattern = @"\b[A-Z]{2}\d{2}[A-Z0-9]{10,30}\b",
                },
                new TextRule
                {
                    Name = "email",
                    Priority = 90,
                    Generator = "email",
                    Pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
                },
            ],
        };

        configure?.Invoke(Profile);
    }

    public string Directory { get; }

    public Profile Profile { get; }

    public ObfuscationEngine CreateEngine() => new(Profile);

    /// <summary>Wie <see cref="CreateEngine()"/>, aber mit einer eigenen Erweiterungsdatei.</summary>
    public ObfuscationEngine CreateEngine(ExtensionLibrary extensions) => new(Profile, extensions);

    /// <summary>Fuegt eine Feldregel hinzu.</summary>
    public TestProfile WithField(string match, FieldAction action, string? generator = null)
    {
        Profile.Fields.Add(new FieldRule
        {
            Match = match,
            MatchType = FieldMatchType.Exact,
            Action = action,
            Generator = generator,
        });
        return this;
    }

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    public static string FromUtf8(byte[] content) => new UTF8Encoding(false).GetString(content);

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Ein liegengebliebenes Wegwerfverzeichnis darf den Testlauf nicht stoeren.
        }
    }
}
