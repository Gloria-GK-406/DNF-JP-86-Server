using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Effects;

internal sealed class GrantOpeningGoldEffectHandler(ICharacterDomainService characters,
    ICharacterCarriedGoldTable balances, ICarriedGoldRuleService limits) : OpeningEffectHandler<GrantOpeningGoldEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, GrantOpeningGoldEffect effect, CancellationToken ct)
    {
        if (effect.Amount <= 0) Refuse(InventoryEffectRejection.UnsupportedItem);
        return ValueTask.CompletedTask;
    }

    private async ValueTask<int> TotalAsync(EffectExecutionContext context, int before, int amount, CancellationToken ct)
    {
        var row = await balances.FindAsync(context.CharacterId, ct).ConfigureAwait(false)
            ?? throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
        var total = (long)before + amount;
        if (before < 0 || total > limits.CarryLimitFor(row.Level, row.StoredCarryLimit))
            Refuse(InventoryEffectRejection.NoRoom);
        return checked((int)total);
    }

    public override async ValueTask PreviewAsync(EffectExecutionContext context, GrantOpeningGoldEffect effect,
        IInventoryEffectPreviewState state, CancellationToken ct)
    {
        await ValidateAsync(context, effect, ct).ConfigureAwait(false);
        state.SetGold(await TotalAsync(context, await state.GoldAsync(ct).ConfigureAwait(false), effect.Amount, ct).ConfigureAwait(false));
    }

    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, GrantOpeningGoldEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var character = characters.Get(context.CharacterId);
        var total = await TotalAsync(context, await character.GoldAsync(ct).ConfigureAwait(false), effect.Amount, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        if (await character.AddGoldAsync(effect.Amount, ct).ConfigureAwait(false) != total)
            throw new InventoryEffectException(InventoryEffectRejection.NoRoom);
        var slot = new BusinessSlot(BusinessInventorySpace.VirtualResource, 0);
        var core = new ItemSnapshot { ItemId = 0, LegacyKind = 0, IsPile = true, Count = total };
        return new OpeningVirtualChange(slot, core, new(slot, 0, effect.Amount, 0, 0, 0, core));
    }
}
