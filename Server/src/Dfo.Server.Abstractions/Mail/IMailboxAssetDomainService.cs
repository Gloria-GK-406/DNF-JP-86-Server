using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Abstractions.Mail;

public sealed record MailboxInventoryUpdate(BusinessSlot Slot, ItemIdentity? Identity, InventoryItemCoreState Core);
public sealed record MailboxAssetProjection(IReadOnlyList<MailboxInventoryUpdate> Updates,
    IReadOnlyList<BusinessSlot> Cleared, IReadOnlyList<InventoryRuntimeValueState> Materials,
    int? Gold, DateTimeOffset PreparedAt);
public interface IMailboxAssetDomainService
{
    ValueTask<MailboxAssetProjection> PrepareAsync(CharacterId character, IReadOnlyList<MailAssetChange> changes, CancellationToken token);
}
