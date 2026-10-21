using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

internal sealed class NameTagExpiredSubscriber(ISelectedSessionDomainService sessions,
    ICharacterVisibleSessionDomainService visibility, IEventService notifications) : IDomainEventSubscriber<CharacterNameTagExpired>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<CharacterNameTagExpired> message, CancellationToken ct)
    {
        var change = message.Payload;
        var owner = sessions.Find(change.AccountId);
        if (owner is null || owner.CharacterId != change.CharacterId || owner.ConnectionId.Value != change.ConnectionId
            || owner.ConnectionGeneration != change.ConnectionGeneration || !sessions.IsInWorld(owner)) return;
        await notifications.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(owner)),
            new CharacterUserStateRefresh(owner), ct).ConfigureAwait(false);
    }
}
