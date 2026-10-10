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
/// <param name="SaintHornTownId">The only town the restricted channel serves.</param>
public sealed record ChannelEntryOptions(
    int SaintHornChannelId,
    int SaintHornMinimumLevel,
    int FreeDuelChannelId,
    bool FreeDuelEnabled,
    int DefaultTownId,
    int SaintHornTownId)
{
    public int PvpTownId { get; init; } = 10;

    public int RaidTownId { get; init; } = 19;

    /// <summary>The values the legacy server uses.</summary>
    public static ChannelEntryOptions Legacy { get; } = new(
        SaintHornChannelId: 100,
        SaintHornMinimumLevel: 70,
        FreeDuelChannelId: 68,
        FreeDuelEnabled: false,
        DefaultTownId: 1,
        SaintHornTownId: 17);
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
