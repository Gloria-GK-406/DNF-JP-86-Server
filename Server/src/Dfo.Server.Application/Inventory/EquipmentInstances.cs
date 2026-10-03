using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentDomainService(ICharacterBusinessInventoryDomainService inventories,
    IDomainTransactionAccessor transactions) : IEquipmentDomainService
{
    public IEquipmentCollection Of(CharacterId owner)
    {
        if (owner.Value <= 0) throw new ArgumentOutOfRangeException(nameof(owner));
        var transaction = transactions.Current
            ?? throw new InvalidOperationException("Equipment instances require an open domain transaction.");
        return new EquipmentCollection(owner, inventories, transactions, transaction);
    }
}

internal sealed class EquipmentCollection(CharacterId owner, ICharacterBusinessInventoryDomainService inventories,
    IDomainTransactionAccessor transactions, IDomainTransaction transaction) : IEquipmentCollection
{
    private void EnsureActive()
    {
        if (!ReferenceEquals(transactions.Current, transaction))
            throw new InvalidOperationException("Obtain a new equipment collection for the current transaction.");
    }
    public async ValueTask<IEquipmentInstance?> FindAsync(int uid, CancellationToken ct)
    {
        EnsureActive();
        if (uid <= 0) return null;
        foreach (var space in Enum.GetValues<BusinessInventorySpace>())
        {
            if (space is BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial
                || ItemContainerAdmission.AcceptedBy(space) == ItemTraits.None) continue;
            var container = inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
            if (container is null) continue;
            foreach (var item in await container.GetAllAsync(ct).ConfigureAwait(false))
                if (!item.IsPile && item.Type.HasFlag(ItemTraits.Equipment) && item.ItemUid?.Value == uid)
                    return await container.GetEquipmentAsync(item.SlotIndex, ct).ConfigureAwait(false);
        }
        return null;
    }
    public ValueTask<IEquipmentInstance?> FindAtAsync(BusinessSlot slot, CancellationToken ct)
    {
        EnsureActive();
        if (slot.Space is BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial
            || ItemContainerAdmission.AcceptedBy(slot.Space) == ItemTraits.None)
            return ValueTask.FromResult<IEquipmentInstance?>(null);
        var container = inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(slot.Space)).FindContainer(slot.Space);
        return container is null ? ValueTask.FromResult<IEquipmentInstance?>(null) : container.GetEquipmentAsync(slot.Index, ct);
    }
}

internal sealed class EquipmentInstance(IBusinessItemContainer container, ItemReference reference,
    IItemLifecycleDomainService lifecycle) : IEquipmentInstance
{
    public ItemReference Reference => reference;
    public int ItemUid => reference.ItemUid!.Value.Value;
    public async ValueTask<ItemReference> ReadAsync(CancellationToken ct)
    {
        var current = await container.GetAsync(reference.SlotIndex, ct).ConfigureAwait(false);
        if (current is null || !reference.SameOccupancyAs(current)
            || current.IsPile || !current.Type.HasFlag(ItemTraits.Equipment))
            throw new InvalidOperationException("Equipment reference no longer occupies its original slot.");
        return current;
    }
    public ValueTask<ItemReference> SetReinforcementLevelAsync(byte level, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, (byte)31);
        return ChangeAsync(core => core with { Attribute = (byte)((core.Attribute & 0xE0) | level) }, ct);
    }

    public ValueTask<ItemReference> SetEnchantmentAsync(int cardId, CancellationToken ct)
    {
        if (cardId < 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        return ChangeAsync(core => core with { EnchantCardId = cardId }, ct);
    }

    public ValueTask<ItemReference> SetForgingLevelAsync(byte level, CancellationToken ct) =>
        ChangeAsync(core => core with { GenuineUpgrade = level }, ct);

    public ValueTask<ItemReference> SetDimensionalAttributeAsync(byte type, ushort value, CancellationToken ct) =>
        ChangeAsync(core => core with { AmplifyType = type, AmplifyValue = value }, ct);

    public ValueTask<ItemReference> SetMagicSealOptionsAsync(IReadOnlyList<RandomOptionState> options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count is < 1 or > 3 || options.Any(option => option is null))
            throw new ArgumentException("An equipment piece has one to three magic-seal options.", nameof(options));
        var copy = options.ToArray();
        return ChangeAsync(core => core with
        {
            RandomOption0 = copy[0],
            RandomOption1 = copy.Length > 1 ? copy[1] : new(0, 0, 0),
            RandomOption2 = copy.Length > 2 ? copy[2] : new(0, 0, 0),
            RandomOptionState = 0,
            RandomOptionChangedIndex = byte.MaxValue,
            RandomOptionChangeState = 0,
            RandomOptionChange = new(0, 0, 0),
        }, ct);
    }

    public ValueTask<ItemReference> SetDurabilityAsync(ushort value, CancellationToken ct) =>
        ChangeAsync(core => core with { Durability = value }, ct);

    public async ValueTask<ItemReference> DecreaseDurabilityAsync(ushort amount, CancellationToken ct)
    {
        if (amount == 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var current = await ReadAsync(ct).ConfigureAwait(false);
        if (current.Snapshot.Durability == 0) return current;
        return await container.UpdateAsync(reference,
            core => core with { Durability = (ushort)Math.Max(0, core.Durability - amount) }, ct).ConfigureAwait(false);
    }

    public async ValueTask<ItemReference> DestroyAsync(CancellationToken ct)
    {
        var before = await ReadAsync(ct).ConfigureAwait(false);
        await lifecycle.DeleteItemAsync(await container.TakeAsync(reference, null, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return before;
    }
    private async ValueTask<ItemReference> ChangeAsync(Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
    {
        _ = await ReadAsync(ct).ConfigureAwait(false);
        return await container.UpdateAsync(reference, change, ct).ConfigureAwait(false);
    }
}
