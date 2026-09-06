using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using IncidentReview.Application.Contracts;
using IncidentReview.Application.DependencyInjection;
using IncidentReview.Desktop.Wpf;
using IncidentReview.Desktop.Wpf.DependencyInjection;
using IncidentReview.Iracing.DependencyInjection;
using IncidentReview.Results;
using IncidentReview.Store.Sqlite.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentReview.Host.Wpf;

internal static class Program
{
    private const string ProductName = "GravelReview";
    private const string VerifyStartupArgument = "--verify-startup";

    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var verifyStartup = args.Contains(VerifyStartupArgument, StringComparer.OrdinalIgnoreCase);
        try
        {
            return Run(args, verifyStartup);
        }
        catch (Exception exception)
        {
            return ReportFatalFailure(exception, verifyStartup);
        }
    }

    private static int Run(string[] args, bool verifyStartup)
    {
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (!singleInstance.IsAcquired)
        {
            return ExitCodes.AlreadyRunning;
        }

        var configurationArguments = args
            .Where(argument => !string.Equals(
                argument,
                VerifyStartupArgument,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var host = BuildHost(configurationArguments);
        IApplicationRuntime? runtime = null;
        var hostStarted = false;
        var shutdownCompleted = false;
        ExceptionDispatchInfo? primaryFailure = null;
        try
        {
            host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            hostStarted = true;

            var configuration = host.Services
                .GetRequiredService<IOptions<HostConfiguration>>()
                .Value;
            IncidentReviewDesktopApplication? desktopApplication = null;
            Action? prepareDesktopApplication = null;
            using (var startupCancellation = new CancellationTokenSource(configuration.StartupTimeout))
            {
                var bootstrap = host.Services.GetRequiredService<BootstrapCoordinator>();
                RequireSuccess(
                    bootstrap.InitializeAsync(startupCancellation.Token).GetAwaiter().GetResult());

                runtime = host.Services.GetRequiredService<IApplicationRuntime>();
                RequireSuccess(
                    runtime.StartAsync(startupCancellation.Token).GetAwaiter().GetResult());
                if (!verifyStartup)
                {
                    var startupPreferences = RequireSuccess(
                        host.Services
                            .GetRequiredService<IIncidentReviewService>()
                            .GetPreferencesAsync(startupCancellation.Token)
                            .GetAwaiter()
                            .GetResult());
                    var applicationToPrepare = host.Services
                        .GetRequiredService<IncidentReviewDesktopApplication>();
                    desktopApplication = applicationToPrepare;
                    prepareDesktopApplication = () =>
                        applicationToPrepare.PrepareForStartup(startupPreferences);
                }
            }

            if (verifyStartup)
            {
                shutdownCompleted = true;
                Shutdown(runtime, host, hostStarted);
                return ExitCodes.Success;
            }

            var application = desktopApplication ?? throw new InvalidOperationException(
                "The desktop application was not prepared for startup.");
            var prepareApplication = prepareDesktopApplication ?? throw new InvalidOperationException(
                "The desktop application has no startup preparation step.");
            using var supervisor = RuntimeSupervisor.Attach(runtime, application.Dispatcher);
            var exitCode = DesktopStartupSequence.Run(
                prepareApplication,
                application.Run);
            supervisor.BeginExpectedStop();

            shutdownCompleted = true;
            Shutdown(runtime, host, hostStarted);
            return exitCode;
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        finally
        {
            ExceptionDispatchInfo? cleanupFailure = null;
            if (!shutdownCompleted)
            {
                cleanupFailure = TryShutdown(runtime, host, hostStarted);
            }

            var disposalFailure = TryDisposeHost(host);
            if (primaryFailure is not null)
            {
                AttachSecondaryFailure(primaryFailure.SourceException, "CleanupFailure", cleanupFailure);
                AttachSecondaryFailure(primaryFailure.SourceException, "DisposalFailure", disposalFailure);
            }
            else if (cleanupFailure is not null)
            {
                AttachSecondaryFailure(
                    cleanupFailure.SourceException,
                    "DisposalFailure",
                    disposalFailure);
                cleanupFailure.Throw();
            }
            else
            {
                disposalFailure?.Throw();
            }
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true,
        });

        _ = builder.Configuration.AddInMemoryCollection(HostConfiguration.CreateDefaults());
        _ = builder.Configuration.AddEnvironmentVariables(prefix: "INCIDENTREVIEW_");
        _ = builder.Configuration.AddCommandLine(
            args,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["--database-path"] = HostConfiguration.DatabasePathKey,
                ["--startup-timeout-seconds"] = HostConfiguration.StartupTimeoutSecondsKey,
            });

        builder.ConfigureContainer(
            new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }));

        _ = builder.Services.AddLogging();
        _ = builder.Services
            .AddOptions<HostConfiguration>()
            .Bind(builder.Configuration)
            .Validate(
                static configuration => HostConfiguration.IsValid(configuration),
                HostConfiguration.InvalidConfigurationMessage)
            .ValidateOnStart();
        _ = builder.Services.AddSingleton(static provider =>
            provider.GetRequiredService<IOptions<HostConfiguration>>()
                .Value
                .CreateStoreOptions());
        _ = builder.Services.AddSingleton<BootstrapCoordinator>();
        _ = builder.Services.AddIncidentReviewApplication();
        _ = builder.Services.AddSqliteStore();
        _ = builder.Services.AddIracingIntegration();
        _ = builder.Services.AddIncidentReviewDesktop();
        return builder.Build();
    }

    private static void Shutdown(IApplicationRuntime runtime, IHost host, bool hostStarted)
    {
        var failure = TryShutdown(runtime, host, hostStarted);
        failure?.Throw();
    }

    private static ExceptionDispatchInfo? TryShutdown(
        IApplicationRuntime? runtime,
        IHost host,
        bool hostStarted)
    {
        var failures = new List<Exception>();
        if (runtime is not null)
        {
            CaptureFailure(
                () => runtime.StopAsync(CancellationToken.None).GetAwaiter().GetResult(),
                failures);
            CaptureFailure(
                () => runtime.Completion.GetAwaiter().GetResult(),
                failures);
        }

        if (hostStarted)
        {
            CaptureFailure(
                () => host.StopAsync(CancellationToken.None).GetAwaiter().GetResult(),
                failures);
        }

        if (failures.Count == 0)
        {
            return null;
        }

        var primary = failures[0];
        for (var index = 1; index < failures.Count; index++)
        {
            primary.Data[$"ShutdownFailure{index}"] = failures[index].ToString();
        }

        return ExceptionDispatchInfo.Capture(primary);
    }

    private static ExceptionDispatchInfo? TryDisposeHost(IHost host)
    {
        try
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                host.Dispose();
            }

            return null;
        }
        catch (Exception exception)
        {
            return ExceptionDispatchInfo.Capture(exception);
        }
    }

    private static void AttachSecondaryFailure(
        Exception primary,
        string key,
        ExceptionDispatchInfo? secondary)
    {
        if (secondary is not null)
        {
            primary.Data[key] = secondary.SourceException.ToString();
        }
    }

    private static void CaptureFailure(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static T RequireSuccess<T>(Result<T> result)
        where T : notnull
    {
        if (!result.IsSuccess)
        {
            throw new FatalApplicationException(result.Error!);
        }

        return result.Value;
    }

    private static void RequireSuccess(Result result)
    {
        if (!result.IsSuccess)
        {
            throw new FatalApplicationException(result.Error!);
        }
    }

    private static int ReportFatalFailure(Exception exception, bool headless)
    {
        var message = exception switch
        {
            FatalApplicationException fatal => fatal.Error.Message,
            OptionsValidationException => HostConfiguration.InvalidConfigurationMessage,
            _ => $"{ProductName} stopped because of an unexpected error.",
        };
        Trace.TraceError("{0} fatal failure: {1}", ProductName, exception);
        if (!headless)
        {
            _ = MessageBox.Show(
                message,
                ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return ExitCodes.FatalFailure;
    }
}
