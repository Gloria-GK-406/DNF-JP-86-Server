using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Application.Mail;
internal sealed class MailboxAssetDomainService(IItemDomainStore store, IDomainTransactionAccessor transactions,
    ICharacterBusinessInventoryDomainService inventories, TimeProvider clock) : IMailboxAssetDomainService
{
    public async ValueTask<MailboxAssetProjection> PrepareAsync(CharacterId character, IReadOnlyList<MailAssetChange> changes, CancellationToken token)
    {
        var session = store.SessionIn(transactions.Current ?? throw new InvalidOperationException("Mailbox projection requires the command transaction."));
        await session.ApplyDecidedWritesAsync(token);
        var at = clock.GetUtcNow();
        var updates = new List<MailboxInventoryUpdate>();
        var cleared = new List<BusinessSlot>();
        foreach (var slot in changes.Where(x => x.Character == character && x.Slot is not null)
            .Select(x => x.Slot!.Value).Distinct())
        {
            var physical = LegacyInventoryStorageCoordinates.ToStorage(slot);
            var container = inventories.GetStore(character, BusinessInventoryOwnership.StoreOf(slot.Space))
                .GetContainer(slot.Space);
            var item = await session.ReadAsync(container.StorageId,
                physical.Index, token).ConfigureAwait(false);
            if (item is null) cleared.Add(slot);
            else updates.Add(new(slot, item.Identity, item.Core));
        }
        var materials = changes.Where(x => x.AccountMaterial is not null).Select(x => x.AccountMaterial!).GroupBy(x => x.VirtualSlotIndex)
            .Select(g => g.Last()).Select(x => new InventoryRuntimeValueState(x.VirtualSlotIndex, x.ItemId, x.FinalCount)).ToArray();
        return new(updates, cleared, materials, changes.LastOrDefault(x => x.GoldBalance is not null)?.GoldBalance, at);
    }
}
