using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Grundlage aller Ansichtsmodelle. Bewusst schlank gehalten und ohne
/// Fremdpaket — es geht um Benachrichtigung bei Wertaenderung, mehr braucht
/// diese Oberflaeche nicht.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Setzt das Feld und meldet die Aenderung. Liefert <c>true</c>, wenn sich
    /// der Wert tatsaechlich geaendert hat.
    /// </summary>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
