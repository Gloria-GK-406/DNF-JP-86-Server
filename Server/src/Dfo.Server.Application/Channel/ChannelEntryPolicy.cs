using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Channel;

/// <summary>
/// Decides what a listener means and who may enter through it.
/// </summary>
/// <remarks>
/// Reverse-engineered from the legacy channel admission policy, which carries exactly one
/// rule: the restricted channel turns away characters below its minimum level. Everything
/// else the legacy policy exposes is spawn behaviour, which is a separate decision.
/// </remarks>
internal sealed class ChannelEntryPolicy(
    IChannelCatalog channels,
    ChannelEntryOptions options) : IChannelEntryPolicy
{
    public async ValueTask<ChannelEntryIdentity> ResolveAsync(
        int listenerPort,
        CancellationToken cancellationToken)
    {
        var catalog = await channels.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var channel in catalog)
        {
            if (channel.Port == listenerPort)
                return new ChannelEntryIdentity(channel.ChannelId, listenerPort, KindOf(channel.ChannelId));
        }

        // An unmatched listener is an ordinary one. Refusing entry here would take a
        // deployment that simply has not listed its channels and make it unplayable.
        return new ChannelEntryIdentity(ChannelIdUnknown, listenerPort, ChannelEntryKind.General);
    }

    public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.Kind is ChannelEntryKind.LevelRestricted
               && characterLevel < options.LevelRestrictedMinimumLevel
            ? RestrictedChannelRefusal
            : null;
    }

    /// <summary>The channel identifier reported for a listener no channel claims.</summary>
    public const int ChannelIdUnknown = 0;

    /// <summary>
    /// The refusal the legacy server sends, byte for byte.
    /// </summary>
    /// <remarks>
    /// The code and the wording are protocol surface, so they are reproduced rather than
    /// reworded. The text reads "this channel admits only characters of level 70 and above".
    /// </remarks>
    private static readonly ChannelAdmissionRefusal RestrictedChannelRefusal = new(
        ErrorCode: 0xFD,
        Message: "当前频道仅限70级及以上角色进入。");

    private ChannelEntryKind KindOf(int channelId)
    {
        if (channelId == options.LevelRestrictedChannelId)
            return ChannelEntryKind.LevelRestricted;

        // A duelling channel that is switched off is an ordinary channel, not a missing
        // one: the legacy server simply does not list its listener.
        return channelId == options.FreeDuelChannelId && options.FreeDuelEnabled
            ? ChannelEntryKind.FreeDuel
            : ChannelEntryKind.General;
    }
}
