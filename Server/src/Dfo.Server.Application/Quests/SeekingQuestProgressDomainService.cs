using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Persists derived quest progress; condition evaluation is shared with all read paths.</summary>
internal sealed class SeekingQuestProgressDomainService(IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs, ICharacterContainerDomainService containers,
    ICharacterDomainService characters, IQuestStateRuleService? stateRules = null,
    IQuestFactsDomainService? facts = null) : ISeekingQuestProgressDomainService
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
        var evaluated = await rules.EvaluateAsync(carried, await log.ClearedAsync(ct).ConfigureAwait(false), holdings, ct).ConfigureAwait(false);
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
