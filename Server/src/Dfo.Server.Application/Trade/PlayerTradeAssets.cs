using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Trade;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Trade;

/// <summary>Reads quotes and exchanges their assets in the caller's single writable transaction.</summary>
public sealed class PlayerTradeAssets(
    IBusinessInventorySlotDomainService slots, ICharacterDomainService characters,
    ICharacterCarriedGoldTable balances, ICarriedGoldRuleService goldRules,
    IItemDeliveryDomainService delivery, IItemLifecycleDomainService lifecycle,
    IPlayerTradeItemRuleService itemRules, TimeProvider clock)
{
    internal async ValueTask<TradeOffer?> QuoteAsync(SelectedSessionIdentity owner, TradeSource source,
        int count, CancellationToken ct)
    {
        if (count <= 0) return null;
        if (source.Slot is not { } slot)
            return source.ItemId == 0 && count <= await characters.Get(owner.CharacterId).GoldAsync(ct).ConfigureAwait(false)
                ? new(source, count, default, null) : null;
        if (slots.Find(owner.CharacterId, slot) is not { Container: { IsDefault: true, IsPersistent: true } container }
            || await container.GetAsync(slot.Index, ct).ConfigureAwait(false) is not { } item
            || item.ItemId != source.ItemId || count > item.Count
            || !(await itemRules.CanTradeAsync(item.Snapshot, slot.Space, clock.GetUtcNow(), ct).ConfigureAwait(false)).Allowed)
            return null;
        return new(source, count, container.StorageId, item.Snapshot);
    }

    internal async ValueTask<bool> ExchangeAsync(PlayerTradeSession trade, CancellationToken ct)
    {
        var left = await PrepareAsync(trade.Inviter, trade.Left, trade.Right, ct).ConfigureAwait(false);
        var right = await PrepareAsync(trade.Invitee, trade.Right, trade.Left, ct).ConfigureAwait(false);
        if (left is null || right is null || left.Items.Count + right.Items.Count == 0
            && !trade.Left.Offers.ContainsKey(0) && !trade.Right.Offers.ContainsKey(0)) return false;

        // Both removals happen before placement so each player can use the slots just vacated.
        var toRight = await TakeAsync(left.Items, ct).ConfigureAwait(false);
        var toLeft = await TakeAsync(right.Items, ct).ConfigureAwait(false);
        await PlaceAsync(trade.Inviter, toLeft, ct).ConfigureAwait(false);
        await PlaceAsync(trade.Invitee, toRight, ct).ConfigureAwait(false);
        return await WriteGoldAsync(trade.Inviter, left, ct).ConfigureAwait(false)
            && await WriteGoldAsync(trade.Invitee, right, ct).ConfigureAwait(false);
    }

    private async ValueTask<SettlementSide?> PrepareAsync(SelectedSessionIdentity actor, TradeSide own,
        TradeSide other, CancellationToken ct)
    {
        if (await balances.FindAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { } balance) return null;
        var outgoing = own.Offers.GetValueOrDefault(0)?.Count ?? 0;
        var incoming = other.Offers.GetValueOrDefault(0)?.Count ?? 0;
        var final = (long)balance.Carried - outgoing + incoming;
        if (outgoing > balance.Carried || final > goldRules.CarryLimitFor(balance.Level, balance.StoredCarryLimit))
            return null;

        var items = new List<SettlementItem>();
        foreach (var offer in own.Offers.Values)
        {
            if (offer.Source.Slot is not { } slot) continue;
            if (slots.Find(actor.CharacterId, slot) is not { Container: { IsDefault: true, IsPersistent: true } container }
                || container.StorageId != offer.Container
                || await container.GetAsync(slot.Index, ct).ConfigureAwait(false) is not { } item
                || item.Snapshot != offer.Snapshot || item.Count < offer.Count)
                return null;
            var decision = await itemRules.CanTradeAsync(item.Snapshot, slot.Space, clock.GetUtcNow(), ct).ConfigureAwait(false);
            if (!decision.Allowed) return null;
            items.Add(new(container, item, offer.Count, decision.TransferredAttribute));
        }
        return new(items, balance.Carried, checked((int)final));
    }

    private async ValueTask<List<ItemDeliveryGrant>> TakeAsync(List<SettlementItem> items, CancellationToken ct)
    {
        var grants = new List<ItemDeliveryGrant>(items.Count);
        foreach (var item in items)
        {
            var record = await item.Container.TakeAsync(item.Reference, item.Count, ct).ConfigureAwait(false);
            if (item.TransferredAttribute is { } attribute)
            {
                // A pile has no unique item identity. Only the transferred portion spends one use.
                var snapshot = record.Snapshot;
                await lifecycle.DeleteItemAsync(record, ct).ConfigureAwait(false);
                record = await lifecycle.CreateAsync(snapshot with { Attribute = attribute },
                    ct).ConfigureAwait(false);
            }
            await item.Container.FlushAsync(ct).ConfigureAwait(false);
            grants.Add(new(record.ItemId, record.Count, Instance: record));
        }
        return grants;
    }

    private async ValueTask PlaceAsync(SelectedSessionIdentity actor, List<ItemDeliveryGrant> grants, CancellationToken ct)
    {
        if (grants.Count == 0) return;
        var receipt = await delivery.DeliverAsync(actor.AccountId, actor.CharacterId, grants, "player trade", ct,
            ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
        if (receipt.MailedCount != 0 || receipt.Inventory.Sum(item => (long)item.Count) != grants.Sum(item => (long)item.Count))
            throw new ContainerAdditionException(ContainerAdditionFailure.NoRoom);
    }

    private async ValueTask<bool> WriteGoldAsync(SelectedSessionIdentity actor, SettlementSide side, CancellationToken ct)
    {
        var delta = side.FinalGold - side.OriginalGold;
        if (delta == 0) return true;
        var character = characters.Get(actor.CharacterId);
        var actual = delta < 0
            ? await character.TrySpendGoldAsync(-delta, ct).ConfigureAwait(false)
            : await character.AddGoldAsync(delta, ct).ConfigureAwait(false);
        return actual == side.FinalGold;
    }

    private sealed record SettlementItem(IBusinessItemContainer Container, ItemReference Reference, int Count, byte? TransferredAttribute);
    private sealed record SettlementSide(List<SettlementItem> Items, int OriginalGold, int FinalGold);
}
