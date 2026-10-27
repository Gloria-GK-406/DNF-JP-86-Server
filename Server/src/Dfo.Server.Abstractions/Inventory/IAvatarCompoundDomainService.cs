using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IAvatarCompoundDomainService
{
    ValueTask<AvatarCompoundResult> CompoundAsync(SelectedSessionIdentity session, AvatarCompoundInput input, IEffectFootprintRecorder footprint, CancellationToken ct);
}
public interface IAvatarCompoundRuleService
{
    ValueTask<AvatarCompoundPlan> PlanAsync(byte job, byte level, AvatarCompoundInput input, int materialId,
        ItemSnapshot first, ItemSnapshot second, int seed, CancellationToken ct);
}
