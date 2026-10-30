using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.Marriage;

/// <summary>Recommended clears belong to each married participant, not only spouse parties.</summary>
internal sealed class CapsuleClearSubscriber(IMarriageRelations relations, CapsuleState state, IMarriageCapsuleRuleService capsules,
    ICharacterDomainService characters, IEventService events) : IDomainEventSubscriber<DungeonMapCleared>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct)
    {
        var clear = message.Payload;
        if (!clear.IsBossRoom || clear.RunId.Value == Guid.Empty) return;
        foreach (var member in clear.Participants.DistinctBy(x => x.CharacterId))
        {
            if (await relations.ForParticipantAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false) is not { Stage: MarriageStage.Married } relation
                || relation.StageSince > clear.ClearedAt) continue;
            var progress = await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
            if (progress is null || !await capsules.ChargesOnClearAsync(clear.DungeonId, progress.Level, ct).ConfigureAwait(false)) continue;
            var rules = capsules.Rules();
            if (!await state.CreditAsync(relation.Id, member.CharacterId, clear.RunId, rules.Gain, rules.MaximumEnergy, ct).ConfigureAwait(false)) continue;
            await events.EnqueueAsync(new MarriageCapsuleEnergyChanged(member.CharacterId,
                (await state.ReadAsync(relation.Id, member.CharacterId, ct).ConfigureAwait(false)).Energy), ct).ConfigureAwait(false);
        }
    }
}
