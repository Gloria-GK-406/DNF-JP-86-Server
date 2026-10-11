using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Persists remaining seeking counts from the live bag, shared materials and carried gold.</summary>
internal sealed class SeekingQuestProgressDomainService(
    IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers,
    ICharacterDomainService characters) : ISeekingQuestProgressDomainService
{
    public async ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var log = logs.LogOf(accountId, characterId);
        var carried = await log.CarriedAsync(cancellationToken).ConfigureAwait(false);
        if (carried.Count == 0)
            return null;

        var bag = containers.FindContainer(characterId, InventoryContainerType.Main);
        if (bag is null)
            return null;
        var items = await bag.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var counts = items
            .Where(item => item.ItemId > 0)
            .GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Count));
        if (containers.FindContainer(characterId, InventoryContainerType.SharedMaterial) is { } shared)
            foreach (var item in await shared.GetAllAsync(cancellationToken).ConfigureAwait(false))
                counts[item.ItemId] = item.Count;
        counts[0] = await characters.Get(characterId).GoldAsync(cancellationToken)
            .ConfigureAwait(false);

        var updated = carried.ToDictionary(quest => quest.Slot);
        var changed = false;
        foreach (var active in carried.OrderBy(quest => quest.Slot))
        {
            var definition = await quests.FindAsync(active.QuestId, cancellationToken)
                .ConfigureAwait(false);
            if (definition?.SeekingItems is not { Count: > 0 } requirements)
                continue;

            long missing = 0;
            foreach (var requirement in requirements)
            {
                missing += Math.Max(
                    0,
                    requirement.RequiredCount - counts.GetValueOrDefault(requirement.ItemId));
            }
            var after = ReplaceChannel0(active.TriggerValue, missing);
            if (after == active.TriggerValue)
                continue;

            if (!await log.RecordProgressAsync(active, after, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    $"Seeking quest {active.QuestId} changed concurrently.");
            }

            updated[active.Slot] = active with { TriggerValue = after };
            changed = true;
        }

        return changed
            ? new SeekingQuestProgressSynchronization(
                [.. updated.Values.OrderBy(quest => quest.Slot)])
            : null;
    }

    internal static int ReplaceChannel0(int trigger, long value) =>
        (trigger & ~0x1FF) | (int)Math.Clamp(value, 0, 0x1FF);
}
