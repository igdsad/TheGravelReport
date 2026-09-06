using System.Windows.Input;

namespace IncidentReview.Desktop.Wpf;

internal sealed class PresentationCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool> _canExecute = canExecute ?? (() => true);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute();

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncPresentationCommand(
    Func<CancellationToken, Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<CancellationToken, Task> _execute =
        execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool> _canExecute = canExecute ?? (() => true);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            await _execute(CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // User and lifetime cancellation are normal presentation outcomes.
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class AsyncPresentationCommand<T>(
    Func<T, CancellationToken, Task> execute,
    Func<T, bool>? canExecute = null) : ICommand
    where T : class
{
    private readonly Func<T, CancellationToken, Task> _execute =
        execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<T, bool> _canExecute = canExecute ?? (_ => true);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T value && _canExecute(value);

    public async void Execute(object? parameter)
    {
        if (parameter is not T value || !CanExecute(value))
        {
            return;
        }

        try
        {
            await _execute(value, CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // User and lifetime cancellation are normal presentation outcomes.
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
