using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

/// <summary>One transaction's persistence and clock boundary, never a detached state copy.</summary>
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
        if (lifetime.Settled)
            throw new InvalidOperationException("Obtain a new creature instance for the next transaction.");
        runtime?.EnsureActive();
    }

    public async ValueTask SynchronizeAsync(CancellationToken ct)
    {
        EnsureActive();
        ct.ThrowIfCancellationRequested();
        if (runtime is not null) await runtime.SynchronizeAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<CreatureState?> ReadAsync(int uid, CancellationToken ct)
    {
        EnsureActive();
        var state = await states.ReadAsync(owner, uid, ct).ConfigureAwait(false);
        var stomach = state is null ? 0 : runtime?.Stomach(uid, state.ExactStomach) ?? state.ExactStomach;
        return state is null ? null : state with
        {
            Stomach = (int)stomach,
            ExactStomach = stomach,
            NameBytes = [.. state.NameBytes]
        };
    }

    public async ValueTask<int?> CurrentUidAsync(CancellationToken ct)
    {
        EnsureActive();
        return (await states.ReadCurrentAsync(owner, ct).ConfigureAwait(false))?.ItemUid;
    }

    public void Stage(CreatureState state, bool restartClock = false)
    {
        EnsureActive();
        states.Write(owner, state with { NameBytes = [.. state.NameBytes] });
        if (restartClock) runtime?.ResetStomach(state.ItemUid);
        else runtime?.SetStomach(state.ItemUid, state.ExactStomach);
    }

    public void Notify(CreatureRuntimeChange change) => runtime?.Notify(change);

    public async ValueTask<CreatureLocation?> LocateAsync(int uid, CancellationToken ct)
    {
        EnsureActive();
        if (inventory is null) return null;
        foreach (var space in new[] { BusinessInventorySpace.WornCreature, BusinessInventorySpace.CreatureInventory })
        {
            var container = inventory.FindContainer(space);
            if (container is null) continue;
            foreach (var item in await container.ReadAllAsync(ct).ConfigureAwait(false))
                if (item.ItemKind == 5 && item.Core.Value == uid)
                    return new(container, new BusinessSlot(space, item.SlotIndex), item.Core);
        }
        return null;
    }

    public bool IsLive(CreatureLocation location, CreatureState state) =>
        location.Core.ItemId == state.ItemTemplateId
        && (location.Core.ExpireTime <= 0 || location.Core.ExpireTime > Now.ToUnixTimeSeconds());

    private sealed class Lifetime : ITransactionParticipant
    {
        public bool Settled { get; private set; }
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken ct) { Settled = true; return ValueTask.CompletedTask; }
    }
}

internal sealed record CreatureLocation(IBusinessItemContainer Container, BusinessSlot Slot, InventoryItemCoreState Core);

internal interface ICreatureInstanceRuntime
{
    void EnsureActive();
    ValueTask SynchronizeAsync(CancellationToken ct);
    double Stomach(int uid, double fallback);
    void SetStomach(int uid, double value);
    void ResetStomach(int uid);
    void Notify(CreatureRuntimeChange change);
}

internal sealed class CreatureCollection(Func<CreatureInstanceContext> context) : ICreatureCollection
{
    public async ValueTask<IPetInstance?> FindPetAsync(int uid, CancellationToken ct)
    {
        var scope = context();
        var state = await scope.ReadAsync(uid, ct).ConfigureAwait(false);
        var rule = state is null ? null : await scope.Rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
        return rule is { IsEgg: false } ? new PetInstance(uid, scope) : null;
    }

    public async ValueTask<IEggInstance?> FindEggAsync(int uid, CancellationToken ct)
    {
        var scope = context();
        var state = await scope.ReadAsync(uid, ct).ConfigureAwait(false);
        var rule = state is null ? null : await scope.Rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
        return rule is { IsEgg: true } ? new EggInstance(uid, scope) : null;
    }

    public async ValueTask<IPetInstance?> CurrentPetAsync(CancellationToken ct)
    {
        var scope = context();
        var uid = await scope.CurrentUidAsync(ct).ConfigureAwait(false);
        if (uid is null) return null;
        var state = await scope.ReadAsync(uid.Value, ct).ConfigureAwait(false);
        var rule = state is null ? null : await scope.Rules.GetAsync(state.ItemTemplateId, ct).ConfigureAwait(false);
        return rule is { IsEgg: false } ? new PetInstance(uid.Value, scope) : null;
    }
}
