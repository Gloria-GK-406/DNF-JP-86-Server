using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;

namespace Dfo.Server.Application.Effects;

internal sealed class StartReinforcementPotionEffectHandler(ICharacterItemEffectTable itemEffects)
    : EffectHandler<StartReinforcementPotionEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, StartReinforcementPotionEffect effect,
        CancellationToken ct)
    {
        if (context.Session is null || effect.ItemId <= 0 || effect.Duration < TimeSpan.FromSeconds(1))
            throw new InvalidOperationException("Reinforcement potion requires a selected session and a positive duration.");
        _ = Expiry(context, effect);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context,
        StartReinforcementPotionEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        var expiry = Expiry(context, effect);
        if (!await itemEffects.TryStartAsync(context.CharacterId, effect.ItemId, checked((int)context.At.ToUnixTimeSeconds()),
            expiry, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Reinforcement potion became active during item use.");
        return new ReinforcementPotionChange(effect.ItemId, DateTimeOffset.FromUnixTimeSeconds(expiry));
    }

    private static int Expiry(EffectExecutionContext context, StartReinforcementPotionEffect effect) =>
        checked((int)context.At.Add(effect.Duration).ToUnixTimeSeconds());
}
