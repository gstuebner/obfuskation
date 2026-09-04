using System.Windows.Input;

namespace Obfuskation.Gui.ViewModels;

/// <summary>Ein Befehl, der einen Aufruf weiterreicht.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Ein Befehl fuer laenger laufende Vorgaenge.
///
/// Solange der Vorgang laeuft, meldet <see cref="CanExecute"/> <c>false</c> —
/// sonst liesse sich dieselbe Verarbeitung durch mehrfaches Klicken mehrfach
/// anstossen, und zwei Laeufe auf derselben Ersetzungstabelle wuerden
/// einander in die Quere kommen.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter)
        => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
