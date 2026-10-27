using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Effects;

internal sealed class DungeonEntryAllowanceEffectHandler(IHerasDungeonDomainService heras) : EffectHandler<AddDungeonEntryAllowanceEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct) => heras.ValidateAllowanceAsync(context, effect, ct);
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        await heras.ApplyAllowanceAsync(context, effect, ct).ConfigureAwait(false);
        return new DungeonEntryAllowanceChange(effect.DungeonId);
    }
}
