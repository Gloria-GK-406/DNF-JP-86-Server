using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World;

internal sealed class QuestItemDropDomainService(ICharacterQuestLogDomainService logs,
    ICharacterContainerDomainService containers, IItemDeliveryDomainService delivery) : IQuestItemDropDomainService
{
    public async ValueTask<IReadOnlyList<ItemDeliveryAddition>> GrantAsync(
        AccountId accountId, CharacterId characterId, IReadOnlyList<RolledDrop>? loot,
        CancellationToken cancellationToken)
    {
        var candidates = loot?.Where(x => x.Delivery == RolledDropDelivery.DirectQuestItem
            && x.OwnerCharacterId == characterId).ToArray() ?? [];
        if (candidates.Length == 0) return [];
        var active = (await logs.LogOf(accountId, characterId).CarriedAsync(cancellationToken).ConfigureAwait(false))
            .Select(x => x.QuestId).ToHashSet();
        var additions = new List<ItemDeliveryAddition>();
        foreach (var drop in candidates)
        {
            if (drop.QuestId <= 0 || !active.Contains(drop.QuestId) || drop.ItemId <= 0 || drop.Count <= 0 || drop.GrantLimit <= 0) continue;
            // Delivery places into the default bag, so that is where the limit is counted. A missing
            // bag throws so the caller's transaction keeps the reward unclaimed.
            var bag = containers.FindContainer(characterId, AccountMaterialCatalog.ContainsItem(drop.ItemId)
                    ? InventoryContainerType.SharedMaterial : InventoryContainerType.Main, isDefault: true)
                ?? throw new InvalidOperationException("Quest inventory is unavailable; reward must not be consumed.");
            var held = (await bag.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(x => x.ItemId == drop.ItemId).Sum(x => (long)x.Count)
                + await delivery.PendingQuantityAsync(characterId, drop.ItemId, cancellationToken).ConfigureAwait(false);
            var count = checked((int)Math.Min(drop.Count, Math.Max(0, drop.GrantLimit - held)));
            if (count <= 0) continue;
            var receipt = await delivery.DeliverAsync(accountId, characterId, [new(drop.ItemId, count)], "任务掉落奖励",
                cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
            additions.AddRange(receipt.Inventory);
        }
        return additions;
    }
}
