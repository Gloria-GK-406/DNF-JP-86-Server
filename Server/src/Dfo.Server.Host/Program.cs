using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application;
using Dfo.Server.Host.Modules;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.LauncherApi;
using Dfo.Server.Networking;
using Dfo.Server.Persistence;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var hostSettings = new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
};
var genericBuilder = Host.CreateApplicationBuilder(hostSettings);

// Balance lives in its own file. It changes for different reasons, at a different rate and often
// by different hands than the settings beside it: appsettings.json says how the server runs,
// experience.json says how the game plays. Optional, and every value in it defaults to what was
// measured -- a deployment without the file behaves exactly as one built before it existed.
genericBuilder.Configuration.AddJsonFile("experience.json", optional: true, reloadOnChange: false);
var launcherApi = LauncherApiOptions.FromConfiguration(genericBuilder.Configuration);

if (launcherApi.Enabled)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Configuration.AddJsonFile("experience.json", optional: true, reloadOnChange: false);
    builder.WebHost.UseUrls(launcherApi.ListenUrl!.AbsoluteUri.TrimEnd('/'));
    builder.AddServerModules(
        new InfrastructureModule(),
        new PersistenceModule(),
        new ApplicationModule(),
        new NetworkingModule(),
        new ProtocolModule(),
        new LauncherApiModule());

    await using var application = builder.Build();
    application.MapLauncherApi();
    await PrepareAndRunAsync(application, builder.Environment.ContentRootPath);
}
else
{
    genericBuilder.AddServerModules(
        new InfrastructureModule(),
        new PersistenceModule(),
        new ApplicationModule(),
        new NetworkingModule(),
        new ProtocolModule());

    using var host = genericBuilder.Build();
    await PrepareAndRunAsync(host, genericBuilder.Environment.ContentRootPath);
}

static async Task PrepareAndRunAsync(IHost host, string contentRoot)
{
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
    contentRoot,
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

// Missing content keeps the existing startup behavior; content requests still fail explicitly.
// When present, prepare all shared stats before any network hosted service accepts a client.
if (archive is not null)
{
    var worldTimer = System.Diagnostics.Stopwatch.StartNew();
    var worldCount = host.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
    logger.LogInformation("World drop tables initialized: {Count} levels in {ElapsedMs} ms", worldCount, worldTimer.ElapsedMilliseconds);
    await host.Services.GetRequiredService<ICharacterStatRuleService>().InitializeAsync();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var count = await host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
    logger.LogInformation("Lottery definitions initialized: {Count} pots in {ElapsedMs} ms", count, timer.ElapsedMilliseconds);
}

await host.RunAsync();
}
