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
            || item.Count < effect.Count || item.Snapshot.ExpireTime is > 0 and < int.MaxValue && item.Snapshot.ExpireTime <= context.At.ToUnixTimeSeconds())
            throw new InvalidOperationException($"Consumption target {effect.Slot} no longer satisfies its plan.");
        return item;
    }
    public override async ValueTask ValidateAsync(EffectExecutionContext context, ConsumeFromContainerEffect effect, CancellationToken ct) =>
        _ = await ReadAsync(context, effect, ct).ConfigureAwait(false);
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, ConsumeFromContainerEffect effect, CancellationToken ct)
    {
        var item = await ReadAsync(context, effect, ct).ConfigureAwait(false);
        var remaining = await ItemContainerOperations.ConsumeAsync(inventory.Get(context.CharacterId, effect.Slot).Container,
            item, effect.Count, lifecycle, ct).ConfigureAwait(false);
        return new ContainerConsumptionChange(effect.Slot, item.ItemId, effect.Count, remaining);
    }
}

internal sealed class StartItemCooldownEffectHandler(ICharacterItemCooldownDomainService cooldowns,
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions) : EffectHandler<StartItemCooldownEffect>
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
        transactions.Current!.Enlist(new object(), () => new Commit(cooldowns, sessions, context, effect));
        return ValueTask.FromResult<EffectChange>(new ItemCooldownChange(effect.Key, effect.Duration));
    }
    private sealed class Commit(ICharacterItemCooldownDomainService cooldowns, ISelectedSessionDomainService sessions,
        EffectExecutionContext context, StartItemCooldownEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            if (sessions.FindByCharacter(context.CharacterId) == context.Session)
                cooldowns.Start(context.CharacterId, effect.Key, effect.Duration);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class StartSpiritPotionEffectHandler(SpiritPotionDomainService potion,
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions) : EffectHandler<StartSpiritPotionEffect>
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
        transactions.Current!.Enlist(new object(), () => new Commit(potion, sessions, context, effect));
        return ValueTask.FromResult<EffectChange>(new SpiritPotionChange(effect.BonusRate, effect.Duration));
    }
    private sealed class Commit(SpiritPotionDomainService potion, ISelectedSessionDomainService sessions,
        EffectExecutionContext context, StartSpiritPotionEffect effect) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct)
        {
            potion.ApplyForSession(context.Session!, sessions, effect.BonusRate, effect.Duration);
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
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, AddPetStomachEffect effect, CancellationToken ct) =>
        new PetStomachChange(await (await ResolveAsync(context, effect, ct).ConfigureAwait(false)).AddStomachAsync(effect.Amount, ct)
            .ConfigureAwait(false));
}
