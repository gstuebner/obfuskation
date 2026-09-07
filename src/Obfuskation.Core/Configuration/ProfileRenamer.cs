using Obfuskation.Core.Mapping;

namespace Obfuskation.Core.Configuration;

/// <param name="MappingStorePinned">Der Pfad war leer und wurde festgeschrieben.</param>
/// <param name="MappingDocumentUpdated">Der Profilname wurde in der Tabelle nachgezogen.</param>
public sealed record RenameOutcome(
    string OldName,
    string NewName,
    bool MappingStorePinned,
    bool MappingDocumentUpdated,
    string MappingStorePath);

/// <summary>
/// Benennt ein Profil um, ohne die Ersetzungstabelle zu verlieren.
///
/// Der kritische Schritt ist die Reihenfolge: ist <see cref="Profile.MappingStore"/>
/// leer, zeigt der Vorgabepfad ueber den (alten) Profilnamen auf ein Verzeichnis.
/// Wuerde erst der Name geaendert, zeigte die Vorgabe danach auf ein *anderes*,
/// leeres Verzeichnis -- alle Pseudonyme waeren neu, Zurueckholen faende nichts
/// mehr. Deshalb wird der bisherige Pfad zuerst festgeschrieben, bevor der Name
/// sich aendert.
///
/// Das Umbenennen der Profildatei selbst ist Sache des Aufrufers -- diese
/// Klasse kennt nur das <see cref="Profile"/>-Objekt im Speicher.
/// </summary>
public static class ProfileRenamer
{
    public static RenameOutcome Rename(Profile profile, string newName)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(newName))
            throw new ConfigurationException("Der neue Profilname darf nicht leer sein.");

        // PathHelper.SanitizeName liefert nie einen leeren String -- ohne
        // brauchbaren Rest faellt sie auf "default" zurueck. Ein bedeutungsloser
        // Name (nur Trennzeichen, nur Punkte) faellt also genau darauf zurueck,
        // ohne dass der Anwender "default" gemeint haette.
        var sanitized = PathHelper.SanitizeName(newName);
        if (sanitized == "default" && !string.Equals(newName.Trim(), "default", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"'{newName}' ergibt keinen brauchbaren Dateinamen. Bitte mindestens ein " +
                "gewoehnliches Zeichen verwenden.");
        }

        var oldName = profile.ProfileName;

        // Der Pfad ergibt sich noch aus dem alten Zustand, festgeschrieben wird
        // er erst unten.
        var mappingStorePinned = string.IsNullOrWhiteSpace(profile.MappingStore);
        var mappingStorePath = PathHelper.ResolveMappingStore(profile);

        // Die Tabelle zuerst: sie kann gesperrt sein oder sich nicht schreiben
        // lassen. Schluege das fehl, nachdem das Profil im Speicher schon
        // umbenannt waere, bliebe ein halb umbenannter Zustand zurueck -- ein
        // spaeteres Speichern haette dann ein Profil, dessen Tabelle noch auf
        // den alten Namen lautet, und jeder Lauf wuerde das anmahnen. So bleibt
        // das Profil bei einem Fehler unberuehrt.
        var mappingDocumentUpdated = false;
        if (File.Exists(mappingStorePath))
        {
            MappingStore.RenameProfile(mappingStorePath, newName);
            mappingDocumentUpdated = true;
        }

        if (mappingStorePinned)
            profile.MappingStore = mappingStorePath;

        profile.ProfileName = newName;

        return new RenameOutcome(oldName, newName, mappingStorePinned, mappingDocumentUpdated, mappingStorePath);
    }
}
