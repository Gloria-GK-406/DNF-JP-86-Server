using Dfo.Server.Abstractions;
using Dfo.Server.Application;
using Dfo.Server.Host.Modules;
using Dfo.Server.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServerModules(
    new ApplicationModule(),
    new NetworkingModule());

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.Host");

logger.LogInformation(
    "Starting DFO server with modules: {Modules}",
    string.Join(", ", host.Services.GetServices<IServerModule>().Select(module => module.Name)));

await host.RunAsync();
