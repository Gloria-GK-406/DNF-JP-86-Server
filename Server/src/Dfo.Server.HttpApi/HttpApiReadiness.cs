using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi;

internal sealed class HttpApiReadiness
{
    private volatile bool ready;

    public bool IsReady => ready;

    public void SetReady(bool value) => ready = value;
}

internal sealed class HttpApiStartupService(
    IAuthenticationDomainService authentication,
    IPublicEndpointSource endpoints,
    IHostApplicationLifetime lifetime,
    HttpApiReadiness readiness,
    HostStartTime startTime,
    GateOptions gate,
    ILogger<HttpApiStartupService> logger) : IHostedService, IDisposable
{
    private CancellationTokenRegistration started;
    private CancellationTokenRegistration stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = authentication;
        _ = endpoints.Snapshot;
        _ = startTime;
        // Registered only when LauncherAuth is enabled, which is exactly when a missing token is worth saying.
        if (!gate.TokenConfigured)
        {
            logger.LogWarning(
                "LauncherAuth is enabled but '{TokenKey}' is not set; the gate endpoints answer 404.", GateOptions.TokenKey);
        }
        started = lifetime.ApplicationStarted.Register(() => readiness.SetReady(true));
        stopping = lifetime.ApplicationStopping.Register(() => readiness.SetReady(false));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        readiness.SetReady(false);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        started.Dispose();
        stopping.Dispose();
    }
}
