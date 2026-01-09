using System;
using System.Windows.Input;

namespace KawaiiStudio.App.ViewModels;

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        var value = ConvertParameter(parameter);
        return _canExecute?.Invoke(value) ?? true;
    }

    public void Execute(object? parameter)
    {
        var value = ConvertParameter(parameter);
        _execute(value);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? ConvertParameter(object? parameter)
    {
        if (parameter is null)
        {
            return default;
        }

        if (parameter is T value)
        {
            return value;
        }

        return default;
    }
}
