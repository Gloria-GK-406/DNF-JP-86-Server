using System.Collections.Frozen;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestMaterialRequirementDomainService(
    IQuestContentRuleService quests,
    ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers) : IQuestMaterialRequirementDomainService
{
    public async ValueTask<IReadOnlySet<int>> GetOutstandingMaterialIdsAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var carried = await logs.LogOf(accountId, characterId).CarriedAsync(cancellationToken).ConfigureAwait(false);
        if (carried.Count == 0) return FrozenSet<int>.Empty;
        var bag = containers.FindContainer(characterId, InventoryContainerType.Main)
            ?? throw new InvalidOperationException($"Missing inventory for quest material query: {characterId}.");
        var held = (await bag.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.ItemId > 0).GroupBy(item => item.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.Count));
        var result = new HashSet<int>();
        foreach (var active in carried)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = await quests.FindAsync(active.QuestId, cancellationToken).ConfigureAwait(false);
            if (definition?.SeekingItems is not { Count: > 0 } requirements) continue;
            // Inventory is authoritative: a stored trigger may lag a pickup or material consumption.
            // Requirements of different quests are not summed; each quest is checked independently.
            foreach (var requirement in requirements)
                if (requirement.ItemId > 0 && requirement.RequiredCount > held.GetValueOrDefault(requirement.ItemId))
                    result.Add(requirement.ItemId);
        }
        return result.ToFrozenSet();
    }
}
