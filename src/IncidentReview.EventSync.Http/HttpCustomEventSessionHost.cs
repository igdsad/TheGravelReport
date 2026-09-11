using IncidentReview.EventSync.Contracts;
using IncidentReview.EventSync.Http.DependencyInjection;
using IncidentReview.EventSync.Http.Options;
using IncidentReview.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IncidentReview.EventSync.Http;

internal sealed class HttpCustomEventSessionHost : ICustomEventSessionHost
{
    private readonly EventSyncHttpOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private WebApplication? _application;
    private TaskCompletionSource? _expectedStop;
    private Task _completion = Task.CompletedTask;
    private bool _disposed;

    public HttpCustomEventSessionHost(EventSyncHttpOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task Completion => Volatile.Read(ref _completion);

    public async Task<Result<JoinCode>> StartAsync(
        CustomEventSessionHostRequest request,
        ICustomEventReceiver receiver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(receiver);
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_application is not null)
            {
                return Result<JoinCode>.Failure(EventSyncHttpErrors.HostAlreadyRunning);
            }

            var joinCode = JoinCode.TryCreate(
                request.AdvertisedBaseUri,
                request.SessionIdentity);
            if (!joinCode.IsSuccess)
            {
                throw new InvalidOperationException("A validated host request produced an invalid join code.");
            }

            var application = BuildApplication(request, receiver);
            try
            {
                await application.StartAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await DisposeFailedStartAsync(application);
                throw;
            }
            catch (Exception exception) when (IsExpectedHostException(exception))
            {
                EventSyncHttpLog.HostUnavailable(_logger, exception);
                await DisposeFailedStartAsync(application);
                return Result<JoinCode>.Failure(EventSyncHttpErrors.HostUnavailable);
            }

            var expectedStop = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _application = application;
            _expectedStop = expectedStop;
            Volatile.Write(
                ref _completion,
                MonitorCompletionAsync(application, expectedStop.Task));
            return joinCode;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<Result> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_application is null)
            {
                return Result.Success();
            }

            _expectedStop?.TrySetResult();
            try
            {
                await _application.StopAsync(cancellationToken);
                await _application.DisposeAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedHostException(exception))
            {
                EventSyncHttpLog.HostUnavailable(_logger, exception);
                return Result.Failure(EventSyncHttpErrors.HostUnavailable);
            }

            _application = null;
            _expectedStop = null;
            return Result.Success();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_application is null)
            {
                return;
            }

            _expectedStop?.TrySetResult();
            try
            {
                await _application.StopAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsExpectedHostException(exception))
            {
                EventSyncHttpLog.HostUnavailable(_logger, exception);
            }

            try
            {
                await _application.DisposeAsync();
            }
            catch (Exception exception) when (IsExpectedHostException(exception))
            {
                EventSyncHttpLog.HostUnavailable(_logger, exception);
            }

            _application = null;
            _expectedStop = null;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private WebApplication BuildApplication(
        CustomEventSessionHostRequest request,
        ICustomEventReceiver receiver)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HttpCustomEventSessionHost).Assembly.FullName,
            Args = [],
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(request.ListenUri.GetLeftPart(UriPartial.Authority));
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AllowSynchronousIO = false;
            options.Limits.MaxRequestBodySize = _options.MaximumRequestBodyBytes;
        });

        var application = builder.Build();
        var pathBase = request.AdvertisedBaseUri.AbsolutePath.TrimEnd('/');
        if (pathBase.Length > 0)
        {
            application.UsePathBase(pathBase);
        }

        application.MapHostedHttpEventSyncReceiver(
            _options,
            _logger,
            request.SessionIdentity,
            receiver);
        return application;
    }

    private static async Task MonitorCompletionAsync(
        WebApplication application,
        Task expectedStop)
    {
        await application.WaitForShutdownAsync(CancellationToken.None);
        if (!expectedStop.IsCompleted)
        {
            throw new InvalidOperationException(
                "The custom-event HTTP host stopped before a stop was requested.");
        }
    }

    private async Task DisposeFailedStartAsync(WebApplication application)
    {
        try
        {
            await application.DisposeAsync();
        }
        catch (Exception exception) when (IsExpectedHostException(exception))
        {
            EventSyncHttpLog.HostUnavailable(_logger, exception);
        }
    }

    private static bool IsExpectedHostException(Exception exception) =>
        exception is IOException or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException or
            System.Net.Sockets.SocketException;
}
