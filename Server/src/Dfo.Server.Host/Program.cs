using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application;
using Dfo.Server.Host.Modules;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking;
using Dfo.Server.Persistence;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Protocol;
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
    new PersistenceModule(),
    new ApplicationModule(),
    new NetworkingModule(),
    new ProtocolModule());

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.Host");
var channel = host.Services.GetRequiredService<ChannelSourceOptions>();
var networking = host.Services.GetRequiredService<NetworkingOptions>();
var serverConfig = host.Services.GetRequiredService<IServerConfigService>();
var pvf = host.Services.GetRequiredService<PvfContentOptions>();

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
    serverConfig.GetConfig<string>("Game:EndpointHost"),
    networking.ChannelPort,
    string.Join(",", networking.GamePorts));

// Which archive, named at startup rather than left to whoever set the environment variable. Two
// clients live side by side here and their archives disagree about a job's starting skills and
// equipment, and a server reading the wrong one runs and answers exactly as if it were right.
var archive = pvf.ResolveArchivePath();
logger.LogInformation(
    "Static content archive: {Archive}",
    archive is null
        ? "none found -- any content read will fail"
        : $"{archive} ({new FileInfo(archive).Length} bytes, "
          + $"{File.GetLastWriteTimeUtc(archive):yyyy-MM-dd HH:mm:ss}Z)");

await host.RunAsync();
