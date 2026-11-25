using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Pvp;

/// <summary>
/// Plan amendment A1: a duel channel the entitlement table does not cover, or duel content the archive cannot
/// supply, fails startup instead of the first battle that needs it.
/// </summary>
/// <remarks>
/// Content warmup only logs a failed item, so it cannot be the check. The listeners a channel can be resolved
/// from are both the configured game ports and the advertised channel ports, because which of the two identifies
/// a channel is a deployment switch the entry service owns; asking it about every one of them covers either.
/// </remarks>
internal sealed class PvpStartupVerification(
    IChannelContentRuleService channels,
    IChannelEntryDomainService entry,
    ChannelListenerOptions listeners,
    IPvpSettlementRuleService settlement,
    IPvpRoomRuleService rooms) : IPvpStartupVerification
{
    public async ValueTask<int> VerifyAsync(CancellationToken cancellationToken)
    {
        var catalog = await channels.ChannelsAsync(cancellationToken).ConfigureAwait(false);
        var duelling = new HashSet<int>();
        foreach (var port in listeners.GamePorts.Concat(catalog.Select(channel => channel.Port)).Distinct())
        {
            var channel = await entry.ResolveAsync(port, cancellationToken).ConfigureAwait(false);
            if (!channel.IsPvp) continue;
            _ = settlement.EntitlementsOf(channel);
            duelling.Add(channel.ChannelId);
        }

        // Both reads load their whole archive table and throw on a missing or malformed section.
        _ = settlement.UpperBound(settlement.GradeOf(0));
        _ = rooms.AcceptsMap(0);
        return duelling.Count;
    }
}
