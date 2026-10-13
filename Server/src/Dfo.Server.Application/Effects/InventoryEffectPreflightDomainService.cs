using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Effects;

internal sealed class InventoryEffectPreflightDomainService(IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipment, ICharacterDomainService characters,
    IItemPlacementRuleService placements, IItemArrivalRuleService arrivals,
    IEnumerable<IInventoryEffectPreviewHandler> extensions, ICharacterContainerDomainService? containers = null,
    IPremiumServiceDomainService? premiums = null, IAccountTable? accountTable = null,
    IPremiumServiceUsageDomainService? usage = null) : IInventoryEffectPreflightDomainService
{
    private readonly IReadOnlyDictionary<Type, IInventoryEffectPreviewHandler> registered = extensions.ToDictionary(x => x.EffectType);

    public async ValueTask<InventoryEffectPreviewResult> PreviewAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct) => await PreviewAsync(context, effects, false, ct).ConfigureAwait(false);
    public ValueTask<InventoryEffectPreviewResult> PreviewWithHoldingsAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, CancellationToken ct) => PreviewAsync(context, effects, true, ct);
    private async ValueTask<InventoryEffectPreviewResult> PreviewAsync(EffectExecutionContext context,
        IReadOnlyList<DomainEffect> effects, bool includeHoldings, CancellationToken ct)
    {
        var state = new Standing(context, slots, characters, containers, premiums, accountTable, usage);
        try
        {
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case ConsumeFromContainerEffect consume:
                        var item = await state.GetAsync(consume.Slot, ct).ConfigureAwait(false);
                        if (consume.Count <= 0 || item is not { IsPile: true } || item.ItemId != consume.ExpectedItemId
                            || item.EquipmentLockId != consume.ExpectedEquipmentLockId
                            || item.ExpireTime is > 0 and < int.MaxValue && item.ExpireTime <= context.At.ToUnixTimeSeconds())
                            Refuse(InventoryEffectRejection.InvalidTarget);
                        if (item!.Count < consume.Count) Refuse(InventoryEffectRejection.InsufficientAssets);
                        state.SetSlot(consume.Slot, item.Count == consume.Count ? null : item with
                        { Count = item.Count - consume.Count });
                        break;
                    case SpendGoldEffect spend:
                        if (spend.Amount < 0) Refuse(InventoryEffectRejection.InvalidTarget);
                        var gold = await state.GoldAsync(ct).ConfigureAwait(false);
                        if (gold < spend.Amount) Refuse(InventoryEffectRejection.InsufficientAssets);
                        state.SetGold(gold - spend.Amount);
                        break;
                    case SetQuestRewardGoldEffect reward:
                        if (reward.Expected < 0 || reward.Value < reward.Expected
                            || await state.GoldAsync(ct).ConfigureAwait(false) != reward.Expected) Refuse(InventoryEffectRejection.InvalidTarget);
                        state.SetGold(reward.Value);
                        break;
                    case EquipmentEffect change when effect is SetEquipmentUpgradeEffect or SetEquipmentEnchantmentEffect
                        or SetEquipmentDimensionalEffect or SetEquipmentMagicSealEffect or SetEquipmentForgingEffect or DestroyEquipmentEffect:
                        var instance = await equipment.Of(context.CharacterId).FindAtAsync(change.Equipment.Reference.BusinessSlot!.Value, ct)
                            .ConfigureAwait(false);
                        if (instance is null || !instance.Reference.SameOccupancyAs(change.Equipment.Reference)) Refuse(InventoryEffectRejection.InvalidTarget);
                        var facts = await instance!.ReadAsync(ct).ConfigureAwait(false);
                        var standing = await state.GetAsync(facts.BusinessSlot!.Value, ct).ConfigureAwait(false);
                        if (standing is null || standing.ItemUid != change.Equipment.Reference.ItemUid || standing.ItemId != change.Equipment.ExpectedItemId)
                            Refuse(InventoryEffectRejection.InvalidTarget);
                        if (change is DestroyEquipmentEffect) state.SetSlot(facts.BusinessSlot!.Value, null);
                        break;
                    case GrantStackableEffect grant:
                        await GrantAsync(context, grant, state, ct).ConfigureAwait(false);
                        break;
                    case GrantBasicCharacterItemEffect basic:
                        await GrantBasicAsync(basic.ItemId, basic.Count, basic.Placement, state, ct).ConfigureAwait(false);
                        break;
                    default:
                        if (registered.TryGetValue(effect.GetType(), out var handler))
                            await handler.PreviewAsync(context, effect, state, ct).ConfigureAwait(false);
                        else if (effect is InventoryEffect)
                            throw new NotSupportedException($"No inventory preview registered for {effect.GetType().Name}.");
                        break;
                }
            }
            return new(InventoryEffectRejection.None, state.RewardEntries,
                includeHoldings ? await state.HoldingsAsync(ct).ConfigureAwait(false) : null);
        }
        catch (InventoryEffectException exception) { return new(exception.Rejection); }
    }

    private async ValueTask GrantAsync(EffectExecutionContext context, GrantStackableEffect grant, Standing state, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(grant.ItemId, ct).ConfigureAwait(false);
        if (grant.Count <= 0
            || placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
            Refuse(InventoryEffectRejection.UnsupportedItem);
        await GrantBasicAsync(grant.ItemId, grant.Count, placement!, state, ct).ConfigureAwait(false);
    }
    private async ValueTask GrantBasicAsync(int itemId, int count, ItemPlacement placement, Standing state, CancellationToken ct)
    {
        if (count <= 0 || itemId <= 0 || placement.ItemKind == 0)
            Refuse(InventoryEffectRejection.UnsupportedItem);
        var limited = InventoryCapacityPlacement.Limit(placement,
            await state.OpenSlotCountAsync(placement.Destination.Space, ct).ConfigureAwait(false));
        if (limited is null)
        {
            if (placement.Destination.Space == BusinessInventorySpace.VirtualResource) Refuse(InventoryEffectRejection.NoRoom);
            return;
        }
        placement = limited;
        var available = Enumerable.Range(placement.Destination.FirstIndex,
            placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        if (placement.Destination.Space != BusinessInventorySpace.SharedMaterial && ItemSlotRuleService.AllowsQuickSlot(placement))
            available = available.Concat(Enumerable.Range(0, 6).Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)));
        var candidates = available.ToArray();
        foreach (var slot in candidates) _ = await state.GetAsync(slot, ct).ConfigureAwait(false);
        var remaining = count;
        while (remaining > 0)
        {
            var arrival = arrivals.ForGrant(candidates.Select(slot => (Slot: slot, Item: state.At(slot)))
                .Where(pair => pair.Item is { IsPile: true } && pair.Item.ItemId == itemId)
                .Select(pair => new ItemPile(pair.Slot, pair.Item!.Count)).ToArray(), remaining, placement,
                candidates.Where(slot => state.At(slot) is not null).ToHashSet());
            BusinessSlot target;
            int added;
            ItemSnapshot core;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile pile:
                    target = pile.Slot; added = pile.Added; core = state.At(target)! with { Count = pile.Total }; break;
                case ItemArrival.IntoEmptySlot empty:
                    target = empty.Slot; added = empty.Count; core = new ItemSnapshot { ItemId = itemId, Count = placement.WorldClass == ItemWorldClass.Interchangeable ? added : 1,
                        LegacyKind = placement.ItemKind, Type = placement.Traits, IsPile = placement.WorldClass == ItemWorldClass.Interchangeable }; break;
                case ItemArrival.NoRoom:
                    if (placement.Destination.Space == BusinessInventorySpace.VirtualResource) Refuse(InventoryEffectRejection.NoRoom);
                    return;
                default: throw new InvalidDataException("Unsupported reward arrival.");
            }
            if (added <= 0 || added > remaining) throw new InvalidDataException("Invalid effect arrival progress.");
            state.SetSlot(target, core);
            state.AddedRewardEntries(1);
            remaining -= added;
        }
    }

    private static void Refuse(InventoryEffectRejection reason) => throw new InventoryEffectException(reason);

    private sealed class Standing(EffectExecutionContext context, IBusinessInventorySlotDomainService slots,
        ICharacterDomainService characters,
        ICharacterContainerDomainService? containers, IPremiumServiceDomainService? premiums,
        IAccountTable? accountTable, IPremiumServiceUsageDomainService? usage) : IInventoryEffectPreviewState
    {
        private readonly Dictionary<BusinessSlot, ItemSnapshot?> items = [];
        private readonly HashSet<BusinessInventorySpace> loaded = [];
        private readonly Dictionary<int, ItemSnapshot?> virtualItems = [];
        private readonly Dictionary<PremiumServiceType, long> expiries = [];
        private int? luck;
        private DoublePotCounterState? doublePot;
        private int? gold;
        public int RewardEntries { get; private set; }
        public ItemSnapshot? At(BusinessSlot slot) => items.GetValueOrDefault(slot);
        public async ValueTask<ItemSnapshot?> GetAsync(BusinessSlot slot, CancellationToken ct)
        {
            if (loaded.Add(slot.Space))
            {
                var bound = slots.Find(context.CharacterId, slot)
                    ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
                foreach (var item in await bound.Container.GetAllAsync(ct).ConfigureAwait(false))
                    items[new(slot.Space, item.SlotIndex)] = item.Snapshot;
            }
            return At(slot);
        }
        public void SetSlot(BusinessSlot slot, ItemSnapshot? item) => items[slot] = item;
        public async ValueTask<int> GoldAsync(CancellationToken ct) =>
            gold ??= await characters.Get(context.CharacterId).GoldAsync(ct).ConfigureAwait(false);
        public void SetGold(int amount) => gold = amount;
        public async ValueTask<ItemSnapshot?> VirtualSlotAsync(int index, CancellationToken ct)
        {
            if (index is < 0 or > 2) Refuse(InventoryEffectRejection.InvalidTarget);
            if (!virtualItems.TryGetValue(index, out var item))
            {
                var main = containers!.FindContainer(context.CharacterId, InventoryContainerType.Main)
                    ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
                virtualItems[index] = item = (await main.GetAsync(index, ct).ConfigureAwait(false))?.Snapshot;
            }
            return item;
        }
        public void SetVirtualSlot(int index, ItemSnapshot? item) => virtualItems[index] = item;
        public async ValueTask<long> PremiumExpiryAsync(PremiumServiceType type, CancellationToken ct)
        {
            if (!expiries.TryGetValue(type, out var expiry))
                expiries[type] = expiry = (await premiums!.For(context.AccountId).ReadAsync(ct).ConfigureAwait(false))
                    .SingleOrDefault(row => row.PremiumType == (int)type)?.ExpiresAtUnixSeconds ?? 0;
            return expiry;
        }
        public void SetPremiumExpiry(PremiumServiceType type, long expiry) => expiries[type] = expiry;
        public async ValueTask<int> SeriaLuckAsync(CancellationToken ct) => luck ??=
            await accountTable!.FindSeriaLuckAsync(context.AccountId, ct).ConfigureAwait(false)
            ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        public void SetSeriaLuck(int value) => luck = value;
        public async ValueTask<DoublePotCounterState> DoublePotCountersAsync(CancellationToken ct) => doublePot ??=
            (await usage!.DoublePotFactsAsync(context.AccountId, context.CharacterId, context.At, ct).ConfigureAwait(false)).Counters;
        public void SetDoublePotCounters(DoublePotCounterState counters) => doublePot = counters;
        public ValueTask<int> OpenSlotCountAsync(BusinessInventorySpace space, CancellationToken ct) =>
            slots.Get(context.CharacterId, new(space, 0)).Container.OpenSlotCountAsync(ct);
        public void AddedRewardEntries(int count) => RewardEntries += count;
        public async ValueTask<InventoryEffectHoldingsSnapshot> HoldingsAsync(CancellationToken ct)
        {
            var main = containers?.FindContainer(context.CharacterId, InventoryContainerType.Main)
                ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
            var result = (await main.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.SlotIndex, x => x.Snapshot);
            var sharedContainer = containers?.FindContainer(context.CharacterId, InventoryContainerType.SharedMaterial);
            var shared = sharedContainer is null ? new Dictionary<int, ItemSnapshot>()
                : (await sharedContainer.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.SlotIndex, x => x.Snapshot);
            foreach (var (slot, item) in items)
            {
                if (slot.Space == BusinessInventorySpace.SharedMaterial)
                {
                    if (item is null) shared.Remove(slot.Index); else shared[slot.Index] = item;
                    continue;
                }
                var physical = LegacyInventoryStorageCoordinates.ToStorage(slot);
                if (physical.List != InventoryContainerType.Main) continue;
                if (item is null) result.Remove(physical.Index);
                else result[physical.Index] = item;
            }
            foreach (var (index, item) in virtualItems)
                if (item is null) result.Remove(index); else result[index] = item;
            return new(System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary(result), await GoldAsync(ct).ConfigureAwait(false))
            { SharedMaterials = System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary(shared) };
        }
    }
}
