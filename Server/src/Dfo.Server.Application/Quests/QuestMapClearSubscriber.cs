using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestMapClearRuleService : IQuestMapClearRuleService
{
    public int? TargetMapOf(QuestDefinition quest) =>
        quest.ObjectiveTypeCode.Trim().Trim('[', ']').Equals("clear map", StringComparison.OrdinalIgnoreCase)
        && quest.ObjectiveArguments.Count is 1 or 2 && quest.ObjectiveArguments[0] > 0
            ? quest.ObjectiveArguments[0] : null;

    public bool Matches(QuestDefinition quest, DungeonMapCleared clear) => TargetMapOf(quest) == clear.MapId
        || quest.ObjectiveSubType == 5 && quest.DungeonClearRequirement is { } target
            && clear.IsBossRoom && target.DungeonId == clear.DungeonId
            && (target.Difficulty < 0 || target.Difficulty == (int)clear.Difficulty)
            && clear.Participants.Select(x => x.CharacterId).Distinct().Count() >= target.MinimumPartyMembers;
}

internal sealed class QuestMapClearSubscriber(ICharacterQuestLogDomainService logs,
    IQuestContentRuleService content, IQuestMapClearRuleService rules, IEventService events)
    : IDomainEventSubscriber<DungeonMapCleared>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<DungeonMapCleared> message, CancellationToken ct)
    {
        var clear = message.Payload;
        foreach (var participant in clear.Participants.DistinctBy(x => x.CharacterId))
        {
            var log = logs.LogOf(participant.AccountId, participant.CharacterId);
            var changed = false;
            foreach (var carried in await log.CarriedAsync(ct).ConfigureAwait(false))
            {
                if (carried.TriggerValue == 0) continue;
                var quest = await content.FindAsync(carried.QuestId, ct).ConfigureAwait(false);
                if (quest is null || quest.Grade == QuestGrade.Crack || !rules.Matches(quest, clear)) continue;
                if (await log.AcceptedAtAsync(carried, ct).ConfigureAwait(false) > clear.ClearedAt) continue;
                changed |= await log.RecordProgressAsync(carried, 0, ct).ConfigureAwait(false);
            }
            if (changed) await events.EnqueueAsync(new QuestProgressChanged(participant.CharacterId), ct).ConfigureAwait(false);
        }
    }
}

internal sealed class QuestProgressNotificationSubscriber(IEventService events) : IDomainEventSubscriber<QuestProgressChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<QuestProgressChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new QuestProgressViewChanged(message.Payload.CharacterId), ct).ConfigureAwait(false);
}

internal static class QuestMapClearRegistration
{
    internal static void AddMapClearQuests(this IServiceCollection services)
    {
        services.TryAddSingleton<IQuestMapClearRuleService, QuestMapClearRuleService>();
        services.AddDomainEvent<DungeonMapCleared>("dungeon.map-cleared", 1);
        services.AddDomainEvent<QuestProgressChanged>("quest.progress-changed", 1);
        services.AddDomainSubscriber<DungeonMapCleared, QuestMapClearSubscriber>("quest.map-clear", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<InventoryContentsChanged, SeekingQuestProgressDomainService>("quest.inventory", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<DungeonMapCleared, MirrorAradQuestDomainService>("quest.mirror-map-clear", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<QuestProgressChanged, QuestProgressNotificationSubscriber>("quest.progress-notification",
            DomainEventDeliveryPolicy.LiveOnly, x => x.CharacterId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
