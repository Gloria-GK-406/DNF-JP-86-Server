using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.LauncherApi;

internal sealed class LauncherApiReadiness
{
    private volatile bool ready;

    public bool IsReady => ready;

    public void SetReady(bool value) => ready = value;
}

internal sealed class LauncherApiStartupService(
    IAuthenticationDomainService authentication,
    IPublicEndpointSource endpoints,
    IConfiguration configuration,
    LauncherApiReadiness readiness) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = authentication;
        _ = endpoints.Snapshot;
        readiness.SetReady(
            bool.TryParse(configuration["Networking:Enabled"], out var networkingEnabled)
            && networkingEnabled);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        readiness.SetReady(false);
        return Task.CompletedTask;
    }
}
