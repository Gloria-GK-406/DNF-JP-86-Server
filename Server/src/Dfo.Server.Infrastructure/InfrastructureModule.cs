using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Channel;
using Dfo.Server.Infrastructure.Accounts;
using Dfo.Server.Infrastructure.Characters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Infrastructure;

public sealed class InfrastructureModule : IServerModule
{
    public string Name => "Infrastructure";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = ChannelSourceOptions.FromConfiguration(configuration);
        services.TryAddSingleton<IChannelScriptSource>(
            new FileChannelScriptSource(options.ScriptVersion, options.ScriptPath));
        services.TryAddSingleton<IChannelCatalog>(
            new ConfigurationChannelCatalog(options.Channels));
        var configuredDatabasePath = configuration["Database:Path"] ?? "Data/dfo-server.db";
        var databasePath = Path.IsPathRooted(configuredDatabasePath)
            ? configuredDatabasePath
            : Path.Combine(AppContext.BaseDirectory, configuredDatabasePath);
        services.TryAddSingleton<IAccountRepository>(
            _ => new SqliteAccountRepository(databasePath));
        services.TryAddSingleton<ICharacterRepository>(
            _ => new SqliteCharacterRepository(databasePath));
    }
}

internal sealed record ChannelSourceOptions(
    string ScriptVersion,
    string ScriptPath,
    IReadOnlyList<ChannelServerInfo> Channels)
{
    public static ChannelSourceOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Channel");
        var scriptVersion = section["ScriptVersion"] ?? "66";
        var configuredPath = section["ScriptPath"] ?? "channel_info.etc";
        var scriptPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
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

        return new ChannelSourceOptions(scriptVersion, scriptPath, channels);
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
    public string Version { get; } = version;

    public async ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) =>
        await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
}

internal sealed class ConfigurationChannelCatalog(IReadOnlyList<ChannelServerInfo> channels)
    : IChannelCatalog
{
    public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(channels);
}
