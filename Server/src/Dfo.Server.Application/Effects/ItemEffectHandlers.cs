using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Effects;

internal sealed class ConsumeFromContainerEffectHandler(IBusinessInventorySlotDomainService inventory, IItemLifecycleDomainService lifecycle)
    : EffectHandler<ConsumeFromContainerEffect>
{
    private async ValueTask<ItemReference> ReadAsync(EffectExecutionContext context,
        ConsumeFromContainerEffect effect, CancellationToken ct)
    {
        var item = await inventory.Get(context.CharacterId, effect.Slot).Container.GetAsync(effect.Slot.Index, ct)
            .ConfigureAwait(false);
        if (effect.Count <= 0 || item is null || !item.IsPile || item.ItemId != effect.ExpectedItemId
            || item.Snapshot.EquipmentLockId != effect.ExpectedEquipmentLockId
            || item.Count < effect.Count || item.Snapshot.Lifetime.IsExpired(context.At))
            throw new InvalidOperationException($"Consumption target {effect.Slot} no longer satisfies its plan.");
        return item;
    }
    public override async ValueTask ValidateAsync(EffectExecutionContext context, ConsumeFromContainerEffect effect, CancellationToken ct) =>
        _ = await ReadAsync(context, effect, ct).ConfigureAwait(false);
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, ConsumeFromContainerEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var item = await ReadAsync(context, effect, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        var remaining = await ItemContainerOperations.ConsumeAsync(inventory.Get(context.CharacterId, effect.Slot).Container,
            item, effect.Count, lifecycle, ct).ConfigureAwait(false);
        return new ContainerConsumptionChange(effect.Slot, item.ItemId, effect.Count, remaining);
    }
}

internal sealed class StartItemCooldownEffectHandler(IOnlineCharacterRuntimeDomainService runtime,
    IDomainTransactionAccessor transactions) : EffectHandler<StartItemCooldownEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, StartItemCooldownEffect effect, CancellationToken ct)
    {
        if (context.Session is null || effect.Duration <= TimeSpan.Zero)
            throw new InvalidOperationException("Item cooldown requires a selected session and positive duration.");
        _ = context.At.Add(effect.Duration);
        return ValueTask.CompletedTask;
    }
    public override ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, StartItemCooldownEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        transactions.Current!.Enlist(new object(), () => new Commit(runtime, context, effect));
        return ValueTask.FromResult<EffectChange>(new ItemCooldownChange(effect.Key, effect.Duration));
    }
    private sealed class Commit(IOnlineCharacterRuntimeDomainService runtime,
        EffectExecutionContext context, StartItemCooldownEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            // Only the session the cooldown was earned in gets it; a replaced one is ignored.
            runtime.StartCooldown(context.Session!, effect.Key, effect.Duration);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class StartSpiritPotionEffectHandler(IOnlineCharacterRuntimeDomainService runtime,
    IDomainTransactionAccessor transactions) : EffectHandler<StartSpiritPotionEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, StartSpiritPotionEffect effect, CancellationToken ct)
    {
        if (context.Session is null || !double.IsFinite(effect.BonusRate) || effect.BonusRate <= 0 || effect.Duration <= TimeSpan.Zero)
            throw new InvalidOperationException("Spirit potion requires a selected session, positive rate and duration.");
        _ = context.At.Add(effect.Duration);
        return ValueTask.CompletedTask;
    }
    public override ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, StartSpiritPotionEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        transactions.Current!.Enlist(new object(), () => new Commit(runtime, context, effect));
        return ValueTask.FromResult<EffectChange>(new SpiritPotionChange(effect.BonusRate, effect.Duration));
    }
    private sealed class Commit(IOnlineCharacterRuntimeDomainService runtime,
        EffectExecutionContext context, StartSpiritPotionEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            runtime.ApplySpiritPotion(context.Session!, effect.BonusRate, effect.Duration);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class AddPetStomachEffectHandler(ICreatureInstanceDomainService creatures) : EffectHandler<AddPetStomachEffect>
{
    private async ValueTask<IPetInstance> ResolveAsync(EffectExecutionContext context, AddPetStomachEffect effect, CancellationToken ct)
    {
        var pet = await creatures.Of(context.CharacterId).FindPetAsync(effect.ItemUid, ct).ConfigureAwait(false);
        if (effect.Amount < 0 || pet is null || pet.Owner != context.CharacterId)
            throw new InvalidOperationException("Pet stomach target no longer satisfies its plan.");
        return pet;
    }
    public override async ValueTask ValidateAsync(EffectExecutionContext context, AddPetStomachEffect effect, CancellationToken ct) =>
        _ = await ResolveAsync(context, effect, ct).ConfigureAwait(false);
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, AddPetStomachEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var pet = await ResolveAsync(context, effect, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        return new PetStomachChange(await pet.AddStomachAsync(effect.Amount, ct).ConfigureAwait(false));
    }
}
