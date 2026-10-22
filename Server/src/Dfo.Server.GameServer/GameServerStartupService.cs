using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Networking.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GameServer;

internal sealed class GameServerStartupService(IServiceProvider services, IHostEnvironment environment)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Dfo.Server.GameServer");
        var channel = services.GetRequiredService<ChannelSourceOptions>();
        var networking = services.GetRequiredService<NetworkingOptions>();
        var serverConfig = services.GetRequiredService<IServerConfigService>();
        var pvf = services.GetRequiredService<PvfContentOptions>();

        logger.LogInformation(
            "Starting DFO server with modules: {Modules}",
            string.Join(", ", services.GetServices<IServerModule>().Select(module => module.Name)));
        logger.LogInformation(
            "Effective startup configuration: ContentRoot={ContentRoot}; ChannelScript={ChannelScript}; " +
            "ChannelAddress={ChannelAddress}; GameAddress={GameAddress}; ChannelPort={ChannelPort}; " +
            "GamePorts={GamePorts}",
            environment.ContentRootPath,
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
            var equipmentCount = await services.GetRequiredService<IEquipmentDefinitionSource>()
                .InitializeAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Equipment content index initialized: {Count} templates", equipmentCount);
            var bossTower = await services.GetRequiredService<Dfo.Server.Abstractions.World.BossTower.IBossTowerRuleService>()
                .DefinitionAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Boss tower content initialized: {Stages} stages, {Maps} maps", bossTower.Stages.Count,
                bossTower.Stages.Sum(stage => stage.Maps.Count));
            var independentCount = services.GetRequiredService<Dfo.Server.Abstractions.Content.IIndependentDropRuleService>().Initialize();
            services.GetRequiredService<ILoggerFactory>().CreateLogger("IndependentDrops").LogInformation("Cached {Count} independent drop rows", independentCount);
            var extraCount = services.GetRequiredService<Dfo.Server.Abstractions.Content.IExtraDropRuleService>().Initialize();
            logger.LogInformation("Extra drop content initialized: {Count} tagged candidates", extraCount);
            var declaredCount = services.GetRequiredService<Dfo.Server.Abstractions.Content.IMonsterDeclaredDropRuleService>().Initialize();
            logger.LogInformation("Monster declared drops initialized: {Count} tables", declaredCount);
            var worldTimer = System.Diagnostics.Stopwatch.StartNew();
            var worldCount = services.GetRequiredService<IWorldDropRuleService>().Initialize();
            logger.LogInformation("World drop tables initialized: {Count} levels in {ElapsedMs} ms", worldCount, worldTimer.ElapsedMilliseconds);
            var dimensionCount = services.GetRequiredService<IDimensionDungeonRuleService>().Initialize();
            logger.LogInformation("Dimension dungeon content initialized: {Count} dungeons", dimensionCount);
            await services.GetRequiredService<ICharacterStatRuleService>().InitializeAsync(cancellationToken);
            // The two startup writes run as startup jobs on the executor (spec appendix B, last row).
            var queue = services.GetRequiredService<IGameplayQueue>();
            await queue.RunAsync(StartupJob("startup:GameServerStartupService/epic-book",
                token => services.GetRequiredService<Dfo.Server.Abstractions.Inventory.IEpicBookDomainService>().InitializeAsync(token)),
                cancellationToken).ConfigureAwait(false);
            var vendingCount = services.GetRequiredService<Dfo.Server.Abstractions.Inventory.IVendingMachineDefinitionSource>().Initialize();
            logger.LogInformation("Vending machine definitions initialized: {Count} groups", vendingCount);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var count = await services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync(cancellationToken);
            logger.LogInformation("Lottery definitions initialized: {Count} pots in {ElapsedMs} ms", count, timer.ElapsedMilliseconds);
            var scale = services.GetRequiredService<IExperienceRuleService>().CharacterScale();
            await queue.RunAsync(StartupJob("startup:GameServerStartupService/experience-storage",
                token => services.GetRequiredService<Dfo.Server.Abstractions.Persistence.ICharacterExperienceStoragePolicy>()
                    .InstallAsync(scale, token)),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Character progression constrained: TopLevel={TopLevel}, MaxExperience={Maximum}",
                scale.TopLevel, scale.MaximumExperience);
            await ContentWarmup.RunAsync(ContentWarmup.IsEnabled(services.GetService<Microsoft.Extensions.Configuration.IConfiguration>()),
                services.GetServices<IContentWarmup>, logger, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static GameplayWork StartupJob(string commandType, Func<CancellationToken, ValueTask> body) =>
        new(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery), commandType, body);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
