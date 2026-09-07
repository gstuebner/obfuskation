using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Obfuskation.Core;
using Obfuskation.Core.Mapping;
using Obfuskation.Gui.Services;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Auskunft über die Ersetzungstabelle.
///
/// Zeigt bewusst <b>keine</b> Werte, sondern nur Anzahlen — genau wie
/// <c>obfuskation mapping list</c>. Die Tabelle enthält sämtliche Echtdaten;
/// sie in einem Fenster auszubreiten, das jemand über die Schulter mitliest,
/// wäre das Gegenteil dessen, wofür dieses Werkzeug da ist.
/// </summary>
public sealed class MappingViewModel : ObservableObject
{
    public MappingViewModel(ObfuscationEngine engine, string profileName)
    {
        ProfileName = profileName;

        // Pfad, Anzahl und Existenz kommen aus der gemeinsamen Hilfe, damit sie
        // nicht ein zweites Mal berechnet werden muessen -- dieselbe Auskunft
        // steht auch in der Kopfzeile des Hauptfensters.
        var summary = MappingSummary.For(engine.ResolveMappingStorePath(), profileName);
        StorePath = summary.StorePath;

        if (!summary.Exists)
        {
            EmptyText = "Es gibt noch keine Ersetzungstabelle. Sie entsteht beim ersten Lauf.";
            Permissions = "—";
            TotalText = "";
            return;
        }

        Permissions = ReadPermissions(StorePath);

        try
        {
            // Fuer die Namensraeume reicht die Kurzauskunft nicht -- dafuer wird
            // die Tabelle hier zusaetzlich geoeffnet.
            using var store = MappingStore.Open(
                StorePath, profileName, readOnly: true, allowInsideGitWorkingTree: true);

            foreach (var name in store.NamespaceNames.OrderBy(n => n, StringComparer.Ordinal))
                Namespaces.Add(new NamedCount(name, store.Entries(name).Count));

            TotalText = summary.Text + " insgesamt";
            EmptyText = "Die Tabelle ist noch leer.";
        }
        catch (Exception ex) when (ex is MappingConflictException
                                       or MappingLockedException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            EmptyText = ex.Message;
            TotalText = "";
        }
    }

    public string ProfileName { get; }
    public string StorePath { get; }
    public string Permissions { get; } = "";
    public string TotalText { get; } = "";
    public string EmptyText { get; } = "";

    public ObservableCollection<NamedCount> Namespaces { get; } = new();

    public bool IsEmpty => Namespaces.Count == 0;

    private static string ReadPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "unter Windows nicht anwendbar";

        try
        {
            var mode = File.GetUnixFileMode(path);

            var owner = ((int)(mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite
                                       | UnixFileMode.UserExecute)) >> 6) & 7;
            var group = ((int)(mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                                       | UnixFileMode.GroupExecute)) >> 3) & 7;
            var others = (int)(mode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite
                                       | UnixFileMode.OtherExecute)) & 7;

            var text = $"0{owner}{group}{others}";
            return group == 0 && others == 0
                ? text + "  (nur für Sie lesbar)"
                : text + "  — ACHTUNG: auch für andere lesbar";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "nicht lesbar";
        }
    }
}
