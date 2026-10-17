using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Abstractions.Inventory;
public sealed record SharedEffectSlotState(ItemSnapshot? Item, DateTimeOffset PreparedAt);
public sealed record SharedEffectUseResult(bool Succeeded, byte Selector = 0, SharedEffectSlotState? State = null);
public interface ISharedEffectDomainService
{
    ValueTask<SharedEffectUseResult> UseAsync(SelectedSessionIdentity actor, BusinessSlot slot, int itemId, CancellationToken ct);
}
