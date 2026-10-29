using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestAcceptanceDomainService(
    IQuestDefinitionDomainService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    IQuestProgressDomainService questProgress,
    IItemDeliveryDomainService delivery,
    ICycleDungeonEntryDomainService cycles,
    IMirrorAradQuestDomainService mirror,
    IMarriageQuestDomainService marriage,
    ISouthernDaleQuestDomainService southern,
    IGiantDragonCrystalExchangeDomainService crystalExchange) : IQuestAcceptanceDomainService
{
    public async ValueTask<QuestAcceptanceResult> AcceptAsync(
        AccountId accountId, CharacterId characterId, ushort questId, CancellationToken cancellationToken)
    {
        var definition = await quests.FindAsync(questId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.NotAcceptable);

        if (definition.Grade == QuestGrade.Crack)
        {
            var accepted = await mirror.AcceptAsync(accountId, characterId, questId, cancellationToken).ConfigureAwait(false);
            return accepted.Accepted
                ? new(questId, unchecked((uint)accepted.TriggerValue), [], null, null)
                : QuestAcceptanceResult.Refuse(questId, RefusalFor(accepted.Refusal));
        }
        var log = logs.LogOf(accountId, characterId);
        if (definition.Grade == QuestGrade.Wedding
            && !await marriage.CanAcceptAsync(accountId, characterId, questId, cancellationToken).ConfigureAwait(false))
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.NotAcceptable);
        if (definition.Grade == QuestGrade.Circle
            && !await cycles.CanAcceptAsync(accountId, characterId, questId, cancellationToken).ConfigureAwait(false))
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.NotAcceptable);
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        // A21's successful ACK inserts a new client quest; a duplicate returns null and crashes.
        // CMD001F refusal 00 12 bypasses that insertion (protocol/game/client-to-server/0x001F.md).
        if (carried.Any(x => x.QuestId == questId))
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.AlreadyCarried);
        if (questId == ISouthernDaleQuestDomainService.QuestId
            && !await southern.CanAcceptAsync(characterId, cancellationToken).ConfigureAwait(false))
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.NotAcceptable);
        if (questId == IGiantDragonCrystalExchangeDomainService.QuestId
            && !await crystalExchange.CanAcceptAsync(characterId, cancellationToken).ConfigureAwait(false))
            return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.AlreadyCleared);
        var taken = await log.AcceptAsync(definition, cancellationToken).ConfigureAwait(false);
        if (!taken.Accepted)
            return QuestAcceptanceResult.Refuse(questId, RefusalFor(taken.Refusal));
        if (definition.Grade == QuestGrade.Wedding)
            await marriage.BindAsync(accountId, characterId, questId, cancellationToken).ConfigureAwait(false);

        var declared = definition.EventItems ?? [];
        IReadOnlyList<ItemDeliveryAddition> placed = [];
        if (declared.Count > 0)
        {
            // Missing the main container refuses the acceptance before any item delivery.
            // Grants themselves are handled by the transactional inventory/mail delivery service.
            var container = containers.FindContainer(characterId, InventoryContainerType.Main);
            if (container is null)
            {
                await log.GiveUpAsync(questId, cancellationToken).ConfigureAwait(false);
                return QuestAcceptanceResult.Refuse(questId, QuestAcceptanceRefusal.NoRoomForItems);
            }

            var receipt = await delivery.DeliverAsync(accountId, characterId,
                declared.Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray(), "任务授予物品", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            placed = receipt.Inventory;
        }

        var progress = placed.Count > 0 || definition.SeekingItems is { Count: > 0 }
            ? (await questProgress.ReportAsync(
                accountId, characterId,
                [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false))
                .NotificationSnapshot
            : null;
        if (progress is null && taken.TriggerValue != definition.InitialTrigger)
        {
            progress = new QuestProgressSnapshot(await log.CarriedAsync(cancellationToken)
                .ConfigureAwait(false));
        }
        if (progress is not null)
            progress = await mirror.MergeAsync(characterId, progress, cancellationToken).ConfigureAwait(false);
        return new(questId, definition.InitialTrigger, placed, progress, null);
    }

    private static QuestAcceptanceRefusal RefusalFor(QuestRefusal? refusal) => refusal switch
    {
        QuestRefusal.AlreadyCarried => QuestAcceptanceRefusal.AlreadyCarried,
        QuestRefusal.AlreadyCleared => QuestAcceptanceRefusal.AlreadyCleared,
        QuestRefusal.NoFreeSlot => QuestAcceptanceRefusal.NoFreeSlot,
        QuestRefusal.ManagedByAchievement => QuestAcceptanceRefusal.ManagedByAchievement,
        QuestRefusal.UnsupportedObjective => QuestAcceptanceRefusal.UnsupportedObjective,
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "No code was decided for this refusal."),
    };
}
