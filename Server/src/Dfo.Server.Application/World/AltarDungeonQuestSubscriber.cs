using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class AltarDungeonQuestSubscriber(IQuestProgressDomainService quests, IEventService events)
    : IDomainEventSubscriber<AltarChallengeCompleted>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<AltarChallengeCompleted> message, CancellationToken ct)
    {
        var clear = message.Payload;
        foreach (var participant in clear.Participants.DistinctBy(x => x.CharacterId))
        {
            var report = await quests.ReportAsync(participant.AccountId, participant.CharacterId,
                [new QuestDungeonCleared(clear.DungeonId, clear.Difficulty, clear.RunId, clear.At)], ct).ConfigureAwait(false);
            if (report.Changes.Count > 0)
                await events.EnqueueAsync(new QuestProgressChanged(participant.CharacterId), ct).ConfigureAwait(false);
        }
    }
}
