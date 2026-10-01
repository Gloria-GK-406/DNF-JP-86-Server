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
        services.TryAddSingleton(options);
        if (!options.Enabled) return;
        services.TryAddSingleton<HttpApiReadiness>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HttpApiStartupService>());
    }
}
