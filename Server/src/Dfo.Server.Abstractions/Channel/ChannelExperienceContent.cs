namespace Dfo.Server.Abstractions.Channel;

/// <summary>
/// What one channel adds to a run in a dungeon it carries.
/// </summary>
/// <param name="ChannelId">The channel the row is for.</param>
/// <param name="DungeonGroup">
/// The name of the dungeon group this channel is bound to, as the archive spells it -- the
/// bracketed name a <c>[dungeon]</c> block opens with.
/// </param>
/// <param name="Rate">
/// What a kill in one of that group's dungeons earns on top of what it is worth. The archive
/// declares it as a whole percentage; this is that percentage over a hundred.
/// </param>
/// <param name="DungeonIds">The dungeons the named group holds.</param>
public sealed record ChannelExperienceEntry(
    int ChannelId,
    string DungeonGroup,
    double Rate,
    IReadOnlySet<int> DungeonIds)
{
    /// <summary>Whether this channel is the one this dungeon belongs to.</summary>
    public bool Covers(int dungeonId) => this.DungeonIds.Contains(dungeonId);
}

/// <summary>
/// Which channel pays extra for which dungeons, as <c>channel_info.etc</c> declares it.
/// </summary>
/// <remarks>
/// The file names a dungeon group per channel and lists the dungeons in each group, so the reward
/// is not a property of the channel alone -- it is a property of playing a channel's own dungeons
/// on it. That condition is the whole rule, and this server paid the rate without it until
/// 2026-08-29: every kill on every map got the bonus.
/// <para>
/// <b>This is content, not policy.</b> It was mistaken for policy once, and the mistake is
/// instructive: the rate was recovered from four captured kills, all in one dungeon on one
/// channel, and every one of them agreed. A number that fits every sample it was derived from
/// says nothing about the condition under which it applies.
/// </para>
/// </remarks>
public sealed class ChannelExperienceCatalog
{
    private readonly IReadOnlyDictionary<int, ChannelExperienceEntry> _byChannel;

    public ChannelExperienceCatalog(IReadOnlyDictionary<int, ChannelExperienceEntry> byChannel)
    {
        ArgumentNullException.ThrowIfNull(byChannel);
        this._byChannel = byChannel;
    }

    /// <summary>A catalogue that declares nothing, so no channel pays.</summary>
    public static ChannelExperienceCatalog Empty { get; } =
        new(new Dictionary<int, ChannelExperienceEntry>());

    /// <summary>Every channel that declares an experience reward.</summary>
    public IReadOnlyCollection<ChannelExperienceEntry> Entries => [.. this._byChannel.Values];

    /// <summary>
    /// What this channel adds to a run in this dungeon, or nothing when the dungeon is not one of
    /// the channel's own.
    /// </summary>
    public double RateFor(int channelId, int dungeonId) =>
        this._byChannel.TryGetValue(channelId, out var entry) && entry.Covers(dungeonId)
            ? entry.Rate
            : 0;
}

/// <summary>The archive's channel-to-dungeon-group rewards.</summary>
public interface IChannelExperienceSource
{
    ChannelExperienceCatalog Load();
}
