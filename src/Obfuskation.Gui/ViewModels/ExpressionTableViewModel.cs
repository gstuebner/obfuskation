namespace Obfuskation.Gui.ViewModels;

/// <summary>
/// Eine Tabelle des Ausdruck-Generators im Generator-Dialog (Plan
/// docs/plan-ausdruck-generator.md, P2b) -- Name und Werte, dazu ob sie im
/// aktuellen Ausdruck ueberhaupt verwendet wird. Jede Aenderung meldet sich
/// ueber <paramref name="onChanged"/> an <see cref="GeneratorEditorViewModel"/>
/// zurueck, das daraus <see cref="Core.Configuration.GeneratorSettings.Tables"/>
/// neu aufbaut: ein Woerterbuch koennte waehrend der Eingabe entstehende
/// doppelte oder leere Namen nicht halten, eine Liste solcher Ansichtsmodelle
/// dagegen schon -- die Namensprüfung selbst uebernimmt das Ansichtsmodell
/// des Dialogs (<c>RefreshErrors</c>), nicht diese Klasse.
/// </summary>
public sealed class ExpressionTableViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private string _name;
    private string _valuesText;
    private bool _isUnused;

    public ExpressionTableViewModel(
        string name, IReadOnlyList<string> values, Action onChanged, Action<ExpressionTableViewModel> remove)
    {
        _name = name;
        _valuesText = string.Join('\n', values);
        _onChanged = onChanged;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Name
    {
        get => _name;
        set
        {
            var neu = value ?? "";
            if (!SetProperty(ref _name, neu))
                return;
            _onChanged();
        }
    }

    /// <summary>Ein Wert je Zeile, wie im Werteliste-Feld des Dialogs.</summary>
    public string ValuesText
    {
        get => _valuesText;
        set
        {
            var neu = value ?? "";
            if (!SetProperty(ref _valuesText, neu))
                return;
            OnPropertyChanged(nameof(CountLabel));
            _onChanged();
        }
    }

    /// <summary>Die Werte, ein Eintrag je nichtleerer Zeile.</summary>
    public List<string> Values => _valuesText
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Where(zeile => zeile.Length > 0)
        .ToList();

    public string CountLabel => Values.Count == 1 ? "1 Wert" : $"{Values.Count} Werte";

    /// <summary>
    /// Gesetzt von <see cref="GeneratorEditorViewModel"/>, sobald sich der
    /// Ausdruck oder die Tabellenliste aendert (<c>RefreshTableUsage</c>).
    /// </summary>
    public void UpdateUsage(bool isUsed)
    {
        var unused = !isUsed;
        if (_isUnused == unused)
            return;

        _isUnused = unused;
        OnPropertyChanged(nameof(UnusedHint));
        OnPropertyChanged(nameof(HasUnusedHint));
    }

    public string? UnusedHint => _isUnused ? "Im Ausdruck nicht verwendet." : null;

    public bool HasUnusedHint => UnusedHint is not null;

    public RelayCommand RemoveCommand { get; }
}

/// <summary>
/// Ein im Ausdruck verwendeter, aber unter <see cref="GeneratorEditorViewModel.Tables"/>
/// fehlender Tabellenname (Plan P2b) -- mit einem eigenen "Anlegen"-Befehl,
/// nach demselben Muster wie <see cref="ExpressionTableViewModel.RemoveCommand"/>:
/// Der Befehl gehoert an den Listeneintrag selbst, statt dass die Oberflaeche
/// aus einer verschachtelten Vorlage auf den Dialog dahinter zurueckgreifen muesste.
/// </summary>
public sealed class MissingTableHint
{
    public MissingTableHint(string name, Action add)
    {
        Name = name;
        AddCommand = new RelayCommand(add);
    }

    public string Name { get; }

    public string Message => $"Im Ausdruck verwendet, aber nicht angelegt: {Name}";

    public RelayCommand AddCommand { get; }
}
