using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class MonsterCardUpgradeRuleService(IMonsterCardUpgradeDefinitionSource content)
{
    internal const int RateScale = 100_000;

    public async ValueTask<(int Gold, int Rate)?> QuoteAsync(ItemSnapshot target, ItemSnapshot material, CancellationToken ct)
    {
        var targetCard = await content.GetCardAsync(target.ItemId, ct).ConfigureAwait(false);
        var materialCard = target.ItemId == material.ItemId ? targetCard
            : await content.GetCardAsync(material.ItemId, ct).ConfigureAwait(false);
        if (targetCard is null || materialCard is null || target.EnchantUpgradeCount == byte.MaxValue
            || !targetCard.Ranks.Contains(target.EnchantUpgradeCount)
            || !targetCard.Ranks.Contains((byte)(target.EnchantUpgradeCount + 1))
            || !materialCard.Ranks.Contains(material.EnchantUpgradeCount)
            || await content.GetUpgradeDefinitionAsync(ct).ConfigureAwait(false) is not { } definition)
            return null;

        if (target.ItemId == material.ItemId || materialCard.Rarity > targetCard.Rarity)
            return (definition.GoldCost, RateScale);
        var gap = targetCard.Rarity - materialCard.Rarity;
        var baseRate = (80 - 10 * targetCard.Rarity) * 1000;
        var rate = (long)(baseRate >> gap) * definition.RateMultiplier / 10;
        return (definition.GoldCost, (int)Math.Min(rate, RateScale));
    }
}

