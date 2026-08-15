using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Channel;

public interface IChannelScriptSource
{
    string Version { get; }

    ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken);
}

public interface IChannelCatalog
{
    ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Which channels carry special entry rules, and what those rules require.
/// </summary>
/// <remarks>
/// Configuration, not rule: it states which channel is the restricted one, while deciding
/// what being restricted means belongs above this contract.
/// </remarks>
/// <param name="DefaultTownId">Where a character with no remembered town arrives.</param>
/// <param name="LevelRestrictedTownId">The only town the restricted channel serves.</param>
/// <param name="LevelRestrictedSpawnAreaId">
/// The area within that town whose geometry supplies the transient arrival point.
/// </param>
public sealed record ChannelEntryOptions(
    int LevelRestrictedChannelId,
    int LevelRestrictedMinimumLevel,
    int FreeDuelChannelId,
    bool FreeDuelEnabled,
    int DefaultTownId,
    int LevelRestrictedTownId,
    int LevelRestrictedSpawnAreaId)
{
    /// <summary>The values the legacy server uses.</summary>
    public static ChannelEntryOptions Legacy { get; } = new(
        LevelRestrictedChannelId: 100,
        LevelRestrictedMinimumLevel: 70,
        FreeDuelChannelId: 68,
        FreeDuelEnabled: false,
        DefaultTownId: 1,
        LevelRestrictedTownId: 17,
        LevelRestrictedSpawnAreaId: 4);
}

/// <summary>
/// Resolves which channel a connection arrived on, and whether a character may enter it.
/// </summary>
public interface IChannelEntryPolicy
{
    /// <summary>
    /// The channel identity for a listener.
    /// </summary>
    /// <remarks>
    /// A listener matching no configured channel resolves to a general channel, which is
    /// how an unconfigured deployment already behaves.
    /// </remarks>
    ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken);

    /// <summary>The reason this character may not enter the channel, or null when they may.</summary>
    ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel);
}
