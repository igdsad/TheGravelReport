using System.Windows.Threading;
using IncidentReview.Application.Contracts;

namespace IncidentReview.Host.Wpf;

internal sealed class RuntimeSupervisor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Task _observation;
    private int _expectedStop;
    private int _disposed;

    private RuntimeSupervisor(IApplicationRuntime runtime, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _observation = runtime.Completion.ContinueWith(
            ObserveCompletion,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static RuntimeSupervisor Attach(
        IApplicationRuntime runtime,
        Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(dispatcher);
        return new RuntimeSupervisor(runtime, dispatcher);
    }

    public void BeginExpectedStop() => Volatile.Write(ref _expectedStop, 1);

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        GC.SuppressFinalize(this);
    }

    private void ObserveCompletion(Task completion)
    {
        _ = completion.Exception;
        if (Volatile.Read(ref _expectedStop) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }
}
