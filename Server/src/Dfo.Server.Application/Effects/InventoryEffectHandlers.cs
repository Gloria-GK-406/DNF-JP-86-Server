using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Effects;

internal sealed class SpendGoldEffectHandler(ICharacterDomainService characters) : EffectHandler<SpendGoldEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SpendGoldEffect effect, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(effect.Amount);
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SpendGoldEffect effect, CancellationToken ct) =>
        new GoldEffectChange(await characters.Get(context.CharacterId).TrySpendGoldAsync(effect.Amount, ct).ConfigureAwait(false)
            ?? throw new InventoryEffectException(InventoryEffectRejection.InsufficientAssets));
}

internal sealed class GrantStackableEffectHandler(IItemGrantDomainService grants,
    IBusinessInventorySlotDomainService slots, IItemPlacementRuleService placements) : EffectHandler<GrantStackableEffect>
{
    public override async ValueTask ValidateAsync(EffectExecutionContext context, GrantStackableEffect effect, CancellationToken ct)
    {
        if (effect.Count <= 0
            || await placements.PlacementForAsync(effect.ItemId, ct).ConfigureAwait(false) is not
                { WorldClass: ItemWorldClass.Interchangeable, ItemKind: > 0 })
            throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, GrantStackableEffect effect, CancellationToken ct)
    {
        var added = await grants.GrantAllAsync(context.CharacterId, effect.ItemId, effect.Count, ct).ConfigureAwait(false);
        var changes = new List<InventorySlotEffectChange>();
        foreach (var slot in added.Items.Select(x => x.Slot).Distinct())
            changes.Add(new(slot, (await slots.Get(context.CharacterId, slot).Container.GetAsync(slot.Index, ct)
                .ConfigureAwait(false))?.Snapshot));
        return new StackableGrantEffectChange(added.Items.Select(x => new StackableGrantAddition(x.Slot, effect.ItemId, x.Count)).ToArray(),
            changes.AsReadOnly());
    }
}

internal abstract class EquipmentEffectHandler<TEffect>(IEquipmentDomainService equipment) : EffectHandler<TEffect>
    where TEffect : EquipmentEffect
{
    private async ValueTask<IEquipmentInstance> ResolveAsync(EffectExecutionContext context, TEffect effect, CancellationToken ct)
    {
        var instance = await equipment.Of(context.CharacterId).FindAtAsync(effect.Equipment.Reference.BusinessSlot
                ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget), ct).ConfigureAwait(false)
            ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        if (!instance.Reference.SameOccupancyAs(effect.Equipment.Reference)
            || (await instance.ReadAsync(ct).ConfigureAwait(false)).ItemId != effect.Equipment.ExpectedItemId)
            throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        return instance;
    }
    protected abstract ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, TEffect effect, CancellationToken ct);
    protected virtual void ValidateValue(TEffect effect) { }
    public override async ValueTask ValidateAsync(EffectExecutionContext context, TEffect effect, CancellationToken ct)
    {
        ValidateValue(effect);
        _ = await ResolveAsync(context, effect, ct).ConfigureAwait(false);
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, TEffect effect, CancellationToken ct)
    {
        var instance = await ResolveAsync(context, effect, ct).ConfigureAwait(false);
        var before = await instance.ReadAsync(ct).ConfigureAwait(false);
        return new EquipmentEffectChange(before.BusinessSlot!.Value, before.Snapshot,
            (await ChangeAsync(instance, effect, ct).ConfigureAwait(false))?.Snapshot);
    }
}
internal sealed class SetEquipmentUpgradeEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<SetEquipmentUpgradeEffect>(equipment)
{
    protected override void ValidateValue(SetEquipmentUpgradeEffect effect) => ArgumentOutOfRangeException.ThrowIfGreaterThan(effect.Level, (byte)31);
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, SetEquipmentUpgradeEffect effect, CancellationToken ct) =>
        await instance.SetReinforcementLevelAsync(effect.Level, ct).ConfigureAwait(false);
}
internal sealed class SetEquipmentEnchantmentEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<SetEquipmentEnchantmentEffect>(equipment)
{
    protected override void ValidateValue(SetEquipmentEnchantmentEffect effect) => ArgumentOutOfRangeException.ThrowIfNegative(effect.CardId);
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, SetEquipmentEnchantmentEffect effect, CancellationToken ct) =>
        await instance.SetEnchantmentAsync(effect.CardId, ct).ConfigureAwait(false);
}
internal sealed class SetEquipmentDimensionalEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<SetEquipmentDimensionalEffect>(equipment)
{
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, SetEquipmentDimensionalEffect effect, CancellationToken ct) =>
        await instance.SetDimensionalAttributeAsync(effect.Type, effect.Value, ct).ConfigureAwait(false);
}
internal sealed class SetEquipmentMagicSealEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<SetEquipmentMagicSealEffect>(equipment)
{
    protected override void ValidateValue(SetEquipmentMagicSealEffect effect)
    {
        if (effect.Options is not { Count: >= 1 and <= 3 } || effect.Options.Any(x => x is null))
            throw new ArgumentException("Invalid magic seal effect options.");
    }
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, SetEquipmentMagicSealEffect effect, CancellationToken ct) =>
        await instance.SetMagicSealOptionsAsync(effect.Options, ct).ConfigureAwait(false);
}
internal sealed class SetEquipmentForgingEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<SetEquipmentForgingEffect>(equipment)
{
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, SetEquipmentForgingEffect effect, CancellationToken ct) =>
        await instance.SetForgingLevelAsync(effect.Level, ct).ConfigureAwait(false);
}
internal sealed class DestroyEquipmentEffectHandler(IEquipmentDomainService equipment) : EquipmentEffectHandler<DestroyEquipmentEffect>(equipment)
{
    protected override async ValueTask<ItemReference?> ChangeAsync(IEquipmentInstance instance, DestroyEquipmentEffect effect, CancellationToken ct)
    { await instance.DestroyAsync(ct).ConfigureAwait(false); return null; }
}
