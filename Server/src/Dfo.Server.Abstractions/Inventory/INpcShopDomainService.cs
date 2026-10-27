using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface INpcShopDomainService
{
    ValueTask<NpcShopPurchaseResult> PurchaseAsync(
        SelectedSessionIdentity session, int itemId, int rawCount, int shopId, int npcId,
        DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);

    ValueTask<NpcShopPurchaseCountsResult> PurchaseCountsAsync(
        SelectedSessionIdentity session, int npcId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>A refusal after a guarded write must leave the command through rollback.</summary>
public sealed class NpcShopPurchaseRejectedException(NpcShopRejection reason)
    : Exception($"NPC shop purchase refused: {reason}.")
{
    public NpcShopRejection Reason { get; } = reason;
}
