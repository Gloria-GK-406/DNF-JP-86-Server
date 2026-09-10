using Dfo.Server.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.LauncherApi;

public sealed class LauncherApiModule : IServerModule
{
    public string Name => "LauncherApi";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = LauncherApiOptions.FromConfiguration(configuration);
        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "The LauncherApi module cannot be registered while LauncherAuth:Enabled is false.");
        }

        services.TryAddSingleton(options);
        services.TryAddSingleton<LauncherApiReadiness>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, LauncherApiStartupService>());
    }
}
