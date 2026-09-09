using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Infrastructure;

public sealed class InfrastructureModule : IServerModule
{
    public string Name => "Infrastructure";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IServerConfigService, ServerConfigService>();
        var options = ChannelSourceOptions.FromConfiguration(configuration);
        services.TryAddSingleton(options);
        services.TryAddSingleton(new FileChannelScriptSource(options.ScriptVersion, options.ScriptPath));
        services.TryAddSingleton<IChannelScriptSource>(
            provider => provider.GetRequiredService<FileChannelScriptSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ChannelScriptStartupService>());
        services.TryAddSingleton<IChannelCatalog>(
            new ConfigurationChannelCatalog(options.Channels));
        // The same file, read a second way. It is sent to the client verbatim as a script and it
        // also declares which channel pays extra for which dungeons, which nothing read until now.
        services.TryAddSingleton<IChannelExperienceSource, ChannelScriptExperienceSource>();
        // Static game content. The archive opens lazily, so a server that never reads
        // content still starts when Script.pvf is absent.
        services.TryAddSingleton(new PvfContentOptions
        {
            ArchivePath = configuration["Pvf:ArchivePath"],
            ChunkCacheBytes = long.TryParse(configuration["Pvf:ChunkCacheBytes"], out var chunkCacheBytes)
                ? chunkCacheBytes
                : 0,
        });
        services.TryAddSingleton<PvfArchiveProvider>();
        services.TryAddSingleton<IQuestDefinitionSource, PvfQuestDefinitionSource>();
        services.TryAddSingleton<IQuestParameterSource, PvfQuestParameterSource>();
        services.TryAddSingleton<IExperienceParameterSource, PvfExperienceParameterSource>();
        services.TryAddSingleton<IDungeonRankGradeSource, PvfDungeonRankGradeSource>();
        services.TryAddSingleton<IGoldCarryLimitSource, PvfGoldCarryLimitSource>();
        services.TryAddSingleton<ICollectionBoxDefinitionSource, PvfCollectionBoxDefinitionSource>();
        services.TryAddSingleton<ISkillRuleDefinitionSource, PvfSkillPointDefinitionSource>();
        services.TryAddSingleton<ICharacterStatDefinitionSource, PvfCharacterStatDefinitionSource>();
        services.TryAddSingleton<IDailyActivityDefinitionSource, PvfDailyActivityDefinitionSource>();
        services.TryAddSingleton<ICreatureDefinitionSource, PvfCreatureDefinitionSource>();
        services.TryAddSingleton<ICreatureEquipmentLifetimeSource, PvfCreatureEquipmentLifetimeSource>();
        services.TryAddSingleton<IEntryLifecycleContentSource, PvfEntryLifecycleContentSource>();
        services.TryAddSingleton<IInitialEquipmentDefinitionSource,
            PvfInitialEquipmentDefinitionSource>();
        services.TryAddSingleton<IDungeonRoomActorSource, PvfDungeonRoomActorSource>();
        services.TryAddSingleton<IDungeonRoomDropSource, PvfDungeonRoomDropSource>();

        // One object behind both seams, because both read the same files and the rate table is
        // where two of the three gold sections live.
        services.TryAddSingleton<PvfDropRateTableSource>();
        services.TryAddSingleton<IDropRateTableSource>(
            provider => provider.GetRequiredService<PvfDropRateTableSource>());
        services.TryAddSingleton<IGoldDropTableSource>(
            provider => provider.GetRequiredService<PvfDropRateTableSource>());
        // Another file of that family, read by itself: its bands are named profiles rather than
        // parallel rates, so it gets its own reader rather than another path into the one above.
        services.TryAddSingleton<IClearRewardRateSource, PvfClearRewardRateSource>();
        services.TryAddSingleton<IDropItemPoolSource, PvfDropItemPoolSource>();
        services.TryAddSingleton<IEquipmentSlotDefinitionSource, PvfEquipmentSlotDefinitionSource>();
        services.TryAddSingleton<IDungeonExperienceProgressionPolicySource,
            PvfDungeonExperienceProgressionPolicySource>();
        services.TryAddSingleton<IExpertSkillContractDefinitionSource,
            PvfExpertSkillContractDefinitionSource>();
        services.TryAddSingleton<ITownDefinitionSource, PvfTownDefinitionSource>();
        services.TryAddSingleton<IDungeonDefinitionSource, PvfDungeonDefinitionSource>();
        services.TryAddSingleton<IInitialSkillDefinitionSource, PvfInitialSkillDefinitionSource>();
        services.TryAddSingleton<ITutorialRewardDefinitionSource, PvfTutorialRewardDefinitionSource>();
        services.TryAddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
        services.TryAddSingleton<INpcShopDefinitionSource, PvfNpcShopDefinitionSource>();
        services.TryAddSingleton<IItemSaleDefinitionSource, PvfItemSaleDefinitionSource>();
        services.TryAddSingleton<IItemUseRuleDefinitionSource, PvfItemUseRuleDefinitionSource>();
        services.TryAddSingleton(ReadChannelEntryOptions(configuration));
    }

    /// <summary>
    /// Which channels carry special entry rules. Defaults reproduce the legacy server, so
    /// a deployment that configures nothing behaves as it did before.
    /// </summary>
    private static ChannelEntryOptions ReadChannelEntryOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("ChannelEntry");
        var defaults = ChannelEntryOptions.Legacy;

        return new ChannelEntryOptions(
            int.TryParse(section["LevelRestrictedChannelId"], out var restrictedId)
                ? restrictedId
                : defaults.LevelRestrictedChannelId,
            int.TryParse(section["LevelRestrictedMinimumLevel"], out var minimumLevel)
                ? minimumLevel
                : defaults.LevelRestrictedMinimumLevel,
            int.TryParse(section["FreeDuelChannelId"], out var freeDuelId)
                ? freeDuelId
                : defaults.FreeDuelChannelId,
            bool.TryParse(section["FreeDuelEnabled"], out var freeDuelEnabled)
                ? freeDuelEnabled
                : defaults.FreeDuelEnabled,
            int.TryParse(section["DefaultTownId"], out var defaultTownId)
                ? defaultTownId
                : defaults.DefaultTownId,
            int.TryParse(section["LevelRestrictedTownId"], out var restrictedTownId)
                ? restrictedTownId
                : defaults.LevelRestrictedTownId,
            int.TryParse(section["LevelRestrictedSpawnAreaId"], out var restrictedAreaId)
                ? restrictedAreaId
                : defaults.LevelRestrictedSpawnAreaId);
    }
}

