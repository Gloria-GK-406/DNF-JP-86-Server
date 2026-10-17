using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Hands dungeon clears to the marriage entity; the entity is a singleton and the subscriber registration is scoped.</summary>
internal sealed class MarriageTrialClearSubscriber(MarriageDomainService marriage) : IDomainEventSubscriber<DungeonMapCleared>
{
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct) =>
        marriage.RecordTrialClearAsync(message.Payload, ct);
}
