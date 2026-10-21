using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Application.Inventory;

internal sealed class InventoryExpansionDomainService(ISelectedSessionDomainService sessions,
    ICharacterTable owners, IDungeonRunDomainService runs,
    ICharacterDomainService characters, ICharacterCarriedGoldTable goldRows, ICarriedGoldRuleService goldRules,
    IAccountCargoStateTable accountStates,
    IInventoryExpansionStateTable writes, IInventoryLevelDomainService levels,
    IInventoryExpansionRuleService rules, IBusinessInventorySlotDomainService slots,
    ICharacterBusinessInventoryDomainService inventories, IItemPlacementRuleService placements,
    IDomainTransactionAccessor transactions, IItemDomainStore items,
    ICharacterInventoryEntryDomainService characterInventory, IAccountInventoryEntryDomainService accountInventory,
    IItemLifecycleDomainService lifecycle, IIllusionTowerDomainService? towers = null)
    : IInventoryExpansionDomainService
{
    public async ValueTask<InventoryExpansionResult> UseItemAsync(SelectedSessionIdentity session, BusinessSlot slot,
        InventoryExpansionKind kind, DateTimeOffset at, CancellationToken ct)
    {
        RequireTransaction();
        if (!await CurrentAsync(session, ct).ConfigureAwait(false)) return new(InventoryExpansionRejection.NoSession);
        if (!slot.Space.IsCarriedItemSpace()) return new(InventoryExpansionRejection.InvalidRequest);
        var bound = slots.Find(session.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
        if (item is not { IsPile: true, Count: > 0 } || item.Snapshot.EquipmentLockId != 0
            || item.Snapshot.ExpireTime > 0 && item.Snapshot.ExpireTime <= at.ToUnixTimeSeconds())
            return new(InventoryExpansionRejection.InvalidRequest);
        var grant = await rules.ItemAsync(item.ItemId, ct).ConfigureAwait(false);
        if (grant is null || grant.Kind != kind) return new(InventoryExpansionRejection.InvalidRequest);
        var current = await ParameterAsync(session, kind, ct).ConfigureAwait(false);
        if (!rules.CanUpgrade(grant, current, await rules.AccountCargoAsync(ct).ConfigureAwait(false), false))
            return new(InventoryExpansionRejection.InvalidStage);
        await lifecycle.DeleteItemAsync(await bound!.Container.TakeAsync(item, 1, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        await ChangeAsync(session, new(kind, current, grant.Parameter), ct).ConfigureAwait(false);
        return new(InventoryExpansionRejection.None, await SnapshotAsync(session, at, ct).ConfigureAwait(false));
    }

    public async ValueTask<InventoryExpansionResult> UpgradeAccountAsync(SelectedSessionIdentity session, bool create,
        DateTimeOffset at, CancellationToken ct)
    {
        RequireTransaction();
        if (!await CurrentAsync(session, ct).ConfigureAwait(false)) return new(InventoryExpansionRejection.NoSession);
        var definition = await rules.AccountCargoAsync(ct).ConfigureAwait(false);
        var current = await ParameterAsync(session, InventoryExpansionKind.AccountCargo, ct).ConfigureAwait(false);
        var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        var next = rules.NextTier(definition, current);
        if (progress is null || progress.Level < definition.RequiredLevel || create != (current == 0)
            || next is null || next.ProductId > 0)
            return new(InventoryExpansionRejection.InvalidStage);
        var gold = await characters.Get(session.CharacterId).GoldAsync(ct).ConfigureAwait(false);
        if (gold < next.GoldCost) return new(InventoryExpansionRejection.InsufficientAssets);
        var costs = new List<(IBusinessItemContainer Bag, ItemReference Item, int Count)>();
        var remaining = next.MaterialCount;
        if (remaining > 0)
        {
            var placement = await placements.PlacementForAsync(next.MaterialId, ct).ConfigureAwait(false);
            if (placement is null) return new(InventoryExpansionRejection.InvalidRequest);
            var spaces = new[] { placement.Destination.Space, BusinessInventorySpace.QuickSlot }.Distinct();
            foreach (var space in spaces)
            {
                var bag = inventories.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
                if (bag is null) continue;
                foreach (var item in (await bag.GetAllAsync(ct).ConfigureAwait(false)).OrderBy(i => i.SlotIndex))
                {
                    if (item.ItemId != next.MaterialId || !item.IsPile || item.Count <= 0 || item.Snapshot.EquipmentLockId != 0
                        || item.Snapshot.ExpireTime > 0 && item.Snapshot.ExpireTime <= at.ToUnixTimeSeconds()) continue;
                    var take = Math.Min(remaining, item.Count);
                    if (take > 0) costs.Add((bag, item, take));
                    remaining -= take;
                }
            }
            if (remaining > 0) return new(InventoryExpansionRejection.InsufficientAssets);
        }
        if (next.GoldCost > 0 && await characters.Get(session.CharacterId).TrySpendGoldAsync(next.GoldCost, ct).ConfigureAwait(false) is null)
            throw new InventoryExpansionException(InventoryExpansionRejection.StateChanged);
        foreach (var cost in costs)
            await lifecycle.DeleteItemAsync(await cost.Bag.TakeAsync(cost.Item, cost.Count, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        await ChangeAsync(session, new(InventoryExpansionKind.AccountCargo, current, next.Capacity), ct).ConfigureAwait(false);
        return new(InventoryExpansionRejection.None, await SnapshotAsync(session, at, ct).ConfigureAwait(false));
    }

    public async ValueTask<InventoryExpansionResult> TransferGoldAsync(SelectedSessionIdentity session, bool deposit,
        int amount, DateTimeOffset at, CancellationToken ct)
    {
        RequireTransaction();
        if (!await CurrentAsync(session, ct).ConfigureAwait(false)) return new(InventoryExpansionRejection.NoSession);
        if (amount <= 0) return new(InventoryExpansionRejection.InvalidRequest);
        var state = (await accountStates.ReadByAccountAsync(session.AccountId, ct).ConfigureAwait(false)).SingleOrDefault();
        var definition = await rules.AccountCargoAsync(ct).ConfigureAwait(false);
        var tier = definition.Tiers.SingleOrDefault(t => t.Capacity == state?.SelectionKey);
        var carried = await goldRows.FindAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (tier is null || state is null || carried is null || state.Value32 < 0 || carried.Carried < 0)
            return new(InventoryExpansionRejection.InvalidStage);
        var cargoAfter = (long)state.Value32 + (deposit ? amount : -amount);
        var goldAfter = (long)carried.Carried + (deposit ? -amount : amount);
        if (cargoAfter < 0 || goldAfter < 0) return new(InventoryExpansionRejection.InsufficientAssets);
        if (deposit && cargoAfter > tier.GoldLimit || !deposit && goldAfter > goldRules.CarryLimitFor(carried.Level, carried.StoredCarryLimit))
            return new(InventoryExpansionRejection.GoldLimit);
        if (!await writes.ChangeAccountGoldAsync(session.AccountId, state.SelectionKey, state.Value32, checked((int)cargoAfter), ct).ConfigureAwait(false))
            throw new InventoryExpansionException(InventoryExpansionRejection.StateChanged);
        var character = characters.Get(session.CharacterId);
        var actual = deposit ? await character.TrySpendGoldAsync(amount, ct).ConfigureAwait(false)
            : await character.AddGoldAsync(amount, ct).ConfigureAwait(false);
        if (actual != goldAfter) throw new InventoryExpansionException(InventoryExpansionRejection.StateChanged);
        return new(InventoryExpansionRejection.None, await SnapshotAsync(session, at, ct).ConfigureAwait(false));
    }

    public async ValueTask<IReadOnlyList<InventoryExpansionChange>?> PlanPurchaseAsync(SelectedSessionIdentity session,
        IReadOnlyList<InventoryExpansionGrant> grants, CancellationToken ct)
    {
        if (!await CurrentAsync(session, ct).ConfigureAwait(false)) return null;
        var definition = await rules.AccountCargoAsync(ct).ConfigureAwait(false);
        var standing = new Dictionary<InventoryExpansionKind, int>();
        var changes = new List<InventoryExpansionChange>();
        foreach (var grant in grants)
        {
            if (!standing.TryGetValue(grant.Kind, out var parameter))
                parameter = await ParameterAsync(session, grant.Kind, ct).ConfigureAwait(false);
            if (!rules.CanUpgrade(grant, parameter, definition, true)) return null;
            if (grant.Kind == InventoryExpansionKind.AccountCargo)
            {
                var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
                if (progress is null || progress.Level < definition.RequiredLevel) return null;
            }
            changes.Add(new(grant.Kind, parameter, grant.Parameter));
            standing[grant.Kind] = grant.Parameter;
        }
        return changes;
    }
    public async ValueTask ApplyPurchaseAsync(SelectedSessionIdentity session, IReadOnlyList<InventoryExpansionChange> changes, CancellationToken ct)
    {
        RequireTransaction();
        foreach (var change in changes) await ChangeAsync(session, change, ct).ConfigureAwait(false);
    }
    public async ValueTask<InventoryExpansionReceipt> SnapshotAsync(SelectedSessionIdentity session, DateTimeOffset at, CancellationToken ct)
    {
        await items.SessionIn(RequireTransaction()).ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        return new(session, await characterInventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false),
            await accountInventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false), at);
    }
    private async ValueTask<bool> CurrentAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        sessions.IsInWorld(session) && runs.Find(session.CharacterId) is null
        && towers?.IsInTower(session.CharacterId) != true
        && await owners.BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false);
    private ValueTask<int> ParameterAsync(SelectedSessionIdentity session, InventoryExpansionKind kind, CancellationToken ct) =>
        levels.GetLevelAsync(Container(session, kind), ct);
    private ValueTask ChangeAsync(SelectedSessionIdentity session, InventoryExpansionChange change, CancellationToken ct) =>
        levels.SetLevelAsync(Container(session, change.Kind), change.Parameter, ct, change.ExpectedParameter);
    private static ContainerId Container(SelectedSessionIdentity session, InventoryExpansionKind kind) =>
        kind == InventoryExpansionKind.AccountCargo ? ContainerId.ForAccountCargo(session.AccountId)
            : ContainerId.ForCharacter(session.CharacterId, Type(kind));
    private IDomainTransaction RequireTransaction() => transactions.Current ?? throw new InvalidOperationException("Inventory expansion requires a transaction.");
    private static InventoryContainerType Type(InventoryExpansionKind kind) => kind switch
    {
        InventoryExpansionKind.PersonalCargo => InventoryContainerType.PersonalCargo,
        InventoryExpansionKind.Main => InventoryContainerType.Main,
        InventoryExpansionKind.Avatar => InventoryContainerType.Avatar,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
