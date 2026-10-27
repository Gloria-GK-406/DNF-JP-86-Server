using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IFatiguePotionDomainService
{
    ValueTask<OpeningInventoryReceipt> UseAsync(SelectedSessionIdentity session, BusinessSlot sourceSlot,
        DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken token);
}
