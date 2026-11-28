using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Effects;

internal sealed class StartReinforcementPotionEffectHandler(ITimedItemEffectTable itemEffects)
    : EffectHandler<StartReinforcementPotionEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, StartReinforcementPotionEffect effect,
        CancellationToken ct)
    {
        if (context.Session is null || effect.ItemId <= 0 || effect.Duration <= TimeSpan.Zero)
            throw new InvalidOperationException("Reinforcement potion requires a selected session and a positive duration.");
        _ = context.At.Add(effect.Duration);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context,
        StartReinforcementPotionEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var expiry = context.At.Add(effect.Duration);
        if (!await itemEffects.TryStartAsync(context.CharacterId, new(effect.ItemId, expiry), context.At, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Reinforcement potion became active during item use.");
        return new ReinforcementPotionChange(effect.ItemId, expiry);
    }
}
