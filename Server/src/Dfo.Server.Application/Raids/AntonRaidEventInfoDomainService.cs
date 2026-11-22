using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application.Channel;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidEventInfoDomainService(IChannelContentRuleService channels) : IEventInfoEntryDomainService
{
    public async ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct)
    {
        var catalog = await channels.ChannelsAsync(ct).ConfigureAwait(false);
        if (!catalog.Any(channel => ChannelContentRuleService.IsRaidEnvironment(channel.LoginEnvironment)))
            return [];

        return [new(AntonRaidRuleService.ChannelEventId, "", "", "", "", "", now, now, "") { HasDetail = false }];
    }
}
