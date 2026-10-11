using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Persists derived quest progress; condition evaluation is shared with all read paths.</summary>
internal sealed class SeekingQuestProgressDomainService(IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs, ICharacterContainerDomainService containers,
    ICharacterDomainService characters, IQuestStateRuleService? stateRules = null,
    IQuestFactsDomainService? facts = null, ICharacterTable? owners = null, IEventService? events = null)
    : ISeekingQuestProgressDomainService, IDomainEventSubscriber<InventoryContentsChanged>
{
    private readonly IQuestStateRuleService rules = stateRules ?? new QuestStateRuleService(quests);
    public async ValueTask<SeekingQuestProgressSynchronization?> SynchronizeAsync(AccountId accountId,
        CharacterId characterId, CancellationToken ct)
    {
        var log = logs.LogOf(accountId, characterId);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        if (carried.Count == 0 || containers.FindContainer(characterId, InventoryContainerType.Main) is null) return null;
        var holdings = facts is not null ? await facts.HoldingsAsync(characterId, ct).ConfigureAwait(false)
            : await QuestHoldingsReader.ReadAsync(containers, rules, characterId,
                await characters.Get(characterId).GoldAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return await PersistAsync(log, carried, carried, holdings, ct).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<InventoryContentsChanged> message, CancellationToken ct)
    {
        var container = message.Payload.Container;
        if (container.TryGetCharacterOwner(out var character, out var type) && type == InventoryContainerType.Main)
        {
            var account = await (owners ?? throw new InvalidOperationException("Inventory quest checks require character ownership."))
                .FindOwnerAsync(character, ct).ConfigureAwait(false);
            if (account is { } owner) await SynchronizeItemsAsync(owner, character, message.Payload, ct).ConfigureAwait(false);
        }
        else if (container.TryGetAccountOwner(out var account, out type) && type == InventoryContainerType.SharedMaterial)
        {
            foreach (var row in await (owners ?? throw new InvalidOperationException("Inventory quest checks require character ownership."))
                .ReadByAccountAsync(account, ct).ConfigureAwait(false))
                await SynchronizeItemsAsync(account, new(row.CharacterId), message.Payload, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask SynchronizeItemsAsync(AccountId account, CharacterId character, InventoryContentsChanged change, CancellationToken ct)
    {
        var log = logs.LogOf(account, character);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        var ids = change.ChangedItemIds.IsDefaultOrEmpty ? null : change.ChangedItemIds.ToHashSet();
        var related = new List<CarriedQuest>();
        foreach (var active in carried)
        {
            var definition = await quests.FindAsync(active.QuestId, ct).ConfigureAwait(false);
            if (definition?.SeekingItems?.Any(x => x.ItemId > 0 && (ids is null || ids.Contains(x.ItemId))) == true)
                related.Add(active);
        }
        if (related.Count == 0) return;
        var holdings = await (facts ?? throw new InvalidOperationException("Queued quest checks require persistent holdings."))
            .PersistentHoldingsAsync(account, character, ct).ConfigureAwait(false);
        if (await PersistAsync(log, carried, related, holdings, ct).ConfigureAwait(false) is not null)
            await (events ?? throw new InvalidOperationException("Queued quest checks require event delivery."))
                .EnqueueAsync(new QuestProgressChanged(character), ct).ConfigureAwait(false);
    }

    private async ValueTask<SeekingQuestProgressSynchronization?> PersistAsync(IQuestLog log,
        IReadOnlyList<CarriedQuest> carried, IReadOnlyList<CarriedQuest> candidates, QuestHoldingsFacts holdings, CancellationToken ct)
    {
        var evaluated = await rules.EvaluateAsync(candidates, await log.ClearedAsync(ct).ConfigureAwait(false), holdings, ct).ConfigureAwait(false);
        var bySlot = evaluated.ToDictionary(x => x.Quest.Slot, x => x.Quest);
        var updated = new List<CarriedQuest>(carried.Count);
        var changed = false;
        foreach (var active in carried.OrderBy(x => x.Slot))
        {
            var after = bySlot.GetValueOrDefault(active.Slot) ?? active;
            if (after.QuestId != active.QuestId) throw new InvalidDataException("Quest slot changed while refreshing progress.");
            if (after.TriggerValue != active.TriggerValue)
            {
                if (!await log.RecordProgressAsync(active, after.TriggerValue, ct).ConfigureAwait(false))
                    throw new InvalidDataException($"Quest {active.QuestId} changed concurrently.");
                changed = true;
            }
            updated.Add(after);
        }
        return changed ? new(updated) : null;
    }
}

internal static class QuestHoldingsReader
{
    public static async ValueTask<QuestHoldingsFacts> ReadAsync(ICharacterContainerDomainService containers,
        IQuestStateRuleService rules, CharacterId character, int gold, CancellationToken ct)
    {
        var bag = containers.FindContainer(character, InventoryContainerType.Main)
            ?? throw new InvalidOperationException($"Missing inventory for quest state query: {character}.");
        var main = await bag.GetAllAsync(ct).ConfigureAwait(false);
        var shared = containers.FindContainer(character, InventoryContainerType.SharedMaterial) is { } account
            ? await account.GetAllAsync(ct).ConfigureAwait(false) : [];
        return rules.Holdings(main.Select(x => x.Snapshot), shared.Select(x => x.Snapshot), gold);
    }
}
