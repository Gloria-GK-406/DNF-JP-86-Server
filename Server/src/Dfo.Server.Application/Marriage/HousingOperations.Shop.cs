using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed record HousingPurchasePlan(int ItemId, int Count, int PointCost, int GoldCost, int Gold, int Sp, int Cera,
    HousingSnapshot? ChangedRoom, ItemSnapshot? HousingItem);

internal sealed partial class HousingOperations
{
    public async ValueTask<(NpcShopRejection Rejection, HousingPurchasePlan? Plan)> PreparePurchaseAsync(SelectedSessionIdentity actor,
        HousingSnapshot room, int itemId, int count, int shopId, CancellationToken ct)
    {
        if (count is <= 0 or > 1000) return (NpcShopRejection.InvalidCount, null);
        var furniture = Rules.Furniture.TryGetValue(itemId, out var rule);
        var ordinary = Rules.Seeds.ContainsKey(itemId) || itemId is 10002765 or 10002609 or >= 10002611 and <= 10002615;
        if (furniture ? shopId != 1206 : shopId != 1207 || !ordinary) return (NpcShopRejection.ItemNotSold, null);
        var definition = await shops.GetItemAsync(itemId, ct).ConfigureAwait(false);
        if (definition is not { IsReadable: true } || definition.Materials.Any(x => x.ItemId != 10002606)) return (NpcShopRejection.UnsupportedItem, null);
        var pointCost = definition.Materials.Sum(x => (long)x.Count) * count;
        var goldCost = Math.Max(0L, (long)(definition.GoldPrice ?? 0) + definition.AddPrice) * count;
        if (pointCost is < 0 or > 999 || goldCost > int.MaxValue) return (NpcShopRejection.InvalidCount, null);
        if (furniture && pointCost != (long)rule!.PointCost * count) return (NpcShopRejection.UnsupportedItem, null);
        var domain = characters.Get(actor.CharacterId);
        var gold = await domain.GoldAsync(ct).ConfigureAwait(false);
        if (gold < goldCost) return (NpcShopRejection.InsufficientGold, null);
        var changed = furniture ? AddFurniture(room, itemId, count) : null;
        if (furniture && changed is null) return (NpcShopRejection.NoRoom, null);
        var skill = await domain.RemainingSkillPointsAsync(ct).ConfigureAwait(false);
        var wallet = await wallets.ReadAsync(actor.AccountId, ct).ConfigureAwait(false);
        if (skill is null || wallet is null) return (NpcShopRejection.NoSession, null);
        var template = furniture ? await TemplateAsync(itemId, ct).ConfigureAwait(false) : null;
        if (template is not null && (!template.IsPile || template.ExpireTime != 0 || template.ItemUid is not null)) return (NpcShopRejection.UnsupportedItem, null);
        return (NpcShopRejection.None, new(itemId, count, (int)pointCost, (int)goldCost, gold, skill.Page0Sp,
            checked((int)wallet.Cera), changed, template is null ? null : template with { Count = count }));
    }
    public async ValueTask<NpcShopPurchaseResult> CompletePurchaseAsync(SelectedSessionIdentity actor, HousingPurchasePlan plan, CancellationToken ct)
    {
        var gold = plan.Gold;
        if (plan.GoldCost > 0) gold = await characters.Get(actor.CharacterId).TrySpendGoldAsync(plan.GoldCost, ct).ConfigureAwait(false)
            ?? throw new NpcShopPurchaseRejectedException(NpcShopRejection.InsufficientGold);
        IReadOnlyList<NpcShopGrantedSlot> grants;
        if (plan.ChangedRoom is { } changed) { await state.SaveAsync(changed, ct).ConfigureAwait(false); grants = []; }
        else
        {
            try
            {
                var receipt = await delivery.DeliverAsync(actor.AccountId, actor.CharacterId, [new(plan.ItemId, plan.Count)], "marriage.housing-shop", ct, ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
                grants = receipt.Inventory.Select(x => new NpcShopGrantedSlot(x.Slot, x.Snapshot, x.Count)).ToArray();
            }
            catch (ContainerAdditionException error) when (error.Reason == ContainerAdditionFailure.NoRoom)
            { throw new NpcShopPurchaseRejectedException(NpcShopRejection.NoRoom); }
        }
        return new(NpcShopRejection.None, new(actor, plan.ItemId, plan.Count, gold, plan.Sp, plan.Cera, plan.GoldCost > 0, [], grants, clock.GetUtcNow())
        { Housing = plan.ChangedRoom, HousingItem = plan.HousingItem });
    }
}
