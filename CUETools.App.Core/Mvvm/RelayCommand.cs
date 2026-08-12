using System.Windows.Input;

namespace CUETools.Wpf.Mvvm;

/// <summary>Standard ICommand relay for view-model commands (used from Phase 3 on).</summary>
/// <remarks>
/// Platform-neutral: CanExecuteChanged is raised explicitly (per command) or
/// globally through <see cref="RequeryHub"/>, which replaced WPF's
/// CommandManager.RequerySuggested coupling when this type moved to the
/// shared app core. Semantics are unchanged: a global requery reaches every
/// live command, exactly as InvalidateRequerySuggested did.
/// </remarks>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        RequeryHub.RequerySuggested += OnRequerySuggested;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private void OnRequerySuggested() => RaiseCanExecuteChanged();
}

/// <summary>
/// Global command-requery signal, the portable stand-in for WPF's
/// CommandManager.InvalidateRequerySuggested. View models call
/// <see cref="RequestRequery"/> after state changes that affect command
/// availability; every <see cref="RelayCommand"/> re-evaluates.
/// </summary>
public static class RequeryHub
{
    internal static event Action? RequerySuggested;

    public static void RequestRequery() => RequerySuggested?.Invoke();
}
