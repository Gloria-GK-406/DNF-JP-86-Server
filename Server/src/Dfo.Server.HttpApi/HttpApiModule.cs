using Dfo.Server.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.HttpApi;

public sealed class HttpApiModule : IServerModule
{
    public string Name => "HttpApi";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = HttpApiOptions.FromConfiguration(configuration);
        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "The HttpApi module cannot be registered while LauncherAuth:Enabled is false.");
        }

        services.TryAddSingleton(options);
        services.TryAddSingleton<HttpApiReadiness>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HttpApiStartupService>());
    }
}
