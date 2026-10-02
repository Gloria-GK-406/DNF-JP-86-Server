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
    IAccountMaterialBagDomainService accounts, IAccountMaterialRuleService accountRules,
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
        var state = new Standing(context, slots, accounts, characters, containers, premiums, accountTable, usage);
        try
        {
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case ConsumeFromContainerEffect consume:
                        var item = await state.ReadSlotAsync(consume.Slot, ct).ConfigureAwait(false);
                        if (consume.Count <= 0 || item is not { IsPile: true } || item.ItemId != consume.ExpectedItemId
                            || item.Core.EquipmentLockId != consume.ExpectedEquipmentLockId
                            || item.Core.ExpireTime is > 0 and < int.MaxValue && item.Core.ExpireTime <= context.At.ToUnixTimeSeconds())
                            Refuse(InventoryEffectRejection.InvalidTarget);
                        if (item!.Count < consume.Count) Refuse(InventoryEffectRejection.InsufficientAssets);
                        state.SetSlot(consume.Slot, item.Count == consume.Count ? null : item with
                        { Core = item.Core with { Value = item.Count - consume.Count } });
                        break;
                    case ConsumeAccountMaterialEffect consume:
                        CheckAccount(consume.Kind, consume.ExpectedItemId, consume.Count);
                        var current = await state.AccountCountAsync(consume.Kind, ct).ConfigureAwait(false);
                        if (current < consume.Count) Refuse(InventoryEffectRejection.InsufficientAssets);
                        state.SetAccountCount(consume.Kind, current - consume.Count);
                        break;
                    case GrantAccountMaterialEffect grant:
                        CheckAccount(grant.Kind, grant.ExpectedItemId, grant.Count);
                        var count = (long)await state.AccountCountAsync(grant.Kind, ct).ConfigureAwait(false) + grant.Count;
                        if (count > int.MaxValue) Refuse(InventoryEffectRejection.NoRoom);
                        state.SetAccountCount(grant.Kind, (int)count);
                        state.AddedAccountReward(grant.Kind);
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
                        var instance = await equipment.Of(context.CharacterId).FindAsync(change.Equipment.Identity.Value, ct)
                            .ConfigureAwait(false);
                        if (instance is null) Refuse(InventoryEffectRejection.InvalidTarget);
                        var facts = await instance!.ReadAsync(ct).ConfigureAwait(false);
                        var standing = await state.ReadSlotAsync(facts.Slot, ct).ConfigureAwait(false);
                        if (standing?.Identity != change.Equipment.Identity || standing.ItemId != change.Equipment.ExpectedItemId)
                            Refuse(InventoryEffectRejection.InvalidTarget);
                        if (change is DestroyEquipmentEffect) state.SetSlot(facts.Slot, null);
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
            return new(InventoryEffectRejection.None, state.PhysicalRewardEntries, state.AccountRewardKinds,
                includeHoldings ? await state.HoldingsAsync(ct).ConfigureAwait(false) : null);
        }
        catch (InventoryEffectException exception) { return new(exception.Rejection); }
    }

    private void CheckAccount(AccountMaterialKind kind, int itemId, int count)
    {
        if (count <= 0 || !accountRules.TryGetByItemId(itemId, out var definition) || definition.Kind != kind)
            Refuse(InventoryEffectRejection.InvalidTarget);
    }

    private async ValueTask GrantAsync(EffectExecutionContext context, GrantStackableEffect grant, Standing state, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(grant.ItemId, ct).ConfigureAwait(false);
        if (grant.Count <= 0 || accountRules.TryGetByItemId(grant.ItemId, out _)
            || placement is not { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
            Refuse(InventoryEffectRejection.UnsupportedItem);
        await GrantBasicAsync(grant.ItemId, grant.Count, placement!, state, ct).ConfigureAwait(false);
    }
    private async ValueTask GrantBasicAsync(int itemId, int count, ItemPlacement placement, Standing state, CancellationToken ct)
    {
        if (count <= 0 || itemId <= 0 || placement.ItemKind == 0 || accountRules.TryGetByItemId(itemId, out _))
            Refuse(InventoryEffectRejection.UnsupportedItem);
        placement = InventoryCapacityPlacement.Limit(placement,
            await state.OpenSlotCountAsync(placement.Destination.Space, ct).ConfigureAwait(false))
            ?? throw new InventoryEffectException(InventoryEffectRejection.NoRoom);
        var available = Enumerable.Range(placement.Destination.FirstIndex,
            placement.Destination.LastIndex - placement.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(placement.Destination.Space, index));
        if (ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
            available = available.Concat(Enumerable.Range(0, 6).Select(index => new BusinessSlot(BusinessInventorySpace.QuickSlot, index)));
        var candidates = available.ToArray();
        foreach (var slot in candidates) _ = await state.ReadSlotAsync(slot, ct).ConfigureAwait(false);
        var remaining = count;
        while (remaining > 0)
        {
            var arrival = arrivals.ForGrant(candidates.Select(slot => (Slot: slot, Item: state.At(slot)))
                .Where(pair => pair.Item is { IsPile: true } && pair.Item.ItemId == itemId)
                .Select(pair => new ItemPile(pair.Slot, pair.Item!.Count)).ToArray(), remaining, placement,
                candidates.Where(slot => state.At(slot) is not null).ToHashSet());
            BusinessSlot target;
            int added;
            InventoryItemCoreState core;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile pile:
                    target = pile.Slot; added = pile.Added; core = state.At(target)!.Core with { Value = pile.Total }; break;
                case ItemArrival.IntoEmptySlot empty:
                    target = empty.Slot; added = empty.Count; core = StackableItemCore.Create(placement.ItemKind, itemId,
                        placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature ? 1 : added); break;
                default: Refuse(InventoryEffectRejection.NoRoom); return;
            }
            if (added <= 0 || added > remaining) throw new InvalidDataException("Invalid effect arrival progress.");
            state.SetSlot(target, new(target.Index, core, placement.WorldClass));
            state.AddedPhysicalRewardEntries(1);
            remaining -= added;
        }
    }

    private static void Refuse(InventoryEffectRejection reason) => throw new InventoryEffectException(reason);

    private sealed class Standing(EffectExecutionContext context, IBusinessInventorySlotDomainService slots,
        IAccountMaterialBagDomainService accounts, ICharacterDomainService characters,
        ICharacterContainerDomainService? containers, IPremiumServiceDomainService? premiums,
        IAccountTable? accountTable, IPremiumServiceUsageDomainService? usage) : IInventoryEffectPreviewState
    {
        private readonly Dictionary<BusinessSlot, ContainerSlotItem?> items = [];
        private readonly HashSet<BusinessInventorySpace> loaded = [];
        private readonly Dictionary<AccountMaterialKind, int> balances = [];
        private readonly HashSet<AccountMaterialKind> rewardKinds = [];
        private readonly Dictionary<int, ContainerSlotItem?> virtualItems = [];
        private readonly Dictionary<PremiumServiceType, long> expiries = [];
        private int? luck;
        private DoublePotCounterState? doublePot;
        private int? gold;
        public int PhysicalRewardEntries { get; private set; }
        public int AccountRewardKinds => rewardKinds.Count;
        public ContainerSlotItem? At(BusinessSlot slot) => items.GetValueOrDefault(slot);
        public async ValueTask<ContainerSlotItem?> ReadSlotAsync(BusinessSlot slot, CancellationToken ct)
        {
            if (loaded.Add(slot.Space))
            {
                var bound = slots.Find(context.CharacterId, slot)
                    ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
                foreach (var item in await bound.Container.ReadAllAsync(ct).ConfigureAwait(false))
                    items[new(slot.Space, item.SlotIndex)] = item;
            }
            return At(slot);
        }
        public void SetSlot(BusinessSlot slot, ContainerSlotItem? item) => items[slot] = item;
        public async ValueTask<int> AccountCountAsync(AccountMaterialKind kind, CancellationToken ct)
        {
            if (!balances.TryGetValue(kind, out var current))
                balances[kind] = current = (await accounts.Get(context.AccountId).SnapshotAsync(ct).ConfigureAwait(false)).Count(kind);
            return current;
        }
        public void SetAccountCount(AccountMaterialKind kind, int count) => balances[kind] = count;
        public async ValueTask<int> GoldAsync(CancellationToken ct) =>
            gold ??= await characters.Get(context.CharacterId).GoldAsync(ct).ConfigureAwait(false);
        public void SetGold(int amount) => gold = amount;
        public async ValueTask<ContainerSlotItem?> VirtualSlotAsync(int index, CancellationToken ct)
        {
            if (index is < 0 or > 2) Refuse(InventoryEffectRejection.InvalidTarget);
            if (!virtualItems.TryGetValue(index, out var item))
            {
                var main = containers!.FindContainer(context.CharacterId, InventoryContainerType.Main)
                    ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
                virtualItems[index] = item = await main.ReadSlotAsync(index, ct).ConfigureAwait(false);
            }
            return item;
        }
        public void SetVirtualSlot(int index, ContainerSlotItem? item) => virtualItems[index] = item;
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
        public void AddedPhysicalRewardEntries(int count) => PhysicalRewardEntries += count;
        public void AddedAccountReward(AccountMaterialKind kind) => rewardKinds.Add(kind);
        public async ValueTask<InventoryEffectHoldingsSnapshot> HoldingsAsync(CancellationToken ct)
        {
            var main = containers?.FindContainer(context.CharacterId, InventoryContainerType.Main)
                ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
            var result = (await main.ReadAllAsync(ct).ConfigureAwait(false)).ToDictionary(x => x.SlotIndex);
            foreach (var (slot, item) in items)
            {
                var physical = LegacyInventoryStorageCoordinates.ToStorage(slot);
                if (physical.List != InventoryContainerType.Main) continue;
                if (item is null) result.Remove(physical.Index);
                else result[physical.Index] = item with { SlotIndex = physical.Index };
            }
            foreach (var (index, item) in virtualItems)
                if (item is null) result.Remove(index); else result[index] = item;
            return new([.. result.Values.OrderBy(x => x.SlotIndex)], await GoldAsync(ct).ConfigureAwait(false));
        }
    }
}
