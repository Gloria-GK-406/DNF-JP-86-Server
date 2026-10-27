using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;
using Dfo.Server.Abstractions.Configuration;

namespace Dfo.Server.Application.Channel;

/// <summary>
/// Decides what a listener means and who may enter through it.
/// </summary>
/// <remarks>Saint Horn identity, level admission and listener mapping are separate decisions.</remarks>
internal sealed class ChannelEntryDomainService(
    IChannelContentRuleService channels,
    ChannelEntryOptions options,
    ChannelListenerOptions listeners,
    IChannelLoginDeploymentSource deployment) : IChannelEntryDomainService
{
    public async ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken)
    {
        var channel = await ResolveAsync(listenerPort, cancellationToken).ConfigureAwait(false);
        var environment = channel.LoginEnvironment ?? deployment.ReadDefaultEnvironment();
        var endpoints = deployment.ReadEndpoints();
        return new ChannelLoginEntry(environment, endpoints);
    }

    public async ValueTask<ChannelEntryIdentity> ResolveAsync(
        int listenerPort,
        CancellationToken cancellationToken)
    {
        var catalog = await channels.ChannelsAsync(cancellationToken).ConfigureAwait(false);

        // Disabled legacy deployments historically identify channels by their advertised port.
        // Do not let a separately ordered listener list reinterpret those channel identities.
        if (listeners.AllowAdvertisedPortFallback)
        {
            foreach (var channel in catalog)
            {
                if (channel.Port == listenerPort)
                {
                    return new ChannelEntryIdentity(
                        channel.ChannelId,
                        listenerPort,
                        KindOf(channel), channel.LoginEnvironment);
                }
            }

            return Unknown(listenerPort);
        }

        // Enabled launcher deployments validate a strict one-to-one projection at startup, so
        // only their independent listener order defines the channel identity here.
        for (var index = 0; index < listeners.GamePorts.Count && index < catalog.Count; index++)
        {
            if (listeners.GamePorts[index] == listenerPort)
            {
                var channel = catalog[index];
                return new ChannelEntryIdentity(
                    channel.ChannelId,
                    listenerPort,
                    KindOf(channel), channel.LoginEnvironment);
            }
        }

        // An unmatched listener is an ordinary one. Refusing entry here would take a
        // deployment that simply has not listed its channels and make it unplayable.
        return Unknown(listenerPort);
    }

    public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.Kind is ChannelEntryKind.SaintHorn
               && characterLevel < options.SaintHornMinimumLevel
            ? RestrictedChannelRefusal
            : null;
    }

    /// <summary>The channel identifier reported for a listener no channel claims.</summary>
    public const int ChannelIdUnknown = 0;

    private static ChannelEntryIdentity Unknown(int listenerPort) =>
        new(ChannelIdUnknown, listenerPort, ChannelEntryKind.General);

    /// <summary>
    /// The native refusal code and a notice reflecting the configured minimum.
    /// </summary>
    /// <remarks>
    /// Preserve the refusal code; describe the configured minimum level in the notice.
    /// </remarks>
    private readonly ChannelAdmissionRefusal RestrictedChannelRefusal = new(
        ErrorCode: 0xFD,
        Message: $"圣者之鸣号频道仅限{options.SaintHornMinimumLevel}级及以上角色进入。");

    private ChannelEntryKind KindOf(ChannelServerInfo channel)
    {
        var channelId = channel.ChannelId;
        if (channelId == options.SaintHornChannelId)
            return ChannelEntryKind.SaintHorn;

        if (channel.LoginEnvironment == 13)
            return ChannelEntryKind.FreeDuel;
        if (channel.LoginEnvironment is 8 or 24)
            return ChannelEntryKind.Pvp;
        if (channel.LoginEnvironment == 23)
            return ChannelEntryKind.Raid;

        // A duelling channel that is switched off is an ordinary channel, not a missing
        // one: the legacy server simply does not list its listener.
        return channelId == options.FreeDuelChannelId && options.FreeDuelEnabled
            ? ChannelEntryKind.FreeDuel
            : ChannelEntryKind.General;
    }
}
