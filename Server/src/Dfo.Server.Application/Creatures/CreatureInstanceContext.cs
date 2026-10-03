using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

/// <summary>One transaction's item reference and clock boundary.</summary>
internal sealed class CreatureInstanceContext(CharacterId owner, IDomainTransaction transaction,
    ICreatureDomainStoreSession states, IBusinessInventoryStore? inventory,
    ICreatureRuleService rules, TimeProvider clock, ICreatureInstanceRuntime? runtime = null)
{
    private readonly Lifetime lifetime = transaction.Enlist(new object(), () => new Lifetime());
    public CharacterId Owner => owner;
    public ICreatureRuleService Rules => rules;
    public DateTimeOffset Now => clock.GetUtcNow();
    public IBusinessInventoryStore? Inventory => inventory;
    public void EnsureActive()
    {
        if (lifetime.Settled) throw new InvalidOperationException("Obtain a new creature instance for the next transaction.");
        runtime?.EnsureActive();
    }
    public async ValueTask SynchronizeAsync(CancellationToken ct)
    {
        EnsureActive(); ct.ThrowIfCancellationRequested();
        if (runtime is not null) await runtime.SynchronizeAsync(ct).ConfigureAwait(false);
    }
    public async ValueTask<CreatureState> ReadAsync(ItemReference reference, CancellationToken ct)
    {
        EnsureActive();
        var current = await states.ReadItemAsync(reference, ct).ConfigureAwait(false);
        var state = CreatureState.FromItem(current.Snapshot);
        var stomach = runtime?.Stomach(state.ItemUid, state.Stomach) ?? state.ExactStomach;
        return state with { Stomach = (int)stomach, ExactStomach = stomach };
    }
    public async ValueTask<int?> CurrentUidAsync(CancellationToken ct)
    {
        EnsureActive();
        return (await states.ReadCurrentAsync(owner, ct).ConfigureAwait(false))?.ItemUid;
    }
    public async ValueTask<ItemReference> StageAsync(ItemReference reference, CreatureState state,
        CancellationToken ct, bool restartClock = false, int? expiration = null)
    {
        EnsureActive();
        var updated = await states.WriteAsync(reference, state, expiration, ct).ConfigureAwait(false);
        if (restartClock) runtime?.ResetStomach(state.ItemUid);
        else runtime?.SetStomach(state.ItemUid, state.ExactStomach);
        return updated;
    }
    public void Notify(CreatureRuntimeChange change) => runtime?.Notify(change);

    public async ValueTask<ItemReference?> FindAsync(int uid, CancellationToken ct)
    {
        EnsureActive();
        return await states.FindItemAsync(owner, uid, ct).ConfigureAwait(false);
    }
    public async ValueTask<CreatureLocation?> LocateAsync(ItemReference reference, CancellationToken ct)
    {
        EnsureActive();
        var item = await states.ReadItemAsync(reference, ct).ConfigureAwait(false);
        if (!item.ContainerId.TryGetCharacterOwner(out var character, out var type) || character != owner) return null;
        var slot = type switch
        {
            InventoryContainerType.Equipment when item.StorageSlotIndex == 25 => new BusinessSlot(BusinessInventorySpace.WornCreature, 0),
            InventoryContainerType.Pet when item.StorageSlotIndex is >= 0 and <= 139 => new BusinessSlot(BusinessInventorySpace.CreatureInventory, item.StorageSlotIndex),
            _ => (BusinessSlot?)null
        };
        return slot is { } value ? new(item, value) : null;
    }
    public bool IsLive(CreatureLocation location, CreatureState state) =>
        location.Item.ItemId == state.ItemTemplateId
        && (location.Item.Snapshot.ExpireTime is <= 0 or int.MaxValue || location.Item.Snapshot.ExpireTime > Now.ToUnixTimeSeconds());
    private sealed class Lifetime : ITransactionParticipant
    {
        public bool Settled { get; private set; }
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
    }
}
internal sealed record CreatureLocation(ItemReference Item, BusinessSlot Slot);
internal interface ICreatureInstanceRuntime
{
    void EnsureActive();
    ValueTask SynchronizeAsync(CancellationToken ct);
    double Stomach(int uid, int baseline);
    void SetStomach(int uid, double value);
    void ResetStomach(int uid);
    void Notify(CreatureRuntimeChange change);
}
internal sealed class CreatureCollection(Func<CreatureInstanceContext> context) : ICreatureCollection
{
    public async ValueTask<IPetInstance?> FindPetAsync(int uid, CancellationToken ct)
    {
        var scope = context();
        var item = await scope.FindAsync(uid, ct).ConfigureAwait(false);
        var rule = item is null ? null : await scope.Rules.GetAsync(item.ItemId, ct).ConfigureAwait(false);
        return rule is { IsEgg: false } ? new PetInstance(item!, scope) : null;
    }
    public async ValueTask<IEggInstance?> FindEggAsync(int uid, CancellationToken ct)
    {
        var scope = context();
        var item = await scope.FindAsync(uid, ct).ConfigureAwait(false);
        var rule = item is null ? null : await scope.Rules.GetAsync(item.ItemId, ct).ConfigureAwait(false);
        return rule is { IsEgg: true } ? new EggInstance(item!, scope) : null;
    }
    public async ValueTask<IPetInstance?> CurrentPetAsync(CancellationToken ct)
    {
        var scope = context();
        var uid = await scope.CurrentUidAsync(ct).ConfigureAwait(false);
        if (uid is null) return null;
        var item = await scope.FindAsync(uid.Value, ct).ConfigureAwait(false);
        var rule = item is null ? null : await scope.Rules.GetAsync(item.ItemId, ct).ConfigureAwait(false);
        return rule is { IsEgg: false } ? new PetInstance(item!, scope) : null;
    }
}
