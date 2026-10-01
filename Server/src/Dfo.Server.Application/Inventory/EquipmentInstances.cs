using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
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
        return new EquipmentCollection(new(owner, inventories, transactions, transaction));
    }
}

internal sealed class EquipmentInstanceContext(CharacterId owner, ICharacterBusinessInventoryDomainService inventories,
    IDomainTransactionAccessor transactions, IDomainTransaction transaction)
{
    private readonly EquipmentDestructionState destruction = transaction.Enlist(
        typeof(EquipmentDestructionState), () => new EquipmentDestructionState());
    private static readonly BusinessInventorySpace[] Spaces =
    [
        BusinessInventorySpace.QuickSlot, BusinessInventorySpace.CarriedEquipment,
        BusinessInventorySpace.Consumable, BusinessInventorySpace.Material,
        BusinessInventorySpace.Quest, BusinessInventorySpace.ExpertMaterial,
        BusinessInventorySpace.AvatarEmblem, BusinessInventorySpace.WornAvatar,
        BusinessInventorySpace.WornCreature,
        BusinessInventorySpace.WornEquipment, BusinessInventorySpace.WornEquipmentExtension,
        BusinessInventorySpace.PersonalCargo, BusinessInventorySpace.CreatureEquipment,
        BusinessInventorySpace.WornCreatureEquipment,
    ];

    public CharacterId Owner => owner;

    public void EnsureActive()
    {
        if (!ReferenceEquals(transactions.Current, transaction))
            throw new InvalidOperationException("Obtain a new equipment instance for the current transaction.");
    }

    public async ValueTask<EquipmentLocation?> AtAsync(BusinessSlot slot, CancellationToken ct)
    {
        EnsureActive();
        ct.ThrowIfCancellationRequested();
        if (!Spaces.Contains(slot.Space)) return null;
        _ = LegacyInventoryStorageCoordinates.ToStorage(slot);
        var container = Container(slot.Space);
        var item = container is null ? null : await container.ReadSlotAsync(slot.Index, ct).ConfigureAwait(false);
        EnsureActive();
        return item is { WorldClass: ItemWorldClass.Equipment, Identity: { Value: > 0 } identity }
            && !destruction.Destroyed.Contains(identity.Value)
            ? new(container!, slot, item) : null;
    }

    public async ValueTask<EquipmentLocation?> LocateAsync(int uid, CancellationToken ct)
    {
        EnsureActive();
        ct.ThrowIfCancellationRequested();
        if (uid <= 0 || destruction.Destroyed.Contains(uid)) return null;
        foreach (var space in Spaces)
        {
            var container = Container(space);
            if (container is null) continue;
            foreach (var item in await container.ReadAllAsync(ct).ConfigureAwait(false))
                if (item is { WorldClass: ItemWorldClass.Equipment } && item.Identity?.Value == uid)
                {
                    EnsureActive();
                    return new(container, new(space, item.SlotIndex), item);
                }
        }
        EnsureActive();
        return null;
    }

    public async ValueTask<EquipmentLocation> RequireAsync(int uid, CancellationToken ct) =>
        await LocateAsync(uid, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Equipment {uid} is no longer owned by character {owner.Value}.");

    private IBusinessItemContainer? Container(BusinessInventorySpace space) =>
        inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);

    public void MarkDestroyed(int uid) => destruction.Destroyed.Add(uid);

    private sealed class EquipmentDestructionState : ITransactionParticipant
    {
        public HashSet<int> Destroyed { get; } = [];
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}

internal sealed record EquipmentLocation(IBusinessItemContainer Container, BusinessSlot Slot, ContainerSlotItem Item);

internal sealed class EquipmentCollection(EquipmentInstanceContext context) : IEquipmentCollection
{
    public async ValueTask<IEquipmentInstance?> FindAsync(int uid, CancellationToken ct) =>
        await context.LocateAsync(uid, ct).ConfigureAwait(false) is null
            ? null : new EquipmentInstance(uid, context);

    public async ValueTask<IEquipmentInstance?> FindAtAsync(BusinessSlot slot, CancellationToken ct)
    {
        var location = await context.AtAsync(slot, ct).ConfigureAwait(false);
        return location is null ? null : new EquipmentInstance(location.Item.Identity!.Value.Value, context);
    }
}

internal sealed class EquipmentInstance(int uid, EquipmentInstanceContext context) : IEquipmentInstance
{
    public CharacterId Owner => context.Owner;
    public int ItemUid => uid;

    public async ValueTask<EquipmentInstanceFacts> ReadAsync(CancellationToken ct) =>
        Facts(await context.RequireAsync(uid, ct).ConfigureAwait(false));

    public ValueTask<EquipmentInstanceFacts> SetReinforcementLevelAsync(byte level, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, (byte)31);
        return ChangeAsync(core => core with { Attribute = (byte)((core.Attribute & 0xE0) | level) }, ct);
    }

    public ValueTask<EquipmentInstanceFacts> SetEnchantmentAsync(int cardId, CancellationToken ct)
    {
        if (cardId < 0) throw new ArgumentOutOfRangeException(nameof(cardId));
        return ChangeAsync(core => core with { EnchantCardId = cardId }, ct);
    }

    public ValueTask<EquipmentInstanceFacts> SetForgingLevelAsync(byte level, CancellationToken ct) =>
        ChangeAsync(core => core with { GenuineUpgrade = level }, ct);

    public ValueTask<EquipmentInstanceFacts> SetDimensionalAttributeAsync(byte type, ushort value, CancellationToken ct) =>
        ChangeAsync(core => core with { AmplifyType = type, AmplifyValue = value }, ct);

    public ValueTask<EquipmentInstanceFacts> SetMagicSealOptionsAsync(IReadOnlyList<RandomOptionState> options,
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

    public ValueTask<EquipmentInstanceFacts> SetDurabilityAsync(ushort value, CancellationToken ct) =>
        ChangeAsync(core => core with { Durability = value }, ct);

    public ValueTask<EquipmentInstanceFacts> DecreaseDurabilityAsync(ushort amount, CancellationToken ct)
    {
        if (amount == 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return ChangeAsync(core => core with { Durability = (ushort)Math.Max(0, core.Durability - amount) }, ct);
    }

    public async ValueTask<EquipmentInstanceFacts> DestroyAsync(CancellationToken ct)
    {
        var location = await context.RequireAsync(uid, ct).ConfigureAwait(false);
        await location.Container.ClearAsync(location.Slot.Index, ct).ConfigureAwait(false);
        context.MarkDestroyed(uid);
        return Facts(location);
    }

    private async ValueTask<EquipmentInstanceFacts> ChangeAsync(
        Func<InventoryItemCoreState, InventoryItemCoreState> change, CancellationToken ct)
    {
        var location = await context.RequireAsync(uid, ct).ConfigureAwait(false);
        var core = change(location.Item.Core);
        if (core != location.Item.Core)
            await location.Container.PutAsync(location.Slot.Index, core, ct).ConfigureAwait(false);
        return new(Owner, uid, location.Slot, core);
    }

    private EquipmentInstanceFacts Facts(EquipmentLocation location) =>
        new(Owner, uid, location.Slot, location.Item.Core);
}
