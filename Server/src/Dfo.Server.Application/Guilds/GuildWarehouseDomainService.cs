using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildWarehouseDomainService(
    IGuildDomainStore store, IGuildWarehouseStore warehouse, IGuildHallAccessDomainService halls,
    ICharacterBusinessInventoryDomainService inventory, IItemPlacementRuleService placements,
    IAccountMaterialRuleService accountMaterials, IMailItemRuleService tradeRules,
    IGuildWarehouseCustodyStore items, IDomainTransactionAccessor transactions, TimeProvider clock)
    : IGuildWarehouseDomainService
{
    public async ValueTask<GuildWarehouseView?> OpenAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        await MembershipInHallAsync(actor, ct).ConfigureAwait(false) is { } membership
            ? await ReadAsync(membership.Guild.Id, ct).ConfigureAwait(false) : null;

    public async ValueTask<GuildWarehouseView> ReadAsync(GuildId guild, CancellationToken ct)
    {
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        return new(capacity, capacity == 0 ? [] : await warehouse.ReadWarehouseItemsAsync(guild, ct).ConfigureAwait(false));
    }

    public async ValueTask<GuildWarehouseTransferResult> DepositAsync(SelectedSessionIdentity actor,
        BusinessSlot source, int warehouseSlot, int itemId, int count, CancellationToken ct)
    {
        if (await MembershipInHallAsync(actor, ct).ConfigureAwait(false) is not { } membership
            || !GuildRankRuleService.CanTransferWarehouseItems(membership.Member))
            return new(GuildWarehouseFailure.AccessDenied);
        var guild = membership.Guild.Id;
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        if (warehouseSlot < 0 || warehouseSlot >= capacity
            || !(IsCarriedSpace(source.Space) || source.Space == BusinessInventorySpace.SharedMaterial))
            return new(GuildWarehouseFailure.InvalidSlot);

        var bag = inventory.GetStore(actor.CharacterId, BusinessInventoryStoreKind.Item).GetContainer(source.Space);
        var held = await bag.GetAsync(source.Index, ct).ConfigureAwait(false);
        if (held is null || held.ItemId != itemId)
            return new(GuildWarehouseFailure.ItemChanged);
        var snapshot = held.Snapshot;
        if (count == 0 && !snapshot.IsPile) count = 1;
        if (count <= 0 || count > snapshot.Count)
            return new(GuildWarehouseFailure.InvalidCount);
        if (snapshot.IsMirror || snapshot.AvatarDetail is not null || snapshot.CreatureDetail is not null
            || !snapshot.Lifetime.IsPermanent || snapshot.EquipmentLockId != 0 || snapshot.SortLockFlag != 0
            || !(await tradeRules.CanSendAsync(snapshot, sameAccount: false, ct).ConfigureAwait(false)).Allowed)
            return new(GuildWarehouseFailure.Untransferable);

        var sourcePlacement = await placements.PlacementForAsync(itemId, source.Space, ct).ConfigureAwait(false);
        var placement = source.Space == BusinessInventorySpace.SharedMaterial
            ? await CarriedPlacementAsync(itemId, ct).ConfigureAwait(false) : sourcePlacement;
        if (placement is null || sourcePlacement is null || !sourcePlacement.Destination.Contains(source))
            return new(GuildWarehouseFailure.Untransferable);
        var custody = Custody();
        var existing = await custody.ReadSlotAsync(guild, warehouseSlot, ct).ConfigureAwait(false);
        if (existing is not null && !existing.CanStackWith(snapshot))
            return new(GuildWarehouseFailure.NoRoom);
        if (!Fits(placement, existing?.Count ?? 0, count))
            return new(GuildWarehouseFailure.NoRoom);

        var detached = await bag.TakeAsync(held, count, ct).ConfigureAwait(false);
        await custody.DepositAsync(guild, warehouseSlot, detached, existing, ct).ConfigureAwait(false);
        await warehouse.AppendWarehouseHistoryAsync(guild, actor.CharacterId, GuildWarehouseAction.Deposit,
            itemId, count, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return await SettledAsync(actor, guild, warehouseSlot, source, count, ct).ConfigureAwait(false);
    }

    public async ValueTask<GuildWarehouseTransferResult> WithdrawAsync(SelectedSessionIdentity actor, int warehouseSlot,
        int itemId, int count, CancellationToken ct)
    {
        if (await MembershipInHallAsync(actor, ct).ConfigureAwait(false) is not { } membership
            || !GuildRankRuleService.CanTransferWarehouseItems(membership.Member))
            return new(GuildWarehouseFailure.AccessDenied);
        var guild = membership.Guild.Id;
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        if (warehouseSlot < 0 || warehouseSlot >= capacity)
            return new(GuildWarehouseFailure.InvalidSlot);

        var custody = Custody();
        var held = await custody.ReadSlotAsync(guild, warehouseSlot, ct).ConfigureAwait(false);
        if (held is null || held.ItemId != itemId)
            return new(GuildWarehouseFailure.ItemChanged);
        if (count == 0 && !held.IsPile) count = 1;
        if (count <= 0 || count > held.Count)
            return new(GuildWarehouseFailure.InvalidCount);

        var placement = await placements.PlacementForAsync(itemId, ct).ConfigureAwait(false);
        if (placement is null || !(IsCarriedSpace(placement.Destination.Space)
            || placement.Destination.Space == BusinessInventorySpace.SharedMaterial))
            return new(GuildWarehouseFailure.Untransferable);
        var destination = placement.Destination;
        var bag = inventory.GetStore(actor.CharacterId, BusinessInventoryStoreKind.Item).GetContainer(destination.Space);
        var limit = Math.Min(destination.LastIndex + 1, await bag.OpenSlotCountAsync(ct).ConfigureAwait(false));
        int? emptySlot = null;
        int? mergeSlot = null;
        for (var index = destination.FirstIndex; index < limit; index++)
        {
            var candidate = await bag.GetAsync(index, ct).ConfigureAwait(false);
            if (candidate is null)
                emptySlot ??= index;
            else if (candidate.Snapshot.CanStackWith(held) && Fits(placement, candidate.Count, count))
            {
                mergeSlot = index;
                break;
            }
        }
        if ((mergeSlot ?? emptySlot) is not { } slot || !Fits(placement, 0, count))
            return new(GuildWarehouseFailure.NoRoom);

        var detached = await custody.WithdrawAsync(guild, warehouseSlot, count, ct).ConfigureAwait(false);
        await bag.PutAsync(slot, detached, ct).ConfigureAwait(false);
        await warehouse.AppendWarehouseHistoryAsync(guild, actor.CharacterId, GuildWarehouseAction.Withdraw,
            itemId, count, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return await SettledAsync(actor, guild, warehouseSlot, new(destination.Space, slot), count, ct).ConfigureAwait(false);
    }

    public async ValueTask<GuildWarehouseMoveResult> MoveAsync(SelectedSessionIdentity actor, int sourceSlot, int sourceItem,
        int targetSlot, int targetItem, CancellationToken ct)
    {
        if (await MembershipInHallAsync(actor, ct).ConfigureAwait(false) is not { } membership
            || !GuildRankRuleService.CanTransferWarehouseItems(membership.Member))
            return new(GuildWarehouseFailure.AccessDenied);
        var guild = membership.Guild.Id;
        var capacity = await warehouse.ReadWarehouseCapacityAsync(guild, ct).ConfigureAwait(false);
        if (sourceSlot < 0 || sourceSlot >= capacity || targetSlot < 0 || targetSlot >= capacity || sourceSlot == targetSlot)
            return new(GuildWarehouseFailure.InvalidSlot);

        var custody = Custody();
        var source = await custody.ReadSlotAsync(guild, sourceSlot, ct).ConfigureAwait(false);
        var target = await custody.ReadSlotAsync(guild, targetSlot, ct).ConfigureAwait(false);
        if (source is null || source.ItemId != sourceItem || (target?.ItemId ?? 0) != targetItem)
            return new(GuildWarehouseFailure.ItemChanged);

        var merge = target is not null && target.CanStackWith(source);
        if (merge && (await CarriedPlacementAsync(sourceItem, ct).ConfigureAwait(false) is not { } placement
            || !Fits(placement, target!.Count, source.Count)))
            return new(GuildWarehouseFailure.NoRoom);

        var moving = await custody.WithdrawAsync(guild, sourceSlot, source.Count, ct).ConfigureAwait(false);
        if (target is not null && !merge)
        {
            var swapped = await custody.WithdrawAsync(guild, targetSlot, target.Count, ct).ConfigureAwait(false);
            await custody.DepositAsync(guild, sourceSlot, swapped, null, ct).ConfigureAwait(false);
        }
        await custody.DepositAsync(guild, targetSlot, moving, merge ? target : null, ct).ConfigureAwait(false);
        await warehouse.AppendWarehouseHistoryAsync(guild, actor.CharacterId, GuildWarehouseAction.Move,
            sourceItem, source.Count, clock.GetUtcNow(), ct).ConfigureAwait(false);
        return new(GuildWarehouseFailure.None, await ReadAsync(guild, ct).ConfigureAwait(false),
            await OtherMembersAsync(actor, ct).ConfigureAwait(false));
    }

    public async ValueTask<IReadOnlyList<GuildWarehouseHistoryEntry>?> HistoryAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        await MembershipInHallAsync(actor, ct).ConfigureAwait(false) is { } membership
            ? await warehouse.ReadWarehouseHistoryAsync(membership.Guild.Id, 50, ct).ConfigureAwait(false)
            : null;

    private async ValueTask<GuildMembership?> MembershipInHallAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        (await halls.VisitAsync(actor, requiredArea: null, ct).ConfigureAwait(false))?.Membership;

    private static bool IsCarriedSpace(BusinessInventorySpace space) => space is
        BusinessInventorySpace.CarriedEquipment or BusinessInventorySpace.Consumable
        or BusinessInventorySpace.Material or BusinessInventorySpace.ExpertMaterial;

    private static bool Fits(ItemPlacement placement, int held, int added) =>
        (long)held + added <= (placement.StackLimit == 0 ? int.MaxValue : placement.StackLimit);

    private ValueTask<ItemPlacement?> CarriedPlacementAsync(int itemId, CancellationToken ct) =>
        accountMaterials.TryGetByItemId(itemId, out _)
            ? placements.PlacementForAsync(itemId, BusinessInventorySpace.Material, ct)
            : placements.PlacementForAsync(itemId, ct);

    private IGuildWarehouseCustodySession Custody() =>
        items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Guild warehouse access requires a transaction."));

    private async ValueTask<GuildWarehouseTransferResult> SettledAsync(SelectedSessionIdentity actor, GuildId guild,
        int warehouseSlot, BusinessSlot bagSlot, int count, CancellationToken ct)
    {
        var view = await ReadAsync(guild, ct).ConfigureAwait(false);
        return new(GuildWarehouseFailure.None, new(warehouseSlot, bagSlot, count, view,
            await OtherMembersAsync(actor, ct).ConfigureAwait(false)));
    }

    private async ValueTask<CharacterId[]> OtherMembersAsync(SelectedSessionIdentity actor, CancellationToken ct) =>
        [.. (await store.ReadRosterCharacterIdsAsync(actor.CharacterId, ct).ConfigureAwait(false))
            .Where(member => member != actor.CharacterId)];
}