public sealed record ChannelSourceOptions(
    string ScriptVersion,
    string ScriptPath,
    string PublicServerIp,
    IReadOnlyList<ChannelServerInfo> Channels)
{
    public static ChannelSourceOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Channel");
        var scriptVersion = section["ScriptVersion"] ?? "66";
        var configuredPath = section["ScriptPath"] ?? "Resources/Channel/channel_info.etc";
        var scriptPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        var publicServerIp = section["PublicServerIp"] ?? "127.0.0.1";

        var channels = section.GetSection("Channels")
            .GetChildren()
            .Select((channel, index) => ParseChannel(channel, index, publicServerIp))
            .ToArray();
        if (channels.Length == 0)
        {
            channels = configuration.GetSection("Networking:GamePorts")
                .GetChildren()
                .Select((port, index) => CreateDefaultChannel(index, ParsePort(port.Value)))
                .Select(channel => channel with { Host = publicServerIp })
                .ToArray();
        }

        if (channels.Length == 0)
        {
            channels = [new ChannelServerInfo(11, "#ch.11", 500, 0, publicServerIp, 10011)];
        }

        return new ChannelSourceOptions(scriptVersion, scriptPath, publicServerIp, channels);
    }

    private static ChannelServerInfo ParseChannel(
        IConfigurationSection section,
        int index,
        string defaultHost)
    {
        var id = int.TryParse(section["Id"], out var configuredId)
            ? configuredId
            : GetDefaultChannelId(index);
        var maximumUsers = int.TryParse(section["MaximumUsers"], out var configuredMaximum)
            ? configuredMaximum
            : 500;
        var currentUsers = int.TryParse(section["CurrentUsers"], out var configuredCurrent)
            ? configuredCurrent
            : 0;
        var port = ParsePort(section["Port"]);
        if (maximumUsers < 0 || currentUsers < 0)
        {
            throw new InvalidOperationException("Channel user counts cannot be negative.");
        }

        return new ChannelServerInfo(
            id,
            section["Name"] ?? $"#ch.{id}",
            maximumUsers,
            currentUsers,
            section["Host"] ?? defaultHost,
            port);
    }

    private static ChannelServerInfo CreateDefaultChannel(int index, int port)
    {
        var id = GetDefaultChannelId(index);
        return new ChannelServerInfo(id, $"#ch.{id}", 500, 0, "127.0.0.1", port);
    }

    private static int GetDefaultChannelId(int index) => index switch
    {
        0 => 11,
        1 => 100,
        2 => 68,
        _ => 101 + index,
    };

    private static int ParsePort(string? value)
    {
        if (!int.TryParse(value, out var port) || port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Channel port '{value}' is invalid.");
        }

        return port;
    }
}

internal sealed class FileChannelScriptSource(string version, string path) : IChannelScriptSource
{
    private readonly object _gate = new();
    private byte[]? _content;

    public string Version { get; } = version;

    public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLoaded();
        lock (_gate)
        {
            return ValueTask.FromResult(_content!.ToArray());
        }
    }

    public void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_content is not null) return;

            byte[] content;
            try
            {
                content = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Required channel script '{path}' could not be read.", exception);
            }

            if (content.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Required channel script '{path}' is empty.");
            }

            _content = content;
        }
    }
}

internal sealed class ChannelScriptStartupService(FileChannelScriptSource source) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source.EnsureLoaded();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class ConfigurationChannelCatalog(IReadOnlyList<ChannelServerInfo> channels)
    : IChannelCatalog
{
    public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(channels);
}
