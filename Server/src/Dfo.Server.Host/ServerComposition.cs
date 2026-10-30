using Dfo.Server.Application;
using Dfo.Server.GameServer;
using Dfo.Server.GmWeb;
using Dfo.Server.Host.Modules;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host;

/// <summary>The one production service graph, shared by the executable and the tests that check its closure.</summary>
internal static class ServerComposition
{
    /// <remarks>
    /// The container is validated on build in every environment: a constructor whose dependency is not registered
    /// fails startup instead of being found by the first request that reaches it.
    /// </remarks>
    public static IHostBuilder Compose(this IHostBuilder builder, IConfiguration configuration) =>
        builder
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .AddServerModules(new InfrastructureModule(), new PersistenceModule(), new ApplicationModule())
            .ConfigureGameServer()
            .ConfigureHttpApi(configuration, new GmWebModule(configuration));
}
