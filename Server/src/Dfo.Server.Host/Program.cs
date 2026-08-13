using Dfo.Server.Abstractions;
using Dfo.Server.Application;
using Dfo.Server.Host.Modules;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking;
using Dfo.Server.Networking.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.AddServerModules(
    new InfrastructureModule(),
    new ApplicationModule(),
    new NetworkingModule());

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.Host");
var channel = host.Services.GetRequiredService<ChannelSourceOptions>();
var networking = host.Services.GetRequiredService<NetworkingOptions>();
var gameLogin = host.Services.GetRequiredService<GameLoginOptions>();

logger.LogInformation(
    "Starting DFO server with modules: {Modules}",
    string.Join(", ", host.Services.GetServices<IServerModule>().Select(module => module.Name)));
logger.LogInformation(
    "Effective startup configuration: ContentRoot={ContentRoot}; ChannelScript={ChannelScript}; " +
    "ChannelAddress={ChannelAddress}; GameAddress={GameAddress}; ChannelPort={ChannelPort}; " +
    "GamePorts={GamePorts}",
    builder.Environment.ContentRootPath,
    channel.ScriptPath,
    channel.PublicServerIp,
    gameLogin.ServerIp,
    networking.ChannelPort,
    string.Join(",", networking.GamePorts));

await host.RunAsync();
