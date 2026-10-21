using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World.BossTower;

namespace Dfo.Server.Application.World.BossTower;

internal sealed class BossTowerTicketDomainService(IBossTowerRuleService rules,
    IDailyRefillConfigurationDomainService refills, IDailyRefillDomainService refill,
    ISelectedSessionDomainService sessions,
    IAuthenticatedGamePresenceQuery online, IEventService events, TimeProvider clock) : IBossTowerTicketDomainService
{
    public async ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session || !sessions.IsInWorld(session) || !online.IsActiveExact(session))
            return false;
        var at = clock.GetUtcNow();
        var configured = (await refills.EffectiveAsync(at, ct).ConfigureAwait(false)).Where(x => x.ItemId == rules.TicketItemId).ToArray();
        var result = await refill.ApplyIndependentRefillAsync(session.AccountId, session.CharacterId, configured, at, ct).ConfigureAwait(false);
        if (result.Grants.Count > 0)
            await events.EnqueueAsync(new BossTowerTicketRefilled(session.CharacterId,
                [.. result.Grants.Select(x => x.Slot).Distinct()]), ct).ConfigureAwait(false);
        return true;
    }
}
