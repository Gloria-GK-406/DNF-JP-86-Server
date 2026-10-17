using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Hands dungeon clears to the pre-wedding quests; that service is a singleton and the subscriber registration is scoped.</summary>
internal sealed class MarriageTrialClearSubscriber(MarriageQuestDomainService quests) : IDomainEventSubscriber<DungeonMapCleared>
{
    public ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct) =>
        quests.RecordTrialClearAsync(message.Payload, ct);
}
