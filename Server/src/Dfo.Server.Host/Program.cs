using Dfo.Server.Application;
using Dfo.Server.GameServer;
using Dfo.Server.GmWeb;
using Dfo.Server.Host;
using Dfo.Server.Host.Modules;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using var configuration = ServerConfiguration.Load(args, out var environment);
using var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .UseEnvironment(environment)
    .ConfigureAppConfiguration((_, sources) =>
    {
        sources.Sources.Clear();
        sources.AddConfiguration(configuration);
    })
    .AddServerModules(new InfrastructureModule(), new PersistenceModule(), new ApplicationModule())
    .ConfigureGameServer()
    .ConfigureHttpApi(configuration, new GmWebModule(configuration))
    .Build();

await host.RunAsync();
