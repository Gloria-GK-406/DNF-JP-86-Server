using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Microsoft.Extensions.Hosting;

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
    HttpApiReadiness readiness) : IHostedService, IDisposable
{
    private CancellationTokenRegistration started;
    private CancellationTokenRegistration stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = authentication;
        _ = endpoints.Snapshot;
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
