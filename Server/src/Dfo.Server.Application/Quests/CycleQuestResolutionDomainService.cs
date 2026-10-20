using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Quests;

internal sealed class CycleQuestResolutionDomainService(ICharacterQuestLogDomainService logs,
    IQuestDefinitionDomainService definitions, IQuestStateRuleService state, IQuestFactsDomainService facts,
    IQuestCompletionSettlementDomainService completion, IEventService events,
    ILogger<CycleQuestResolutionDomainService> logger) : ICycleQuestResolutionDomainService
{
    public async ValueTask ResolveAsync(DungeonMapCleared clear, CancellationToken ct)
    {
        if (!clear.IsBossRoom || clear.RunId == default || clear.CycleQuestId is not (> 0 and <= ushort.MaxValue)
            || clear.CycleAcceptances.IsDefaultOrEmpty) return;
        var questId = checked((ushort)clear.CycleQuestId.Value);
        if (await definitions.FindAsync(questId, ct).ConfigureAwait(false) is not { Grade: QuestGrade.Circle }) return;
        foreach (var acceptance in clear.CycleAcceptances.DistinctBy(x => x.CharacterId))
        {
            var member = acceptance;
            if (!clear.Participants.Any(x => x.AccountId == member.AccountId && x.CharacterId == member.CharacterId)) continue;
            var log = logs.LogOf(member.AccountId, member.CharacterId);
            var carried = await log.FindCarriedAsync(questId, ct).ConfigureAwait(false);
            // A repeated clear or an old queued event must never consume a later acceptance.
            if (carried is null || carried.Slot != acceptance.Slot
                || await log.AcceptedAtAsync(carried, ct).ConfigureAwait(false) != acceptance.AcceptedAt) continue;
            var effective = await state.EvaluateAsync(await log.CarriedAsync(ct).ConfigureAwait(false),
                await log.ClearedAsync(ct).ConfigureAwait(false),
                await facts.PersistentHoldingsAsync(member.AccountId, member.CharacterId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            var ready = effective.Single(x => x.Quest.QuestId == questId).CanComplete;
            QuestCompletionReceipt? receipt = null;
            if (ready)
            {
                receipt = await completion.FinishAsync(member.AccountId, member.CharacterId, questId,
                    ushort.MaxValue, 1, clear.ClearedAt, ct).ConfigureAwait(false);
                // Capacity/configuration/settlement refusal is not an unmet objective: retain and retry.
                if (!receipt.Finished) throw new InvalidOperationException($"Cycle quest {questId} was ready but settlement refused.");
            }
            else if (!await log.GiveUpAsync(questId, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Cycle acceptance changed within settlement.");
            var rewards = ImmutableArray.CreateBuilder<CycleQuestReward>();
            foreach (var applied in receipt?.Applied?.Applied ?? [])
            {
                if (applied is { Effect: GrantBasicCharacterItemEffect item, Change: BasicCharacterItemChange added })
                    rewards.AddRange(added.Additions.Select(x => new CycleQuestReward(x.Slot, item.ItemId, x.Count)));
                if (applied.Change is QuestRewardGoldChange gold && gold.After > gold.Before)
                    rewards.Add(new(new(BusinessInventorySpace.VirtualResource, 0), 0, gold.After - gold.Before));
            }
            await events.EnqueueAsync(new CycleQuestResolved(clear.RunId, acceptance, questId, ready,
                receipt?.Objective, receipt?.Award?.Gained ?? 0, clear.ClearedAt, rewards.ToImmutable()), ct).ConfigureAwait(false);
            logger.LogInformation("Cycle quest resolved run={RunId} character={Character} quest={Quest} verdict={Verdict}",
                clear.RunId, member.CharacterId, questId, ready ? "finished" : "cancelled");
        }
    }
}

internal sealed class CycleQuestResolutionSubscriber(IEventService events) : IDomainEventSubscriber<CycleQuestResolved>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<CycleQuestResolved> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.Acceptance.CharacterId]),
            new CycleQuestResolutionView(message.Payload), ct).ConfigureAwait(false);
}