internal sealed class MonsterCardUpgradeDomainService(MonsterCardUpgradeRuleService rules, ISelectedSessionDomainService sessions,
    ICharacterDomainService characters, IBusinessInventorySlotDomainService slots, ICharacterBusinessInventoryDomainService bags,
    IItemLifecycleDomainService lifecycle, IItemPlacementRuleService placements, IItemArrivalRuleService arrivals,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IMonsterCardUpgradeDomainService
{
    public async ValueTask<MonsterCardUpgradeResult> UpgradeAsync(SelectedSessionIdentity session, BusinessSlot targetSlot,
        BusinessSlot materialSlot, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Card upgrade requires a domain transaction.");
        if (!sessions.IsInWorld(session) || !IsCardSpace(targetSlot.Space) || !IsCardSpace(materialSlot.Space)
            || slots.Find(session.CharacterId, targetSlot) is not { } target
            || slots.Find(session.CharacterId, materialSlot) is not { } material)
            return new(MonsterCardUpgradeRejection.InvalidRequest);

        var now = clock.GetUtcNow();
        var targetCard = await target.Container.GetAsync(targetSlot.Index, ct).ConfigureAwait(false);
        var sameSlot = targetSlot == materialSlot;
        var materialCard = sameSlot ? targetCard : await material.Container.GetAsync(materialSlot.Index, ct).ConfigureAwait(false);
        if (targetCard is not { Count: > 0, IsPile: true } || materialCard is not { Count: > 0, IsPile: true }
            || sameSlot && targetCard.Count < 2 || !Usable(targetCard.Snapshot, now) || !Usable(materialCard.Snapshot, now)
            || await rules.QuoteAsync(targetCard.Snapshot, materialCard.Snapshot, ct).ConfigureAwait(false) is not { } quote)
            return new(MonsterCardUpgradeRejection.InvalidRequest);

        var character = characters.Get(session.CharacterId);
        if (await character.GoldAsync(ct).ConfigureAwait(false) < quote.Gold)
            return new(MonsterCardUpgradeRejection.InsufficientGold);

        var remainingTarget = targetCard.Count - (sameSlot ? 1 : 0);
        var upgraded = targetCard.Snapshot with { Count = 1, EnchantUpgradeCount = (byte)(targetCard.Snapshot.EnchantUpgradeCount + 1) };
        (IBusinessItemContainer Container, ItemArrival Arrival)? splitDestination = null;
        if (remainingTarget > 1)
        {
            splitDestination = await PlanSplitArrivalAsync(session.CharacterId, upgraded, materialSlot, ct)
                .ConfigureAwait(false);
            if (splitDestination is null) return new(MonsterCardUpgradeRejection.InventoryFull);
        }
        else if (targetSlot.Index >= await target.Container.OpenSlotCountAsync(ct).ConfigureAwait(false))
        {
            return new(MonsterCardUpgradeRejection.InventoryFull);
        }

        var succeeded = Random.Shared.Next(MonsterCardUpgradeRuleService.RateScale) < quote.Rate;
        if (quote.Gold > 0 && await character.TrySpendGoldAsync(quote.Gold, ct).ConfigureAwait(false) is null)
            return new(MonsterCardUpgradeRejection.InsufficientGold);
        _ = await ItemContainerOperations.ConsumeAsync(material.Container, materialCard, 1, lifecycle, ct).ConfigureAwait(false);
        if (succeeded)
        {
            var currentTarget = sameSlot
                ? await target.Container.GetAsync(targetSlot.Index, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Target card vanished after taking the material.")
                : targetCard;
            if (splitDestination is { } destination)
            {
                _ = await ItemContainerOperations.ConsumeAsync(target.Container, currentTarget, 1, lifecycle, ct).ConfigureAwait(false);
                _ = await ItemContainerOperations.ApplyArrivalAsync(destination.Container, destination.Arrival,
                    upgraded, lifecycle, ct).ConfigureAwait(false);
            }
            else
                _ = await target.Container.UpdateAsync(currentTarget, _ => upgraded, ct).ConfigureAwait(false);
        }

        return new(MonsterCardUpgradeRejection.None, new(targetCard.ItemId,
            succeeded ? upgraded.EnchantUpgradeCount : targetCard.Snapshot.EnchantUpgradeCount, succeeded, now));
    }

    private async ValueTask<(IBusinessItemContainer Container, ItemArrival Arrival)?> PlanSplitArrivalAsync(
        CharacterId owner, ItemSnapshot upgraded, BusinessSlot materialSlot, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(upgraded.ItemId, ct).ConfigureAwait(false);
        if (placement is null || !IsCardSpace(placement.Destination.Space)) return null;
        var space = placement.Destination.Space;
        var bag = bags.GetStore(owner, BusinessInventoryStoreKind.Item).FindContainer(space);
        if (bag is null) return null;
        var open = await bag.OpenSlotCountAsync(ct).ConfigureAwait(false);
        if (InventoryCapacityPlacement.Limit(placement, open) is not { } bounded) return null;
        var occupied = new HashSet<BusinessSlot>();
        var matching = new List<ItemPile>();
        foreach (var entry in await bag.GetAllAsync(ct).ConfigureAwait(false))
        {
            var slot = new BusinessSlot(space, entry.SlotIndex);
            var remaining = entry.Count - (slot == materialSlot ? 1 : 0);
            if (remaining == 0) continue;
            occupied.Add(slot);
            if (slot.Index < open && entry.Snapshot.CanStackWith(upgraded))
                matching.Add(new(slot, remaining));
        }

        var arrival = arrivals.ForGrant(matching, 1, bounded, occupied);
        return arrival switch
        {
            ItemArrival.IntoEmptySlot empty when empty.Slot.Space == space => (bag, arrival),
            ItemArrival.OntoExistingPile pile when pile.Slot.Space == space => (bag, arrival),
            _ => null,
        };
    }

    private static bool IsCardSpace(BusinessInventorySpace space) =>
        space is BusinessInventorySpace.Material or BusinessInventorySpace.ExpertMaterial;

    private static bool Usable(ItemSnapshot card, DateTimeOffset now) =>
        !card.IsMirror && card.EquipmentLockId == 0 && card.SortLockFlag == 0 && !card.Lifetime.IsExpired(now);
}
