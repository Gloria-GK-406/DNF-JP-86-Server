using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Application.Mail;
internal sealed class MailboxAssetDomainService(IItemDomainStore store, IDomainTransactionAccessor transactions, TimeProvider clock) : IMailboxAssetDomainService
{
    public async ValueTask<MailboxAssetProjection> PrepareAsync(CharacterId character, IReadOnlyList<MailAssetChange> changes, CancellationToken token)
    {
        var session = store.SessionIn(transactions.Current ?? throw new InvalidOperationException("Mailbox projection requires the command transaction."));
        await session.ApplyDecidedWritesAsync(token);
        var at = clock.GetUtcNow();
        var updates = new List<CommittedInventoryUpdateState>();
        var cleared = new List<(InventoryContainerType, int)>();
        foreach (var group in changes.Where(x => x.Character == character && x.Container is not null && x.SlotIndex is not null).GroupBy(x => (Type: x.Container!.Value, Id: x.ContainerId)))
        {
            var items = new List<InventoryItemState>();
            foreach (var slot in group.Select(x => x.SlotIndex!.Value).Distinct())
            {
                var item = await session.ReadAsync(group.Key.Id is { } id ? new ContainerId(id) : ContainerId.ForCharacter(character, group.Key.Type), slot, token);
                if (item is null) cleared.Add((group.Key.Type, slot));
                else items.Add(new InventoryItemState(item.Identity, slot, item.Core));
            }
            if (items.Count != 0) updates.Add(new(group.Key.Type, items, at));
        }
        var materials = changes.Where(x => x.AccountMaterial is not null).Select(x => x.AccountMaterial!).GroupBy(x => x.VirtualSlotIndex)
            .Select(g => g.Last()).Select(x => new InventoryRuntimeValueState(x.VirtualSlotIndex, x.ItemId, x.FinalCount)).ToArray();
        return new(updates, cleared, materials, changes.LastOrDefault(x => x.GoldBalance is not null)?.GoldBalance, at);
    }
}
